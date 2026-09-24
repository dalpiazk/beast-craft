# Research: Sword x Staff speed and damage formulas (2026-09-23)

Desk research on the community-documented combat formulas of *Sword x Staff*, the progression
reference already named in `docs/design/battle-system.md` ("Progression tie-in"). The user approved
adopting its speed and defense model for Beast Craft. The numbers are as the cited wiki reports them.
The wiki is a community source, not official documentation, and has not been checked against the
game. Beast Craft adopts the **shape** of the formulas, not the reference's constants.

Sources:

- Speed: https://purrwikimania.vercel.app/combat/speed.html
- Damage: https://purrwikimania.vercel.app/combat/damage.html

## 1. Speed: turn interval ∝ 1 / sqrt(SPD)

- The reference's turn interval is `interval = 100000 / sqrt(SPD × rankSpeedScale)`.
  `rankSpeedScale` is a promotion-band constant (1, then 10, then 150), which Beast Craft has no use
  for.
- So the number of turns one unit takes while another takes one is `sqrt(SPD_a / SPD_b)`, and turns
  grow with the **square root** of Speed. Stacked Speed has diminishing returns: 4× the Speed is 2×
  the turns, and +21% Speed is +10% turns.
- The wiki's "cost of one extra turn": in a fight of N opponent turns, one more turn of your own
  needs a Speed ratio of `((N + 1) / N)²`. That is +21% Speed in a 10-turn fight, +13.8% in a 15-turn
  fight and +6.8% in a 30-turn fight.

## 2. Damage: stat × coefficient, mitigated by ATK / (ATK + DEF)

The wiki's damage pipeline, in order:

1. **Base damage** = the skill's base stat × its coefficient (for example 0.8899 on Water Bullet).
2. **Flat damage** is added after that. Water Bullet's flat bonus is 8,899.
3. **Armor mitigation**: × `ATK / (ATK + DEF)`. The wiki notes that ATK appears **twice**, once in
   the base and once in this term.
4. Skill damage reduction, applied as a divisor.
5. The elemental layer, calculated separately.
6. Damage resistance: × `(1 + DMG Boost + PvE/PvP bonus) / (1 + target DMG RES + reduction scales)`.
7. **Crit / block**, if triggered. The crit multiplier is `max(1.3, 1 + critDamage − critDamageReduction)`.
   The worked example is `max(1.3, 1 + 0.63 − 0.0385) = 1.5915`.
8. Level / rank offset.
9. Situational bonuses such as food buffs, distance and execute scaling.

## 3. What Beast Craft adopted

All of these are named, tunable constants. See `TurnManager`, `DamageFormula` and the design doc.

| Reference | Beast Craft | Where |
| --- | --- | --- |
| Turn interval `100000 / sqrt(SPD × scale)` | Gauge fill rate `round(100 × sqrt(max(1, Speed)))` against a threshold of 100000, computed exactly in integers (integer square root, rounded half up) so the turn order is deterministic on every platform. A Speed-100 unit acts once per 1.0 of normalized time. | `TurnManager.FillRateForSpeed`, `FillScale` = 100, `ActionThreshold` = 100000 |
| Base = stat × coefficient | `Power` (the damage effect's magnitude) is a **percent of the attacking stat**: Power 120 is 120% of Atk (physical) or SpA (special). | `DamageFormula.PowerPercent` = 100 |
| × `ATK / (ATK + DEF)` | × `A / (A + DefenseWeight × D)` | `DamageFormula.DefenseWeight` = 1.0 |
| Crit `max(1.3, 1 + critDmg − critDmgRed)` | `max(MinCritMultiplier, CritMultiplier − reduction)`, which is 1.5 today because nothing supplies a reduction or a crit-damage stat yet | `DamageFormula.MinCritMultiplier` = 1.3, `CritMultiplier` = 1.5, `GetCritMultiplier` |
| (overall scaling) | A uniform multiplier on every hit | `DamageFormula.GlobalScale` = 1.0 |

The formula as built:

```
damage = max(1, truncate(Power / 100 × A × A / (A + DefenseWeight × D) × GlobalScale
                         × element × crit × roll / 100))
```

**The level term is gone.** The old Pokémon-style `(2 × Level / 5 + 2)` term is removed. Stats
already scale with level through the growth curve, so with A, D and HP on the same curve, a hit
between equally levelled beasts takes the same share of HP at every level.

**Not adopted, or deferred:**

- Flat skill damage (step 2).
- Skill damage reduction (step 4).
- Damage boost and damage resistance (step 6).
- The level / rank offset (step 8).
- Situational bonuses (step 9).
- A crit-damage or crit-resist stat. The 1.3 floor is already in place for when one arrives.
- The reference's rank speed scale.

The elemental layer stays Beast Craft's own `ElementChart`.

## 4. Consequences recorded elsewhere

- **Speed spread (user decision).** The "fastest vs slowest differ by 10–15%" target now applies to
  **turns**, not to the Speed stat. Under the square root, a 1.10–1.15× turn spread needs about a
  1.21–1.32× Speed spread. The current roster (Speed 92–105, 1.14×) spreads turns by only 1.07×.
  Widening it is the next roster retune; see `docs/balance/tuning-log.md`, "Sqrt speed +
  mitigation formula".
- **Power rescale.** Kit and enemy powers were rescaled to the percent-of-stat meaning so that a
  neutral hit between two average level-50 roster beasts removes the same share of HP as under the
  old formula (Blast 40 → 68). The design doc's "Damage formula" section and the tuning log have the
  before/after shares at levels 1, 50 and 100.
- This supersedes the Speed bullet of `research-crit-variance-speed.md` §5 on how Speed converts to
  turns (turns were linear in Speed there).
