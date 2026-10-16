using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace JustBeBetter.Patcher
{
    // LZMA bundles are unpacked whole into memory every time they're opened (2.3 s and 680 MB for AsteriaMod's)
    // The first time a large one is seen, an LZ4 copy is made after the main menu with Unity's own recompressor
    // Later launches open that copy instead, which only reads what's used, and the contents are identical
    internal static class BundleCache
    {
        private const long MinBytes = 1024 * 1024;
        private const int SampleBytes = 64 * 1024;
        private const int UnusedDays = 30;

        private static readonly Queue<Job> Jobs = new Queue<Job>();
        private static readonly HashSet<string> Queued = new HashSet<string>();
        private static ManualLogSource? log;

        // Verbose Logging, for the lines written on every launch
        private static bool verbose;
        private static bool building;
        private static bool menuReached;

        // Progress for the main menu notice, with packed bytes standing in for the work
        private static int jobsTotal;
        private static int jobsDone;
        private static int failures;
        private static long bytesTotal;
        private static long bytesDone;
        private static Job? currentJob;
        private static AssetBundleRecompressOperation? current;

        // Set when the last bundle opened came from its copy, for the report's label
        [ThreadStatic]
        private static bool servedCopy;

        internal static bool Enabled { get; private set; }

        internal static string Folder { get; } = Path.Combine(Paths.CachePath, "Just_Be_Better");

        internal static void Init(bool enabled, bool verbose, ManualLogSource log)
        {
            BundleCache.log = log;
            BundleCache.verbose = verbose;
            Enabled = enabled;
            if (enabled)
            {
                Tidy();
            }
        }

        // "state\tdone\ttotal\tfraction\tmod\tfailures", state being idle, building or done
        // Read by the plugin half, which can't see these types
        internal static string Status()
        {
            string state = jobsTotal == 0 ? "idle" : building ? "building" : "done";
            double working = current != null && currentJob != null ? current.progress * currentJob.Value.Bytes : 0;
            double fraction = bytesTotal > 0 ? Math.Min(1, (bytesDone + working) / bytesTotal) : 1;
            string name = currentJob?.Name ?? string.Empty;
            return string.Join("\t", state, jobsDone, jobsTotal, fraction.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture), name, failures);
        }

        internal static bool TakeServedCopy()
        {
            bool served = servedCopy;
            servedCopy = false;
            return served;
        }

        internal static bool IsCopy(string path)
        {
            return path.StartsWith(Folder, StringComparison.OrdinalIgnoreCase);
        }

        internal static AssetBundle FromFile(string path, uint crc, Assembly? caller, Func<AssetBundle> load)
        {
            if (!Enabled || IsCopy(path) || !File.Exists(path) || new FileInfo(path).Length < MinBytes)
            {
                return load();
            }

            var info = new FileInfo(path);
            try
            {
                using FileStream file = File.OpenRead(path);
                if (!UnityFs.IsLzma(file))
                {
                    return load();
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log?.LogWarning($"Bundle cache: couldn't read {info.Name}; opening it as usual: {e.Message}");
                return load();
            }

            string key = Hash($"file\n{info.FullName}\n{info.Length}\n{info.LastWriteTimeUtc.Ticks}", null);
            return Serve(key, info.Name, ModName(caller, info.Name), crc, load, () => path, false);
        }

        internal static AssetBundle FromMemory(byte[]? bytes, uint crc, Assembly? caller, Func<AssetBundle> load)
        {
            if (!Enabled || bytes == null || bytes.Length < MinBytes)
            {
                return load();
            }

            using (var view = new MemoryStream(bytes, false))
            {
                if (!UnityFs.IsLzma(view))
                {
                    return load();
                }

                string key = EmbeddedKey(view, caller);
                return Serve(key, Label(caller), ModName(caller, "a mod"), crc, load, () => SaveSource(key, view), true);
            }
        }

        internal static AssetBundle FromStream(Stream? stream, uint crc, Assembly? caller, Func<AssetBundle> load)
        {
            if (!Enabled || stream == null || !stream.CanSeek || !stream.CanRead)
            {
                return load();
            }

            // The mod's own stream, so anything odd about it leaves the load to Unity
            string key;
            try
            {
                if (stream.Length - stream.Position < MinBytes || !UnityFs.IsLzma(stream))
                {
                    return load();
                }

                key = EmbeddedKey(stream, caller);
            }
            catch (Exception e) when (e is IOException or NotSupportedException or ObjectDisposedException)
            {
                log?.LogWarning($"Bundle cache: couldn't read a bundle stream from {Label(caller)}; opening it as usual: {e.Message}");
                return load();
            }

            string label = Label(caller);
            if (TryCopy(key, label, crc, out AssetBundle? copy))
            {
                return copy!;
            }

            // Saved before Unity reads it, since Unity may keep reading the stream later
            string? source = Queued.Contains(key) ? null : SaveSource(key, stream);
            AssetBundle bundle = load();
            if (bundle && source != null)
            {
                Enqueue(key, label, ModName(caller, "a mod"), source, true);
            }

            return bundle;
        }

        // Called once the main menu shows, so copies aren't made while the game is starting
        internal static void StartBuilding()
        {
            menuReached = true;
            if (building || Jobs.Count == 0)
            {
                return;
            }

            building = true;
            log?.LogInfo($"Bundle cache: making {Jobs.Count} LZ4 cop{(Jobs.Count == 1 ? "y" : "ies")} in the background");
            Next();
        }

        private static AssetBundle Serve(string key, string label, string name, uint crc, Func<AssetBundle> load, Func<string?> source, bool ownsSource)
        {
            if (TryCopy(key, label, crc, out AssetBundle? copy))
            {
                return copy!;
            }

            AssetBundle bundle = load();
            if (bundle && !Queued.Contains(key))
            {
                string? path = source();
                if (path != null)
                {
                    Enqueue(key, label, name, path, ownsSource);
                }
            }

            return bundle;
        }

        private static bool TryCopy(string key, string label, uint crc, out AssetBundle? bundle)
        {
            bundle = null;
            string path = CopyPath(key);
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                bundle = AssetBundle.LoadFromFile(path, crc);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log?.LogWarning($"Bundle cache: couldn't open the copy of {label}; using the original: {e.Message}");
            }

            if (bundle)
            {
                if (verbose)
                {
                    log?.LogInfo($"Bundle cache: {label} opened from its LZ4 copy");
                }

                servedCopy = true;
                return true;
            }

            // A bad copy is dropped and rebuilt from the original
            log?.LogWarning($"Bundle cache: the copy of {label} didn't open; using the original and making a new copy");
            Delete(path);
            bundle = null;
            return false;
        }

        private static void Enqueue(string key, string label, string name, string source, bool ownsSource)
        {
            long bytes = File.Exists(source) ? new FileInfo(source).Length : 0;
            Queued.Add(key);
            Jobs.Enqueue(new Job(key, label, name, source, ownsSource, bytes));
            jobsTotal++;
            bytesTotal += bytes;
            log?.LogInfo($"Bundle cache: {label} is LZMA; an LZ4 copy will be made after the main menu");

            // Past the menu, a running chain picks it up, or this starts one
            if (menuReached)
            {
                StartBuilding();
            }
        }

        private static void Next()
        {
            if (Jobs.Count == 0)
            {
                building = false;
                currentJob = null;
                current = null;
                return;
            }

            Job job = Jobs.Dequeue();
            currentJob = job;
            string output = CopyPath(job.Key) + ".tmp";
            Delete(output);
            long start = Stopwatch.GetTimestamp();
            AssetBundleRecompressOperation operation;
            try
            {
                Directory.CreateDirectory(Folder);
                operation = AssetBundle.RecompressAssetBundleAsync(job.Source, output, BuildCompression.LZ4Runtime, 0, UnityEngine.ThreadPriority.Low);
            }
            catch (Exception e)
            {
                log?.LogWarning($"Bundle cache: couldn't start the copy of {job.Label}: {e.Message}");
                failures++;
                Finish(job, output);
                Next();
                return;
            }

            current = operation;
            operation.completed += _ =>
            {
                double seconds = (Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency;
                if (operation.success && File.Exists(output))
                {
                    Keep(job, output, seconds);
                }
                else
                {
                    log?.LogWarning($"Bundle cache: couldn't copy {job.Label}: {operation.humanReadableResult}");
                    failures++;
                }

                Finish(job, output);
                Next();
            };
        }

        // Antivirus may still hold the fresh copy, then it's dropped and remade next launch
        private static void Keep(Job job, string output, double seconds)
        {
            string path = CopyPath(job.Key);
            try
            {
                Delete(path);
                File.Move(output, path);
                log?.LogInfo($"Bundle cache: LZ4 copy of {job.Label} made ({new FileInfo(path).Length / 1e6:0} MB, {seconds:0.0} s); it opens from there next launch");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log?.LogWarning($"Bundle cache: couldn't keep the copy of {job.Label}: {e.Message}");
                failures++;
            }
        }

        private static void Finish(Job job, string output)
        {
            current = null;
            jobsDone++;
            bytesDone += job.Bytes;
            Delete(output);
            if (job.OwnsSource)
            {
                Delete(job.Source);
            }
        }

        // Embedded bundles have no file of their own: keyed by the mod's DLL and samples of the bundle bytes
        private static string EmbeddedKey(Stream stream, Assembly? caller)
        {
            long start = stream.Position;
            long length = stream.Length - start;
            string dll = string.Empty;
            try
            {
                if (caller != null && !string.IsNullOrEmpty(caller.Location))
                {
                    var info = new FileInfo(caller.Location);
                    dll = $"{info.FullName}\n{info.Length}\n{info.LastWriteTimeUtc.Ticks}";
                }
            }
            catch (Exception e) when (e is IOException or NotSupportedException or UnauthorizedAccessException)
            {
                dll = caller?.FullName ?? string.Empty;
            }

            var samples = new MemoryStream();
            foreach (long at in new[] { 0, (length / 2) - (SampleBytes / 2), length - SampleBytes })
            {
                stream.Position = start + Math.Max(0, at);
                var buffer = new byte[SampleBytes];
                int read = stream.Read(buffer, 0, buffer.Length);
                samples.Write(buffer, 0, Math.Max(0, read));
            }

            stream.Position = start;
            return Hash($"embedded\n{dll}\n{length}", samples.ToArray());
        }

        private static string? SaveSource(string key, Stream stream)
        {
            long start = stream.Position;
            string path = CopyPath(key) + ".source.tmp";
            try
            {
                Directory.CreateDirectory(Folder);
                using (FileStream file = File.Create(path))
                {
                    stream.CopyTo(file);
                }

                return path;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log?.LogWarning($"Bundle cache: couldn't save a bundle to copy later: {e.Message}");
                Delete(path);
                return null;
            }
            finally
            {
                stream.Position = start;
            }
        }

        // Leftovers from a session that ended mid-copy, and copies nothing has opened for a month
        private static void Tidy()
        {
            try
            {
                if (!Directory.Exists(Folder))
                {
                    return;
                }

                foreach (string path in Directory.GetFiles(Folder))
                {
                    if (path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                        || File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-UnusedDays))
                    {
                        Delete(path);
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log?.LogWarning($"Bundle cache: couldn't tidy {Folder}: {e.Message}");
            }
        }

        private static string CopyPath(string key)
        {
            return Path.Combine(Folder, key + ".bundle");
        }

        private static string ModName(Assembly? caller, string fallback)
        {
            return caller?.GetName().Name ?? fallback;
        }

        private static string Label(Assembly? caller)
        {
            return $"{caller?.GetName().Name ?? "a mod"}'s embedded bundle";
        }

#pragma warning disable CA5351 // MD5 only names cache files, nothing depends on it being secure
        private static string Hash(string text, byte[]? extra)
        {
            using MD5 md5 = MD5.Create();
            byte[] head = Encoding.UTF8.GetBytes(text);
            md5.TransformBlock(head, 0, head.Length, null, 0);
            md5.TransformFinalBlock(extra ?? Array.Empty<byte>(), 0, extra?.Length ?? 0);
            return BitConverter.ToString(md5.Hash).Replace("-", string.Empty).ToLowerInvariant();
        }
#pragma warning restore CA5351

        private static void Delete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (verbose)
                {
                    log?.LogInfo($"Bundle cache: couldn't delete {path}: {e.Message}");
                }
            }
        }

        private readonly struct Job
        {
            internal Job(string key, string label, string name, string source, bool ownsSource, long bytes)
            {
                Key = key;
                Label = label;
                Name = name;
                Source = source;
                OwnsSource = ownsSource;
                Bytes = bytes;
            }

            internal string Key { get; }

            internal string Label { get; }

            // The mod's name, for the main menu notice
            internal string Name { get; }

            internal long Bytes { get; }

            internal string Source { get; }

            // Saved from memory or a stream, so deleted once copied
            internal bool OwnsSource { get; }
        }
    }
}
