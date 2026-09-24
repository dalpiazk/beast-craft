using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Vfx;

namespace BeastCraft.Presentation.Playback
{
    /// <summary>One target of a <see cref="SkillBeat"/>: who, and the damage the skill's hits dealt it.</summary>
    public readonly struct BeatTarget
    {
        public BeatTarget(string unitId, int damage, bool crit)
        {
            UnitId = unitId;
            Damage = damage;
            Crit = crit;
        }

        public string UnitId { get; }

        /// <summary>The summed <see cref="DamageRoll.Amount"/> of every hit on this target (0 for a buff or heal).</summary>
        public int Damage { get; }

        /// <summary>Whether any of those hits was a critical.</summary>
        public bool Crit { get; }
    }

    /// <summary>
    /// One skill going off within a turn, as the viewer plays it: who cast what, of which element,
    /// at whom, for how much. Read from a <see cref="BattleTurnResult"/> after the fact; building
    /// beats changes nothing in the battle.
    /// </summary>
    public sealed class SkillBeat
    {
        public SkillBeat(string casterId, string skillId, string skillName, Element element, IReadOnlyList<BeatTarget> targets,
                         string primaryKey = null, IReadOnlyList<string> effectKeys = null)
        {
            CasterId = casterId;
            SkillId = skillId;
            SkillName = skillName;
            Element = element;
            Targets = targets ?? new BeatTarget[0];
            PrimaryKey = primaryKey;
            EffectKeys = effectKeys ?? new string[0];
        }

        public string CasterId { get; }

        public string SkillId { get; }

        public string SkillName { get; }

        public Element Element { get; }

        public IReadOnlyList<BeatTarget> Targets { get; }

        /// <summary>
        /// The skill's primary effect type for VFX (<see cref="VfxLibrary.PrimaryKey"/>): null for
        /// a damage skill (it looks like its element), else a heal, a taunt...
        /// </summary>
        public string PrimaryKey { get; }

        /// <summary>Every effect type the skill can apply (<see cref="VfxEffectKey"/>), each once: what its on-apply overlays may be.</summary>
        public IReadOnlyList<string> EffectKeys { get; }

        /// <summary>
        /// Every skill that fired on <paramref name="turn"/>, in firing order: a beast's fired slots
        /// (<see cref="BattleTurnResult.SkillOutcomes"/>), then the avatar's casts
        /// (<see cref="BattleTurnResult.AvatarActivations"/>). A unit that fired nothing gives none.
        /// </summary>
        public static List<SkillBeat> FromTurn(BattleTurnResult turn)
        {
            List<SkillBeat> beats = new List<SkillBeat>();
            if (turn == null || turn.Unit == null)
            {
                return beats;
            }

            foreach (BattleSkillOutcome outcome in turn.SkillOutcomes)
            {
                if (outcome != null && outcome.Fired && outcome.Activation != null)
                {
                    beats.Add(FromActivation(turn.Unit.Id, outcome.Activation));
                }
            }

            foreach (SkillActivation activation in turn.AvatarActivations)
            {
                if (activation != null)
                {
                    beats.Add(FromActivation(turn.Unit.Id, activation));
                }
            }

            return beats;
        }

        /// <summary>One activation as a beat: its targets in order, each with the damage its hits dealt.</summary>
        public static SkillBeat FromActivation(string casterId, SkillActivation activation)
        {
            SkillSO skill = activation.Skill;
            List<BeatTarget> targets = new List<BeatTarget>();
            List<string> order = new List<string>();
            Dictionary<string, int> damage = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, bool> crit = new Dictionary<string, bool>(StringComparer.Ordinal);

            foreach (BattleUnit target in activation.Targets ?? new BattleUnit[0])
            {
                if (target != null && !damage.ContainsKey(target.Id))
                {
                    order.Add(target.Id);
                    damage[target.Id] = 0;
                    crit[target.Id] = false;
                }
            }

            foreach (DamageHit hit in activation.Hits)
            {
                if (hit.Target == null)
                {
                    continue;
                }

                if (!damage.ContainsKey(hit.Target.Id))
                {
                    order.Add(hit.Target.Id);
                    damage[hit.Target.Id] = 0;
                    crit[hit.Target.Id] = false;
                }

                damage[hit.Target.Id] += hit.Roll.Amount;
                crit[hit.Target.Id] |= hit.Roll.IsCrit;
            }

            foreach (string id in order)
            {
                targets.Add(new BeatTarget(id, damage[id], crit[id]));
            }

            List<string> keys = new List<string>();
            foreach (SkillEffect effect in skill == null || skill.Effects == null ? new List<SkillEffect>() : skill.Effects)
            {
                string key = VfxLibrary.KeyOf(effect, skill.Element);
                if (key != null && !keys.Contains(key))
                {
                    keys.Add(key);
                }
            }

            return new SkillBeat(casterId, skill == null ? null : skill.SkillId, skill == null ? null : skill.DisplayName,
                                 skill == null ? Element.None : skill.Element, targets, VfxLibrary.PrimaryKey(skill), keys);
        }
    }
}
