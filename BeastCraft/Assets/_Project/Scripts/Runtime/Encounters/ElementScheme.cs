namespace BeastCraft.Encounters
{
    /// <summary>How a generated encounter's enemies got their elements.</summary>
    public enum ElementScheme
    {
        /// <summary>The whole enemy team shares one element.</summary>
        Uniform = 0,

        /// <summary>Each enemy type in the encounter gets its own element (units of a type share it).</summary>
        PerType = 1,

        /// <summary>Every unit gets its own element: fully mixed.</summary>
        PerUnit = 2,

        /// <summary>No enemy has an element.</summary>
        None = 3
    }
}
