namespace BeastCraft.Battle.Grid
{
    /// <summary>
    /// The fixed battlefield size an encounter uses. Every encounter picks exactly one of these
    /// three presets; there is no single global arena size.
    /// </summary>
    public enum ArenaSize
    {
        /// <summary>Tight board, suited to duels and corridor-flavoured encounters.</summary>
        Small = 0,

        /// <summary>Default board for standard squad encounters.</summary>
        Medium = 1,

        /// <summary>Wide board, for set-piece and boss encounters that want room to manoeuvre.</summary>
        Large = 2
    }
}
