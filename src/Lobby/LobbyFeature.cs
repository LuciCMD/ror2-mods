using BepInEx.Configuration;
using BepInEx.Logging;
using JustBeBetter.Shared;

namespace JustBeBetter.Lobby
{
    // The character select lobby: a survivor grid with search and favourites that fits big rosters, loadout and
    // rule rows that wrap, and artifact votes kept across restarts
    internal sealed class LobbyFeature : IFeature
    {
        private readonly LobbyLayout lobbyLayout;
        private readonly RuleStripWrap ruleStripWrap;
        private readonly VoteStore voteStore;
        private readonly SurvivorGrid survivorGrid;

        internal LobbyFeature(ConfigEntry<int> repeatSurvivors, ManualLogSource log)
        {
            lobbyLayout = new LobbyLayout(log);
            ruleStripWrap = new RuleStripWrap(log);
            voteStore = new VoteStore(log);
            survivorGrid = new SurvivorGrid(log, new Favourites("Survivor Favourites", log), repeatSurvivors);
        }

        public void Enable()
        {
            lobbyLayout.Enable();
            ruleStripWrap.Enable();
            voteStore.Enable();
            survivorGrid.Enable();
        }

        public void Disable()
        {
            lobbyLayout.Disable();
            ruleStripWrap.Disable();
            voteStore.Disable();
            survivorGrid.Disable();
        }
    }
}
