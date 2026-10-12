using BepInEx;
using SaveIt.Ui;

namespace SaveIt
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class SaveItPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "mephit.Save_It";
        public const string PluginName = "Save_It";
        public const string PluginVersion = "0.1.0";

        private SaveSession? session;
        private PauseMenuButton? pauseMenuButton;
        private SavesPanel? savesPanel;
        private GameOverRetry? gameOverRetry;

        private void Awake()
        {
            GameFields.ReportMissing(Logger);
            var store = new SaveStore(Logger);
            session = new SaveSession(store, Logger);
            session.Enable();
            pauseMenuButton = new PauseMenuButton(session, Logger);
            pauseMenuButton.Enable();
            savesPanel = new SavesPanel(session, Logger);
            savesPanel.Enable();
            gameOverRetry = new GameOverRetry(session, Logger);
            gameOverRetry.Enable();
            gameObject.AddComponent<SaveIndicator>();
            Logger.LogInfo($"Saves are kept in {SaveStore.Folder}");
        }

        private void OnDestroy()
        {
            gameOverRetry?.Disable();
            savesPanel?.Disable();
            pauseMenuButton?.Disable();
            session?.Disable();
        }
    }
}
