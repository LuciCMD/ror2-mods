using BepInEx;
using BepInEx.Bootstrap;

namespace EquipmentBeQuiet
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(RiskOfOptionsSupport.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class EquipmentBeQuietPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "mephit.Equipment_Be_Quiet";
        public const string PluginName = "Equipment_Be_Quiet";
        public const string PluginVersion = "0.1.0";

        private EquipmentSounds? equipmentSounds;

        private void Awake()
        {
            var settings = new Settings(Config);
            if (Chainloader.PluginInfos.ContainsKey(RiskOfOptionsSupport.PluginGuid))
            {
                RiskOfOptionsSupport.Register(settings);
                Logger.LogInfo("Settings added to Risk Of Options");
            }

            equipmentSounds = new EquipmentSounds(settings, Logger);
            equipmentSounds.Enable();
        }

        private void OnDestroy()
        {
            equipmentSounds?.Disable();
        }
    }
}
