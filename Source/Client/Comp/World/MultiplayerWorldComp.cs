using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Multiplayer.Client.Persistent;
using Multiplayer.Client.Saving;

namespace Multiplayer.Client;

public class MultiplayerWorldComp
{
    // SortedDictionary to ensure determinism
    public SortedDictionary<int, FactionWorldData> factionData = new();

    public World world;

    public TileTemperaturesComp uiTemperatures;
    public List<MpTradeSession> trading = new();
    public CaravanSplittingSession splitSession;

    public Faction spectatorFaction;

    private int currentFactionId;

    public MultiplayerWorldComp(World world)
    {
        this.world = world;
        uiTemperatures = new TileTemperaturesComp(world);
    }

    // Called from AsyncWorldTimeComp.ExposeData (for backcompat)
    public void ExposeData()
    {
        ExposeFactionData();

        Scribe_Collections.Look(ref trading, "tradingSessions", LookMode.Deep);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            if (trading.RemoveAll(t => t.trader == null || t.playerNegotiator == null) > 0)
                Log.Message("Some trading sessions had null entries");

        DoBackCompat();
    }

    private void DoBackCompat()
    {
        if (Scribe.mode != LoadSaveMode.PostLoadInit)
            return;

        if (spectatorFaction == null)
        {
            void AddSpectatorFaction()
            {
                spectatorFaction = HostUtil.AddNewFaction("Spectator", FactionDefOf.PlayerColony);

                factionData[spectatorFaction.loadID] = FactionWorldData.New(spectatorFaction.loadID);
                factionData[spectatorFaction.loadID].ReassignIds();

                foreach (var map in Find.Maps)
                    MapSetup.InitNewFactionData(map, spectatorFaction);
            }

            void RemoveOpponentFaction()
            {
                // Test faction left in by mistake in version 0.7
                var opponent =
                    Find.FactionManager.AllFactions.FirstOrDefault(f => f.Name == "Opponent" && f.IsPlayer);

                if (opponent is not null)
                {
                    opponent.RemoveAllRelations();
                    Find.FactionManager.allFactions.Remove(opponent);
                    Log.Warning("Multiplayer removed dummy Opponent faction");
                }
            }

            AddSpectatorFaction();
            RemoveOpponentFaction();
        }
    }

    private void ExposeFactionData()
    {
        Scribe_References.Look(ref spectatorFaction, "spectatorFaction");

        if (Scribe.mode == LoadSaveMode.Saving)
        {
            int currentFactionId = GetFactionId(Find.ResearchManager);
            Scribe_Custom.LookValue(currentFactionId, "currentFactionId");

            var savedFactionData = new SortedDictionary<int, FactionWorldData>(factionData);
            savedFactionData.Remove(currentFactionId);

            Scribe_Custom.LookValueDeep(ref savedFactionData, "factionData");
        }
        else
        {
            // The faction whose data is currently set
            Scribe_Values.Look(ref currentFactionId, "currentFactionId");

            Scribe_Custom.LookValueDeep(ref factionData, "factionData");
            factionData ??= new SortedDictionary<int, FactionWorldData>();
        }

        if (Scribe.mode == LoadSaveMode.LoadingVars && Multiplayer.session != null && Multiplayer.game != null)
        {
            Multiplayer.game.myFactionLoading =
                Find.FactionManager.GetById(Multiplayer.session.myFactionId) ?? spectatorFaction;
        }

        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            // Game manager order?
            factionData[currentFactionId] = FactionWorldData.FromCurrent(currentFactionId);
        }
    }

    public void TickWorldTrading()
    {
        for (int i = trading.Count - 1; i >= 0; i--)
        {
            var session = trading[i];
            if (session.playerNegotiator.Spawned) continue;

            if (session.ShouldCancel())
                RemoveTradeSession(session);
        }
    }

    public void RemoveTradeSession(MpTradeSession session)
    {
        int index = trading.IndexOf(session);
        trading.Remove(session);
        Find.WindowStack?.WindowOfType<TradingWindow>()?.Notify_RemovedSession(index);
    }

    public void SetFaction(Faction faction)
    {
        if (!factionData.TryGetValue(faction.loadID, out FactionWorldData data))
            return;

        Game game = Current.Game;
        game.researchManager = data.researchManager;
        game.drugPolicyDatabase = data.drugPolicyDatabase;
        game.outfitDatabase = data.outfitDatabase;
        game.foodRestrictionDatabase = data.foodRestrictionDatabase;
        game.playSettings = data.playSettings;

        game.history = data.history;
        game.storyteller = data.storyteller;
        game.storyWatcher = data.storyWatcher;
    }

    public void DirtyColonyTradeForMap(Map map)
    {
        if (map == null) return;
        foreach (MpTradeSession session in trading)
            if (session.playerNegotiator.Map == map)
                session.deal.recacheColony = true;
    }

    public void DirtyTraderTradeForTrader(ITrader trader)
    {
        if (trader == null) return;
        foreach (MpTradeSession session in trading)
            if (session.trader == trader)
                session.deal.recacheTrader = true;
    }

    public void DirtyTradeForSpawnedThing(Thing t)
    {
        if (t is not { Spawned: true }) return;
        foreach (MpTradeSession session in trading)
            if (session.playerNegotiator.Map == t.Map)
                session.deal.recacheThings.Add(t);
    }

    public bool AnyTradeSessionsOnMap(Map map)
    {
        foreach (MpTradeSession session in trading)
            if (session.playerNegotiator.Map == map)
                return true;
        return false;
    }

    public int GetFactionId(ResearchManager researchManager)
    {
        return factionData.First(kv => kv.Value.researchManager == researchManager).Key;
    }

    public override string ToString()
    {
        return $"{nameof(MultiplayerWorldComp)}_{world}";
    }
}
