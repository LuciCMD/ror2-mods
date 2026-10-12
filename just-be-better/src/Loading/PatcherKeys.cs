namespace JustBeBetter.Loading
{
    // AppDomain data the patcher half leaves for this half
    // Same names as JustBeBetterPatcher's, since the two assemblies never reference each other
    internal static class PatcherKeys
    {
        internal const string PluginTimes = "mephit.Just_Be_Better.PluginTimes";
        internal const string PreloaderDone = "mephit.Just_Be_Better.PreloaderDone";
        internal const string BundleTimes = "mephit.Just_Be_Better.BundleTimes";
        internal const string Hooks = "mephit.Just_Be_Better.PatcherHooks";
        internal const string MainMenu = "mephit.Just_Be_Better.MainMenuReached";
        internal const string CacheStatus = "mephit.Just_Be_Better.BundleCacheStatus";
    }
}
