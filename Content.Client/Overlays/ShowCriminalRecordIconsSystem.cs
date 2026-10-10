using Content.Shared._RMC14.MilitaryPolice;
using Content.Shared.Overlays;
using Content.Shared.Security.Components;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;

namespace Content.Client.Overlays;

public sealed class ShowCriminalRecordIconsSystem : EquipmentHudSystem<ShowCriminalRecordIconsComponent>
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CriminalRecordComponent, GetStatusIconsEvent>(OnGetStatusIconsEvent);
        SubscribeLocalEvent<RMCPoliceStatusComponent, GetStatusIconsEvent>(OnGetPoliceStatusIcons);
    }

    private void OnGetStatusIconsEvent(EntityUid uid, CriminalRecordComponent component, ref GetStatusIconsEvent ev)
    {
        if (!IsActive || HasComp<RMCPoliceStatusComponent>(uid))
            return;

        if (_prototype.TryIndex(component.StatusIcon, out var iconPrototype))
            ev.StatusIcons.Add(iconPrototype);
    }

    private void OnGetPoliceStatusIcons(Entity<RMCPoliceStatusComponent> ent, ref GetStatusIconsEvent args)
    {
        if (!IsActive || ent.Comp.Status == RMCPoliceStatus.None)
            return;
        if (_prototype.TryIndex<SecurityIconPrototype>($"RMCPoliceIcon{ent.Comp.Status}", out var icon))
            args.StatusIcons.Add(icon);
    }
}
