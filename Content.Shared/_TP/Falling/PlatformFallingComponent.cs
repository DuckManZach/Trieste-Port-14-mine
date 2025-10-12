namespace Content.Shared._TP.Falling;

/// <summary>
/// This is used for...
/// </summary>
[RegisterComponent]
public sealed partial class PlatformFallingComponent : Component
{
    /// <summary>
    ///     Time it should take for the falling animation (scaling down) to complete.
    /// </summary>
    [DataField]
    public TimeSpan AnimationTime = TimeSpan.FromSeconds(1.5f);
}
