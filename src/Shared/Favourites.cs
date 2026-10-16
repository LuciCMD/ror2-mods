using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using RoR2;

namespace JustBeBetter.Shared
{
    // Favourites per game profile, kept by catalog name so mods coming and going don't shift them
    // One file per kind (survivors, items) and profile, loaded lazily for whichever profile is playing
    internal sealed class Favourites
    {
        private const int MaxEntries = 1000;

        private readonly string kind;
        private readonly ManualLogSource log;
        private readonly HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
        private string? loadedPath;
        private UserProfile? loadedProfile;
        private bool loaded;

        internal Favourites(string kind, ManualLogSource log)
        {
            this.kind = kind ?? throw new ArgumentNullException(nameof(kind));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal bool Contains(string? name)
        {
            Load();
            return name != null && names.Contains(name);
        }

        internal void Toggle(string? name)
        {
            Load();
            if (name == null || loadedPath == null)
            {
                return;
            }

            if (!names.Remove(name) && names.Count < MaxEntries)
            {
                names.Add(name);
            }

            try
            {
                ProfileFiles.Write(loadedPath, string.Join(Environment.NewLine, names.OrderBy(n => n, StringComparer.Ordinal)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.LogWarning($"Couldn't save {kind.ToLowerInvariant()}: {e.Message}");
            }
        }

        // Reloads when the profile changes
        // The picker calls this per button every frame, so it's cheap otherwise
        private void Load()
        {
            LocalUser? user = LocalUserManager.GetFirstLocalUser();
            UserProfile? profile = user?.userProfile;
            if (loaded && profile == loadedProfile)
            {
                return;
            }

            loaded = true;
            loadedProfile = profile;
            string? path = ProfileFiles.PathFor(kind, user);
            loadedPath = path;
            names.Clear();
            if (path == null || !File.Exists(path))
            {
                return;
            }

            try
            {
                foreach (string line in File.ReadAllLines(path).Take(MaxEntries))
                {
                    if (line.Trim().Length > 0)
                    {
                        names.Add(line.Trim());
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.LogWarning($"Couldn't load {kind.ToLowerInvariant()}: {e.Message}");
            }
        }
    }
}
