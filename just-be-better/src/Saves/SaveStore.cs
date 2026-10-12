using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BepInEx.Logging;
using JustBeBetter.Shared;

namespace JustBeBetter.Saves
{
    // One XML file per named save, named by id so renaming never touches the file name
    internal sealed class SaveStore
    {
        private const string Extension = ".xml";

        private readonly ManualLogSource log;

        internal SaveStore(ManualLogSource log)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            Directory.CreateDirectory(Folder);
        }

        internal static string Folder => DataFolder.Of("Saves");

        internal List<RunSave> List()
        {
            var saves = new List<RunSave>();
            foreach (string path in Directory.GetFiles(Folder, "*" + Extension))
            {
                RunSave? save = Read(path);
                if (save != null)
                {
                    saves.Add(save);
                }
            }

            return saves.OrderByDescending(save => save.SavedUtc).ToList();
        }

        internal RunSave? Load(string id)
        {
            string path = PathOf(id);
            return File.Exists(path) ? Read(path) : null;
        }

        // Temporary file, then replace, so a crash mid-write can't leave a broken save
        internal bool Write(RunSave save)
        {
            string path = PathOf(save.Id);
            string temp = path + ".tmp";
            try
            {
                SaveXml.ToXml(save).Save(temp);
                if (File.Exists(path))
                {
                    File.Replace(temp, path, null);
                }
                else
                {
                    File.Move(temp, path);
                }

                return true;
            }
            catch (Exception e)
            {
                log.LogError($"[Store] Couldn't write \"{save.Name}\" to {path}: {e}");
                TryDelete(temp);
                return false;
            }
        }

        internal void Delete(string id)
        {
            TryDelete(PathOf(id));
        }

        private static string PathOf(string id)
        {
            return Path.Combine(Folder, id + Extension);
        }

        private RunSave? Read(string path)
        {
            try
            {
                XElement root = XElement.Load(path);
                RunSave save = SaveXml.FromXml(root);
                save.Id = Path.GetFileNameWithoutExtension(path);
                return save;
            }
            catch (Exception e)
            {
                log.LogError($"[Store] Couldn't read {path}: {e.Message}");
                return null;
            }
        }

        private void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception e)
            {
                log.LogError($"[Store] Couldn't delete {path}: {e.Message}");
            }
        }
    }
}
