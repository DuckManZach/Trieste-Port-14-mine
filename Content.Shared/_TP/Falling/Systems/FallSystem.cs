using System.Linq;
using System.Numerics;
using Content.Shared._TP.Falling;
using Content.Shared.ActionBlocker;
using Content.Shared.Climbing.Systems;
using Content.Shared.Damage;
using Content.Shared.Movement.Components;
using Content.Shared.Stunnable;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._TP.Falling.Systems;

public sealed class FallSystem : EntitySystem
{
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _xformSys = default!;
    [Dependency] private readonly ClimbSystem _climb = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;


    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TransformComponent, EntParentChangedMessage>(OnParentChanged);
    }

    public override void Update(float frameTime)
    {
        var fallingQuery = EntityQueryEnumerator<PlatformFallingComponent>();
        while (fallingQuery.MoveNext(out var uid, out var comp))
        {
            if (TryComp<JumpingComponent>(uid, out var jumping) && jumping.IsJumping)
                continue;

            if (_timing.CurTime < comp.NextDeletionTime)
                continue;

            _xformSys.SetMapCoordinates(uid, new MapCoordinates(new Vector2(0,0), Transform(comp.Destination).MapID));
            RemCompDeferred<PlatformFallingComponent>(uid);
        }
    }

    private void OnParentChanged(Entity<TransformComponent> ent, ref EntParentChangedMessage ev)
    {
        // A check that should fix the round-start crash/restart. - Cookie (FatherCheese)
        // If the entity is not initialized, or we're below 10 seconds, return.
        if (MetaData(ev.Entity).EntityLifeStage < EntityLifeStage.MapInitialized)
            return;

        TryFall(ent);
    }

    private void TryFall(EntityUid ent, bool playSound = true)
    {
        var map = Transform(ent).MapUid;

        //Check if the map has falling enabled
        if (!TryComp<TriesteAirspaceComponent>(map, out var airspace))
            return;

        //Check if they are currently on a grid
        if (Transform(ent).GridUid != null)
            return;

        if (_whitelistSystem.IsBlacklistPass(airspace.Exempt, ent))
            return;

        var fallingComp = EnsureComp<PlatformFallingComponent>(ent);
        if (playSound)
            _audio.PlayPredicted(fallingComp.FallingSound, Transform(ent).Coordinates, ent);
        fallingComp.Destination = airspace.Destination;
        fallingComp.NextDeletionTime = _timing.CurTime + fallingComp.DeletionTime;
    }
}

