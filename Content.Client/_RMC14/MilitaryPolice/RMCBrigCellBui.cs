using System.Linq;
using Content.Shared._RMC14.MilitaryPolice;
using Content.Shared.Access.Systems;
using JetBrains.Annotations;
using Robust.Client.Player;
using Robust.Client.UserInterface;

namespace Content.Client._RMC14.MilitaryPolice;

[UsedImplicitly]
public sealed class RMCBrigCellBui(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [Dependency] private readonly IPlayerManager _players = default!;

    private RMCBrigCellWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<RMCBrigCellWindow>();
        _window.Message += SendMessage;
        _window.CanPardon = CanPardon;
        if (State is RMCBrigCellBuiState state)
            UpdateState(state);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not RMCBrigCellBuiState cell || _window == null)
            return;
        _window.Update(cell, CanPardon());
    }

    private bool CanPardon()
    {
        return _players.LocalEntity is { } actor &&
               EntMan.TryGetComponent(Owner, out RMCBrigCellComponent? controller) &&
               EntMan.System<AccessReaderSystem>().FindAccessTags(actor).Any(controller.PardonAccess.Contains);
    }
}
