using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Battle;
using BeastCraft.Bonds;
using BeastCraft.Creatures;
using BeastCraft.Presentation.Content;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>What one line of the battle log records.</summary>
    public enum BattleLogKind
    {
        /// <summary>A unit's turn begins (it moved, waited or was stunned).</summary>
        Turn = 0,
        Hit = 1,
        Heal = 2,
        Shield = 3,
        Status = 4,
        StatChange = 5,

        /// <summary>Damage over time (burn, poison) ticked at the start of the unit's turn.</summary>
        DamageOverTime = 6,

        /// <summary>A team bond at battle start, or a bond's reaction.</summary>
        Bond = 7,

        /// <summary>An avatar passive fired.</summary>
        Passive = 8,

        /// <summary>A unit fell.</summary>
        Defeat = 9
    }

    /// <summary>
    /// One damage hit taken apart: the inputs <see cref="DamageFormula"/> recorded on the roll
    /// (<see cref="DamageBreakdown"/>) and the roll's crit and variance, in the formula's order.
    /// <see cref="Recomputed"/> feeds them back to the Core formula; it equals <see cref="Amount"/>
    /// for every hit the battle recorded.
    /// </summary>
    public sealed class DamageBreakdownView
    {
        public float Power;
        public int Attack;
        public int Defense;
        public DamageCategory Category;

        /// <summary><c>Power / 100 * A * A / (A + D)</c> (<see cref="DamageFormula.ComputeBase"/>).</summary>
        public double Base;

        public float ElementMultiplier;
        public bool Crit;

        /// <summary>What a crit multiplies by (applied only when <see cref="Crit"/>).</summary>
        public float CritMultiplier;

        public int VariancePercent;
        public double ExecuteMultiplier;
        public double LevelMultiplier;

        /// <summary>The whole HP the hit was worth (before the target's remaining HP clamps it).</summary>
        public int Amount;

        /// <summary>What a shield soaked of it.</summary>
        public int Absorbed;

        /// <summary>
        /// The product before truncation, in the formula's order:
        /// <c>base * element * crit * variance / 100 * execute * level</c>, each identity factor left
        /// out exactly as <see cref="DamageFormula.Compute(float, int, int, float, int, bool, double, double)"/> leaves it out.
        /// </summary>
        public double Product
        {
            get
            {
                double scaled = Base * ElementMultiplier;
                if (Crit)
                {
                    scaled *= CritMultiplier;
                }

                if (VariancePercent != DamageFormula.NeutralVariancePercent)
                {
                    scaled = scaled * (VariancePercent < 0 ? 0 : VariancePercent) / 100.0;
                }

                if (ExecuteMultiplier != 1.0)
                {
                    scaled *= ExecuteMultiplier < 0.0 ? 0.0 : ExecuteMultiplier;
                }

                if (LevelMultiplier != 1.0)
                {
                    scaled *= LevelMultiplier < 0.0 ? 0.0 : LevelMultiplier;
                }

                return scaled;
            }
        }

        /// <summary>The Core formula on the recorded components (<see cref="DamageFormula.Compute(float, int, int, float, int, bool, double, double)"/>).</summary>
        public int Recomputed
        {
            get { return DamageFormula.Compute(Power, Attack, Defense, ElementMultiplier, VariancePercent, Crit, ExecuteMultiplier, LevelMultiplier); }
        }

        /// <summary>The breakdown as label/value lines, for the log panel.</summary>
        public List<KeyValuePair<string, string>> Lines()
        {
            string a = Category == DamageCategory.Special ? "SpA" : "Atk";
            string d = Category == DamageCategory.Special ? "SpD" : "Def";
            List<KeyValuePair<string, string>> lines = new List<KeyValuePair<string, string>>
            {
                Line("Skill power", Number(Power) + "%"),
                Line(a + " vs " + d, Attack.ToString(CultureInfo.InvariantCulture) + " vs " + Defense.ToString(CultureInfo.InvariantCulture)),
                Line("Base: power x A²/(A+D)", Base.ToString("0.##", CultureInfo.InvariantCulture)),
                Line("Element", DerivedStats.Times(ElementMultiplier)),
                Line("Crit", Crit ? DerivedStats.Times(CritMultiplier) : "no"),
                Line("Variance roll", VariancePercent.ToString(CultureInfo.InvariantCulture) + "%"),
                Line("Level gap", DerivedStats.Times(LevelMultiplier))
            };
            if (ExecuteMultiplier != 1.0)
            {
                lines.Add(Line("Execute", DerivedStats.Times(ExecuteMultiplier)));
            }

            lines.Add(Line("Final", Amount.ToString(CultureInfo.InvariantCulture) + (Absorbed > 0 ? " (" + Absorbed.ToString(CultureInfo.InvariantCulture) + " to the shield)" : string.Empty)));
            return lines;
        }

        private static KeyValuePair<string, string> Line(string label, string value)
        {
            return new KeyValuePair<string, string>(label, value);
        }

        private static string Number(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>The breakdown of a recorded hit, or null when the roll carries no components.</summary>
        public static DamageBreakdownView Of(DamageHit hit)
        {
            DamageBreakdown b = hit.Roll.Breakdown;
            if (!b.IsRecorded)
            {
                return null;
            }

            return new DamageBreakdownView
            {
                Power = b.Power,
                Attack = b.Attack,
                Defense = b.Defense,
                Category = b.Category,
                Base = b.Base,
                ElementMultiplier = b.ElementMultiplier,
                Crit = hit.Roll.IsCrit,
                CritMultiplier = b.CritMultiplier,
                VariancePercent = hit.Roll.VariancePercent,
                ExecuteMultiplier = b.ExecuteMultiplier,
                LevelMultiplier = b.LevelMultiplier,
                Amount = hit.Roll.Amount,
                Absorbed = hit.Absorbed
            };
        }
    }

    /// <summary>One line of the battle log.</summary>
    public sealed class BattleLogEntry
    {
        /// <summary>The turn it happened on (1-based; 0 for battle start).</summary>
        public int Turn;

        public BattleLogKind Kind;

        /// <summary>Who acted (the caster, the reactor; the unit whose turn it is).</summary>
        public string ActorId;

        /// <summary>Who it landed on (null for a turn line).</summary>
        public string TargetId;

        /// <summary>What did it: the skill, passive or bond's name.</summary>
        public string Source;

        /// <summary>The line as the panel shows it.</summary>
        public string Text;

        /// <summary>The hit's damage, heal's HP, shield's points, stat change's delta, or DoT's damage.</summary>
        public int Amount;

        public bool Crit;

        /// <summary>A hit's damage taken apart; null for anything else.</summary>
        public DamageBreakdownView Breakdown;

        /// <summary>Whether the line concerns <paramref name="unitId"/> (acting or landed on).</summary>
        public bool Involves(string unitId)
        {
            return unitId == null || string.Equals(ActorId, unitId, StringComparison.Ordinal) || string.Equals(TargetId, unitId, StringComparison.Ordinal);
        }
    }

    /// <summary>A unit the log can be filtered by.</summary>
    public sealed class BattleLogUnit
    {
        public string UnitId;
        public string Name;
        public BattleTeam Team;
    }

    /// <summary>
    /// The battle log: every turn's events rebuilt from the battle's own records (the turn results,
    /// each hit's <see cref="DamageBreakdown"/>, each landed effect's amount, the passives, bond
    /// reactions and the avatar's arts), filterable by unit. Read-only: building it never touches
    /// the battle. Used by the in-battle panel and by the Results screen.
    /// </summary>
    public sealed class BattleLogViewModel
    {
        private readonly Func<string, string> _names;
        private readonly List<BattleLogEntry> _entries = new List<BattleLogEntry>();
        private BattleUnit _avatar;

        public BattleLogViewModel(Func<string, string> names)
        {
            _names = names ?? (id => id);
        }

        /// <summary>Every entry, oldest first.</summary>
        public IReadOnlyList<BattleLogEntry> Entries
        {
            get { return _entries; }
        }

        /// <summary>The units in the log (the filter chips), team first then enemies, in first-seen order.</summary>
        public List<BattleLogUnit> Units { get; } = new List<BattleLogUnit>();

        /// <summary>The unit the log is filtered to; null for everyone.</summary>
        public string Filter { get; set; }

        /// <summary>The entries <see cref="Filter"/> lets through.</summary>
        public List<BattleLogEntry> Filtered()
        {
            return Filter == null ? new List<BattleLogEntry>(_entries) : _entries.FindAll(e => e.Involves(Filter));
        }

        /// <summary>The log of a whole (or partly played) battle.</summary>
        public static BattleLogViewModel Build(BattleRun battle, IEnumerable<BattleUnit> units, Func<string, string> names)
        {
            BattleLogViewModel log = new BattleLogViewModel(names);
            if (battle == null)
            {
                return log;
            }

            log.AddUnits(units ?? battle.Units, battle.Avatar);
            log.AddStart(battle.BondActivations, battle.OpeningPassiveActivations);
            for (int i = 0; i < battle.Turns.Count; i++)
            {
                log.AddTurn(i + 1, battle.Turns[i]);
            }

            log.MarkDefeats(units ?? battle.Units);
            return log;
        }

        /// <summary>A name lookup for a campaign battle's units: species and enemy names (numbered when repeated), the Beastbinder.</summary>
        public static Func<string, string> NamesFor(GameContent content, IReadOnlyDictionary<string, string> speciesByUnit, string avatarId)
        {
            Dictionary<string, string> names = UnitNames(content, speciesByUnit);
            return id =>
            {
                if (id != null && id == avatarId)
                {
                    return CampaignAvatar.DisplayName;
                }

                return id != null && names.TryGetValue(id, out string name) ? name : id;
            };
        }

        /// <summary>Unit ids to display names: the species or enemy's name, numbered from the second of a kind ("Slime", "Slime 2").</summary>
        public static Dictionary<string, string> UnitNames(GameContent content, IEnumerable<KeyValuePair<string, string>> speciesByUnit)
        {
            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> entry in speciesByUnit ?? new Dictionary<string, string>())
            {
                string display = entry.Value;
                if (content?.Battle?.GetSpecies(entry.Value) != null)
                {
                    display = content.Battle.GetSpecies(entry.Value).DisplayName;
                }
                else if (content?.Enemies?.Get(entry.Value) != null)
                {
                    display = content.Enemies.Get(entry.Value).DisplayName;
                }

                display = display ?? entry.Key;
                counts.TryGetValue(display, out int seen);
                counts[display] = seen + 1;
                names[entry.Key] = seen == 0 ? display : display + " " + (seen + 1).ToString(CultureInfo.InvariantCulture);
            }

            return names;
        }

        private string Name(BattleUnit unit)
        {
            return unit == null ? "?" : _names(unit.Id) ?? unit.Id;
        }

        private void AddUnits(IEnumerable<BattleUnit> units, BattleUnit avatar)
        {
            _avatar = avatar;
            List<BattleUnit> all = new List<BattleUnit>();
            foreach (BattleUnit unit in units ?? new BattleUnit[0])
            {
                if (unit != null)
                {
                    all.Add(unit);
                }
            }

            if (avatar != null && !all.Contains(avatar))
            {
                all.Add(avatar);
            }

            foreach (BattleTeam team in new[] { BattleTeam.Player, BattleTeam.Enemy })
            {
                foreach (BattleUnit unit in all)
                {
                    if (unit.Team == team)
                    {
                        Units.Add(new BattleLogUnit { UnitId = unit.Id, Name = Name(unit), Team = unit.Team });
                    }
                }
            }
        }

        private void AddStart(IReadOnlyList<TeamBondActivation> bonds, IReadOnlyList<PassiveActivation> passives)
        {
            foreach (TeamBondActivation bond in bonds ?? new TeamBondActivation[0])
            {
                string name = bond.Bond?.DisplayName ?? bond.Bond?.BondId ?? "Bond";
                List<string> who = new List<string>();
                foreach (BattleUnit unit in bond.Recipients)
                {
                    who.Add(Name(unit));
                }

                _entries.Add(new BattleLogEntry
                {
                    Turn = 0,
                    Kind = BattleLogKind.Bond,
                    Source = name,
                    ActorId = bond.Recipients.Count > 0 ? bond.Recipients[0].Id : null,
                    Text = "Bond " + name + " (tier " + bond.Tier + ") on " + (who.Count == 0 ? "no one" : string.Join(", ", who))
                });
            }

            foreach (PassiveActivation passive in passives ?? new PassiveActivation[0])
            {
                AddPassive(0, passive);
            }
        }

        /// <summary>Appends one turn's events (in the order they happened: damage over time, the skills or arts, passives, bond reactions).</summary>
        public void AddTurn(int turnNumber, BattleTurnResult turn)
        {
            if (turn == null || turn.Unit == null)
            {
                return;
            }

            string actor = Name(turn.Unit);
            string what = turn.Stunned ? "is stunned" : turn.MovementSpent > 0 ? "moves " + turn.MovementSpent + (turn.MovementSpent == 1 ? " hex" : " hexes") : "acts";
            _entries.Add(new BattleLogEntry { Turn = turnNumber, Kind = BattleLogKind.Turn, ActorId = turn.Unit.Id, Text = "Turn " + turnNumber + ": " + actor + " " + what });
            if (turn.StatusDamage > 0)
            {
                _entries.Add(new BattleLogEntry
                {
                    Turn = turnNumber,
                    Kind = BattleLogKind.DamageOverTime,
                    ActorId = turn.Unit.Id,
                    TargetId = turn.Unit.Id,
                    Amount = turn.StatusDamage,
                    Text = actor + " takes " + turn.StatusDamage + " from burn/poison"
                });
            }

            foreach (BattleSkillOutcome outcome in turn.SkillOutcomes)
            {
                if (outcome.Fired && outcome.Activation != null)
                {
                    AddActivation(turnNumber, turn.Unit, outcome.Activation, null);
                }
            }

            foreach (SkillActivation art in turn.AvatarActivations)
            {
                AddActivation(turnNumber, turn.Unit, art, "art");
            }

            foreach (PassiveActivation passive in turn.PassiveActivations)
            {
                AddPassive(turnNumber, passive);
            }

            foreach (BondReactionRecord reaction in turn.BondReactions)
            {
                string bond = reaction.Bond?.DisplayName ?? reaction.Bond?.BondId ?? "Bond";
                _entries.Add(new BattleLogEntry
                {
                    Turn = turnNumber,
                    Kind = BattleLogKind.Bond,
                    ActorId = reaction.Reactor?.Id,
                    TargetId = reaction.InterceptedFrom?.Id,
                    Source = bond,
                    Text = bond + ": " + Name(reaction.Reactor) + " reacts" + (reaction.InterceptedFrom != null ? ", covering " + Name(reaction.InterceptedFrom) : string.Empty)
                });
                if (reaction.Activation != null)
                {
                    AddActivation(turnNumber, reaction.Reactor, reaction.Activation, bond);
                }
            }
        }

        private void AddPassive(int turnNumber, PassiveActivation passive)
        {
            if (passive == null)
            {
                return;
            }

            string name = passive.Passive?.DisplayName ?? passive.Passive?.PassiveId ?? "Passive";
            _entries.Add(new BattleLogEntry
            {
                Turn = turnNumber,
                Kind = BattleLogKind.Passive,
                ActorId = _avatar?.Id,
                TargetId = passive.TriggeringUnit?.Id,
                Source = name,
                Text = "Passive " + name + " (" + passive.Trigger + ")"
            });
            if (passive.Activation != null)
            {
                AddActivation(turnNumber, _avatar, passive.Activation, name);
            }
        }

        /// <summary>A skill's hits and landed effects, one line each.</summary>
        private void AddActivation(int turnNumber, BattleUnit caster, SkillActivation activation, string via)
        {
            string skill = activation.Skill?.DisplayName ?? activation.Skill?.SkillId ?? "?";
            string source = via == null || via == skill ? skill : via == "art" ? skill + " (art)" : via + ": " + skill;
            string casterName = caster == null ? via ?? "?" : Name(caster);
            if (activation.Targets.Count == 0)
            {
                _entries.Add(new BattleLogEntry { Turn = turnNumber, Kind = BattleLogKind.Status, ActorId = caster?.Id, Source = source, Text = casterName + ": " + source + " finds no target" });
                return;
            }

            foreach (DamageHit hit in activation.Hits)
            {
                DamageBreakdownView breakdown = DamageBreakdownView.Of(hit);
                _entries.Add(new BattleLogEntry
                {
                    Turn = turnNumber,
                    Kind = BattleLogKind.Hit,
                    ActorId = caster?.Id,
                    TargetId = hit.Target?.Id,
                    Source = source,
                    Amount = hit.Roll.Amount,
                    Crit = hit.Roll.IsCrit,
                    Breakdown = breakdown,
                    Text = casterName + " > " + Name(hit.Target) + ": " + source + " " + hit.Roll.Amount + (hit.Roll.IsCrit ? " CRIT" : string.Empty) +
                           (hit.Absorbed > 0 ? " (" + hit.Absorbed + " shielded)" : string.Empty)
                });
            }

            foreach (AppliedEffect applied in activation.Applied)
            {
                BattleLogEntry entry = Applied(turnNumber, caster, casterName, source, applied);
                if (entry != null)
                {
                    _entries.Add(entry);
                }
            }
        }

        private BattleLogEntry Applied(int turnNumber, BattleUnit caster, string casterName, string source, AppliedEffect applied)
        {
            SkillEffect effect = applied.Effect;
            if (effect == null)
            {
                return null;
            }

            BattleLogEntry entry = new BattleLogEntry { Turn = turnNumber, ActorId = caster?.Id, TargetId = applied.Target?.Id, Source = source, Amount = applied.Amount };
            string target = Name(applied.Target);
            switch (effect.EffectType)
            {
                case SkillEffectType.Heal:
                    entry.Kind = BattleLogKind.Heal;
                    entry.Text = casterName + " heals " + target + " +" + applied.Amount + " HP (" + source + ")";
                    break;
                case SkillEffectType.BuffStat:
                case SkillEffectType.DebuffStat:
                    entry.Kind = BattleLogKind.StatChange;
                    entry.Text = target + " " + (applied.Amount >= 0 ? "+" : string.Empty) + applied.Amount + " " + DerivedStats.ShortName(effect.AffectedStat) +
                                 (effect.DurationTurns > 0 ? " for " + effect.DurationTurns + (effect.DurationTurns == 1 ? " turn" : " turns") : string.Empty) + " (" + source + ")";
                    break;
                case SkillEffectType.Cleanse:
                    entry.Kind = BattleLogKind.Status;
                    entry.Text = target + " is cleansed (" + source + ")";
                    break;
                default:
                    switch (effect.Status)
                    {
                        case StatusType.Shield:
                            entry.Kind = BattleLogKind.Shield;
                            entry.Text = applied.Amount > 0 ? target + " gains a " + applied.Amount + " shield (" + source + ")" : target + " keeps its larger shield (" + source + ")";
                            break;
                        case StatusType.DamageOverTime:
                            entry.Kind = BattleLogKind.Status;
                            entry.Text = target + " is " + (effect.Status == StatusType.DamageOverTime && SourceElement(applied) == Element.Fire ? "burned" : "poisoned") +
                                         (applied.Amount > 0 ? ": " + applied.Amount + " a turn" : string.Empty) + " (" + source + ")";
                            break;
                        case StatusType.Stun:
                            entry.Kind = BattleLogKind.Status;
                            entry.Text = target + " is stunned (" + source + ")";
                            break;
                        case StatusType.Taunt:
                            entry.Kind = BattleLogKind.Status;
                            entry.Text = target + " is taunted by " + casterName + " (" + source + ")";
                            break;
                        case StatusType.Knockback:
                            entry.Kind = BattleLogKind.Status;
                            entry.Text = target + " is knocked back " + applied.Amount + (applied.Amount == 1 ? " hex" : " hexes") + " (" + source + ")";
                            break;
                        default:
                            return null;
                    }

                    break;
            }

            return entry;
        }

        private static Element SourceElement(AppliedEffect applied)
        {
            foreach (ActiveStatus status in applied.Target?.Statuses ?? new ActiveStatus[0])
            {
                if (status.Origin == applied.Effect && status.Source != null && status.Source.Elements.Count > 0)
                {
                    return status.Source.Elements[0];
                }
            }

            return Element.None;
        }

        /// <summary>Marks each fallen unit's last hit (or damage-over-time tick) and adds a line for its fall.</summary>
        public void MarkDefeats(IEnumerable<BattleUnit> units)
        {
            foreach (BattleUnit unit in units ?? new BattleUnit[0])
            {
                if (unit == null || !unit.IsDefeated)
                {
                    continue;
                }

                int last = _entries.FindLastIndex(e => (e.Kind == BattleLogKind.Hit || e.Kind == BattleLogKind.DamageOverTime) && e.TargetId == unit.Id);
                if (last < 0)
                {
                    continue;
                }

                BattleLogEntry blow = _entries[last];
                _entries.Insert(last + 1, new BattleLogEntry
                {
                    Turn = blow.Turn,
                    Kind = BattleLogKind.Defeat,
                    ActorId = blow.ActorId,
                    TargetId = unit.Id,
                    Text = Name(unit) + " falls"
                });
            }
        }
    }
}
