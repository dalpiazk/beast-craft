using System;
using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using UnityEngine;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// The avatar the PvE runs can field (<c>--avatar</c>). <see cref="None"/> (the default) fields
    /// no avatar at all, exactly as before avatar support existed, so the committed report is
    /// unchanged. <see cref="Support"/> is a <strong>simple fixture</strong> for exercising the
    /// passive engine end to end — not authored content and not a balance claim.
    /// <see cref="Library"/> is the authored avatar: the skill library's default loadout (its first
    /// three actives and its <c>AvatarDefaultPassives</c>) at <c>--skill-level</c>.
    /// </summary>
    public sealed class AvatarPresets
    {
        /// <summary>No avatar: the default, and the committed report's setting.</summary>
        public const string None = "none";

        /// <summary>
        /// A passive-only support avatar: a crit aura, a shield on any beast that drops below 40% HP,
        /// and an attack surge on each enemy defeat. No active skills.
        /// </summary>
        public const string Support = "support";

        /// <summary>The skill library's default avatar loadout (actives and passives) at <c>--skill-level</c>.</summary>
        public const string Library = "library";

        /// <summary>Every accepted <c>--avatar</c> value, for parsing and usage text.</summary>
        public static readonly string[] Names = { None, Support, Library };

        private readonly List<PassiveSkillSO> _passives = new List<PassiveSkillSO>();
        private readonly List<SkillSO> _actives = new List<SkillSO>();
        private readonly SkillLibraryKits _library;

        /// <summary>
        /// Builds the preset's passives (and, for <see cref="Library"/>, actives) once; <see cref="None"/>
        /// has none. <paramref name="library"/> is required for <see cref="Library"/> and ignored otherwise.
        /// </summary>
        public AvatarPresets(string preset, SkillLibraryKits library = null)
        {
            Preset = preset ?? None;

            if (Preset == Library)
            {
                _library = library ?? throw new ArgumentException("The library avatar preset needs the skill library.", nameof(library));
                _passives.AddRange(library.AvatarPassives);
                _actives.AddRange(library.AvatarActives);
            }

            if (Preset == Support)
            {
                _passives.Add(Passive("sim_aura_crit", PassiveTrigger.Aura, PassiveTarget.AllAllies,
                                      new SkillEffect { EffectType = SkillEffectType.BuffStat, AffectedStat = StatType.CritChance, Magnitude = 5f }));

                PassiveSkillSO guard = Passive("sim_last_stand", PassiveTrigger.AllyBelowHpPercent, PassiveTarget.TriggeringUnit,
                                               new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Shield, Magnitude = 100f, DurationTurns = 2 });
                guard.HpThresholdPercent = 40;
                guard.InternalCooldown = 2;
                _passives.Add(guard);

                PassiveSkillSO surge = Passive("sim_victory_surge", PassiveTrigger.EnemyDefeated, PassiveTarget.AllAllies,
                                               new SkillEffect
                                               {
                                                   EffectType = SkillEffectType.BuffStat,
                                                   AffectedStat = StatType.Attack,
                                                   Magnitude = 10f,
                                                   IsPercent = true,
                                                   DurationTurns = 2,
                                               });
                surge.ProcChance = 50;
                _passives.Add(surge);
            }
        }

        /// <summary>The preset name.</summary>
        public string Preset { get; }

        /// <summary>Whether an avatar is fielded at all.</summary>
        public bool Enabled
        {
            get { return Preset != None; }
        }

        /// <summary>The preset's passives in slot order (empty for <see cref="None"/>).</summary>
        public IReadOnlyList<PassiveSkillSO> Passives
        {
            get { return _passives; }
        }

        /// <summary>The preset's active skills in slot order (only <see cref="Library"/> has any).</summary>
        public IReadOnlyList<SkillSO> Actives
        {
            get { return _actives; }
        }

        /// <summary>Whether the preset is authored content (<see cref="Library"/>) rather than a fixture.</summary>
        public bool IsAuthored
        {
            get { return Preset == Library; }
        }

        /// <summary>Whether <paramref name="name"/> is a known preset.</summary>
        public static bool IsKnown(string name)
        {
            return Array.IndexOf(Names, name) >= 0;
        }

        /// <summary>
        /// A fresh avatar and passive loadout for one battle at <paramref name="level"/>, or
        /// <c>null</c> (and a null loadout) for <see cref="None"/>. The avatar's stats are a flat
        /// fixture block that grows linearly with the battle level (roughly a mid-roster beast's
        /// Attack and Defense), since its shield scales off its Defense.
        /// </summary>
        public BattleUnit Build(int level, out PassiveLoadout passives)
        {
            if (!Enabled)
            {
                passives = null;
                return null;
            }

            List<PassiveInstance> instances = new List<PassiveInstance>();
            foreach (PassiveSkillSO passive in _passives)
            {
                instances.Add(_library == null ? new PassiveInstance(passive) : _library.PassiveInstance(passive));
            }

            passives = new PassiveLoadout(instances);
            SkillLoadout actives = null;
            if (_library != null)
            {
                List<SkillInstance> skills = new List<SkillInstance>();
                foreach (SkillSO skill in _actives)
                {
                    skills.Add(_library.Instance(skill));
                }

                actives = SkillLoadout.FromInstances(skills);
            }

            int stat = 10 + level;
            return BattleAvatar.Create(actives, new StatBlock(1, stat, stat, stat, stat, 0), null, BattleAvatar.DefaultId, level);
        }

        private static PassiveSkillSO Passive(string id, PassiveTrigger trigger, PassiveTarget scope, SkillEffect effect)
        {
            PassiveSkillSO passive = ScriptableObject.CreateInstance<PassiveSkillSO>();
            passive.PassiveId = id;
            passive.DisplayName = id;
            passive.Trigger = trigger;
            passive.TargetScope = scope;
            passive.Effects = new List<SkillEffect> { effect };
            return passive;
        }
    }
}
