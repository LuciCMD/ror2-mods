using BepInEx;
using BepInEx.Bootstrap;

namespace CommandGrace
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(RiskOfOptionsSupport.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class CommandGracePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "mephit.Command_Grace";
        public const string PluginName = "Command_Grace";
        public const string PluginVersion = "0.1.0";

        private Settings? settings;
        private PickerTracker? pickerTracker;
        private SoloPause? soloPause;
        private Protection? protection;
        private RunTimerKeeper? runTimerKeeper;
        private EscapeClosesPicker? escapeClosesPicker;

        private void Awake()
        {
            settings = new Settings(Config);

            if (Chainloader.PluginInfos.ContainsKey(RiskOfOptionsSupport.PluginGuid))
            {
                RiskOfOptionsSupport.Register(settings);
                Logger.LogInfo("Settings added to Risk Of Options");
            }

            pickerTracker = new PickerTracker(Logger);
            soloPause = new SoloPause(settings, pickerTracker, Logger);
            soloPause.Enable();
            protection = new Protection(settings, pickerTracker, Logger);
            protection.Enable();
            pickerTracker.Enable();

            runTimerKeeper = new RunTimerKeeper(Logger);
            runTimerKeeper.Enable();

            escapeClosesPicker = new EscapeClosesPicker(Logger);
            escapeClosesPicker.Enable();
        }

        private void OnDestroy()
        {
            escapeClosesPicker?.Disable();
            runTimerKeeper?.Disable();
            protection?.Disable();
            soloPause?.Disable();
            pickerTracker?.Disable();
        }
    }
}
