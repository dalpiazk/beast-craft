using BeastCraft.Avatar;

namespace BeastCraft.Battle
{
    /// <summary>
    /// One avatar passive that fired: which one, from which slot, on which trigger and about which
    /// unit, and the <see cref="SkillActivation"/> its effects were applied through (its targets,
    /// and the damage hits it landed). Recorded on <see cref="BattleTurnResult.PassiveActivations"/>
    /// and <see cref="BattleResult.OpeningPassiveActivations"/>; read by
    /// <see cref="BattleSkillUsage.CountPassiveTriggers"/> for practice XP and by the simulator.
    /// <para>
    /// Only firings are recorded. A trigger that was blocked (spent, on cooldown, nobody to land on)
    /// or whose proc roll failed leaves no record. Purely a record: reading it changes nothing.
    /// </para>
    /// </summary>
    public class PassiveActivation
    {
        public PassiveActivation(PassiveInstance instance, int slotIndex, PassiveTrigger trigger, BattleUnit triggeringUnit, SkillActivation activation)
        {
            Instance = instance;
            SlotIndex = slotIndex;
            Trigger = trigger;
            TriggeringUnit = triggeringUnit;
            Activation = activation;
        }

        /// <summary>The passive, at the level and tier it fired at, with its per-battle state.</summary>
        public PassiveInstance Instance { get; }

        /// <summary>The authored passive (<see cref="PassiveInstance.Passive"/>).</summary>
        public PassiveSkillSO Passive
        {
            get { return Instance == null ? null : Instance.Passive; }
        }

        /// <summary>Its position in the <see cref="PassiveLoadout"/> (slot order, empty slots closed up).</summary>
        public int SlotIndex { get; }

        /// <summary>What fired it.</summary>
        public PassiveTrigger Trigger { get; }

        /// <summary>
        /// The unit the trigger was about (see the design doc, "Avatar passives"), or <c>null</c>
        /// for <see cref="PassiveTrigger.Aura"/>, <see cref="PassiveTrigger.BattleStart"/> and an
        /// <see cref="PassiveTrigger.EnemyDefeated"/> with no ally to credit.
        /// </summary>
        public BattleUnit TriggeringUnit { get; }

        /// <summary>The effects as applied: targets and damage hits. Never <c>null</c> for a recorded firing.</summary>
        public SkillActivation Activation { get; }
    }
}
