namespace BeastCraft.Save
{
    /// <summary>
    /// The ids a save may legitimately refer to, for <see cref="SaveValidator"/>. The game answers
    /// from its loaded assets; <see cref="SaveContentCatalog"/> answers from id lists or the authored
    /// JSON data.
    /// </summary>
    public interface ISaveContentCatalog
    {
        /// <summary>Whether <paramref name="speciesId"/> is a known <c>CreatureSpeciesSO.SpeciesId</c>.</summary>
        bool IsKnownSpecies(string speciesId);

        /// <summary>Whether <paramref name="skillId"/> is a known <c>SkillSO.SkillId</c> (beast skill or avatar active).</summary>
        bool IsKnownSkill(string skillId);

        /// <summary>Whether <paramref name="passiveId"/> is a known <c>PassiveSkillSO.PassiveId</c>.</summary>
        bool IsKnownPassive(string passiveId);

        /// <summary>Whether <paramref name="materialId"/> is a known <c>SkillMaterialSO.MaterialId</c>.</summary>
        bool IsKnownMaterial(string materialId);
    }
}
