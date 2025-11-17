using Content.Shared.Whitelist;

namespace Content.Shared._TP.Falling
{
    [RegisterComponent]
    public sealed partial class TriesteAirspaceComponent : Component
    {
        /// <summary>
        /// If it matches with this, it will be exempt from falling
        /// </summary>
        [DataField]
        public EntityWhitelist Exempt = new()
        {
            Components =
            [
                "Fultoned",
                "Ghost",
                "NoFTL",
                "CanMoveInAir",
                "Revenant",
            ],
        };

        /// <summary>
        /// UID of the map you will fall to
        /// </summary>
        [DataField]
        public EntityUid Destination;
    }
}
