using System;
using BeastCraft.Customization;
using BeastCraft.Progression;

namespace BeastCraft.Save
{
    /// <summary>
    /// One beast in the player's collection, as save data: a stable per-beast id (two beasts of the
    /// same species are different beasts), its <see cref="BeastProgress"/> (species, level, XP) and
    /// its <see cref="BeastSkillBook"/> and its worn gear. Plain serializable data with public fields; see
    /// <see cref="PlayerSave"/>.
    /// </summary>
    [Serializable]
    public class OwnedBeast
    {
        /// <summary>
        /// Unique within one <see cref="PlayerSave"/>; how teams and other save data refer to this
        /// beast. Assigned once when the beast is obtained and never changed.
        /// </summary>
        public string BeastId;

        /// <summary>Species, level and XP.</summary>
        public BeastProgress Progress = new BeastProgress();

        /// <summary>Learned skills, their progress, and the equipped slots.</summary>
        public BeastSkillBook Skills = new BeastSkillBook();

        /// <summary>
        /// Worn beast gear, by slot: index <c>(int)GearSlot</c> holds a <see cref="OwnedGear.InstanceId"/>
        /// from <see cref="GearInventory.BeastGear"/>, or null/"" when empty. Change it through
        /// <see cref="GearRules"/>. Added in schema 2.
        /// </summary>
        public string[] EquippedGear = new string[GearRules.BeastSlotCount];

        /// <summary>
        /// This beast's chosen look, from its species' cosmetic categories (a category missing here
        /// reads as its default). Purely cosmetic, no stats; change it through <c>CosmeticRules</c>.
        /// Added in schema 4.
        /// </summary>
        public CustomizationSelection Appearance = new CustomizationSelection();

        /// <summary>A new beast: <paramref name="beastId"/> of <paramref name="speciesId"/> at <paramref name="level"/>, knowing nothing yet.</summary>
        public static OwnedBeast Create(string beastId, string speciesId, int level)
        {
            return new OwnedBeast
            {
                BeastId = beastId,
                Progress = new BeastProgress(speciesId, level)
            };
        }
    }
}
