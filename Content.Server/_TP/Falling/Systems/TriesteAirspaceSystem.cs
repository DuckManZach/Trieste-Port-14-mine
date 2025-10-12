using Content.Server._TP.Falling.Components;
using Content.Shared._TP.Falling;
using Robust.Shared.Random;

namespace Content.Server._TP.Falling.Systems;

public sealed class TriesteAirspaceSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<TriesteAirspaceComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<TriesteAirspaceComponent> ent, ref MapInitEvent args)
    {
        var possibleDestinations = new HashSet<EntityUid>();

        var query = EntityQueryEnumerator<FallingDestinationComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            possibleDestinations.Add(uid);
        }

        ent.Comp.Destination = _random.Pick(possibleDestinations);
    }
}
