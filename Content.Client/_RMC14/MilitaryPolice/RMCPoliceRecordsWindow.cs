using System.Linq;
using System.Numerics;
using Content.Client._RMC14.UserInterface.Crt;
using Content.Client.Lobby;
using Content.Client.UserInterface.Controls;
using Content.Shared._RMC14.MilitaryPolice;
using Content.Shared.Roles;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._RMC14.MilitaryPolice;

public sealed class RMCPoliceRecordsWindow : RMCPoliceWindow
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    public event Action<RMCPoliceRecordMsg>? Message;
    private readonly RMCCrtLineEdit _search = new();
    private readonly RMCCrtOptionButton _statusFilter = new();
    private readonly RMCCrtOptionButton _sort = new();
    private readonly BoxContainer _list = Vertical();
    private readonly Dictionary<(uint Record, int Incident), bool> _expandedIncidents = new();
    private RMCPoliceBuiState? _state;
    private uint? _selected;
    private bool _scanner;
    private EntityUid? _portrait;
    private RecordTab _tab;

    private enum RecordTab : byte
    {
        Security,
        Personal,
        History,
    }

    public RMCPoliceRecordsWindow() : base("rmc-police-records-title")
    {
        IoCManager.InjectDependencies(this);
        _search.PlaceHolder = Loc.GetString("rmc-police-search");
        _search.OnTextChanged += _ => PopulateList();
        Toolbar.AddChild(_search);
        var actions = Horizontal();
        Toolbar.AddChild(actions);
        actions.AddChild(Button("rmc-police-new-record", () =>
            Message?.Invoke(new RMCPoliceRecordMsg(RMCPoliceRecordAction.CreateGeneral)), RMCCrtIcons.IdCard));
        actions.AddChild(Button("rmc-police-fingerprints", () =>
        {
            _scanner = true;
            PopulateDetails();
        }, RMCCrtIcons.Cog));
        actions.AddChild(Button("rmc-police-records-title", () =>
        {
            _scanner = false;
            PopulateDetails();
        }, RMCCrtIcons.Users));
        _statusFilter.AddItem(Loc.GetString("rmc-police-all-statuses"), -1);
        foreach (var status in Enum.GetValues<RMCPoliceStatus>())
            _statusFilter.AddItem(Loc.GetString($"rmc-police-status-{status}"), (int) status);
        _statusFilter.SelectId(-1);
        _statusFilter.OnItemSelected += _ => PopulateList();
        _sort.AddItem(Loc.GetString("rmc-police-sort-name"), 0);
        _sort.AddItem(Loc.GetString("rmc-police-sort-rank"), 1);
        _sort.AddItem(Loc.GetString("rmc-police-sort-id"), 2);
        _sort.OnItemSelected += _ => PopulateList();
        Left.AddChild(Heading("rmc-police-personnel"));
        Left.AddChild(_statusFilter);
        Left.AddChild(_sort);
        Left.AddChild(_list);
        OnClose += DeletePortrait;
    }

    public void Update(RMCPoliceBuiState state)
    {
        _state = state;
        Connection.Text = state.Connected ? string.Empty : Loc.GetString("rmc-police-disconnected");
        Connection.Visible = !state.Connected;
        PopulateList();
        PopulateDetails();
    }

    private void PopulateList()
    {
        _list.DisposeAllChildren();
        if (_state == null)
            return;

        var records = _state.Records.Where(record =>
            (_statusFilter.SelectedId == -1 ||
             (int) (record.Security?.Status ?? RMCPoliceStatus.None) == _statusFilter.SelectedId) &&
            ($"{record.General.Name} {record.General.JobTitle} {record.General.Squad} " +
             $"{record.Id} {record.General.Fingerprint}").Contains(_search.Text, StringComparison.OrdinalIgnoreCase));
        records = _sort.SelectedId switch
        {
            1 => records.OrderBy(record => record.General.JobTitle).ThenBy(record => record.General.Name),
            2 => records.OrderBy(record => record.Id),
            _ => records.OrderBy(record => record.General.Name),
        };
        var count = 0;
        foreach (var record in records)
        {
            count++;
            var button = Button("rmc-police-select", () =>
            {
                _selected = record.Id;
                _scanner = false;
                PopulateList();
                PopulateDetails();
            });
            button.Text = $"{record.General.Name}\n{record.General.JobTitle} / {record.General.Squad}\n" +
                          Loc.GetString($"rmc-police-status-{record.Security?.Status ?? RMCPoliceStatus.None}");
            button.ContentAlignment = RMCCrtContentAlignment.Left;
            button.Selected = _selected == record.Id;
            button.Variant = RMCCrtButtonVariant.Flat;
            _list.AddChild(button);
        }
        if (count == 0)
            _list.AddChild(Text(Loc.GetString("rmc-police-no-results"), RMCCrtTone.Muted));
    }

    private void Send(RMCPoliceRecordAction action, string text = "", string field = "", int index = 0)
    {
        if (_selected is { } id)
            Message?.Invoke(new RMCPoliceRecordMsg(action, id, text, field, index));
    }

    private void PopulateDetails()
    {
        Right.DisposeAllChildren();
        DeletePortrait();
        if (_scanner)
        {
            PopulateScanner();
            return;
        }
        var record = _state?.Records.FirstOrDefault(record => record.Id == _selected);
        if (record == null)
        {
            Right.AddChild(Text(Loc.GetString("rmc-police-select-record"), RMCCrtTone.Muted));
            return;
        }

        var header = Horizontal();
        header.AddChild(new RMCCrtLabel
        {
            Text = record.General.Name,
            TextFontSize = 12,
            Heading = true,
            HorizontalExpand = true,
        });
        var print = Button("rmc-police-print-record", () => Send(RMCPoliceRecordAction.Print), RMCCrtIcons.PaperPlane);
        print.HorizontalExpand = false;
        header.AddChild(print);
        Right.AddChild(header);
        Right.AddChild(Text(Loc.GetString("rmc-police-record-id", ("id", record.Id))));

        var tabs = Horizontal();
        AddTab(tabs, "rmc-police-security-data", RecordTab.Security);
        AddTab(tabs, "rmc-police-personal-data", RecordTab.Personal);
        AddTab(tabs, "rmc-police-incidents", RecordTab.History);
        Right.AddChild(tabs);
        Right.AddChild(new RMCCrtSeparator { Margin = new Thickness(0, 4) });
        switch (_tab)
        {
            case RecordTab.Personal:
                PopulatePersonal(record);
                break;
            case RecordTab.History:
                PopulateHistory(record.Security);
                break;
            default:
                PopulateSecurity(record);
                break;
        }
    }

    private void AddTab(BoxContainer tabs, LocId label, RecordTab tab)
    {
        var button = Button(label, () => { _tab = tab; PopulateDetails(); }, flat: true);
        button.Selected = _tab == tab;
        tabs.AddChild(button);
    }

    private void PopulatePersonal(RMCPoliceRecordView record)
    {
        var general = record.General;
        Field("rmc-police-name", general.Name, "name", 49);
        Field("rmc-police-age", general.Age.ToString(), "age", 3);
        var rank = Horizontal();
        rank.AddChild(Text(Loc.GetString("rmc-police-rank-value", ("rank", general.JobTitle))));
        var editRank = Button("rmc-police-edit", () => EditRank(record.Id, general.JobPrototype));
        editRank.ToolTip = Loc.GetString("rmc-police-edit-rank");
        editRank.HorizontalExpand = false;
        rank.AddChild(editRank);
        Right.AddChild(rank);
        Right.AddChild(Text(Loc.GetString("rmc-police-squad-value",
            ("squad", general.Squad ?? Loc.GetString("rmc-police-unknown")))));
        Right.AddChild(Text(Loc.GetString("rmc-police-gender"), RMCCrtTone.Muted));
        var gender = new RMCCrtOptionButton();
        foreach (var value in Enum.GetValues<Gender>())
            gender.AddItem(Loc.GetString($"rmc-police-gender-{value}"), (int) value);
        gender.SelectId((int) general.Gender);
        gender.OnItemSelected += id => Message?.Invoke(new RMCPoliceRecordMsg(
            RMCPoliceRecordAction.EditGeneral, record.Id, ((Gender) id).ToString(), "gender"));
        Right.AddChild(gender);
        if (record.Security?.Portrait is { } portrait)
        {
            _prototypes.TryIndex<JobPrototype>(general.JobPrototype, out var job);
            _portrait = _ui.GetUIController<LobbyUIController>().LoadProfileEntity(portrait, job, job != null);
            var portraits = Horizontal();
            foreach (var direction in new[] { Direction.South, Direction.West })
            {
                var view = new SpriteView
                {
                    OverrideDirection = direction,
                    Scale = new Vector2(3),
                    SetSize = new Vector2(96, 96),
                    HorizontalExpand = true,
                };
                view.SetEntity(_portrait);
                portraits.AddChild(view);
            }
            Right.AddChild(portraits);
        }
        Right.AddChild(new RMCCrtSeparator { Margin = new Thickness(0, 6) });
        var delete = Button("rmc-police-delete-record", () => Confirm("rmc-police-delete-record", () =>
            Message?.Invoke(new RMCPoliceRecordMsg(RMCPoliceRecordAction.DeleteGeneral, record.Id))),
            RMCCrtIcons.Ban, true);
        delete.ToolTip = Loc.GetString("rmc-police-high-command-only");
        Right.AddChild(delete);
    }

    private void PopulateSecurity(RMCPoliceRecordView record)
    {
        if (record.Security is not { } security)
        {
            Right.AddChild(Button("rmc-police-new-security-record", () => Send(RMCPoliceRecordAction.CreateSecurity)));
            return;
        }
        Status<RMCPoliceStatus>("rmc-police-status", "rmc-police-status-", security.Status,
            RMCPoliceRecordAction.Status);
        Status<RMCPolicePhysicalStatus>("rmc-police-physical", "rmc-police-physical-", security.PhysicalStatus,
            RMCPoliceRecordAction.PhysicalStatus);
        Status<RMCPoliceMentalStatus>("rmc-police-mental", "rmc-police-mental-", security.MentalStatus,
            RMCPoliceRecordAction.MentalStatus);
        if (!string.IsNullOrWhiteSpace(security.MajorCrimes))
            Right.AddChild(Text(security.MajorCrimes, RMCCrtTone.Warning));
        Right.AddChild(new RMCCrtSeparator { Margin = new Thickness(0, 6) });
        var comments = Horizontal();
        comments.AddChild(Heading("rmc-police-comments"));
        var addComment = Button("rmc-police-add-comment-short", () =>
        {
            var id = record.Id;
            Edit("rmc-police-add-comment", "", text => Message?.Invoke(new RMCPoliceRecordMsg(
                RMCPoliceRecordAction.Comment, id, text)), 500);
        });
        addComment.HorizontalExpand = false;
        comments.AddChild(addComment);
        Right.AddChild(comments);
        for (var i = 0; i < security.Comments.Count; i++)
        {
            var index = i;
            var comment = security.Comments[i];
            var content = Vertical();
            content.Margin = new Thickness(0, 4);
            var header = Horizontal();
            header.AddChild(Text($"{comment.Author} / {comment.CreatedAt:hh\\:mm\\:ss}", RMCCrtTone.Muted));
            if (comment.DeletedBy == null)
            {
                var delete = Button("rmc-police-remove-short", () => Confirm("rmc-police-delete-comment", () =>
                    Message?.Invoke(new RMCPoliceRecordMsg(RMCPoliceRecordAction.DeleteComment, record.Id,
                        index: index))), RMCCrtIcons.Ban, true);
                delete.HorizontalExpand = false;
                delete.ToolTip = Loc.GetString("rmc-police-delete-comment");
                header.AddChild(delete);
            }
            content.AddChild(header);
            content.AddChild(Text(comment.DeletedBy == null ? comment.Text :
                Loc.GetString("rmc-police-comment-deleted", ("author", comment.DeletedBy),
                    ("time", comment.DeletedAt?.ToString(@"hh\:mm\:ss") ?? ""))));
            Right.AddChild(content);
        }
        if (security.Comments.Count == 0)
            Right.AddChild(Text(Loc.GetString("rmc-police-no-comments"), RMCCrtTone.Muted));
    }

    private void PopulateHistory(RMCPoliceRecord? security)
    {
        if (security == null || security.Incidents.Count == 0)
        {
            Right.AddChild(Text(Loc.GetString("rmc-police-no-incidents"), RMCCrtTone.Muted));
            return;
        }
        for (var i = security.Incidents.Count - 1; i >= 0; i--)
        {
            var incident = security.Incidents[i];
            var key = (_selected!.Value, i);
            var section = new RMCCrtCollapsibleSection
            {
                Title = Loc.GetString("rmc-police-history-entry", ("number", i + 1),
                    ("sentence", Sentence(incident, _prototypes))),
                Expanded = _expandedIncidents.TryGetValue(key, out var expanded)
                    ? expanded : i == security.Incidents.Count - 1,
            };
            section.OnToggled += expanded => _expandedIncidents[key] = expanded;
            IncidentDetails(section.Body, incident, _prototypes);
            Right.AddChild(section);
        }
    }

    private void Field(LocId label, string value, string field, int maxLength)
    {
        var id = _selected!.Value;
        var row = Horizontal();
        row.AddChild(Text($"{Loc.GetString(label)}: {value}"));
        var edit = Button("rmc-police-edit", () => Edit(label, value, text =>
            Message?.Invoke(new RMCPoliceRecordMsg(RMCPoliceRecordAction.EditGeneral, id, text, field)),
            maxLength, multiline: false, validate: field == "age" ? ValidateAge : ValidateName));
        edit.HorizontalExpand = false;
        row.AddChild(edit);
        Right.AddChild(row);
    }

    private static string? ValidateAge(string value) => int.TryParse(value, out var age) && age is >= 18 and <= 100
        ? null : Loc.GetString("rmc-police-invalid-age");

    private static string? ValidateName(string value)
    {
        value = value.Trim();
        return value.Length is >= 1 and <= 49 && value.All(character =>
            char.IsLetter(character) || character is ' ' or '\'' or '-')
            ? null : Loc.GetString("rmc-police-invalid-name");
    }

    private void EditRank(uint id, string selected)
    {
        var window = new DefaultWindow
        {
            Title = Loc.GetString("rmc-police-edit-rank"),
            SetSize = new Vector2(400, 150),
        };
        var theme = new RMCCrtThemeScope { Palette = RMCCrtPalettePreset.Red };
        window.Contents.AddChild(theme);
        var layout = Vertical();
        layout.Margin = new Thickness(8);
        theme.AddChild(layout);
        var jobs = new RMCCrtOptionButton();
        var values = _prototypes.EnumeratePrototypes<JobPrototype>().OrderBy(job => job.LocalizedName).ToList();
        for (var i = 0; i < values.Count; i++)
            jobs.AddItem(values[i].LocalizedName, i);
        var selectedIndex = values.FindIndex(job => job.ID == selected);
        if (selectedIndex >= 0)
            jobs.SelectId(selectedIndex);
        layout.AddChild(jobs);
        layout.AddChild(Button("rmc-police-save", () =>
        {
            if (jobs.SelectedId >= 0 && jobs.SelectedId < values.Count)
                Message?.Invoke(new RMCPoliceRecordMsg(RMCPoliceRecordAction.EditGeneral, id,
                    values[jobs.SelectedId].ID, "rank"));
            window.Close();
        }));
        OpenEditor(window);
    }

    private void Status<T>(LocId label, string prefix, T selected, RMCPoliceRecordAction action) where T : struct, Enum
    {
        Right.AddChild(Text(Loc.GetString(label), RMCCrtTone.Muted));
        var id = _selected!.Value;
        var option = new RMCCrtOptionButton { HorizontalExpand = true };
        var values = Enum.GetValues<T>();
        for (var i = 0; i < values.Length; i++)
            option.AddItem(Loc.GetString($"{prefix}{values[i]}"), i);
        option.SelectId(Array.IndexOf(values, selected));
        option.OnItemSelected += selectedId => Message?.Invoke(new RMCPoliceRecordMsg(action, id, index: selectedId));
        Right.AddChild(option);
    }

    private void PopulateScanner()
    {
        Right.AddChild(Heading("rmc-police-fingerprints"));
        Right.AddChild(Text(Loc.GetString(_state?.ScannerConnected == true ? "rmc-police-scanner-connected" :
            "rmc-police-scanner-disconnected")));
        if (_state?.ScannerConnected != true)
            return;
        Right.AddChild(Text(Loc.GetString("rmc-police-scanner-count", ("count", _state.Fingerprints.Count)),
            RMCCrtTone.Muted));
        foreach (var fingerprint in _state.Fingerprints)
            Right.AddChild(Text($"{fingerprint.Name}\n{fingerprint.Notes}"));
        if (_state.Fingerprints.Count == 0)
            Right.AddChild(Text(Loc.GetString("rmc-police-no-fingerprints"), RMCCrtTone.Muted));
        Right.AddChild(Button("rmc-police-print-fingerprints", () =>
            Message?.Invoke(new RMCPoliceRecordMsg(RMCPoliceRecordAction.PrintFingerprints))));
        Right.AddChild(Button("rmc-police-clear-fingerprints", () => Confirm("rmc-police-clear-fingerprints", () =>
            Message?.Invoke(new RMCPoliceRecordMsg(RMCPoliceRecordAction.ClearFingerprints))), danger: true));
        Right.AddChild(Button("rmc-police-eject-scanner", () =>
            Message?.Invoke(new RMCPoliceRecordMsg(RMCPoliceRecordAction.EjectScanner)), RMCCrtIcons.DoorOpen));
    }

    private void DeletePortrait()
    {
        if (_portrait is { } portrait)
            _entities.DeleteEntity(portrait);
        _portrait = null;
    }
}
