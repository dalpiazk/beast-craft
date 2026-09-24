using System;
using System.Collections.Generic;
using BeastCraft.Battle;

namespace BeastCraft.Bonds
{
    /// <summary>
    /// A behaviour bond's in-battle reaction: when <see cref="Trigger"/> happens, one of the bond's
    /// members (the reactor) does something about it — steps in front of a hit, returns fire,
    /// cleanses a stunned ally. Carried by a <see cref="TeamBondTier"/> beside (or instead of) its
    /// battle-start <see cref="TeamBondTier.Effects"/>; <see cref="TeamBondLoadout"/> runs it and
    /// the battle-system design doc, "Team bonds", is the reference.
    /// <para>
    /// <strong>Who reacts.</strong> The bond's members, tried in <see cref="ReactorOrder"/>; the
    /// first that is alive, not stunned, in <see cref="Range"/>, off its <see cref="Cooldown"/>
    /// and under every cap reacts, and only then is <see cref="Chance"/> rolled (one draw, only
    /// below 100). One reaction per bond per event. A reaction's own hits, crits and defeats never
    /// trigger anything (no chaining).
    /// </para>
    /// <para>
    /// Inert at its defaults (<see cref="BondTrigger.None"/>), because Unity zero-fills a
    /// serializable field rather than leaving it null.
    /// </para>
    /// </summary>
    [Serializable]
    public class BondReaction
    {
        /// <summary>What it answers. <see cref="BondTrigger.None"/> = no reaction.</summary>
        public BondTrigger Trigger = BondTrigger.None;

        /// <summary>What it does: apply its effects, or intercept the hit.</summary>
        public BondAction Action = BondAction.Apply;

        /// <summary>Who <see cref="BondAction.Apply"/> effects land on (an intercept's land on the reactor).</summary>
        public BondReactionTarget Target = BondReactionTarget.TriggerTarget;

        /// <summary>For an ally trigger: which of the team's beasts may be the triggering ally.</summary>
        public BondTriggerFilter TriggerFilter = BondTriggerFilter.Any;

        /// <summary>The order the members are tried in as reactors.</summary>
        public BondReactorOrder ReactorOrder = BondReactorOrder.TeamOrder;

        /// <summary>Percent chance it fires once a reactor is found, 1-100 (rolled last; 100 draws nothing).</summary>
        public int Chance = 100;

        /// <summary>
        /// After reacting, a member cannot react for this bond again until this many of its own
        /// turns have begun. 0 = no cooldown.
        /// </summary>
        public int Cooldown;

        /// <summary>Most reactions per member per battle. 0 = no cap.</summary>
        public int MaxPerMember;

        /// <summary>Most reactions per triggering ally per battle. 0 = no cap.</summary>
        public int MaxPerTriggerUnit;

        /// <summary>Most reactions per battle, whoever reacts. 0 = no cap.</summary>
        public int MaxPerBattle;

        /// <summary>
        /// Hexes (nearest tile to nearest tile) the reactor may be from the unit it acts on: the
        /// targeted ally of an intercept, the single target of an <see cref="BondAction.Apply"/>,
        /// or the radius of <see cref="BondReactionTarget.EnemiesNearReactor"/>. 0 = unlimited
        /// (except as a radius, which then reaches nobody). Ignored for
        /// <see cref="BondReactionTarget.Self"/> and <see cref="BondReactionTarget.Team"/>.
        /// </summary>
        public int Range;

        /// <summary>For <see cref="BondTrigger.AllyBelowHpPercent"/>: the threshold, percent of max HP.</summary>
        public int HpThresholdPercent = 50;

        /// <summary>
        /// The effects, cast by the reactor through <see cref="SkillEffectApplier"/> at level 1 with
        /// the reactor's first element and its stronger attacking category (see
        /// <see cref="TeamBondLoadout"/>), in authored order.
        /// </summary>
        public List<SkillEffect> Effects = new List<SkillEffect>();

        /// <summary>Whether it reacts to anything at all.</summary>
        public bool IsActive
        {
            get { return Trigger != BondTrigger.None; }
        }
    }
}
