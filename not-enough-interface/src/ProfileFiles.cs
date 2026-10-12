using System.IO;
using BepInEx;
using RoR2;

namespace NotEnoughInterface
{
    // Per game profile files under BepInEx\config\Not_Enough_Interface\<kind>\, named after the profile's own
    // save file so each person sharing the PC keeps their own
    internal static class ProfileFiles
    {
        internal static string Folder(string kind)
        {
            return System.IO.Path.Combine(Paths.ConfigPath, "Not_Enough_Interface", kind);
        }

        internal static string? PathFor(string kind, LocalUser? localUser)
        {
            string? name = localUser?.userProfile?.fileName;
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            string safe = string.Concat(name!.Split(System.IO.Path.GetInvalidFileNameChars()));
            return System.IO.Path.Combine(Folder(kind), System.IO.Path.GetFileNameWithoutExtension(safe) + ".txt");
        }

        internal static void Write(string path, string text)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
    }
}
