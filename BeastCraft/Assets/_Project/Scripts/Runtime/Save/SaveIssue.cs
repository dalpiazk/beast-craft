namespace BeastCraft.Save
{
    /// <summary>What kind of problem a <see cref="SaveIssue"/> reports.</summary>
    public enum SaveIssueKind
    {
        /// <summary>A beast has no <see cref="OwnedBeast.BeastId"/>.</summary>
        MissingBeastId,

        /// <summary>Two beasts share a <see cref="OwnedBeast.BeastId"/>.</summary>
        DuplicateBeastId,

        /// <summary>A beast's species id is empty or not in the catalog.</summary>
        UnknownSpecies,

        /// <summary>A known or equipped skill id is not in the catalog.</summary>
        UnknownSkill,

        /// <summary>A known or equipped avatar passive id is not in the catalog.</summary>
        UnknownPassive,

        /// <summary>A held material id is empty or not in the catalog.</summary>
        UnknownMaterial,

        /// <summary>An equipped slot names an id the book has not learned.</summary>
        EquippedNotLearned,

        /// <summary>A book lists the same id as learned more than once.</summary>
        DuplicateSkill,

        /// <summary>A level, XP, tier, quantity or counter is outside its valid range.</summary>
        InvalidValue
    }

    /// <summary>
    /// One problem <see cref="SaveValidator"/> found in a loaded save. Issues are reports, not
    /// failures: the save still loads, and the game decides what to do (typically ignore the
    /// entry, e.g. a skill removed from the library, and log it).
    /// </summary>
    public class SaveIssue
    {
        public SaveIssue(SaveIssueKind kind, string path, string id, string message)
        {
            Kind = kind;
            Path = path;
            Id = id;
            Message = message;
        }

        public SaveIssueKind Kind;

        /// <summary>Where in the save, e.g. <c>Beasts[2].Skills.Known[0]</c>.</summary>
        public string Path;

        /// <summary>The offending id, when the issue is about one.</summary>
        public string Id;

        public string Message;

        public override string ToString()
        {
            return Kind + " at " + Path + ": " + Message;
        }
    }
}
