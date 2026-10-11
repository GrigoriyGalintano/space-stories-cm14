using System.Linq;
using Content.Client._RMC14.UserInterface.Crt;
using Content.Shared._RMC14.MilitaryPolice;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._RMC14.MilitaryPolice;

public sealed class RMCBrigCellWindow : RMCPoliceWindow
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public event Action<RMCBrigCellMsg>? Message;
    public Func<bool>? CanPardon;

    private RMCBrigCellBuiState? _state;
    private RMCCrtLabel? _timer;
    private readonly RMCCrtActionButton _doors;
    private bool _canPardon;
    private TimeSpan _nextPermissionRefresh;

    public RMCBrigCellWindow() : base("rmc-brig-title")
    {
        IoCManager.InjectDependencies(this);
        _doors = Button("rmc-brig-open-doors", () => Send(RMCBrigCellAction.ToggleDoors));
        Toolbar.AddChild(_doors);
    }

    public void Update(RMCBrigCellBuiState state, bool canPardon)
    {
        _state = state;
        _doors.Text = Loc.GetString(state.DoorsOpen ? "rmc-brig-close-doors" : "rmc-brig-open-doors");
        _doors.Disabled = !state.CanToggleDoors;
        Title = state.CellName;
        _canPardon = canPardon;
        _timer = null;
        Left.DisposeAllChildren();
        Right.DisposeAllChildren();
        Left.AddChild(Heading("rmc-brig-reports"));
        Left.AddChild(Text(Loc.GetString("rmc-brig-insert-hint"), RMCCrtTone.Muted));

        foreach (var view in state.Reports)
        {
            var status = view.Report.Detention?.Status ?? RMCBrigSentenceStatus.Ready;
            var button = Button("rmc-brig-select", () => Send(RMCBrigCellAction.Select, view.Paper), flat: true);
            button.Text = Loc.GetString("rmc-brig-report-entry", ("name", view.Report.Suspect),
                ("status", Loc.GetString($"rmc-brig-status-{status}")));
            button.Selected = view.Paper == state.ViewedReport;
            Left.AddChild(button);
        }

        if (state.Reports.FirstOrDefault(view => view.Paper == state.ViewedReport) is not { } selected)
        {
            Right.AddChild(Text(Loc.GetString("rmc-brig-no-report"), RMCCrtTone.Muted));
            return;
        }

        var report = selected.Report;
        var detention = report.Detention?.Status ?? RMCBrigSentenceStatus.Ready;
        Right.AddChild(Heading("rmc-brig-timer"));
        _timer = Text(string.Empty, RMCCrtTone.Warning);
        _timer.TextFontSize = 18;
        Right.AddChild(_timer);
        UpdateTimer();

        var actions = Horizontal();
        if (detention is RMCBrigSentenceStatus.Ready or RMCBrigSentenceStatus.Paused)
        {
            actions.AddChild(Button(detention == RMCBrigSentenceStatus.Ready ? "rmc-brig-start" : "rmc-brig-resume",
                () => Send(RMCBrigCellAction.Start)));
        }
        if (state.ActiveReport == selected.Paper)
        {
            actions.AddChild(Button("rmc-brig-pause", () => Send(RMCBrigCellAction.Pause)));
            actions.AddChild(Button("rmc-brig-end", () => ConfirmAction(RMCBrigCellAction.End, "rmc-brig-end"),
                danger: true));
        }
        if (actions.ChildCount > 0)
            Right.AddChild(actions);
        var management = Horizontal();
        management.AddChild(Button("rmc-brig-reset", () => ConfirmAction(RMCBrigCellAction.Reset, "rmc-brig-reset"),
            danger: true));
        management.AddChild(Button("rmc-brig-eject", () => Send(RMCBrigCellAction.Eject)));
        Right.AddChild(management);
        if (canPardon && detention is not (RMCBrigSentenceStatus.Served or RMCBrigSentenceStatus.Pardoned))
        {
            Right.AddChild(Button("rmc-brig-pardon", () => ConfirmAction(RMCBrigCellAction.Pardon, "rmc-brig-pardon"),
                RMCCrtIcons.Ban, danger: true));
        }
        Right.AddChild(new RMCCrtSeparator());
        IncidentDetails(Right, report, _prototypes, ComponentFactory);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        UpdateTimer();
        if (_state == null || CanPardon == null || _timing.CurTime < _nextPermissionRefresh)
            return;
        _nextPermissionRefresh = _timing.CurTime + TimeSpan.FromSeconds(1);
        var canPardon = CanPardon();
        if (_canPardon != canPardon)
            Update(_state, canPardon);
    }

    private void UpdateTimer()
    {
        if (_timer == null || _state?.Reports.FirstOrDefault(view => view.Paper == _state.ViewedReport) is not { } selected)
            return;
        var report = selected.Report;
        var status = report.Detention?.Status ?? RMCBrigSentenceStatus.Ready;
        _timer.Text = status is RMCBrigSentenceStatus.Served or RMCBrigSentenceStatus.Pardoned
            ? Loc.GetString($"rmc-brig-status-{status}")
            : report.FinalSentence is { Punishment: RMCMarineLawPunishment.Brig } sentence
                ? RMCBrigSentence.FormatTime(_state.ActiveReport == selected.Paper && _state.FinishAt is { } deadline
                    ? deadline - _timing.CurTime
                    : TimeSpan.FromMinutes(sentence.BrigTime) - (report.Detention?.TimeServed ?? TimeSpan.Zero))
                : Loc.GetString("rmc-brig-indefinite");
    }

    private void Send(RMCBrigCellAction action, NetEntity? paper = null)
    {
        if (_state != null)
            Message?.Invoke(new RMCBrigCellMsg(action, _state.Revision, paper ?? _state.ViewedReport));
    }

    private void ConfirmAction(RMCBrigCellAction action, LocId title)
    {
        if (_state == null)
            return;
        var message = new RMCBrigCellMsg(action, _state.Revision, _state.ViewedReport);
        Confirm(title, () => Message?.Invoke(message));
    }
}
