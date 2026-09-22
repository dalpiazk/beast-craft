using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;

namespace BeastCraft.Battle
{
    /// <summary>
    /// One caster's equipped skills, in the order they were authored, plus the live cooldown
    /// counter behind each slot.
    /// <para>
    /// This is the rotation the producer confirmed: a caster equips a fixed, authored <em>stack</em>
    /// of skills and does not choose between them at runtime. Every counter starts at its own
    /// skill's <see cref="SkillSO.Cooldown"/>, every counter ticks down by 1 each time the owner
    /// takes a turn, and any counter that lands on exactly 0 fires that turn and immediately resets
    /// to its authored cooldown. Several skills may therefore fire on the same turn, and they do so
    /// in stack order.
    /// </para>
    /// <para>
    /// Deliberately owner-agnostic. <see cref="Tick"/> knows nothing about the board, the roster or
    /// even <see cref="BattleUnit"/>, so the player's avatar — which is not a grid piece and has no
    /// <see cref="HexCoordinate"/> — can drive the identical rotation once its timing is wired up.
    /// The confirmed rule for that is that the avatar's loadout ticks once per <em>player-side
    /// beast turn</em> (three player beasts means three avatar ticks per round), which is not
    /// implemented here; see the battle-system design doc.
    /// </para>
    /// <para>
    /// Cooldowns only. This does not spend <see cref="SkillSO.ResourceCost"/>, apply
    /// <see cref="SkillSO.Effects"/>, move anybody, or decide when a turn happens — the turn-executor
    /// pass that calls this is still to come.
    /// </para>
    /// </summary>
    public class SkillLoadout
    {
        private readonly List<SkillSO> _skills;
        private readonly List<int> _authoredCooldowns;
        private readonly List<int> _counters;

        /// <summary>
        /// Builds a loadout from an ordered stack of skills. Each slot's counter starts at that
        /// skill's own <see cref="SkillSO.Cooldown"/>, so nothing fires before it has counted down
        /// at least once — there is no turn-one alpha strike.
        /// <para>
        /// A <c>Cooldown</c> of 0 and a <c>Cooldown</c> of 1 both end up firing every turn (0 starts
        /// already at zero, 1 reaches zero on the first tick). That is intended, not a degenerate
        /// case to special-case away.
        /// </para>
        /// <para>
        /// Defensive, never throwing: a null stack and null entries are simply skipped, and a
        /// negative authored cooldown clamps to 0 rather than counting upward forever.
        /// </para>
        /// <para>
        /// The authored value is captured here rather than re-read from the asset on every reset, so
        /// a battle in progress cannot change its own pacing if the <see cref="SkillSO"/> is edited
        /// mid-play (which the Unity editor makes possible) — the same determinism concern that
        /// drives the id-based tie-breaks in <see cref="TurnManager"/> and
        /// <see cref="SkillTargetResolver"/>.
        /// </para>
        /// </summary>
        public SkillLoadout(IEnumerable<SkillSO> skills)
        {
            _skills = new List<SkillSO>();
            _authoredCooldowns = new List<int>();
            _counters = new List<int>();

            if (skills == null)
            {
                return;
            }

            foreach (SkillSO skill in skills)
            {
                if (skill == null)
                {
                    continue;
                }

                int cooldown = skill.Cooldown < 0 ? 0 : skill.Cooldown;

                _skills.Add(skill);
                _authoredCooldowns.Add(cooldown);
                _counters.Add(cooldown);
            }
        }

        /// <summary>
        /// The equipped skills in authored stack order. Read-only: the stack is fixed for the
        /// duration of a battle, and the order is what fixes the fire order.
        /// </summary>
        public IReadOnlyList<SkillSO> Skills
        {
            get { return _skills; }
        }

        /// <summary>How many slots are equipped. Zero is legal; an empty loadout simply never fires.</summary>
        public int Count
        {
            get { return _skills.Count; }
        }

        /// <summary>
        /// Turns left on one slot's counter, for UI and debugging. Out-of-range indices return 0
        /// rather than throwing, matching the rest of this namespace's non-throwing stance.
        /// <para>
        /// Keyed by <em>slot index</em>, not by <see cref="SkillSO"/>, because the same skill may be
        /// equipped in more than one slot and each copy runs its own counter.
        /// </para>
        /// </summary>
        public int RemainingCooldown(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < _counters.Count ? _counters[slotIndex] : 0;
        }

        /// <summary>
        /// Advances the whole rotation by one turn and reports which skills fire as a result.
        /// <para>
        /// Every slot's counter drops by 1, clamped at 0 so it never goes negative. Any slot whose
        /// counter is 0 after that drop fires: it is appended to the returned list and its counter
        /// is reset to its authored cooldown in the same pass, so firing and re-arming are one
        /// operation and a caller cannot forget the second half. A slot already sitting at 0 before
        /// the tick (an authored cooldown of 0, or 1) therefore fires again this tick, which is the
        /// intended "every turn" behaviour.
        /// </para>
        /// <para>
        /// The returned list is in stack order, which is what makes a multi-skill turn deterministic:
        /// when two counters reach 0 together, the earlier-equipped skill resolves first.
        /// </para>
        /// <para>
        /// Returns an empty list — never <c>null</c> — when nothing is ready, including for an empty
        /// loadout. Pure cooldown bookkeeping: no targeting, no board, no roster.
        /// </para>
        /// </summary>
        public IReadOnlyList<SkillSO> Tick()
        {
            List<SkillSO> ready = new List<SkillSO>();

            for (int i = 0; i < _skills.Count; i++)
            {
                int counter = _counters[i] > 0 ? _counters[i] - 1 : 0;

                if (counter == 0)
                {
                    ready.Add(_skills[i]);
                    counter = _authoredCooldowns[i];
                }

                _counters[i] = counter;
            }

            return ready;
        }

        /// <summary>
        /// A caster's whole turn of casting: tick the rotation, then resolve targets for each skill
        /// that came up ready, in the order they fired.
        /// <para>
        /// This lives on the loadout because the loadout is the only type that owns both halves of
        /// the answer — which skills fire and in what order. <see cref="BattleUnit"/> is a
        /// deliberately minimal data record with no battle logic on it, and
        /// <see cref="SkillTargetResolver"/> documents itself as targeting only, explicitly
        /// disclaiming the choice of which equipped skill fires. <paramref name="caster"/> is passed
        /// straight through to the resolver rather than read for anything the loadout decides, so
        /// this stays a thin join of the two systems.
        /// </para>
        /// <para>
        /// A null or defeated caster yields no activations <em>and does not tick</em>: a unit that is
        /// out of the fight does not take a turn, so its rotation should not advance behind its
        /// back. That check is made here rather than inside <see cref="Tick"/>, which stays
        /// owner-agnostic; <see cref="SkillTargetResolver.ResolveTargets"/> makes the same check for
        /// its own sake, but on its own it would return an activation with an empty target list
        /// rather than no activation at all.
        /// </para>
        /// <para>
        /// A skill that fires and hits nothing still produces an activation with an empty
        /// <see cref="SkillActivation.Targets"/>: it fired, it reset its cooldown, and it whiffed.
        /// Callers that only care about hits can skip those.
        /// </para>
        /// <para>
        /// Takes exactly what <see cref="SkillTargetResolver.ResolveTargets"/> needs and nothing
        /// more. <paramref name="rng"/> is supplied by the caller for the same reproducibility
        /// reason the resolver documents.
        /// </para>
        /// </summary>
        public IReadOnlyList<SkillActivation> TickAndResolve(BattleUnit caster, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng)
        {
            List<SkillActivation> activations = new List<SkillActivation>();

            if (caster == null || caster.IsDefeated)
            {
                return activations;
            }

            IReadOnlyList<SkillSO> ready = Tick();

            for (int i = 0; i < ready.Count; i++)
            {
                IReadOnlyList<BattleUnit> targets = SkillTargetResolver.ResolveTargets(ready[i], caster, allUnits, grid, rng);
                activations.Add(new SkillActivation(ready[i], targets));
            }

            return activations;
        }
    }
}
