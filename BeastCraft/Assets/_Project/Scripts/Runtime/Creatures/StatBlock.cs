using System;
using UnityEngine;

namespace BeastCraft.Creatures
{
    /// <summary>
    /// A full set of combat stats. A value type so it can be combined and passed around cheaply
    /// (base stats + level scaling + gear bonuses) without aliasing the authored species data.
    /// <para>
    /// Holds the six combat axes plus <see cref="MoveRange"/>, the per-turn movement budget. Move
    /// range lives here rather than on the battle unit so that it comes from the same place every
    /// other stat does — the species' authored base, then gear, then timed buffs and debuffs — and
    /// so everything that already moves a stat by <see cref="StatType"/> moves it too.
    /// </para>
    /// </summary>
    [Serializable]
    public struct StatBlock
    {
        public int Hp;
        public int Attack;
        public int Defense;
        public int SpecialAttack;
        public int SpecialDefense;
        public int Speed;

        /// <summary>
        /// Hex steps a unit may move per turn. Authored on the species as a small whole number and
        /// exempt from level scaling; see <see cref="StatType.MoveRange"/>.
        /// </summary>
        public int MoveRange;

        /// <summary>
        /// Builds a stat block. <paramref name="moveRange"/> is optional and defaults to 0 ("does
        /// not move"), so a block built only from the six combat axes keeps its old meaning.
        /// </summary>
        public StatBlock(int hp, int attack, int defense, int specialAttack, int specialDefense, int speed, int moveRange = 0)
        {
            Hp = hp;
            Attack = attack;
            Defense = defense;
            SpecialAttack = specialAttack;
            SpecialDefense = specialDefense;
            Speed = speed;
            MoveRange = moveRange;
        }

        /// <summary>Reads a single stat by axis.</summary>
        public int GetStat(StatType type)
        {
            return type switch
            {
                StatType.HP => Hp,
                StatType.Attack => Attack,
                StatType.Defense => Defense,
                StatType.SpecialAttack => SpecialAttack,
                StatType.SpecialDefense => SpecialDefense,
                StatType.Speed => Speed,
                StatType.MoveRange => MoveRange,
                _ => 0
            };
        }

        /// <summary>Writes a single stat by axis.</summary>
        public void SetStat(StatType type, int value)
        {
            switch (type)
            {
                case StatType.HP:
                    Hp = value;
                    break;
                case StatType.Attack:
                    Attack = value;
                    break;
                case StatType.Defense:
                    Defense = value;
                    break;
                case StatType.SpecialAttack:
                    SpecialAttack = value;
                    break;
                case StatType.SpecialDefense:
                    SpecialDefense = value;
                    break;
                case StatType.Speed:
                    Speed = value;
                    break;
                case StatType.MoveRange:
                    MoveRange = value;
                    break;
                default:
                    Debug.LogError("[Creatures] SetStat called with unhandled StatType " + type + ".");
                    break;
            }
        }

        /// <summary>Component-wise sum, for layering level/gear bonuses onto base stats.</summary>
        public static StatBlock operator +(StatBlock a, StatBlock b)
        {
            return new StatBlock(
                a.Hp + b.Hp,
                a.Attack + b.Attack,
                a.Defense + b.Defense,
                a.SpecialAttack + b.SpecialAttack,
                a.SpecialDefense + b.SpecialDefense,
                a.Speed + b.Speed,
                a.MoveRange + b.MoveRange);
        }

        public override string ToString()
        {
            return "HP " + Hp + " / ATK " + Attack + " / DEF " + Defense +
                   " / SPA " + SpecialAttack + " / SPD " + SpecialDefense + " / SPE " + Speed +
                   " / MOV " + MoveRange;
        }
    }
}
