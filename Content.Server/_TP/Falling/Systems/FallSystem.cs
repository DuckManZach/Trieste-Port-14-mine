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

            SubscribeLocalEvent<EntParentChangedMessage>(OnEntParentChanged);
        }

        public override void Update(float frameTime)
        {

            var fallingQuery = EntityQueryEnumerator<PlatformFallingComponent>();
            while (fallingQuery.MoveNext(out var uid, out var comp))
            {
                if (TryComp<JumpingComponent>(uid, out var jumping))
                {
                    RemCompDeferred<PlatformFallingComponent>(uid);
                    continue;
                }

                _transformSystem.SetCoordinates(uid, new EntityCoordinates(comp.Destination, Vector2.Zero));

                // Stuns the fall-ee for five seconds
                var stunTime = TimeSpan.FromSeconds(5);
                _stun.TryKnockdown(uid, stunTime, refresh: true);
                _stun.TryAddStunDuration(uid, stunTime);

                // Defines the damage being dealt
                var damage = new DamageSpecifier
                {
                    DamageDict = { ["Blunt"] = 80f }
                };
                _damageable.TryChangeDamage(uid, damage, origin: uid);

                // Causes a popup
                _popup.PopupEntity(Loc.GetString("fell-to-seafloor"), uid, PopupType.LargeCaution);

                // Randomly teleports you in a radius around the landing zone
                TeleportRandomly(uid);
            }
        }

        private void OnEntParentChanged(ref EntParentChangedMessage ev) // called when the entity changes parents
        {
            // A check that should fix the round-start crash/restart. - Cookie (FatherCheese)
            // If the entity is not initialized, or we're below 10 seconds, return.
            if (MetaData(ev.Entity).EntityLifeStage < EntityLifeStage.MapInitialized)
                return;

            // A check to see if the player jumped from one grid to
            // another, and if so, return, so they don't fall.
            if (ev.OldParent == null ||
                ev.Transform.GridUid != null ||
                TerminatingOrDeleted(ev.Entity))
                return;

            if (ExemptFromFalling(ev.Entity))
                return;

            if (!TryFall(ev.Entity))
                return;

            // Force stop climbing when entering airspace via parent change
            if (TryComp<ClimbingComponent>(ev.Entity, out var climbComp) && climbComp.IsClimbing)
                _climb.StopClimb(ev.Entity, climbComp);
        }

        private bool ExemptFromFalling(EntityUid uid)
        {
            if (!TryComp<TriesteAirspaceComponent>(Transform(uid).MapUid, out var airspace))
                return true;

            return _whitelistSystem.IsBlacklistPass(airspace.Exempt, uid);
        }

        private bool TryFall(EntityUid owner)
        {
            if (!TryComp<TriesteAirspaceComponent>(Transform(owner).MapUid, out var map))
                return true;

            if (map.Destination is not { } destination)
            {
                // If there's no destination, something broke
                Log.Error($"No valid falling sites available!");
                return false;
            }

            if (!EnsureComp<PlatformFallingComponent>(owner, out var fallingComp))
                return false;

            fallingComp.Destination = destination;

            return true;
        }

        private void TeleportRandomly(EntityUid owner)
        {
            var coords = Transform(owner).Coordinates;
            var newCoords = coords; // Start with the current coordinates

            for (var i = 0; i < MaxRandomTeleportAttempts; i++)
            {
                // Generate a random offset based on a defined radius
                // var offset = _random.NextVector2(component.MaxRandomRadius);
                // newCoords = coords.Offset(offset);

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
