// ReSharper disable CheckNamespace
using System.Linq;
using System.Threading.Tasks;
using Content.Server._Stories.DistressSignal;
using Content.Shared._Stories.DistressSignal;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Shared._RMC14.CCVar;
using Content.Shared._RMC14.Rules;
using Content.Shared.Chat;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._RMC14.Rules.DistressSignal;

public sealed partial class CMDistressSignalRuleSystem
{
    private Task<StoriesDistressSignalState>? _persistenceLoadTask;
    private Task? _persistenceWriteTask;
    private StoriesDistressSignalStore? _persistenceStore;
    private StoriesDistressSignalOutbox? _persistenceOutbox;
    private bool _persistenceLoaded;
    private bool _persistenceInitialized;
    private bool _persistenceNeedsLoad = true;
    private bool _persistenceConfigurationInvalid;
    private bool _persistencePlanetVotePending;
    private bool _applyingPersistedBalance;
    private DateTime _nextPersistenceAttempt;
    private int? _lastFinalizedRoundId;
    private float _persistedMarinesPerXeno;

    private void InitializePersistence()
    {
        var url = _config.GetCVar(StoriesDistressSignalCVars.ApiUrl);
        if (string.IsNullOrWhiteSpace(url))
            return;

        _persistedMarinesPerXeno = _marinesPerXeno;
        try
        {
            var serverId = _config.GetCVar(StoriesDistressSignalCVars.ServerId);
            _persistenceStore = new StoriesDistressSignalStore(
                url, _config.GetCVar(StoriesDistressSignalCVars.ApiToken), serverId);
            _persistenceOutbox = new StoriesDistressSignalOutbox(_distressResources.UserData, serverId);
            ProcessPersistence();
        }
        catch (Exception e)
        {
            _persistenceConfigurationInvalid = true;
            Log.Error($"Failed to initialize Distress Signal persistence:\n{e}");
        }
    }

    public override void Shutdown()
    {
        // Unacknowledged writes already live in the journal; shutdown does not wait for HTTP.
        _persistenceStore?.Dispose();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        ProcessPersistence();
        if (_persistencePlanetVotePending && TryPreparePersistence())
            StartPlanetVote();
    }

    private bool TryPreparePersistence()
    {
        ProcessPersistence();
        return !_persistenceConfigurationInvalid &&
               (_persistenceStore == null ||
                (_persistenceInitialized && !_persistenceNeedsLoad &&
                 _persistenceLoadTask == null && _persistenceWriteTask == null && _persistenceOutbox!.Count == 0));
    }

    private void ProcessPersistence()
    {
        if (_persistenceStore == null || _persistenceConfigurationInvalid)
            return;

        try
        {
            if (_persistenceWriteTask is { } write)
            {
                if (!write.IsCompleted)
                    return;

                write.GetAwaiter().GetResult();
                _persistenceOutbox!.Acknowledge();
                _persistenceWriteTask = null;
                if (_persistenceOutbox.Count == 0)
                    _persistenceNeedsLoad = true;
            }

            if (_persistenceLoadTask is { } load)
            {
                if (!load.IsCompleted)
                    return;

                var state = load.GetAwaiter().GetResult();
                _persistenceLoadTask = null;
                _persistenceLoaded = true;
                // Replay the journal before publishing remote state, which may be older than local writes.
                if (_persistenceOutbox!.Count == 0)
                {
                    _persistenceNeedsLoad = false;
                    ApplyLoadedPersistence(state);
                    _persistenceInitialized = true;
                }
            }

            if (DateTime.UtcNow < _nextPersistenceAttempt)
                return;

            if (_persistenceLoaded && _persistenceOutbox!.Count > 0)
            {
                _persistenceWriteTask = _persistenceStore.Write(_persistenceOutbox.Next);
            }
            else if (!_persistenceLoaded || _persistenceNeedsLoad)
            {
                _persistenceLoadTask = _persistenceStore.GetOrCreateState(_mapVoteExcludeLast, _marinesPerXeno);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to synchronize Distress Signal persistence; the journal will be retried:\n{e}");
            _persistenceLoadTask = null;
            _persistenceWriteTask = null;
            _persistenceLoaded = false;
            _persistenceNeedsLoad = true;
            _nextPersistenceAttempt = DateTime.UtcNow.AddSeconds(5);
        }
    }

    private bool TryQueuePersistence(StoriesDistressSignalWrite write)
    {
        if (_persistenceConfigurationInvalid)
            return false;

        try
        {
            _persistenceOutbox!.Enqueue(write);
            return true;
        }
        catch (Exception e)
        {
            // Do not claim success or start another round when the local durable store is unavailable.
            _persistenceConfigurationInvalid = true;
            Log.Error($"Failed to write the Distress Signal journal. Further round starts are blocked:\n{e}");
            return false;
        }
    }

    private void ApplyLoadedPersistence(StoriesDistressSignalState state)
    {
        ReplaceRecentPlanets(state.RecentPlanetIds);
        var allPlanets = _rmcPlanet.GetAllPlanets();
        var allPlanetIds = allPlanets.Select(p => p.Proto.ID).ToHashSet();
        var carryoverVotes = state.CarryoverVotes
            .Where(v => v.Value > 0 && allPlanetIds.Contains(v.Key))
            .ToDictionary();

        // A valid administrative selection may be outside the current rotation.
        allPlanets.TryFirstOrNull(p => p.Proto.ID == state.SelectedPlanetId, out var selected);
        if (state.SelectedPlanetId != null && selected == null)
        {
            var candidates = _rmcPlanet.GetCandidatesInRotation();
            if (candidates.Count > 0)
                selected = _random.Pick(candidates);
        }

        var selectedPlanetId = selected?.Proto.ID;
        // Do not replace voting state underneath an active vote.
        if (_currentVote == null)
        {
            _carryoverVotes.Clear();
            foreach (var (planetId, votes) in carryoverVotes)
                _carryoverVotes[new EntProtoId<RMCPlanetMapPrototypeComponent>(planetId)] = votes;

            // A round-start write clears the next selection in the API, not the currently loaded planet.
            if (GameTicker.RunLevel == GameRunLevel.PreRoundLobby)
                SelectedPlanetMap = selected;
        }
        ApplyPersistedBalance(state.MarinesPerXeno);

        if (carryoverVotes.Count != state.CarryoverVotes.Count || selectedPlanetId != state.SelectedPlanetId)
        {
            TryQueuePersistence(new StoriesDistressSignalWrite(
                StoriesDistressSignalWriteKind.Voting, PlanetId: selectedPlanetId, CarryoverVotes: carryoverVotes));
        }
    }

    private void OnPersistenceRoundStarting(RoundStartingEvent ev)
    {
        if (TryGetActiveRuleEntity() != null && TryPreparePersistence())
            SelectRandomPlanet();
    }

    protected override void BeforeStartAttempt(RoundStartAttemptEvent ev)
    {
        if (TryGetActiveRuleEntity() == null || TryPreparePersistence())
            return;

        var message = Loc.GetString("stories-distress-signal-persistence-unavailable");
        _chatManager.SendAdminAnnouncement(message);
        _chatManager.DispatchServerAnnouncement(message);
        ev.Cancel();
    }

    private void OnMarinesPerXenoChanged(float value)
    {
        _marinesPerXeno = value;
        if (_persistenceStore == null || _applyingPersistedBalance)
            return;

        if (!_persistenceInitialized || !float.IsFinite(value) || value <= 0 ||
            !TryQueuePersistence(new StoriesDistressSignalWrite(
                StoriesDistressSignalWriteKind.Balance, MarinesPerXeno: value)))
        {
            ApplyPersistedBalance(_persistedMarinesPerXeno);
            return;
        }

        ApplyPersistedBalance(value);
    }

    private void ApplyPersistedBalance(float value)
    {
        _persistedMarinesPerXeno = value;
        _applyingPersistedBalance = true;
        try
        {
            _config.SetCVar(RMCCVars.CMMarinesPerXeno, value);
            _marinesPerXeno = value;
        }
        finally
        {
            _applyingPersistedBalance = false;
        }
    }

    private void OnMapVoteExcludeLastChanged(int value)
    {
        var previous = _mapVoteExcludeLast;
        _mapVoteExcludeLast = Math.Max(0, value);
        TrimRecentPlanets();
        if (_persistenceStore != null && _mapVoteExcludeLast > previous)
            _persistenceNeedsLoad = true;
    }

    private void ReplaceRecentPlanets(IEnumerable<string> planetIds)
    {
        _lastPlanetMaps.Clear();
        foreach (var planetId in planetIds)
            _lastPlanetMaps.Enqueue(new EntProtoId<RMCPlanetMapPrototypeComponent>(planetId));
        TrimRecentPlanets();
    }

    private void TrackPlayedPlanet(EntProtoId<RMCPlanetMapPrototypeComponent> planetId)
    {
        if (_persistenceStore != null &&
            (!_persistenceInitialized || !TryQueuePersistence(new StoriesDistressSignalWrite(
                StoriesDistressSignalWriteKind.StartRound, GameTicker.RoundId, planetId.Id, _marinesPerXeno))))
        {
            throw new InvalidOperationException("Could not durably record the Distress Signal round start.");
        }

        _lastPlanetMaps.Enqueue(planetId);
        TrimRecentPlanets();
    }

    private void TrimRecentPlanets()
    {
        while (_lastPlanetMaps.Count > _mapVoteExcludeLast)
            _lastPlanetMaps.Dequeue();
    }

    private void FinishPersistentRound(int roundId, DistressSignalRuleResult result, float marinesPerXeno)
    {
        if (_lastFinalizedRoundId == roundId)
            return;

        if (_persistenceStore != null && !TryQueuePersistence(new StoriesDistressSignalWrite(
                StoriesDistressSignalWriteKind.FinishRound, roundId, MarinesPerXeno: marinesPerXeno, Result: (int) result)))
            return;

        ApplyPersistedBalance(marinesPerXeno);
        _lastFinalizedRoundId = roundId;
    }

    private bool TryPersistVotingState(
        RMCPlanet? selectedPlanet,
        IReadOnlyDictionary<EntProtoId<RMCPlanetMapPrototypeComponent>, int> carryoverVotes,
        string? announcement = null)
    {
        var votes = carryoverVotes.Where(v => v.Value > 0).ToDictionary(v => v.Key.Id, v => v.Value);
        if (_persistenceConfigurationInvalid ||
            (_persistenceStore != null &&
             (!_persistenceInitialized || !TryQueuePersistence(new StoriesDistressSignalWrite(
                 StoriesDistressSignalWriteKind.Voting, PlanetId: selectedPlanet?.Proto.ID, CarryoverVotes: votes)))))
            return false;

        _carryoverVotes.Clear();
        foreach (var (planetId, count) in votes)
            _carryoverVotes[new EntProtoId<RMCPlanetMapPrototypeComponent>(planetId)] = count;
        SelectedPlanetMap = selectedPlanet;
        if (announcement != null)
        {
            _chatManager.ChatMessageToAll(
                ChatChannel.Server, announcement, announcement, EntityUid.Invalid, hideChat: false, recordReplay: true);
        }

        return true;
    }
}
