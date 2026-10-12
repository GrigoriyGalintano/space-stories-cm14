using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Content.Server.Access.Systems;
using Content.Server.CriminalRecords.Systems;
using Content.Server.Destructible;
using Content.Server.Station.Systems;
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Shared._RMC14.CCVar;
using Content.Shared._RMC14.Marines.Squads;
using Content.Shared._RMC14.MilitaryPolice;
using Content.Shared._RMC14.Overwatch;
using Content.Shared._RMC14.Power;
using Content.Shared.Access.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Access.Systems;
using Content.Shared.Administration.Logs;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.CriminalRecords;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.Destructible;
using Content.Shared.Forensics.Components;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.IdentityManagement.Components;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.Preferences;
using Content.Shared.Security;
using Content.Shared.StationRecords;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._RMC14.MilitaryPolice;

/// <summary>
/// Security records, JAS incident reports and Overwatch's insubordination reports share station record keys.
/// </summary>
public sealed class RMCPoliceSystem : EntitySystem
{
    private const int MaxTextLength = 4096;
    private const int MaxCommentLength = 500;
    private const int MaxComments = 256;

    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly CriminalRecordsSystem _criminal = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly IdCardSystem _id = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PaperSystem _paper = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedRMCPowerSystem _power = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly StationRecordsSystem _records = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedGameTicker _ticker = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    private TimeSpan _nextHudRefresh;
    private EntProtoId<RMCMarineLawCatalogueComponent> _lawCatalogue;

    public EntProtoId<RMCMarineLawCatalogueComponent> LawCatalogue => _lawCatalogue;

    public override void Initialize()
    {
        _lawCatalogue = _config.GetCVar(RMCCVars.RMCMarineLawCatalogue);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<AfterGeneralRecordCreatedEvent>(OnRecordCreated);
        SubscribeLocalEvent<RecordModifiedEvent>(OnRecordModified);
        SubscribeLocalEvent<RecordRemovedEvent>(OnRecordRemoved);
        SubscribeLocalEvent<InventoryComponent, DidEquipEvent>(OnEquipped);
        SubscribeLocalEvent<InventoryComponent, DidUnequipEvent>(OnUnequipped);
        SubscribeLocalEvent<IdentityComponent, IdentityChangedEvent>(OnIdentityChanged);
        SubscribeLocalEvent<RMCPoliceConsoleComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        SubscribeLocalEvent<RMCPoliceConsoleComponent, BoundUserInterfaceMessageAttempt>(OnMessageAttempt);
        SubscribeLocalEvent<RMCPoliceConsoleComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<RMCPoliceConsoleComponent, EntInsertedIntoContainerMessage>(OnContainerInserted);
        SubscribeLocalEvent<RMCPoliceConsoleComponent, EntRemovedFromContainerMessage>(OnContainerRemoved);
        SubscribeLocalEvent<RMCPoliceConsoleComponent, BreakageEventArgs>(OnBreakage);
        SubscribeLocalEvent<RMCPoliceConsoleComponent, DamageChangedEvent>(OnDamageChanged,
            after: [typeof(DestructibleSystem)]);

        Subs.BuiEvents<RMCPoliceConsoleComponent>(RMCPoliceUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOpened);
            subs.Event<RMCPoliceRecordMsg>(OnRecordMessage);
            subs.Event<RMCSentencingMsg>(OnSentencingMessage);
        });
        Subs.BuiEvents<OverwatchConsoleComponent>(OverwatchConsoleUI.Key, subs =>
            subs.Event<RMCOverwatchInsubordinationMsg>(OnInsubordination));
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        _lawCatalogue = _config.GetCVar(RMCCVars.RMCMarineLawCatalogue);
    }

    private void OnRecordCreated(AfterGeneralRecordCreatedEvent args)
    {
        // Store only the appearance needed by the mugshot, not the player's job or antagonist preferences.
        var portrait = args.Profile == null ? null : new HumanoidCharacterProfile
        {
            Name = args.Profile.Name,
            Age = args.Profile.Age,
            Species = args.Profile.Species,
            Appearance = args.Profile.Appearance,
        }.WithSex(args.Profile.Sex).WithGender(args.Profile.Gender);
        _records.AddRecordEntry(args.Key, new RMCPoliceRecord { Portrait = portrait });
        if (!string.IsNullOrEmpty(args.Record.Fingerprint))
        {
            var owners = EntityQueryEnumerator<FingerprintComponent>();
            while (owners.MoveNext(out var owner, out var fingerprint))
            {
                if (fingerprint.Fingerprint == args.Record.Fingerprint)
                    EnsureComp<RMCPoliceIdentityComponent>(owner).Key = args.Key;
            }
        }
        _records.Synchronize(args.Key);
    }

    private void OnRecordModified(RecordModifiedEvent args)
    {
        // Other security consoles and cartridges can update the underlying station record too.
        if (_records.TryGetRecord<RMCPoliceRecord>(args.Key, out var security) &&
            _records.TryGetRecord<CriminalRecord>(args.Key, out var criminal) &&
            ToSecurityStatus(security.Status) != criminal.Status)
        {
            security.Status = criminal.Status switch
            {
                SecurityStatus.Wanted => RMCPoliceStatus.Arrest,
                SecurityStatus.Detained => RMCPoliceStatus.Incarcerated,
                SecurityStatus.Discharged => RMCPoliceStatus.Released,
                SecurityStatus.Suspected => RMCPoliceStatus.Suspect,
                _ => RMCPoliceStatus.None,
            };
        }
        RefreshStation(args.Key.OriginStation);
        RefreshHud();
    }

    private void OnRecordRemoved(RecordRemovedEvent args)
    {
        RefreshStation(args.Key.OriginStation);
        RefreshHud();
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextHudRefresh)
            return;
        _nextHudRefresh = _timing.CurTime + TimeSpan.FromSeconds(1);
        RefreshHud();
    }

    private void OnEquipped(Entity<InventoryComponent> ent, ref DidEquipEvent args)
    {
        if (args.Slot == "id")
            RefreshHud(ent);
    }

    private void OnUnequipped(Entity<InventoryComponent> ent, ref DidUnequipEvent args)
    {
        if (args.Slot == "id")
            RefreshHud(ent);
    }

    private void OnIdentityChanged(Entity<IdentityComponent> ent, ref IdentityChangedEvent args) => RefreshHud(ent);

    private void RefreshHud()
    {
        var query = EntityQueryEnumerator<InventoryComponent>();
        while (query.MoveNext(out var uid, out _))
            RefreshHud(uid);
    }

    private void RefreshHud(EntityUid wearer)
    {
        if (!TryWornId(wearer, out var card) ||
            CompOrNull<StationRecordKeyStorageComponent>(card)?.Key is not { } key ||
            !_records.TryGetRecord<RMCPoliceRecord>(key, out var record))
        {
            RemComp<RMCPoliceStatusComponent>(wearer);
            return;
        }

        var status = EnsureComp<RMCPoliceStatusComponent>(wearer);
        if (status.Status == record.Status)
            return;
        status.Status = record.Status;
        Dirty(wearer, status);
    }

    private bool CanUse(EntityUid console, EntityUid user)
    {
        var canGhostInteract = TryComp<GhostComponent>(user, out var ghost) && ghost.CanGhostInteract;
        return !(CompOrNull<DestructibleComponent>(console)?.IsBroken ?? false) &&
               _access.IsAllowed(user, console) && _power.IsPowered(console) &&
               (canGhostInteract || _mobState.IsAlive(user)) && _actionBlocker.CanInteract(user, console) &&
               _interaction.InRangeUnobstructed(user, console);
    }

    private void OnBreakage(Entity<RMCPoliceConsoleComponent> ent, ref BreakageEventArgs args)
    {
        _appearance.SetData(ent, RMCPoliceConsoleVisuals.Broken, true);
        _ui.CloseUis(ent.Owner);
    }

    private void OnDamageChanged(Entity<RMCPoliceConsoleComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased || CompOrNull<DestructibleComponent>(ent)?.IsBroken != false)
            return;
        _appearance.SetData(ent, RMCPoliceConsoleVisuals.Broken, false);
    }

    private void OnOpenAttempt(Entity<RMCPoliceConsoleComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (!CanUse(ent, args.User))
            args.Cancel();
    }

    private void OnMessageAttempt(Entity<RMCPoliceConsoleComponent> ent, ref BoundUserInterfaceMessageAttempt args)
    {
        if (CanUse(ent, args.Actor))
            return;

        args.Cancel();
        Error(args.Actor, "rmc-police-access-denied");
    }

    private void OnOpened(Entity<RMCPoliceConsoleComponent> ent, ref BoundUIOpenedEvent args) => Refresh(ent);

    private void OnContainerInserted(Entity<RMCPoliceConsoleComponent> ent, ref EntInsertedIntoContainerMessage args)
        => Refresh(ent);

    private void OnContainerRemoved(Entity<RMCPoliceConsoleComponent> ent, ref EntRemovedFromContainerMessage args)
        => Refresh(ent);

    private EntityUid? GetStation(EntityUid console)
    {
        var station = _station.GetOwningStation(console);
        return HasComp<StationRecordsComponent>(station) ? station : null;
    }

    private void RefreshStation(EntityUid station)
    {
        var query = EntityQueryEnumerator<RMCPoliceConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            if (GetStation(uid) == station && _ui.IsUiOpen(uid, RMCPoliceUiKey.Key))
                Refresh((uid, console));
        }
    }

    private void Refresh(Entity<RMCPoliceConsoleComponent> ent)
    {
        var station = GetStation(ent);
        var views = new List<RMCPoliceRecordView>();
        if (station != null)
        {
            foreach (var (id, general) in _records.GetRecordsOfType<GeneralStationRecord>(station.Value))
            {
                _records.TryGetRecord<RMCPoliceRecord>(new StationRecordKey(id, station.Value), out var security);
                views.Add(new RMCPoliceRecordView(id, general, security));
            }
        }

        var fingerprints = new List<RMCIncidentEntry>();
        var scannerConnected = TryScanner(ent, out var scanner);
        if (scannerConnected && scanner != null)
        {
            fingerprints = ClueEntries(scanner, views);
        }

        _ui.SetUiState(ent.Owner, RMCPoliceUiKey.Key,
            new RMCPoliceBuiState(station != null, views, ent.Comp.Report, ent.Comp.Revision,
                scannerConnected, fingerprints, _lawCatalogue));
    }

    public void RefreshScanner(EntityUid scanner)
    {
        if (_containers.TryGetContainingContainer(scanner, out var container) &&
            TryComp<RMCPoliceConsoleComponent>(container.Owner, out var console))
            Refresh((container.Owner, console));
    }

    private List<RMCIncidentEntry> ClueEntries(RMCClueScannerComponent scanner,
        List<RMCPoliceRecordView> records)
    {
        var entries = new List<RMCIncidentEntry>();
        foreach (var clue in scanner.Clues)
        {
            var match = records.FirstOrDefault(view => !string.IsNullOrEmpty(clue.Fingerprint) &&
                                                       view.General.Fingerprint == clue.Fingerprint);
            var captured = !string.IsNullOrEmpty(clue.Name);
            entries.Add(new RMCIncidentEntry(
                captured ? clue.Name : match?.General.Name ?? Loc.GetString("rmc-police-unknown"),
                $"{(captured ? clue.Rank : match?.General.JobTitle)} / " +
                $"{(captured ? clue.Squad : match?.General.Squad)}\n{clue.Fingerprint}\n{clue.Description}"));
        }
        return entries;
    }

    private bool TryScanner(EntityUid console, [NotNullWhen(true)] out RMCClueScannerComponent? scanner)
    {
        scanner = null;
        return _containers.TryGetContainer(console, RMCPoliceConsoleComponent.ScannerSlot, out var container) &&
               container.ContainedEntities.Count == 1 && TryComp(container.ContainedEntities[0], out scanner);
    }

    private string Officer(EntityUid actor)
    {
        if (TryWornId(actor, out var card))
            return $"{card.Comp.FullName ?? Identity.Name(actor, EntityManager)} ({card.Comp.LocalizedJobTitle})";
        return Identity.Name(actor, EntityManager);
    }

    private bool TryWornId(EntityUid wearer, out Entity<IdCardComponent> card)
    {
        card = default;
        return _inventory.TryGetSlotEntity(wearer, "id", out var worn) &&
               _id.TryGetIdCard(worn.Value, out card);
    }

    private void Error(EntityUid actor, LocId message, params (string, object)[] args)
    {
        _popup.PopupEntity(Loc.GetString(message, args), actor, actor, PopupType.MediumCaution);
    }

    private void LogChange(EntityUid actor, EntityUid console, string change)
    {
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(actor):player} used {ToPrettyString(console)}: {change}");
    }

    private bool HighCommand(EntityUid actor) => _access.FindAccessTags(actor).Contains("CMAccessHighCommand");

    private void OnRecordMessage(Entity<RMCPoliceConsoleComponent> ent, ref RMCPoliceRecordMsg args)
    {
        if (ent.Comp.Sentencing || !CanUse(ent, args.Actor) || args.Text.Length > MaxTextLength)
            return;

        var station = GetStation(ent);
        if (station == null)
        {
            Error(args.Actor, "rmc-police-disconnected");
            return;
        }

        if (args.Action == RMCPoliceRecordAction.CreateGeneral)
        {
            var key = _records.AddRecordEntry(station.Value, new GeneralStationRecord
            {
                Name = Loc.GetString("rmc-police-new-record-name"),
                Age = RMCPoliceRecord.GetAgeLimits(_prototypes, null).Min,
                JobTitle = Loc.GetString("rmc-police-unknown"),
            });
            _records.AddRecordEntry(key, new RMCPoliceRecord());
            _records.AddRecordEntry(key, new CriminalRecord());
            _records.Synchronize(key);
            LogChange(args.Actor, ent, $"created personnel record {key.Id}");
            return;
        }

        if (args.Action is RMCPoliceRecordAction.PrintFingerprints or
            RMCPoliceRecordAction.ClearFingerprints or RMCPoliceRecordAction.EjectScanner)
        {
            ScannerAction(ent, args);
            return;
        }

        var recordKey = new StationRecordKey(args.Id, station.Value);
        if (!_records.TryGetRecord<GeneralStationRecord>(recordKey, out var general))
            return;

        if (args.Action == RMCPoliceRecordAction.CreateSecurity)
        {
            if (!_records.TryGetRecord<RMCPoliceRecord>(recordKey, out _))
            {
                _records.AddRecordEntry(recordKey, new RMCPoliceRecord());
                _records.Synchronize(recordKey);
            }
            return;
        }

        if (args.Action == RMCPoliceRecordAction.DeleteGeneral)
        {
            if (!HighCommand(args.Actor))
            {
                Error(args.Actor, "rmc-police-high-command-only");
                return;
            }

            _criminal.UpdateCriminalIdentity(general.Name, SecurityStatus.None);
            _records.RemoveRecord(recordKey);
            LogChange(args.Actor, ent, $"deleted personnel record {general.Name} ({args.Id})");
            return;
        }

        if (args.Action == RMCPoliceRecordAction.EditGeneral)
        {
            EditGeneral(ent, args, recordKey, general);
            return;
        }

        _records.TryGetRecord<RMCPoliceRecord>(recordKey, out var security);
        if (args.Action == RMCPoliceRecordAction.Print)
        {
            Print(ent, args.Actor, ent.Comp.RecordPaper, general.Name,
                FormatRecord(general, security ?? new RMCPoliceRecord()));
            return;
        }

        if (security == null)
            return;

        switch (args.Action)
        {
            case RMCPoliceRecordAction.Status:
                if (!Enum.IsDefined(typeof(RMCPoliceStatus), (byte) args.Index) || args.Index is < 0 or > 255)
                    return;
                security.Status = (RMCPoliceStatus) args.Index;
                UpdateStatus(recordKey, security, args.Actor);
                break;
            case RMCPoliceRecordAction.PhysicalStatus:
                if (args.Index is < 0 or > 2)
                    return;
                security.PhysicalStatus = (RMCPolicePhysicalStatus) args.Index;
                break;
            case RMCPoliceRecordAction.MentalStatus:
                if (args.Index is < 0 or > 3)
                    return;
                security.MentalStatus = (RMCPoliceMentalStatus) args.Index;
                break;
            case RMCPoliceRecordAction.Comment:
                var text = args.Text.Trim();
                if (text.Length is < 1 or > MaxCommentLength || security.Comments.Count >= MaxComments)
                    return;
                security.Comments.Add(new RMCPoliceComment
                {
                    Text = text,
                    Author = Officer(args.Actor),
                    CreatedAt = _ticker.RoundDuration(),
                });
                break;
            case RMCPoliceRecordAction.DeleteComment:
                if (args.Index < 0 || args.Index >= security.Comments.Count)
                    return;
                var comment = security.Comments[args.Index];
                if (comment.DeletedBy != null)
                    return;
                comment.DeletedBy = Officer(args.Actor);
                comment.DeletedAt = _ticker.RoundDuration();
                break;
            default:
                return;
        }

        LogChange(args.Actor, ent, $"{args.Action} on record {general.Name} ({args.Id}): {args.Text} {args.Index}");
        _records.Synchronize(recordKey);
    }

    private void EditGeneral(Entity<RMCPoliceConsoleComponent> ent, RMCPoliceRecordMsg args,
        StationRecordKey key, GeneralStationRecord general)
    {
        var value = args.Text.Trim();
        switch (args.Field)
        {
            case "name":
                if (value.Length is < 1 or > 49 || value.Any(character =>
                        !char.IsLetter(character) && character is not (' ' or '\'' or '-')))
                {
                    Error(args.Actor, "rmc-police-invalid-name");
                    return;
                }
                _criminal.UpdateCriminalIdentity(general.Name, SecurityStatus.None);
                general.Name = value;
                if (_records.TryGetRecord<RMCPoliceRecord>(key, out var security))
                    UpdateStatus(key, security, args.Actor);
                break;
            case "age":
                _records.TryGetRecord<RMCPoliceRecord>(key, out var personal);
                var limits = RMCPoliceRecord.GetAgeLimits(_prototypes, personal?.Portrait);
                if (!int.TryParse(value, out var age) || age < limits.Min || age > limits.Max)
                {
                    Error(args.Actor, "rmc-police-invalid-age", ("min", limits.Min), ("max", limits.Max));
                    return;
                }
                general.Age = age;
                break;
            case "gender":
                if (!Enum.TryParse<Gender>(value, out var gender) || !Enum.IsDefined(gender))
                    return;
                general.Gender = gender;
                break;
            case "rank":
                if (!HighCommand(args.Actor))
                {
                    Error(args.Actor, "rmc-police-high-command-only");
                    return;
                }
                if (!_prototypes.TryIndex<Content.Shared.Roles.JobPrototype>(value, out var job) ||
                    !RMCPoliceRecord.CanAssignJob(_prototypes, job))
                    return;
                general.JobPrototype = job.ID;
                general.JobTitle = job.LocalizedName;
                general.JobIcon = job.Icon;
                break;
            default:
                return;
        }

        LogChange(args.Actor, ent, $"edited record {key.Id}: {args.Field} = {value}");
        _records.Synchronize(key);
    }

    private void UpdateStatus(StationRecordKey key, RMCPoliceRecord security, EntityUid actor)
    {
        if (!_records.TryGetRecord<CriminalRecord>(key, out var criminal))
        {
            criminal = new CriminalRecord();
            _records.AddRecordEntry(key, criminal);
        }

        var status = ToSecurityStatus(security.Status);
        _criminal.OverwriteStatus(key, criminal, status,
            status is SecurityStatus.Wanted or SecurityStatus.Suspected ? security.MajorCrimes : null, Officer(actor));
    }

    private static SecurityStatus ToSecurityStatus(RMCPoliceStatus status) => status switch
    {
        RMCPoliceStatus.Arrest => SecurityStatus.Wanted,
        RMCPoliceStatus.Incarcerated => SecurityStatus.Detained,
        RMCPoliceStatus.Released => SecurityStatus.Discharged,
        RMCPoliceStatus.Suspect => SecurityStatus.Suspected,
        _ => SecurityStatus.None,
    };

    private void ScannerAction(Entity<RMCPoliceConsoleComponent> ent, RMCPoliceRecordMsg args)
    {
        if (!TryScanner(ent, out var scanner))
            return;

        switch (args.Action)
        {
            case RMCPoliceRecordAction.EjectScanner:
                _slots.TryEject(ent, RMCPoliceConsoleComponent.ScannerSlot, args.Actor, out _);
                break;
            case RMCPoliceRecordAction.ClearFingerprints:
                if (_containers.TryGetContainer(ent, RMCPoliceConsoleComponent.ScannerSlot, out var slot))
                    EntityManager.System<RMCPoliceClueSystem>().Clear((slot.ContainedEntities[0], scanner));
                break;
            case RMCPoliceRecordAction.PrintFingerprints:
                if (scanner.Clues.Count == 0)
                    break;
                var content = new StringBuilder(Loc.GetString("rmc-police-fingerprints"));
                var station = GetStation(ent);
                var records = new List<RMCPoliceRecordView>();
                if (station != null)
                {
                    foreach (var (id, general) in _records.GetRecordsOfType<GeneralStationRecord>(station.Value))
                        records.Add(new RMCPoliceRecordView(id, general, null));
                }
                foreach (var clue in ClueEntries(scanner, records))
                    content.AppendLine().AppendLine(clue.Name).AppendLine(clue.Notes);
                Print(ent, args.Actor, ent.Comp.RecordPaper, Loc.GetString("rmc-police-fingerprints"),
                    content.ToString());
                break;
        }

        Refresh(ent);
    }

    private void OnSentencingMessage(Entity<RMCPoliceConsoleComponent> ent, ref RMCSentencingMsg args)
    {
        if (!ent.Comp.Sentencing || !CanUse(ent, args.Actor) || args.Text.Length > MaxTextLength)
            return;

        if (args.Revision != ent.Comp.Revision)
        {
            Error(args.Actor, "rmc-police-report-changed");
            Refresh(ent);
            return;
        }

        if (args.Action == RMCSentencingAction.New)
        {
            if (ent.Comp.Report == null && _prototypes.TryIndex(_lawCatalogue, out var prototype) &&
                prototype.TryGetComponent(out RMCMarineLawCatalogueComponent? _, EntityManager.ComponentFactory))
                ent.Comp.Report = new RMCIncidentReport { LawCatalogue = _lawCatalogue };
        }
        else if (ent.Comp.Report is { } report)
        {
            switch (args.Action)
            {
                case RMCSentencingAction.Scrap:
                    ent.Comp.Report = null;
                    break;
                case RMCSentencingAction.Suspect:
                    if (!TrySuspectId(args.Actor, out var card, out var key))
                    {
                        Error(args.Actor, "rmc-police-hold-id");
                        return;
                    }
                    report.Suspect = card.FullName ?? string.Empty;
                    report.RecordId = key?.OriginStation == GetStation(ent) ? key?.Id : null;
                    report.RecordStation = key is { } recordKey ? GetNetEntity(recordKey.OriginStation) : null;
                    break;
                case RMCSentencingAction.Summary:
                    report.Summary = args.Text.Trim();
                    break;
                case RMCSentencingAction.AddCharge:
                    if (!_prototypes.TryIndex(report.LawCatalogue, out var cataloguePrototype) ||
                        !cataloguePrototype.TryGetComponent(out RMCMarineLawCatalogueComponent? catalogue,
                            EntityManager.ComponentFactory) ||
                        !catalogue.Laws.Contains(args.Text) || !_prototypes.HasIndex<RMCMarineLawPrototype>(args.Text) ||
                        report.Charges.Count >= RMCIncidentReport.MaxEntries)
                        return;
                    report.Charges.Add(new RMCIncidentCharge(args.Text));
                    break;
                case RMCSentencingAction.RelatedCrime:
                    if (args.Index < 0 || args.Index >= report.Charges.Count ||
                        !_prototypes.TryIndex(report.Charges[args.Index].Law, out var aiding) ||
                        aiding.Punishment != RMCMarineLawPunishment.SameAsCrime ||
                        !_prototypes.TryIndex(report.LawCatalogue, out var relatedPrototype) ||
                        !relatedPrototype.TryGetComponent(out RMCMarineLawCatalogueComponent? relatedCatalogue,
                            EntityManager.ComponentFactory) ||
                        !relatedCatalogue.Laws.Contains(args.Text) ||
                        !_prototypes.TryIndex<RMCMarineLawPrototype>(args.Text, out var related) ||
                        related.Punishment == RMCMarineLawPunishment.SameAsCrime)
                        return;
                    report.Charges[args.Index].RelatedCrime = args.Text;
                    break;
                case RMCSentencingAction.RemoveCharge:
                    if (args.Index < 0 || args.Index >= report.Charges.Count)
                        return;
                    report.Charges.RemoveAt(args.Index);
                    break;
                case RMCSentencingAction.AddWitness:
                    if (!TryHeldId(args.Actor, out var witness, out _) ||
                        string.IsNullOrWhiteSpace(witness.FullName))
                    {
                        Error(args.Actor, "rmc-police-hold-id");
                        return;
                    }
                    if (report.Witnesses.Count >= RMCIncidentReport.MaxEntries)
                        return;
                    report.Witnesses.Add(new RMCIncidentEntry(witness.FullName));
                    break;
                case RMCSentencingAction.AddEvidence:
                    if (!TryHeldItem(args.Actor, out var item))
                    {
                        Error(args.Actor, "rmc-police-hold-evidence");
                        return;
                    }
                    if (report.Evidence.Count >= RMCIncidentReport.MaxEntries)
                        return;
                    report.Evidence.Add(new RMCIncidentEntry(Name(item.Value)));
                    break;
                case RMCSentencingAction.EditWitness:
                case RMCSentencingAction.RemoveWitness:
                case RMCSentencingAction.EditEvidence:
                case RMCSentencingAction.RemoveEvidence:
                    var entries = args.Action is RMCSentencingAction.EditWitness or RMCSentencingAction.RemoveWitness
                        ? report.Witnesses : report.Evidence;
                    if (args.Index < 0 || args.Index >= entries.Count)
                        return;
                    if (args.Action is RMCSentencingAction.EditWitness or RMCSentencingAction.EditEvidence)
                        entries[args.Index].Notes = args.Text.Trim();
                    else
                        entries.RemoveAt(args.Index);
                    break;
                case RMCSentencingAction.Export:
                    if (!Export(ent, args.Actor, report))
                        return;
                    ent.Comp.Report = null;
                    break;
                default:
                    return;
            }
        }
        else
        {
            return;
        }

        ent.Comp.Revision++;
        LogChange(args.Actor, ent, $"incident report {args.Action}: {args.Text} {args.Index}");
        Refresh(ent);
    }

    private bool TryHeldItem(EntityUid actor, [NotNullWhen(true)] out EntityUid? item)
    {
        // Resolve the other hand of a two-handed item, but reject placeholders for pulling or carrying.
        return _interaction.TryGetUsedEntity(actor, out item) && !TerminatingOrDeleted(item.Value) &&
               !HasComp<VirtualItemComponent>(item.Value) && HasComp<ItemComponent>(item.Value) &&
               _hands.IsHolding(actor, item);
    }

    private bool TryHeldId(EntityUid actor, [NotNullWhen(true)] out IdCardComponent? card,
        out StationRecordKey? key)
    {
        card = null;
        key = null;
        if (!TryHeldItem(actor, out var held) || !TryComp(held, out card))
            return false;

        key = CompOrNull<StationRecordKeyStorageComponent>(held.Value)?.Key;
        return !string.IsNullOrWhiteSpace(card.FullName);
    }

    private bool TrySuspectId(EntityUid actor, [NotNullWhen(true)] out IdCardComponent? card,
        out StationRecordKey? key)
    {
        if (TryHeldId(actor, out card, out key))
            return true;
        if (CompOrNull<PullerComponent>(actor)?.Pulling is not { } pulled ||
            !_interaction.InRangeUnobstructed(actor, pulled) || !TryWornId(pulled, out var id))
            return false;
        card = id.Comp;
        key = CompOrNull<StationRecordKeyStorageComponent>(id)?.Key;
        return !string.IsNullOrWhiteSpace(card.FullName);
    }

    private bool Export(Entity<RMCPoliceConsoleComponent> ent, EntityUid actor, RMCIncidentReport report)
    {
        if (report.LawCatalogue != _lawCatalogue || string.IsNullOrWhiteSpace(report.Suspect) ||
            !report.TryCalculateSentence(_prototypes, EntityManager.ComponentFactory, out var sentence))
        {
            Error(actor, "rmc-police-report-incomplete");
            return false;
        }

        var paper = Print(ent, actor, ent.Comp.ReportPaper, report.Suspect, FormatIncident(report));
        if (paper == null)
            return false;

        report.FinalSentence = sentence;
        EnsureComp<RMCIncidentReportPaperComponent>(paper.Value).Report = report.Clone();
        if (GetStation(ent) is { } station && report.RecordId is { } id &&
            report.RecordStation == GetNetEntity(station))
        {
            var key = new StationRecordKey(id, station);
            if (_records.TryGetRecord<RMCPoliceRecord>(key, out var record))
            {
                record.Incidents.Add(report.Clone());
                _records.Synchronize(key);
            }
        }

        return true;
    }

    private void OnInteractUsing(Entity<RMCPoliceConsoleComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !ent.Comp.Sentencing || !CanUse(ent, args.User))
            return;

        if (TryComp(args.Used, out RMCIncidentReportPaperComponent? paper) && paper.Report != null)
        {
            args.Handled = true;
            if (paper.Report.LawCatalogue != _lawCatalogue ||
                !paper.Report.TryCalculateSentence(_prototypes, EntityManager.ComponentFactory, out _))
            {
                Error(args.User, "rmc-police-report-wrong-catalogue");
                return;
            }
            if (ent.Comp.Report != null)
            {
                Error(args.User, "rmc-police-report-in-progress");
                return;
            }
            ent.Comp.Report = paper.Report.Clone();
            ent.Comp.Report.Id = Guid.NewGuid();
            ent.Comp.Report.FinalSentence = null;
            ent.Comp.Report.Detention = null;
            ent.Comp.Revision++;
            LogChange(args.User, ent, $"imported incident report for {paper.Report.Suspect}");
            QueueDel(args.Used);
            Refresh(ent);
            _ui.TryOpenUi(ent.Owner, RMCPoliceUiKey.Key, args.User);
        }
        else if (HasComp<PaperComponent>(args.Used))
        {
            Error(args.User, "rmc-police-original-only");
            args.Handled = true;
        }
    }

    private EntityUid? Print(Entity<RMCPoliceConsoleComponent> ent, EntityUid actor,
        EntProtoId prototype, string name, string content)
    {
        if (_timing.CurTime < ent.Comp.NextPrint)
            return null;

        ent.Comp.NextPrint = _timing.CurTime + TimeSpan.FromSeconds(1.5);
        var paper = Spawn(prototype, Transform(ent).Coordinates);
        _paper.SetContent(paper, FormattedMessage.EscapeText(content));
        var metadata = MetaData(paper);
        EntityManager.System<MetaDataSystem>().SetEntityName(paper,
            Loc.GetString(ent.Comp.Sentencing ? "rmc-police-incident-print-title" : "rmc-police-print-title",
                ("name", name)), metadata);
        _audio.PlayPvs(ent.Comp.PrintSound, ent);
        LogChange(actor, ent, $"printed {name}");
        Refresh(ent);
        return paper;
    }

    private string FormatRecord(GeneralStationRecord general, RMCPoliceRecord record)
    {
        var content = new StringBuilder(Loc.GetString("rmc-police-record-print",
            ("name", general.Name), ("id", general.Fingerprint ?? ""),
            ("rank", general.JobTitle), ("age", general.Age), ("squad", general.Squad ?? ""),
            ("gender", Loc.GetString($"rmc-police-gender-{general.Gender}")),
            ("physical", Loc.GetString($"rmc-police-physical-{record.PhysicalStatus}")),
            ("mental", Loc.GetString($"rmc-police-mental-{record.MentalStatus}")),
            ("status", Loc.GetString($"rmc-police-status-{record.Status}")),
            ("crimes", record.MajorCrimes)));
        foreach (var comment in record.Comments)
        {
            content.AppendLine().AppendLine(comment.Author).AppendLine(comment.CreatedAt.ToString(@"hh\:mm\:ss"));
            content.AppendLine(comment.DeletedBy == null ? comment.Text :
                Loc.GetString("rmc-police-comment-deleted", ("author", comment.DeletedBy),
                    ("time", comment.DeletedAt?.ToString(@"hh\:mm\:ss") ?? "")));
        }
        foreach (var incident in record.Incidents)
            content.AppendLine().AppendLine(FormatIncident(incident));
        return content.ToString();
    }

    private string FormatIncident(RMCIncidentReport report)
    {
        report.TryCalculateSentence(_prototypes, EntityManager.ComponentFactory, out var calculated);
        var time = (report.FinalSentence ?? calculated).GetDescription();
        var content = new StringBuilder(Loc.GetString("rmc-police-incident-print",
            ("name", report.Suspect), ("sentence", time), ("summary", report.Summary)));
        if (report.Detention is { } detention)
            content.AppendLine(detention.GetDescription());
        content.AppendLine().AppendLine(Loc.GetString("rmc-police-incident-charges"));
        foreach (var charge in report.Charges)
        {
            if (!_prototypes.TryIndex(charge.Law, out var law))
                continue;
            content.AppendLine().AppendLine(Loc.GetString(law.Name));
            content.AppendLine(RMCMarineSentence.DescribePunishmentField(law.Punishment, law.BrigTime));
            if (charge.RelatedCrime is { } related && _prototypes.TryIndex(related, out var relatedLaw))
                content.AppendLine(Loc.GetString("rmc-police-related-crime-value", ("crime", Loc.GetString(relatedLaw.Name))));
            if (law.SpecialPunishment is { } punishment)
                content.AppendLine(Loc.GetString(punishment));
            if (law.Conditions is { } conditions)
                content.AppendLine(Loc.GetString(conditions));
        }
        content.AppendLine().AppendLine(Loc.GetString("rmc-police-witnesses"));
        foreach (var witness in report.Witnesses)
            content.AppendLine(witness.Name).AppendLine(witness.Notes);
        content.AppendLine().AppendLine(Loc.GetString("rmc-police-evidence"));
        foreach (var evidence in report.Evidence)
            content.AppendLine(evidence.Name).AppendLine(evidence.Notes);
        return content.ToString();
    }

    public void SynchronizeIncidentReport(Entity<RMCIncidentReportPaperComponent> paper)
    {
        if (paper.Comp.Report is not { } report)
            return;

        _paper.SetContent(paper, FormattedMessage.EscapeText(FormatIncident(report)));
        if (report.RecordId is not { } id || report.RecordStation is not { } station ||
            !TryGetEntity(station, out var stationUid))
            return;

        var key = new StationRecordKey(id, stationUid.Value);
        if (!_records.TryGetRecord<RMCPoliceRecord>(key, out var record))
            return;

        var incident = record.Incidents.FirstOrDefault(entry => entry.Id == report.Id);
        if (incident == null)
            return;
        incident.Detention = report.Detention;
        _records.Synchronize(key);
    }

    private void OnInsubordination(Entity<OverwatchConsoleComponent> ent, ref RMCOverwatchInsubordinationMsg args)
    {
        if (!CanUse(ent, args.Actor) || ent.Comp.Squad is not { } selected ||
            !TryGetEntity(args.Target, out var target) ||
            !TryComp(target, out SquadMemberComponent? member) || member.Squad != GetEntity(selected) ||
            !TryEnlistmentKey(target.Value, GetStation(ent), out var key) ||
            !_records.TryGetRecord<RMCPoliceRecord>(key, out var record))
            return;

        if (record.Status == RMCPoliceStatus.Incarcerated)
        {
            _popup.PopupEntity(Loc.GetString("rmc-police-insubordination-incarcerated"), ent, args.Actor);
            return;
        }

        var charge = Loc.GetString("rmc-police-insubordination-charge");
        var alreadyReported = record.MajorCrimes.Contains(charge, StringComparison.Ordinal);
        if (alreadyReported && record.Status == RMCPoliceStatus.Arrest)
        {
            _popup.PopupEntity(Loc.GetString("rmc-police-insubordination-already-wanted"), ent, args.Actor);
            return;
        }

        if (!alreadyReported)
            record.MajorCrimes = string.IsNullOrWhiteSpace(record.MajorCrimes) ? charge : $"{record.MajorCrimes}\n{charge}";
        record.Status = RMCPoliceStatus.Arrest;
        UpdateStatus(key, record, args.Actor);
        LogChange(args.Actor, ent, $"reported {ToPrettyString(target.Value):player} for insubordination");
        _popup.PopupEntity(Loc.GetString("rmc-police-insubordination-reported"), target.Value, target.Value);
        _popup.PopupEntity(Loc.GetString("rmc-police-insubordination-confirmed"), ent, args.Actor);
        _records.Synchronize(key);
    }

    public bool IsReportSubject(EntityUid actor, RMCIncidentReport report)
    {
        if (report.RecordId is { } id && report.RecordStation is { } netStation && TryGetEntity(netStation, out var station))
        {
            if (TryComp<RMCPoliceIdentityComponent>(actor, out var identity))
                return identity.Key.Id == id && identity.Key.OriginStation == station;
            if (TryEnlistmentKey(actor, station, out var key))
                return key.Id == id;
        }

        // Reports made from an unregistered ID still identify the subject by their recorded name.
        return string.Equals(Name(actor), report.Suspect, StringComparison.Ordinal);
    }

    private bool TryEnlistmentKey(EntityUid marine, EntityUid? station, out StationRecordKey key)
    {
        key = default;
        if (station == null)
            return false;
        if (TryComp<RMCPoliceIdentityComponent>(marine, out var identity))
        {
            key = identity.Key;
            return key.OriginStation == station;
        }

        // Covers existing characters whose records were created before the police system was loaded.
        var fingerprint = CompOrNull<FingerprintComponent>(marine)?.Fingerprint;
        if (!string.IsNullOrEmpty(fingerprint))
        {
            foreach (var (id, record) in _records.GetRecordsOfType<GeneralStationRecord>(station.Value))
            {
                if (record.Fingerprint != fingerprint)
                    continue;
                key = new StationRecordKey(id, station.Value);
                EnsureComp<RMCPoliceIdentityComponent>(marine).Key = key;
                return true;
            }
        }
        return false;
    }
}
