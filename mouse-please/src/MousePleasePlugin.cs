using BepInEx;
using BepInEx.Bootstrap;

namespace MousePlease
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(RiskOfOptionsSupport.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class MousePleasePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "mephit.Mouse_Please";
        public const string PluginName = "Mouse_Please";
        public const string PluginVersion = "0.1.0";

        private CursorRules? cursorRules;

        private void Awake()
        {
            var settings = new Settings(Config);
            if (Chainloader.PluginInfos.ContainsKey(RiskOfOptionsSupport.PluginGuid))
            {
                RiskOfOptionsSupport.Register(settings);
                Logger.LogInfo("Settings added to Risk Of Options");
            }

            cursorRules = new CursorRules(settings, Logger);
            cursorRules.Enable();
        }

        private void OnDestroy()
        {
            cursorRules?.Disable();
        }
    }
}
