using BepInEx;
using BepInEx.Configuration;

namespace NotEnoughInterface
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class NotEnoughInterfacePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "mephit.Not_Enough_Interface";
        public const string PluginName = "Not_Enough_Interface";
        public const string PluginVersion = "0.1.0";

        private LobbyLayout? lobbyLayout;
        private RuleStripWrap? ruleStripWrap;
        private VoteStore? voteStore;
        private SurvivorGrid? survivorGrid;
        private PickerSearch? pickerSearch;

        private void Awake()
        {
            lobbyLayout = new LobbyLayout(Logger);
            lobbyLayout.Enable();
            ruleStripWrap = new RuleStripWrap(Logger);
            ruleStripWrap.Enable();
            voteStore = new VoteStore(Logger);
            voteStore.Enable();

            // Debug only, so it stays out of Risk Of Options
            ConfigEntry<int> repeat = Config.Bind(
                "Debug",
                "Repeat Survivors",
                0,
                "Shows every survivor this many extra times in the lobby grid, to try out paging and small icons without a big modded roster. Leave at 0 to play.");
            survivorGrid = new SurvivorGrid(Logger, new Favourites("Favourites", Logger), repeat);
            survivorGrid.Enable();
            pickerSearch = new PickerSearch(Logger, new Favourites("Item Favourites", Logger));
            pickerSearch.Enable();
        }

        private void OnDestroy()
        {
            lobbyLayout?.Disable();
            ruleStripWrap?.Disable();
            voteStore?.Disable();
            survivorGrid?.Disable();
            pickerSearch?.Disable();
        }
    }
}
