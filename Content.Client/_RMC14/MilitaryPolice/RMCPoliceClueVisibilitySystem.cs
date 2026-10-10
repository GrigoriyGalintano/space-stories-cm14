using Content.Shared._RMC14.MilitaryPolice;
using Robust.Client.GameObjects;

namespace Content.Client._RMC14.MilitaryPolice;

public sealed class RMCPoliceClueVisibilitySystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<RMCPoliceClueVisibilityComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<RMCPoliceClueVisibilityComponent, AfterAutoHandleStateEvent>(OnState);
    }

    private void OnStartup(Entity<RMCPoliceClueVisibilityComponent> ent, ref ComponentStartup args)
        => _sprite.SetVisible(ent.Owner, ent.Comp.Revealed);

    private void OnState(Entity<RMCPoliceClueVisibilityComponent> ent, ref AfterAutoHandleStateEvent args)
        => _sprite.SetVisible(ent.Owner, ent.Comp.Revealed);
}
