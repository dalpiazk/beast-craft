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
        InvalidValue,

        /// <summary>An owned gear instance has an empty gear id or one not in the gear catalog.</summary>
        UnknownGear,

        /// <summary>A gear instance has no instance id, or shares one with another instance.</summary>
        DuplicateGearInstance,

        /// <summary>An equip slot names an instance the right gear inventory list does not hold.</summary>
        UnknownGearInstance,

        /// <summary>A gear instance is worn in more than one slot or by more than one owner.</summary>
        DoubleEquippedGear,

        /// <summary>A gear instance is worn in a slot other than its gear's own.</summary>
        GearSlotMismatch,

        /// <summary>A beast wears gear whose minimum level is above its own (the gear has no effect).</summary>
        GearLevelTooLow
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
