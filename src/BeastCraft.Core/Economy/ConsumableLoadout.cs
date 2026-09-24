using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Save;

namespace BeastCraft.Economy
{
    /// <summary>
    /// Uses consumables as a battle begins — before the team's bonds and the avatar's opening
    /// passives, which then see the boosted stats. For a <see cref="ConsumableRecipients.Team"/>
    /// consumable every living team beast, in team order, applies the effects to itself (caster and
    /// only target, as a bond does: a shield scales with the beast's own Defense, a percent buff with
    /// its own stat); for <see cref="ConsumableRecipients.Enemies"/> the team's first living beast
    /// applies them to every living enemy at once (hostile: each effect's chance less the enemy's
    /// status resist). Through a cached <c>Self</c>- or enemy-side carrier <see cref="SkillSO"/> at
    /// level 1, like <c>TeamBondLoadout</c>. The only randomness is the rng handed in (the session
    /// seeds it <c>LootRoller.DeriveSeed(battleSeed, PostBattleAward.ConsumableStream)</c>, so a
    /// battle without consumables is exactly the battle it always was).
    /// </summary>
    public static class ConsumableLoadout
    {
        /// <summary>At most this many consumables per battle (the lead's decision: one).</summary>
        public const int MaxPerBattle = 1;

        private static readonly ConditionalWeakTable<ConsumableSO, SkillSO> Carriers = new ConditionalWeakTable<ConsumableSO, SkillSO>();
        private static readonly object CarrierLock = new object();

        /// <summary>Applies <paramref name="consumables"/> in order (see the class remarks). Nulls are skipped.</summary>
        public static void Apply(IEnumerable<ConsumableSO> consumables, IReadOnlyList<BattleUnit> team, IReadOnlyList<BattleUnit> enemies, HexGrid grid, System.Random rng)
        {
            if (consumables == null || team == null)
            {
                return;
            }

            foreach (ConsumableSO consumable in consumables)
            {
                if (consumable == null)
                {
                    continue;
                }

                SkillSO carrier = CarrierFor(consumable);
                if (consumable.Recipients == ConsumableRecipients.Team)
                {
                    foreach (BattleUnit unit in team)
                    {
                        if (unit != null && !unit.IsDefeated)
                        {
                            SkillEffectApplier.Apply(new SkillActivation(new SkillInstance(carrier), new[] { unit }), unit, rng, grid);
                        }
                    }

                    continue;
                }

                BattleUnit caster = null;
                foreach (BattleUnit unit in team)
                {
                    if (unit != null && !unit.IsDefeated)
                    {
                        caster = unit;
                        break;
                    }
                }

                List<BattleUnit> targets = new List<BattleUnit>();
                foreach (BattleUnit unit in enemies ?? new List<BattleUnit>())
                {
                    if (unit != null && !unit.IsDefeated)
                    {
                        targets.Add(unit);
                    }
                }

                if (caster != null && targets.Count > 0)
                {
                    SkillEffectApplier.Apply(new SkillActivation(new SkillInstance(carrier), targets), caster, rng, grid);
                }
            }
        }

        /// <summary>
        /// Whether the team may use <paramref name="consumableIds"/> this battle: at most
        /// <see cref="MaxPerBattle"/>, no repeats, each known to <paramref name="lookup"/> and held
        /// in <paramref name="save"/>. Adds a reason per problem to <paramref name="errors"/>.
        /// </summary>
        public static bool Check(PlayerSave save, IReadOnlyList<string> consumableIds, Func<string, ConsumableSO> lookup, List<string> errors)
        {
            int before = errors.Count;
            if (consumableIds == null || consumableIds.Count == 0)
            {
                return true;
            }

            if (consumableIds.Count > MaxPerBattle)
            {
                errors.Add("At most " + MaxPerBattle + " consumable can be used per battle; " + consumableIds.Count + " were chosen.");
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in consumableIds)
            {
                if (!seen.Add(id ?? string.Empty))
                {
                    errors.Add("Consumable '" + id + "' is chosen twice.");
                }
                else if (lookup == null || lookup(id) == null)
                {
                    errors.Add("Unknown consumable '" + id + "'.");
                }
                else if (ConsumableInventory.Quantity(save, id) < 1)
                {
                    errors.Add("Consumable '" + id + "' is not held.");
                }
            }

            return errors.Count == before;
        }

        /// <summary>The cached carrier skill of <paramref name="consumable"/>: its effects, Self-shaped for the team, enemy-side for enemies.</summary>
        public static SkillSO CarrierFor(ConsumableSO consumable)
        {
            lock (CarrierLock)
            {
                if (Carriers.TryGetValue(consumable, out SkillSO carrier) && carrier != null)
                {
                    return carrier;
                }

                Carriers.Remove(consumable);
                carrier = new SkillSO();
                carrier.name = consumable.name;
                carrier.SkillId = consumable.ConsumableId;
                carrier.DisplayName = consumable.DisplayName;
                carrier.Effects = consumable.Effects ?? new List<SkillEffect>();
                bool team = consumable.Recipients == ConsumableRecipients.Team;
                carrier.TargetShape = team ? SkillTargetShape.Self : SkillTargetShape.SingleTarget;
                carrier.TargetSide = team ? SkillTargetSide.Ally : SkillTargetSide.Enemy;
                carrier.Range = 0;
                Carriers.Add(consumable, carrier);
                return carrier;
            }
        }
    }

    /// <summary>
    /// The pack (<see cref="PlayerSave.Consumables"/>): how many of each consumable are held, capped
    /// at each one's max stack. Non-throwing; a refused change changes nothing.
    /// </summary>
    public static class ConsumableInventory
    {
        /// <summary>How many of <paramref name="consumableId"/> are held.</summary>
        public static int Quantity(PlayerSave save, string consumableId)
        {
            ConsumableStack stack = Find(save, consumableId);
            return stack == null ? 0 : Math.Max(0, stack.Quantity);
        }

        /// <summary>Whether <paramref name="quantity"/> more fit under <paramref name="maxStack"/>.</summary>
        public static bool CanAdd(PlayerSave save, string consumableId, int quantity, int maxStack)
        {
            return save != null && !string.IsNullOrEmpty(consumableId) && quantity > 0 && Quantity(save, consumableId) + quantity <= maxStack;
        }

        /// <summary>Adds <paramref name="quantity"/> when they fit under <paramref name="maxStack"/>. False (nothing added) otherwise.</summary>
        public static bool TryAdd(PlayerSave save, string consumableId, int quantity, int maxStack)
        {
            if (!CanAdd(save, consumableId, quantity, maxStack))
            {
                return false;
            }

            ConsumableStack stack = Find(save, consumableId);
            if (stack == null)
            {
                if (save.Consumables == null)
                {
                    save.Consumables = new List<ConsumableStack>();
                }

                save.Consumables.Add(new ConsumableStack(consumableId, quantity));
            }
            else
            {
                stack.Quantity += quantity;
            }

            return true;
        }

        /// <summary>Removes <paramref name="quantity"/> when held (a stack at 0 is dropped). False (nothing removed) otherwise.</summary>
        public static bool TryRemove(PlayerSave save, string consumableId, int quantity)
        {
            ConsumableStack stack = Find(save, consumableId);
            if (stack == null || quantity <= 0 || stack.Quantity < quantity)
            {
                return false;
            }

            stack.Quantity -= quantity;
            if (stack.Quantity == 0)
            {
                save.Consumables.Remove(stack);
            }

            return true;
        }

        private static ConsumableStack Find(PlayerSave save, string consumableId)
        {
            if (save == null || save.Consumables == null || string.IsNullOrEmpty(consumableId))
            {
                return null;
            }

            foreach (ConsumableStack stack in save.Consumables)
            {
                if (stack != null && string.Equals(stack.ConsumableId, consumableId, StringComparison.Ordinal))
                {
                    return stack;
                }
            }

            return null;
        }
    }
}
