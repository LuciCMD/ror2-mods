using System;
using System.IO;
using System.Text;

namespace JustLoad.Patcher
{
    // Reads just enough of a UnityFS bundle header to tell whether its data is LZMA, which Unity has to unpack
    // whole on load. Layout as in AssetStudio's BundleFile; everything is big-endian
    internal static class UnityFs
    {
        private const int MaxBlocks = 100_000;
        private const int MaxInfoBytes = 16 * 1024 * 1024;
        private const int LzmaFlag = 1;

        // The stream's position is left where it was
        internal static bool IsLzma(Stream stream)
        {
            long start = stream.Position;
            try
            {
                return ReadIsLzma(stream, start);
            }
            catch (Exception e) when (e is IOException or ArgumentException or IndexOutOfRangeException or InvalidDataException)
            {
                return false;
            }
            finally
            {
                stream.Position = start;
            }
        }

        private static bool ReadIsLzma(Stream stream, long start)
        {
            if (Encoding.ASCII.GetString(ReadExactly(stream, 8)) != "UnityFS\0")
            {
                return false;
            }

            uint version = ReadUInt32(stream);
            SkipCString(stream);
            SkipCString(stream);
            long size = (long)ReadUInt64(stream);
            uint packedInfo = ReadUInt32(stream);
            uint unpackedInfo = ReadUInt32(stream);
            uint flags = ReadUInt32(stream);
            if (packedInfo > MaxInfoBytes || unpackedInfo > MaxInfoBytes)
            {
                return false;
            }

            if (version >= 7)
            {
                stream.Position = start + ((stream.Position - start + 15) & ~15L);
            }

            if ((flags & 0x80) != 0)
            {
                stream.Position = start + size - packedInfo;
            }

            byte[] info = ReadExactly(stream, (int)packedInfo);
            info = (flags & 0x3F) switch
            {
                0 => info,
                2 or 3 => Lz4.Decode(info, (int)unpackedInfo),

                // Block info packed some other way; can't tell, so leave the bundle alone
                _ => Array.Empty<byte>(),
            };
            return info.Length > 0 && HasLzmaBlock(info);
        }

        // 16-byte hash, block count, then (unpacked, packed, flags) per block
        private static bool HasLzmaBlock(byte[] info)
        {
            int count = BigEndian32(info, 16);
            if (count < 0 || count > MaxBlocks)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                int flags = (info[20 + (i * 10) + 8] << 8) | info[20 + (i * 10) + 9];
                if ((flags & 0x3F) == LzmaFlag)
                {
                    return true;
                }
            }

            return false;
        }

        private static byte[] ReadExactly(Stream stream, int count)
        {
            var buffer = new byte[count];
            int read = 0;
            while (read < count)
            {
                int got = stream.Read(buffer, read, count - read);
                if (got <= 0)
                {
                    throw new InvalidDataException("Bundle header ends early");
                }

                read += got;
            }

            return buffer;
        }

        private static void SkipCString(Stream stream)
        {
            for (int i = 0; i < 256; i++)
            {
                int b = stream.ReadByte();
                if (b <= 0)
                {
                    return;
                }
            }

            throw new InvalidDataException("Bundle header string too long");
        }

        private static uint ReadUInt32(Stream stream)
        {
            return (uint)BigEndian32(ReadExactly(stream, 4), 0);
        }

        private static ulong ReadUInt64(Stream stream)
        {
            byte[] b = ReadExactly(stream, 8);
            return ((ulong)(uint)BigEndian32(b, 0) << 32) | (uint)BigEndian32(b, 4);
        }

        private static int BigEndian32(byte[] b, int at)
        {
            return (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
        }
    }

    // LZ4 block format decoder, only for the small block info table
    internal static class Lz4
    {
        internal static byte[] Decode(byte[] source, int size)
        {
            var output = new byte[size];
            int s = 0;
            int d = 0;
            while (s < source.Length)
            {
                int token = source[s++];
                int literals = ReadLength(source, ref s, token >> 4);
                Buffer.BlockCopy(source, s, output, d, literals);
                s += literals;
                d += literals;
                if (s >= source.Length)
                {
                    break;
                }

                int offset = source[s] | (source[s + 1] << 8);
                s += 2;
                if (offset == 0 || offset > d)
                {
                    throw new InvalidDataException("Bad LZ4 offset");
                }

                int match = ReadLength(source, ref s, token & 0xF) + 4;
                for (int i = 0; i < match; i++)
                {
                    output[d] = output[d - offset];
                    d++;
                }
            }

            return output;
        }

        private static int ReadLength(byte[] source, ref int s, int length)
        {
            if (length != 15)
            {
                return length;
            }

            int b;
            do
            {
                b = source[s++];
                length += b;
            }
            while (b == 255);
            return length;
        }
    }
}
