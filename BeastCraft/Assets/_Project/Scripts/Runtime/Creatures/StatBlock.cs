using System;
using UnityEngine;

namespace BeastCraft.Creatures
{
    /// <summary>
    /// A full set of combat stats. A value type so it can be combined and passed around cheaply
    /// (base stats + level scaling + gear bonuses) without aliasing the authored species data.
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

        public StatBlock(int hp, int attack, int defense, int specialAttack, int specialDefense, int speed)
        {
            Hp = hp;
            Attack = attack;
            Defense = defense;
            SpecialAttack = specialAttack;
            SpecialDefense = specialDefense;
            Speed = speed;
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
                a.Speed + b.Speed);
        }

        public override string ToString()
        {
            return "HP " + Hp + " / ATK " + Attack + " / DEF " + Defense +
                   " / SPA " + SpecialAttack + " / SPD " + SpecialDefense + " / SPE " + Speed;
        }
    }
}
