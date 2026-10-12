using System.IO;
using BepInEx;

namespace JustBeBetter.Shared
{
    // Under config so r2modman's profile export carries saves, votes and favourites along
    internal static class DataFolder
    {
        internal static string Of(string name)
        {
            return Path.Combine(Paths.ConfigPath, JustBeBetterPlugin.PluginName, name);
        }
    }
}
