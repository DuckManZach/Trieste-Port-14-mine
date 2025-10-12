using System.Linq;
using System.Numerics;
using Content.Server._TP.Falling.Components;
using Content.Server.Popups;
using Content.Shared._TP.Falling;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Systems;
using Content.Shared.Damage;
using Content.Shared.Ghost;
using Content.Shared.Movement.Components;
using Content.Shared.Popups;
using Content.Shared.Revenant.Components;
using Content.Shared.Salvage.Fulton;
using Content.Shared.Shuttles.Components;
using Content.Shared.Stunnable;
using Content.Shared.Whitelist;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._TP.Falling.Systems
{
    public sealed class FallSystem : EntitySystem
    {
        [Dependency] private readonly SharedStunSystem _stun = default!;
        [Dependency] private readonly DamageableSystem _damageable = default!;
        [Dependency] private readonly PopupSystem _popup = default!;
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly EntityLookupSystem _lookup = default!;
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
        [Dependency] private readonly ClimbSystem _climb = default!;
        [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;

        private const int MaxRandomTeleportAttempts = 20; // The # of times it's going to try to find a valid spot to randomly teleport an object

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<FallSystemComponent, EntParentChangedMessage>(OnEntParentChanged);
        }

        public override void Update(float frameTime)
        {
            // First we start with an entity enumerator for the JumpingComponent (aka players)
            // If it passes, start a while loop and get the UID/component.
            // If the player is on a grid, run 'continue' to skip falling.
            var jumpingQuery = EntityQueryEnumerator<JumpingComponent>();
            while (jumpingQuery.MoveNext(out var uid, out var jumpComp))
            {
                var transform = _transformSystem.GetGrid(uid);
                if (transform != null)
                {
                    continue;
                }

                // Now check if the entity WAS jumping.
                // If it was, and it's not jumping anymore, it will fall.
                // At the end we set wasJumping to IsJumping.
                var entityParent = _transformSystem.GetParentUid(uid);
                if (HasComp<TriesteAirspaceComponent>(entityParent) &&
                    jumpComp is { IsJumping: false, WasJumping: true })
                {
                    if (TryComp<FallSystemComponent>(uid, out var fallSystemComponent))
                        TryFall(uid, fallSystemComponent);
                }

                jumpComp.WasJumping = jumpComp.IsJumping;
            }

            // This part catches if the player has climbed over the railing
            // and will force them to stop (and fall)!
            var fallQuery = EntityQueryEnumerator<FallSystemComponent>();
            while (fallQuery.MoveNext(out var uid, out var fallComp))
            {
                // Skip if already handled by jumping logic above
                if (HasComp<JumpingComponent>(uid))
                    continue;

                var transform = _transformSystem.GetGrid(uid);
                if (transform != null)
                    continue; // Still on a grid, don't fall

                var entityParent = _transformSystem.GetParentUid(uid);
                if (HasComp<TriesteAirspaceComponent>(entityParent))
                {
                    // Check if they should be exempt from falling
                    if (ExemptFromFalling(uid))
                        continue;

                    // Now check if they've been knocked down (aka slipped)
                    if (TryComp<KnockedDownComponent>(uid, out _))
                        TryFall(uid, fallComp);

                    // Force stop climbing if they're in airspace
                    if (TryComp<ClimbingComponent>(uid, out var climbComp) && climbComp.IsClimbing)
                        _climb.StopClimb(uid, climbComp);

                    TryFall(uid, fallComp);
                }
            }
        }

        private bool ExemptFromFalling(EntityUid uid)
        {
            if (!TryComp<TriesteAirspaceComponent>(Transform(uid).MapUid, out var airspace))
                return true;

            return _whitelistSystem.IsBlacklistPass(airspace.Exempt, uid);
        }

        private void OnEntParentChanged(Entity<FallSystemComponent> ent, ref EntParentChangedMessage args) // called when the entity changes parents
        {
            // A check that should fix the round-start crash/restart. - Cookie (FatherCheese)
            // If the entity is not initialized, or we're below 10 seconds, return.
            if (MetaData(ent).EntityLifeStage < EntityLifeStage.MapInitialized)
                return;

            // A check to see if the player jumped from one grid to
            // another, and if so, return, so they don't fall.
            if (args.OldParent == null ||
                args.Transform.GridUid != null ||
                TerminatingOrDeleted(ent.Owner))
                return;

            if (ExemptFromFalling(ent.Owner))
                return;

            if (!TryFall(ent.Owner, ent.Comp))
                return;

            // Force stop climbing when entering airspace via parent change
            if (TryComp<ClimbingComponent>(ent.Owner, out var climbComp) && climbComp.IsClimbing)
                _climb.StopClimb(ent.Owner, climbComp);
        }

        private bool TryFall(EntityUid owner, FallSystemComponent component)
        {
            if (!TryComp<TriesteAirspaceComponent>(Transform(owner).MapUid, out var map))
                return true;

            if (map.Destination is not { } destination)
            {
                // If there's no destination, something broke
                Log.Error($"No valid falling sites available!");
                return false;
            }

            _transformSystem.SetCoordinates(owner, new EntityCoordinates(destination, Vector2.Zero));

            // Stuns the fall-ee for five seconds
            var stunTime = TimeSpan.FromSeconds(5);
            _stun.TryKnockdown(owner, stunTime, refresh: true);
            _stun.TryAddStunDuration(owner, stunTime);

            // Defines the damage being dealt
            var damage = new DamageSpecifier
            {
                DamageDict = { ["Blunt"] = 80f }
            };
            _damageable.TryChangeDamage(owner, damage, origin: owner);

            // Causes a popup
            _popup.PopupEntity(Loc.GetString("fell-to-seafloor"), owner, PopupType.LargeCaution);

            // Randomly teleports you in a radius around the landing zone
            TeleportRandomly(owner, component);

            return true;
        }

        private void TeleportRandomly(EntityUid owner, FallSystemComponent component)
        {
            var coords = Transform(owner).Coordinates;
            var newCoords = coords; // Start with the current coordinates

            for (var i = 0; i < MaxRandomTeleportAttempts; i++)
            {
                // Generate a random offset based on a defined radius
                var offset = _random.NextVector2(component.MaxRandomRadius);
                newCoords = coords.Offset(offset);

                // Check if the new coordinates are free of static entities
                if (!_lookup.GetEntitiesIntersecting(newCoords.ToMap(EntityManager, _transformSystem), LookupFlags.Static).Any())
                {
                    break; // Found a valid location
                }
            }

            // Set the new coordinates to teleport the entity
            _transformSystem.SetCoordinates(owner, newCoords);
        }
    }
}
