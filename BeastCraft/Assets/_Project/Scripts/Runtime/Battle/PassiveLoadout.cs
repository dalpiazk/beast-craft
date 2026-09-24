using System;
using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle.Grid;
using BeastCraft.Progression;

namespace BeastCraft.Battle
{
    /// <summary>
    /// The avatar's equipped passives for one battle, in slot order, and the trigger rules that
    /// fire them. Built fresh per battle (from the avatar's book with <see cref="FromBook"/>, or
    /// from instances), handed to <see cref="BattleTurnExecutor.RunBattle(TurnManager, IEnumerable{BattleUnit}, HexGrid, Random, BattleUnit, PassiveLoadout, int)"/>
    /// (or <see cref="BattleTurnExecutor.BeginBattle"/> and
    /// <see cref="BattleTurnExecutor.ExecuteTurn(BattleUnit, IEnumerable{BattleUnit}, HexGrid, Random, BattleUnit, PassiveLoadout)"/>),
    /// which call the hooks below at the moments the design names. See the battle-system design
    /// doc, "Avatar passives".
    /// <para>
    /// <strong>Firing.</strong> When a trigger happens, every passive with that trigger is tried in
    /// slot order. Each fires only if it is not spent (<see cref="PassiveSkillSO.MaxTriggersPerBattle"/>),
    /// is off its internal cooldown, finds at least one target, and passes its
    /// <see cref="PassiveSkillSO.ProcChance"/> roll — in that order, so the roll is the last check
    /// and the only draw. Its effects are then applied by <see cref="SkillEffectApplier"/> with the
    /// avatar as the caster, the defeated are lifted off the grid, and the firing is recorded.
    /// </para>
    /// <para>
    /// <strong>No chaining.</strong> A unit defeated by a passive's own effect is recorded as
    /// defeated silently: it never fires <see cref="PassiveTrigger.EnemyDefeated"/> or
    /// <see cref="PassiveTrigger.AllyDefeated"/>, and a passive's crits are not
    /// <see cref="PassiveTrigger.AllyCrit"/>s. That bounds the passives fired by any one event and
    /// keeps a passive from feeding itself.
    /// </para>
    /// </summary>
    public sealed class PassiveLoadout
    {
        private readonly List<PassiveInstance> _slots = new List<PassiveInstance>();
        private readonly HashSet<BattleUnit> _seenDefeated = new HashSet<BattleUnit>();

        /// <summary>
        /// The passives in priority order. Null instances and instances with no passive are skipped
        /// (the order closes up, as a <see cref="SkillLoadout"/>'s does); duplicates are not
        /// policed — the book forbids them.
        /// </summary>
        public PassiveLoadout(IEnumerable<PassiveInstance> passives)
        {
            if (passives == null)
            {
                return;
            }

            foreach (PassiveInstance passive in passives)
            {
                if (passive != null && passive.Passive != null)
                {
                    _slots.Add(passive);
                }
            }
        }

        /// <summary>
        /// The loadout for a passive book: each equipped slot in slot order, resolved by
        /// <paramref name="passiveLookup"/> (passive id to asset) and put in at the level and tier
        /// its <see cref="SkillProgress"/> records. Empty, unknown and unresolvable slots are
        /// skipped. A null book or lookup gives an empty loadout. Never <c>null</c>.
        /// </summary>
        public static PassiveLoadout FromBook(SkillBook passiveBook, Func<string, PassiveSkillSO> passiveLookup)
        {
            List<PassiveInstance> instances = new List<PassiveInstance>();

            if (passiveBook != null && passiveLookup != null)
            {
                for (int slot = 0; slot < passiveBook.SlotCount; slot++)
                {
                    string passiveId = passiveBook.GetEquipped(slot);
                    SkillProgress progress = passiveBook.GetProgress(passiveId);

                    if (progress == null)
                    {
                        continue;
                    }

                    PassiveSkillSO passive = passiveLookup(passiveId);

                    if (passive != null)
                    {
                        instances.Add(PassiveInstance.FromProgress(passive, progress));
                    }
                }
            }

            return new PassiveLoadout(instances);
        }

        /// <summary>The equipped passives in priority order. Read-only.</summary>
        public IReadOnlyList<PassiveInstance> Passives
        {
            get { return _slots; }
        }

        /// <summary>How many passives are equipped. Zero is legal and changes nothing about a battle.</summary>
        public int Count
        {
            get { return _slots.Count; }
        }

        /// <summary>Whether the battle-start hook has run. A loadout serves one battle.</summary>
        public bool HasBegun { get; private set; }

        /// <summary>
        /// The battle-start hook: remembers who is already defeated (they never trigger), then fires
        /// every <see cref="PassiveTrigger.Aura"/> passive in slot order, then every
        /// <see cref="PassiveTrigger.BattleStart"/> passive in slot order. Runs once; later calls
        /// do nothing.
        /// </summary>
        internal void Begin(BattleUnit avatar, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng, List<PassiveActivation> sink)
        {
            if (HasBegun)
            {
                return;
            }

            HasBegun = true;
            SyncDefeated(allUnits);
            FireAll(PassiveTrigger.Aura, null, avatar, allUnits, grid, rng, sink);
            FireAll(PassiveTrigger.BattleStart, null, avatar, allUnits, grid, rng, sink);
        }

        /// <summary>
        /// A player beast is starting its turn (after its damage-over-time, which it survived):
        /// every <see cref="PassiveTrigger.AllyTurnStart"/> passive, in slot order, with that beast
        /// as the triggering unit.
        /// </summary>
        internal void OnAllyTurnStart(BattleUnit ally, BattleUnit avatar, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng, List<PassiveActivation> sink)
        {
            FireAll(PassiveTrigger.AllyTurnStart, ally, avatar, allUnits, grid, rng, sink);
        }

        /// <summary>
        /// The after-damage hook, called after every application that can change HP outside a
        /// passive (a beast's skill, an avatar active, damage-over-time at a turn's start). Events
        /// are handled in this order, each trying its passives in slot order:
        /// <list type="number">
        /// <item><description>
        /// <see cref="PassiveTrigger.AllyCrit"/>: once per critical hit in
        /// <paramref name="beastActivation"/> (in hit order), when that activation was fired by
        /// <paramref name="turnUnit"/> and it is one of the player's beasts. The crit-landing beast
        /// is the triggering unit. Avatar actives and passives pass no activation, so their crits
        /// never count.
        /// </description></item>
        /// <item><description>
        /// Defeats: every roster unit defeated since the last look, in roster order —
        /// <see cref="PassiveTrigger.AllyDefeated"/> (the fallen beast is the triggering unit) or
        /// <see cref="PassiveTrigger.EnemyDefeated"/> (the triggering unit is
        /// <paramref name="turnUnit"/> when it is a living player beast — the beast whose turn it
        /// is gets the credit — and otherwise none, as on an enemy's or the avatar's own turn).
        /// </description></item>
        /// <item><description>
        /// <see cref="PassiveTrigger.AllyBelowHpPercent"/>: every living player beast, in roster
        /// order, now strictly below a passive's threshold that the passive has not yet reacted
        /// to. One attempt per crossing, whether or not it fires; seen at or above the threshold,
        /// the beast re-arms that passive.
        /// </description></item>
        /// </list>
        /// A no-op before <see cref="Begin"/>.
        /// </summary>
        internal void AfterApplication(BattleUnit turnUnit, SkillActivation beastActivation, BattleUnit avatar, IEnumerable<BattleUnit> allUnits, HexGrid grid,
                                       Random rng, List<PassiveActivation> sink)
        {
            if (!HasBegun || avatar == null || _slots.Count == 0)
            {
                return;
            }

            if (beastActivation != null && turnUnit != null && turnUnit.Team == avatar.Team)
            {
                IReadOnlyList<DamageHit> hits = beastActivation.Hits;

                for (int h = 0; h < hits.Count; h++)
                {
                    if (hits[h].Roll.IsCrit)
                    {
                        FireAll(PassiveTrigger.AllyCrit, turnUnit, avatar, allUnits, grid, rng, sink);
                    }
                }
            }

            List<BattleUnit> fallen = SyncDefeated(allUnits);

            for (int i = 0; i < fallen.Count; i++)
            {
                BattleUnit unit = fallen[i];

                if (unit.Team == avatar.Team)
                {
                    FireAll(PassiveTrigger.AllyDefeated, unit, avatar, allUnits, grid, rng, sink);
                }
                else
                {
                    BattleUnit credited = turnUnit != null && turnUnit.Team == avatar.Team && !turnUnit.IsDefeated ? turnUnit : null;
                    FireAll(PassiveTrigger.EnemyDefeated, credited, avatar, allUnits, grid, rng, sink);
                }
            }

            CheckThresholds(avatar, allUnits, grid, rng, sink);
        }

        /// <summary>
        /// Notes every defeated roster unit not yet seen without firing anything: a unit defeated by
        /// something that must not trigger passives (a team bond's reaction) never fires
        /// <see cref="PassiveTrigger.EnemyDefeated"/> or <see cref="PassiveTrigger.AllyDefeated"/>.
        /// </summary>
        internal void SyncDefeatedSilently(IEnumerable<BattleUnit> allUnits)
        {
            SyncDefeated(allUnits);
        }

        /// <summary>One avatar turn off every passive's internal cooldown (once per avatar turn, before its actives).</summary>
        internal void TickCooldowns()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                _slots[i].TickCooldown();
            }
        }

        private void CheckThresholds(BattleUnit avatar, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng, List<PassiveActivation> sink)
        {
            bool any = false;

            for (int s = 0; s < _slots.Count; s++)
            {
                any |= _slots[s].Trigger == PassiveTrigger.AllyBelowHpPercent;
            }

            if (!any || allUnits == null)
            {
                return;
            }

            List<BattleUnit> allies = new List<BattleUnit>();

            foreach (BattleUnit unit in allUnits)
            {
                if (unit != null && unit.Team == avatar.Team)
                {
                    allies.Add(unit);
                }
            }

            for (int u = 0; u < allies.Count; u++)
            {
                BattleUnit ally = allies[u];

                for (int s = 0; s < _slots.Count; s++)
                {
                    PassiveInstance passive = _slots[s];

                    if (passive.Trigger != PassiveTrigger.AllyBelowHpPercent || ally.IsDefeated)
                    {
                        continue;
                    }

                    if (!IsBelow(ally, passive.HpThresholdPercent))
                    {
                        passive.BelowThreshold.Remove(ally);
                    }
                    else if (passive.BelowThreshold.Add(ally))
                    {
                        TryFire(s, PassiveTrigger.AllyBelowHpPercent, ally, avatar, allUnits, grid, rng, sink);
                    }
                }
            }
        }

        /// <summary>
        /// Whether a unit is strictly below <paramref name="thresholdPercent"/> of its max HP, in
        /// 64-bit integers (<c>CurrentHp * 100 &lt; threshold * Stats.Hp</c>).
        /// </summary>
        private static bool IsBelow(BattleUnit unit, int thresholdPercent)
        {
            return (long)unit.CurrentHp * 100 < (long)thresholdPercent * unit.Stats.Hp;
        }

        private void FireAll(PassiveTrigger trigger, BattleUnit triggeringUnit, BattleUnit avatar, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng,
                             List<PassiveActivation> sink)
        {
            for (int s = 0; s < _slots.Count; s++)
            {
                if (_slots[s].Trigger == trigger)
                {
                    TryFire(s, trigger, triggeringUnit, avatar, allUnits, grid, rng, sink);
                }
            }
        }

        /// <summary>
        /// One passive's attempt: spent or on cooldown → nothing; no targets → nothing; proc roll
        /// (a draw only below 100) fails → nothing; otherwise apply, lift the defeated, count the
        /// firing, start the cooldown, silently note anyone it defeated, and record it.
        /// </summary>
        private void TryFire(int slot, PassiveTrigger trigger, BattleUnit triggeringUnit, BattleUnit avatar, IEnumerable<BattleUnit> allUnits, HexGrid grid,
                             Random rng, List<PassiveActivation> sink)
        {
            PassiveInstance passive = _slots[slot];

            if (avatar == null || avatar.IsDefeated || !passive.IsReady)
            {
                return;
            }

            IReadOnlyList<BattleUnit> targets = passive.ResolveTargets(avatar, allUnits, triggeringUnit, rng);

            if (targets.Count == 0 || !SkillEffectApplier.RollChance(passive.ProcChance, rng))
            {
                return;
            }

            SkillActivation activation = new SkillActivation(passive.Skill, targets);
            SkillEffectApplier.Apply(activation, avatar, rng, grid);
            BattleTurnExecutor.LiftDefeated(allUnits, grid);
            passive.MarkTriggered();
            SyncDefeated(allUnits);

            if (sink != null)
            {
                sink.Add(new PassiveActivation(passive, slot, trigger, triggeringUnit, activation));
            }
        }

        /// <summary>
        /// Notes every defeated roster unit not yet seen, and returns those, in roster order. Units
        /// noted here never trigger a defeat passive afterwards.
        /// </summary>
        private List<BattleUnit> SyncDefeated(IEnumerable<BattleUnit> allUnits)
        {
            List<BattleUnit> fallen = new List<BattleUnit>();

            if (allUnits == null)
            {
                return fallen;
            }

            foreach (BattleUnit unit in allUnits)
            {
                if (unit != null && unit.IsDefeated && _seenDefeated.Add(unit))
                {
                    fallen.Add(unit);
                }
            }

            return fallen;
        }
    }
}
