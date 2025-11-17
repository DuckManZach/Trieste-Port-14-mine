using Content.Shared._TP.Falling;
using Content.Shared.Chasm;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;

namespace Content.Client._TP14.Falling;

public sealed class PlatformFallingVisualsSystem : EntitySystem
{

    [Dependency] private readonly AnimationPlayerSystem _anim = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    private readonly string _platformFallAnimationKey = "chasm_fall";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlatformFallingComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<PlatformFallingComponent, ComponentRemove>(OnComponentRemove);
    }

    private void OnComponentInit(EntityUid uid, PlatformFallingComponent component, ComponentInit args)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite) ||
            TerminatingOrDeleted(uid))
        {
            return;
        }

        component.OriginalScale = sprite.Scale;

        if (!TryComp<AnimationPlayerComponent>(uid, out var player))
            return;

        if (_anim.HasRunningAnimation(player, _platformFallAnimationKey))
            return;

        _anim.Play((uid, player), GetFallingAnimation(component), _platformFallAnimationKey);
    }

    private void OnComponentRemove(EntityUid uid, PlatformFallingComponent component, ComponentRemove args)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        _sprite.SetScale((uid, sprite), component.OriginalScale);

        if (!TryComp<AnimationPlayerComponent>(uid, out var player))
            return;

        if (_anim.HasRunningAnimation(player, _platformFallAnimationKey))
            _anim.Stop((uid, player), _platformFallAnimationKey);
    }

    private Animation GetFallingAnimation(PlatformFallingComponent component)
    {
        var length = component.AnimationTime;

        return new Animation()
        {
            Length = length,
            AnimationTracks =
            {
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Scale),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(component.OriginalScale, 0.0f),
                        new AnimationTrackProperty.KeyFrame(component.AnimationScale, length.Seconds),
                    },
                    InterpolationMode = AnimationInterpolationMode.Cubic
                }
            }
        };
    }
}
