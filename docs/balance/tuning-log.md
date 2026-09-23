# Roster tuning log — first simulator pass

The first tuning pass of the starter roster's base stats against the headless balance simulator's
PvE mode. Only `BeastCraft/Assets/_Project/Data/Creatures/beast-roster.json` base stats (six stats and
`MoveRange`) changed; the simulator's kit, encounters, damage formula, element chart and Runtime code
are untouched.

- **Before:** [`baseline-report.md`](baseline-report.md) — first-draft stats, kept unchanged.
- **After:** [`tuned-report.md`](tuned-report.md) — the default run on the tuned stats
  (`dotnet run --project Tooling/BalanceSim -c Release -- --out docs/balance/tuned-report.md`).

**Measured under the old round-based turn order.** Every number in this log, and the committed
`baseline-report.md`, predates the ATB speed gauge (design doc, decision 3, amended): battles were
counted in rounds and every living unit acted once per round. `tuned-report.md` has since been
regenerated under ATB and no longer shows the "after" numbers quoted here; see "After the ATB
turn-order change" at the end.

**Superseded by a second pass.** The roster has since been re-tuned for the ATB gauge, combat
stances, variance and crits and the generated encounters, with base Speed held to a 15% band (user
decision). See "Retune for ATB + stances + crits + mixed encounters" at the end; that section's
stats are the current roster and `tuned-report.md` is its default-seed run.

This is still **not confirmed balance**. It makes the roster even under the simulator's current
assumptions (one standard kit, fixture enemies, nearest-enemy targeting, no skills), and every number
is expected to move again once real skills and encounters exist.

## Targets

1. Overall marginal clear rate within ±5 points for every beast in `elemental` mode (primary);
   within about ±7 in `neutral` mode (secondary).
2. Every beast top-3 in at least one encounter (`elemental`).
3. No beast top-3 in every encounter.
4. Archetype identity preserved.
5. Roster rules unchanged: six-stat total 570–630 (600 ±5%), `MoveRange` 2–5, every stat at least 1,
   all beasts on the `medium` curve.

## Stats, before → after

Max-level base stats; changed values in bold.

| Beast | HP | Atk | Def | SpA | SpD | Spe | Total | Move |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Phoenix | 70 → **100** | 130 → **125** | 55 → **75** | 140 | 70 → **90** | 135 → **100** | 600 → 630 | 4 |
| Leviathan | 150 → **125** | 90 → **85** | 130 → **125** | 90 → **85** | 95 | 45 → **55** | 600 → 570 | 3 |
| Golem | 150 → **160** | 80 → **105** | 170 → **150** | 40 → **70** | 125 → **105** | 35 → **40** | 600 → 630 | 2 |
| Griffin | 95 → **105** | 110 → **115** | 85 → **90** | 85 → **90** | 85 → **90** | 140 → **115** | 600 → 605 | 5 |
| Thunderbird | 65 → **100** | 140 → **125** | 55 → **80** | 125 → **120** | 60 → **85** | 155 → **120** | 600 → 630 | 4 |
| Frost Wyrm | 95 | 75 | 125 → **120** | 100 | 125 → **115** | 80 | 600 → 585 | 3 |
| Treant | 160 → **130** | 80 → **85** | 100 → **95** | 85 → **90** | 130 → **120** | 45 → **50** | 600 → 570 | 3 |
| Tarasque | 110 → **115** | 140 | 145 → **130** | 50 → **55** | 80 | 75 | 600 → 595 | 3 |
| Kirin | 100 | 50 | 80 | 140 → **135** | 135 → **120** | 95 | 600 → 580 | 4 |
| Basilisk | 80 → **100** | 95 | 55 → **80** | 145 → **150** | 90 → **95** | 135 → **110** | 600 → 630 | 5 |

The shape of the change, in one line each:

- **The bulk carriers gave budget back.** Leviathan and Treant drop to the 570 floor (mostly HP),
  Kirin to 580 (Special Defense), Frost Wyrm to 585.
- **The fragile beasts traded Speed for bulk and rose to the 630 ceiling.** In this simulator Speed
  is close to ordinal: what matters is who acts first, not by how much, and acting first mostly means
  walking into the enemy first (see caveats). The speed *order* is kept — Thunderbird 120 > Griffin
  115 > Basilisk 110 > Phoenix 100 > Kirin 95 > the rest — and the freed points bought HP and both
  defences.
- **Golem got something to hit with.** A wall with Attack 80 / SpA 40 contributes almost nothing to a
  clear rate, because nothing in the game makes enemies attack it (see caveats); it keeps the highest
  HP and Defense, the lowest Speed and Move 2.

## Marginal clear rate, before → after (default seed)

Points of clear rate (teams with the beast minus teams without), levels 1/50/100 averaged; (n) = rank
within the encounter. The `boss` column is the same in both modes: the Colossus has no element, so
every beast's kit hits it at 1x either way.

### `elemental` (primary)

| Beast | `boss` before | `boss` after | `swarm` before | `swarm` after | `pack` before | `pack` after | Overall before | Overall after |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Kirin | +33.9 (1) | +14.3 (2) | +4.8 (6) | −4.1 (8) | +13.5 (3) | −5.2 (8) | +17.4 | +1.7 |
| Leviathan | −0.5 (6) | −21.4 (10) | +23.3 (1) | +7.1 (2) | +28.0 (1) | +19.3 (1) | +16.9 | +1.7 |
| Treant | +2.8 (5) | −20.1 (9) | +21.3 (2) | +7.8 (1) | +18.1 (2) | +9.4 (2) | +14.1 | −1.0 |
| Tarasque | +10.1 (4) | +16.3 (1) | +7.4 (4) | −10.1 (10) | +2.9 (4) | −13.1 (10) | +6.8 | −2.3 |
| Frost Wyrm | +10.7 (3) | +13.0 (3) | +10.7 (3) | +5.2 (3) | −3.0 (5) | −7.8 (9) | +6.1 | +3.4 |
| Griffin | +14.0 (2) | +1.7 (6) | +4.8 (5) | −0.8 (6) | −11.6 (8) | +0.8 (4) | +2.4 | +0.6 |
| Golem | −29.6 (10) | −9.5 (8) | +4.1 (7) | +2.5 (4) | −7.0 (7) | −4.5 (7) | −10.8 | −3.8 |
| Basilisk | −9.8 (7) | +3.0 (5) | −24.3 (9) | −4.1 (7) | −3.7 (6) | +7.4 (3) | −12.6 | +2.1 |
| Phoenix | −11.1 (8) | −0.3 (7) | −16.4 (8) | +1.2 (5) | −22.9 (10) | −3.2 (5) | −16.8 | −0.7 |
| Thunderbird | −20.4 (9) | +3.0 (4) | −35.6 (10) | −4.8 (9) | −14.3 (9) | −3.2 (6) | −23.4 | −1.6 |

Overall spread: −23.4 … +17.4 before, −3.8 … +3.4 after.

### `neutral` (secondary)

| Beast | `boss` before | `boss` after | `swarm` before | `swarm` after | `pack` before | `pack` after | Overall before | Overall after |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Kirin | +33.9 (1) | +14.3 (2) | +0.1 (6) | −19.0 (10) | +31.0 (1) | +3.8 (5) | +21.6 | −0.3 |
| Treant | +2.8 (5) | −20.1 (9) | +35.8 (1) | +12.7 (2) | +25.7 (2) | +8.5 (3) | +21.4 | +0.4 |
| Leviathan | −0.5 (6) | −21.4 (10) | +17.3 (2) | +22.0 (1) | +23.0 (3) | +6.5 (4) | +13.3 | +2.3 |
| Frost Wyrm | +10.7 (3) | +13.0 (3) | +14.0 (4) | +2.8 (4) | +1.9 (6) | −1.5 (7) | +8.9 | +4.8 |
| Tarasque | +10.1 (4) | +16.3 (1) | −2.5 (7) | −5.8 (8) | +3.2 (5) | −0.8 (6) | +3.6 | +3.2 |
| Griffin | +14.0 (2) | +1.7 (6) | +8.1 (5) | −5.2 (7) | −16.7 (8) | −18.7 (9) | +1.8 | −7.4 |
| Golem | −29.6 (10) | −9.5 (8) | +14.7 (3) | −4.5 (6) | +10.4 (4) | +15.7 (1) | −1.5 | +0.6 |
| Basilisk | −9.8 (7) | +3.0 (5) | −23.7 (8) | +0.8 (5) | −14.7 (7) | −1.5 (8) | −16.0 | +0.8 |
| Phoenix | −11.1 (8) | −0.3 (7) | −25.7 (9) | +2.8 (3) | −26.6 (9) | +8.5 (2) | −21.1 | +3.7 |
| Thunderbird | −20.4 (9) | +3.0 (4) | −38.2 (10) | −6.5 (9) | −37.2 (10) | −20.6 (10) | −31.9 | −8.0 |

Overall spread: −31.9 … +21.6 before, −8.0 … +4.8 after.

### Seed robustness (final stats)

The same stats on four base seeds (12345 — the committed report — 999, 7 and 2024). The seed only
changes slot shuffles and speed-tie assignment, so this is the noise of the measurement itself.

| Beast | `elemental` 4-seed mean | `elemental` seed range | `neutral` 4-seed mean | `neutral` seed range |
| --- | ---: | --- | ---: | --- |
| Griffin | +3.3 | +0.6 … +6.1 | −7.6 | −10.8 … −5.8 |
| Leviathan | +3.1 | +1.7 … +4.9 | −2.2 | −9.3 … +2.3 |
| Basilisk | +2.7 | +0.5 … +5.8 | +3.9 | +0.8 … +5.6 |
| Tarasque | +1.4 | −2.3 … +4.0 | +6.9 | +3.2 … +12.2 |
| Frost Wyrm | +0.4 | −2.2 … +3.4 | +2.5 | +0.1 … +4.8 |
| Phoenix | −0.5 | −3.0 … +3.5 | +3.3 | +1.2 … +4.3 |
| Kirin | −0.9 | −4.5 … +1.7 | −2.5 | −5.0 … −0.3 |
| Treant | −2.3 | −3.7 … −1.0 | +2.8 | +0.4 … +5.6 |
| Golem | −3.2 | −6.3 … −0.9 | +1.4 | +0.5 … +3.2 |
| Thunderbird | −4.0 | −7.3 … −0.8 | −8.4 | −9.6 … −7.9 |

## Targets: met and missed

| Target | Result |
| --- | --- |
| 1a. `elemental` overall within ±5 | **Met** on the committed run (−3.8 … +3.4) and on the 4-seed mean (−4.0 … +3.3). Not on every single seed: Griffin +6.1, Golem −6.3 and Thunderbird −7.3 each appear once. |
| 1b. `neutral` overall within about ±7 | **Mostly met.** Eight of ten inside ±5 on the committed run; Griffin −7.4 and Thunderbird −8.0 are just outside ±7 (4-seed means −7.6 and −8.4). See "What binds". |
| 2. Every beast top-3 in some encounter | **Not attainable as stated**, and missed: Griffin (best 4th), Phoenix (5th), Thunderbird (4th) and Golem (4th) have no top-3 slot. Three encounters × three slots = nine slots for ten beasts, so at least one beast is always left out; the committed run fills the nine slots with six beasts (Frost Wyrm, Leviathan and Treant hold two each). Every beast is top-5 somewhere, and the report's own "no niche" flag (bottom three everywhere) fires for nobody. |
| 3. No beast top-3 everywhere | **Met.** Frost Wyrm comes closest (3rd, 3rd, 9th). |
| 4. Archetype identity | **Kept, with one erosion** — Golem's Attack (see below). |
| 5. Roster rules | **Met.** Totals 570–630, Move 2–5, all on `medium`; the roster tests and validator pass. |

## What binds, and what a design change would need

- **The niche target is a pigeonhole.** With three encounters it cannot hold for ten beasts. Either a
  fourth and fifth encounter shape (for example a ranged artillery line, or an escort / protect
  objective) or a looser target ("top 4 somewhere", or "not bottom 3 everywhere", which the report
  already flags) is needed. With every overall marginal inside ±5, which beast lands 3rd versus 5th in
  an encounter is also partly seed noise.
- **In `neutral` mode the fastest fragile beast on a team is punished against the pack.** Every
  simulated unit walks at the nearest enemy, so whoever moves first arrives first and takes the pack's
  focus (three melee direwolves with Speed 110 plus three ranged wisps). This is a function of speed
  *order*: when Thunderbird's Speed was dropped below Griffin's in a probe, Thunderbird's `neutral`
  pack marginal went from about −22 to −3 and Griffin's from about −20 to −41. The approved identities
  require Thunderbird to be the fastest beast and Griffin to be fast, so the two of them carry that
  penalty. In `elemental` mode their element matchups against the pack (Lightning is strong against the
  Water and Air wisps, Air against the Earth direwolf) offset it, which is why they are fine there. Stats alone
  cannot remove it without flattening the speed identities. What would: AI that does not charge
  alone (hold position until allies can support, or retreat when low), a threat or guard mechanic, or
  deployment that puts fast fragile beasts behind the front line.
- **A pure wall has no mechanic to be valuable through.** Enemies target the nearest beast, so
  Golem's Defense only matters when it happens to be in front, and with Move 2 it rarely is. At its
  first-draft line (Attack 80, Special Attack 40, Defense 170) it was −29.6 against the boss. The
  tuned Golem keeps the highest HP (160) and Defense (150) and the lowest Speed and Move, but its
  Attack rises from 80 to 105 — mid-roster, no longer "low" — so that it can contribute to a clear.
  Restoring a low-attack Golem needs a taunt / guard / intercept mechanic, not stats.
- **The slow tanks are the worst beasts against the boss.** Leviathan (−21.4) and Treant (−20.1)
  arrive late to a damage race against one enemy, then are top-2 against the swarm and pack. That is a
  niche, not a defect, but it means their overall number is a balance of two large opposite effects,
  so small stat moves swing it more than they swing other beasts.

## Iteration log

Candidates were judged on the default seed and, from iteration 3, on a second seed (999); from
iteration 6 on four seeds (12345, 999, 7, 2024), preferring the candidate whose multi-seed mean was
inside the targets over one that was only inside on the default seed. "Out" lists the beasts outside
±5 (`elemental`) or ±7 (`neutral`) on the default seed.

| # | Change (from the previous iteration unless noted) | `elemental` overall, default seed | `neutral` overall, default seed |
| ---: | --- | --- | --- |
| 0 | First draft (baseline) | −23.4 … +17.4; out: 9 of 10 | −31.9 … +21.6; out: 7 |
| 1 | Bulk off the three leaders (Leviathan HP −20, Treant HP −20 / SpD −10, Kirin SpA −10 / SpD −15), Tarasque Def −10, Frost Wyrm SpD −10; fragile beasts trade Speed for HP / Def / SpD up to 625–630; Golem Def / SpD → Atk / SpA | −8.3 … +5.6; out: Phoenix −8.3, Golem −6.7, Leviathan +5.4, Treant +5.6 | −11.6 … +9.5; out: Thunderbird, Treant |
| 2 | Phoenix Speed −10 → HP; Golem more Atk / SpA; Leviathan Def −5; Treant HP −5 → Atk | −5.7 … +6.8; out: Phoenix, Leviathan | out: Thunderbird −9.5, Basilisk −7.3 |
| — | Seed 999 rerun of #2: overall moves up to ±4, single encounter cells up to ±10 → multi-seed evaluation from here. Pack-only Phoenix probe (5 variants): the pack deficit follows speed order and Move, not damage | | |
| 3 | Phoenix Speed 115 → 100, Def / SpD up; Leviathan HP → Speed 55; Kirin SpA / SpD +5 | −9.8 … +5.2; out: Griffin, Kirin | out: Thunderbird, Basilisk, Treant |
| 4 | Griffin Speed 140 → 125 → bulk; Thunderbird Speed 145 → 130 → bulk; Basilisk Speed 125 → 115 → bulk; Treant HP → Speed; Kirin SpD −5 | −6.0 … +6.4; out: Golem, Griffin | out: Thunderbird −7.5 |
| 5 | Griffin −10 bulk; Golem Def / SpD → Atk / Speed; Tarasque +20 (HP, SpA, SpD); Thunderbird Atk → HP; Basilisk SpA +5 | −4.5 … +5.6; out: Frost Wyrm | out: Thunderbird, Tarasque |
| 6 | Speed order compressed (Thunderbird 120 > Griffin 115 > Basilisk 110 > Phoenix 100), freed points to bulk; Frost Wyrm Def −5; Tarasque HP −5 | −4.7 … +3.7; none out | out: Thunderbird −9.5 |
| — | Pack-only Thunderbird probe (4 variants × 4 seeds): Atk / SpA mix and Move 5 change nothing in `neutral` pack; dropping its Speed below Griffin's moves the penalty to Griffin (the "fastest fragile beast" finding) | | |
| 7 | Thunderbird / Griffin speed and Atk shuffles | Griffin −7.8 out | out: Thunderbird −11.0 |
| 8 | (from #6) Griffin and Thunderbird bias toward SpA; Tarasque −10; Golem SpA +5 | Griffin −7.0 out | worse: Thunderbird −11.7, Griffin −8.2 |
| 9 | #6 plus #8's Tarasque and Golem | −4.8 … +3.6; none out | out: Thunderbird −8.6 |
| 10 | Leviathan HP −5; Griffin HP −5 | −4.9 … +4.2; none out (4-seed mean max 4.4) | out: Thunderbird −9.2 |
| 11 | Identity restore: Leviathan HP 125 (Atk / SpA 85), Golem HP 160 / Def 155 / Atk 100 | −5.1 … +3.7; out: Golem −5.1, Tarasque −5.1 | out: Thunderbird −7.9 |
| 12 | Tarasque Atk +5; Golem Def −5 → Atk | −4.1 … +4.5; none out | out: Thunderbird −9.1, Griffin −8.2 |
| 13 | **Final.** Tarasque SpD −5; Griffin HP +5 | −3.8 … +3.4; none out (4-seed mean −4.0 … +3.3) | out: Thunderbird −8.0, Griffin −7.4 |

## Caveats

- **Measurement noise is about the size of the target.** Each beast is in 84 of the 210 teams per cell,
  so a single cell's marginal has a standard error of roughly 7 points and the nine-cell overall of
  roughly 2–3. The seed table above shows single-seed overall ranges up to about 6 points wide. A
  ±5 target is therefore about two standard errors; hitting it on one seed is partly luck, which is
  why candidates were chosen on multi-seed means. Per-encounter ranks for beasts near zero are
  largely noise.
- **Overfitting to the fixtures.** The encounters are simulator fixtures, not game content. The one
  sharp fixture threshold found — the pack's Direwolf Speed 110 — was deliberately *not* tuned
  around (for example by putting every fast beast just under 110). Speeds were set by identity order,
  not by fixture values. The boss-vs-swarm/pack split of the slow tanks and the pack penalty on fast
  beasts are properties of nearest-enemy targeting and charge-first movement, and will change once
  real AI, skills and encounters exist.
- **Speed has become cheap in this model**, which is why the fragile beasts' Speed was the budget
  source. If the real AI ever makes acting first valuable (kiting, first-strike kills, interrupts),
  Speed will need re-pricing and these beasts will look stronger than here.
- **PvP (secondary section) moved the other way.** With more bulk on the fast beasts, the 1v1
  round-robin now favours them (`elemental`: Griffin 74%, Basilisk 67%, Thunderbird 63%) and
  disfavours the support casters and control (Kirin 22%, Frost Wyrm 30%). PvP was not a target; if
  PvP ever ships, it needs its own pass.
- **Calibration.** One cell still misses the 50% calibration target by more than 10 points
  (`neutral` `swarm` L1, 61.0%), down from two in the baseline. No PvE battle stalemates.
- **Identity checks**, per the approved roster: Phoenix and Thunderbird keep the lowest combined
  HP + Def + SpD (265 each, against 325–415 for the four tanks); Leviathan keeps high HP / Def (125 /
  125) and low Speed (55); Golem the highest HP and Def, lowest Speed and Move (but not low Attack);
  Griffin the second-highest Speed with Move 5; Thunderbird the highest Speed with high Attack;
  Frost Wyrm high Def / SpD (120 / 115); Treant high HP / SpD (130 / 120) and low Speed (50);
  Tarasque the highest Attack (140) with high Defense (130); Kirin high SpA / SpD (135 / 120) and the
  lowest Attack; Basilisk the highest SpA (150) with Move 5 and low Defense.

## After the ATB turn-order change

The turn order moved from a round-based initiative queue (everyone once per round, fastest first) to
an ATB speed gauge (fill by Speed, act at 1000, overflow carried), so twice the Speed is now twice
the turns. The roster was **not** re-tuned; `tuned-report.md` was regenerated with the same stats,
kit, fixtures and seed. Overall marginal clear rate, levels and encounters averaged (points):

| Beast | Spe | `elemental` before | `elemental` ATB | `neutral` before | `neutral` ATB |
| --- | ---: | ---: | ---: | ---: | ---: |
| Thunderbird | 120 | −1.6 | +6.0 | −8.0 | +2.4 |
| Griffin | 115 | +0.6 | +14.8 | −7.4 | +9.7 |
| Basilisk | 110 | +2.1 | +17.4 | +0.8 | +13.4 |
| Phoenix | 100 | −0.7 | +13.2 | +3.7 | +14.1 |
| Kirin | 95 | +1.7 | +12.3 | −0.3 | +17.2 |
| Frost Wyrm | 80 | +3.4 | +2.9 | +4.8 | +0.7 |
| Tarasque | 75 | −2.3 | +2.0 | +3.2 | +1.8 |
| Leviathan | 55 | +1.7 | −15.4 | +2.3 | −15.4 |
| Treant | 50 | −1.0 | −17.6 | +0.4 | −14.6 |
| Golem | 40 | −3.8 | −35.5 | +0.6 | −29.3 |

The swing follows Speed almost monotonically: the five fastest beasts gain, the three slowest lose
15–35 points (in `elemental` mode all three are bottom three against every encounter: no niche). The "fastest fragile beast
takes the pack's focus" finding above no longer dominates: Thunderbird and Griffin rose out of the
`neutral` penalty. The caveat above that Speed was cheap has come true in reverse — Speed is now the
most valuable stat, and the ±5% budget does not price it. Re-pricing Speed (for example a smaller
Speed spread, or charging it more of the budget) is the next tuning question; it is deliberately
not done in the change that introduced the gauge.

## After combat stances

`BattleTurnExecutor` gained combat stances (design doc, "Combat stances"): every species is a
Vanguard, Ranged or Skirmisher unit (Ranged: Phoenix, Kirin, Basilisk; Skirmisher: Thunderbird,
Griffin; the five tanks and bruisers Vanguard). Ranged units never walk into melee (Strike fires only
on an adjacent enemy), Ranged and Skirmisher units avoid crowded stop tiles and spend leftover
movement backing off to the edge of their reach, and a Vanguard's equally short approaches prefer
tiles next to its fragile allies. The fixtures changed with it: the wisps are Ranged and the stingers
Skirmishers (their sting is range 1, which a Ranged unit would never walk in for), and the wisps' bolt
and the stingers' sting now aim at the beast with the lowest maximum HP instead of the nearest one.
The roster was **not** re-tuned; `tuned-report.md` was regenerated with the same stats, kit and seed.
Overall marginal clear rate, levels and encounters averaged (points; "ATB" is the previous section's
"ATB" column):

| Beast | Stance | `elemental` ATB | `elemental` stances | `neutral` ATB | `neutral` stances |
| --- | --- | ---: | ---: | ---: | ---: |
| Thunderbird | Skirmisher | +6.0 | +1.6 | +2.4 | −2.4 |
| Griffin | Skirmisher | +14.8 | +24.6 | +9.7 | +23.2 |
| Basilisk | Ranged | +17.4 | +3.8 | +13.4 | −0.2 |
| Phoenix | Ranged | +13.2 | −0.4 | +14.1 | +2.2 |
| Kirin | Ranged | +12.3 | +4.7 | +17.2 | +3.4 |
| Frost Wyrm | Vanguard | +2.9 | +13.3 | +0.7 | +11.7 |
| Tarasque | Vanguard | +2.0 | +0.1 | +1.8 | +5.8 |
| Leviathan | Vanguard | −15.4 | −7.2 | −15.4 | −12.7 |
| Treant | Vanguard | −17.6 | −15.6 | −14.6 | −15.2 |
| Golem | Vanguard | −35.5 | −25.0 | −29.3 | −15.8 |

Per encounter, levels averaged, `elemental`:

| Beast | `boss` ATB | `boss` stances | `swarm` ATB | `swarm` stances | `pack` ATB | `pack` stances |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Thunderbird | +21.4 | +18.0 | +2.1 | +12.3 | −5.7 | −25.4 |
| Griffin | +18.8 | +15.3 | +8.7 | +16.3 | +16.8 | +42.1 |
| Basilisk | +15.5 | +8.7 | +8.7 | −1.6 | +28.0 | +4.4 |
| Phoenix | +18.8 | −0.5 | +13.4 | −2.9 | +7.5 | +2.4 |
| Kirin | +22.1 | +2.8 | +6.7 | −1.6 | +8.2 | +13.0 |
| Frost Wyrm | −1.1 | +12.0 | +10.7 | +16.9 | −1.1 | +11.0 |
| Tarasque | +1.6 | +11.4 | +2.8 | −9.5 | +1.6 | −1.6 |
| Leviathan | −26.9 | −24.3 | −6.5 | +11.0 | −13.0 | −8.2 |
| Treant | −27.5 | −25.7 | −14.4 | −15.5 | −11.0 | −5.6 |
| Golem | −42.7 | −17.7 | −32.3 | −25.4 | −31.5 | −32.0 |

`neutral`:

| Beast | `boss` ATB | `boss` stances | `swarm` ATB | `swarm` stances | `pack` ATB | `pack` stances |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Thunderbird | +21.4 | +18.0 | −7.0 | −11.5 | −7.1 | −13.6 |
| Griffin | +18.8 | +15.3 | +18.1 | +27.5 | −7.8 | +26.7 |
| Basilisk | +15.5 | +8.7 | +2.2 | −12.8 | +22.6 | +3.6 |
| Phoenix | +18.8 | −0.5 | +4.9 | −6.2 | +18.7 | +13.5 |
| Kirin | +22.1 | +2.8 | +12.2 | −1.6 | +17.3 | +8.9 |
| Frost Wyrm | −1.1 | +12.0 | +4.2 | +18.3 | −1.2 | +4.9 |
| Tarasque | +1.6 | +11.4 | −3.7 | −2.9 | +7.4 | +8.9 |
| Leviathan | −26.9 | −24.3 | −7.0 | +3.7 | −12.4 | −17.6 |
| Treant | −27.5 | −25.7 | −1.7 | −4.9 | −14.4 | −14.9 |
| Golem | −42.7 | −17.7 | −22.2 | −9.5 | −23.0 | −20.2 |

The boss column changes only with the beasts' stances (the Colossus stays a nearest-target
Vanguard). What moved:

- **The three Ranged beasts lost most of their ATB lead** (overall +12.3 … +17.4 down to −0.4 … +4.7),
  and the most against the boss (Phoenix +18.8 → −0.5, Kirin +22.1 → +2.8). With the standard kit a
  Ranged beast gives up Strike, half its single-target damage, unless an enemy walks up to it, and
  its Burst (radius 2) rarely reaches anyone from range 3. The kit-parity table shows it: the
  physical share of single-target power fell from 49–52% to 37–47%, so `Attack` is now worth less
  than `SpecialAttack` across the roster. That is a property of the one-kit simulator (a real Ranged
  beast would carry ranged skills), and `StrikePower` was derived for a roster that always walks in.
- **The spread narrowed.** `elemental` overall is −25.0 … +24.6 (from −35.5 … +17.4) and `neutral`
  −15.8 … +23.2 (from −29.3 … +17.2). The slow tanks gained from Vanguard screening and from no
  longer being the only ones in front (Golem +10.5 / +13.5, Leviathan +8.2 / +2.7); Frost Wyrm is now
  top 3 against every encounter in `elemental` mode (no weakness).
- **Griffin now leads** (+24.6 / +23.2). A Skirmisher still gets full value from Strike, then backs
  out of reach, and it has the most movement for it (Move 5). Its `pack` jump (+16.8 → +42.1
  `elemental`) is mostly the fixture's new targeting, not its stance: rerunning with the stances on
  but the old fixtures (every enemy Vanguard, nearest-target) gives Griffin +14.0 / +8.9 overall and
  +12.4 / +2.8 against the pack. The wisps now aim at the lowest *maximum* HP: Frost Wyrm (95)
  first, then four beasts at exactly 100 (Phoenix, Thunderbird, Kirin, Basilisk; ties go by unit
  id), so the pack's ranged focus lands on Griffin (105) only when none of those five is on the
  team (4 of Griffin's 84 teams). Thunderbird is the mirror image (`pack` −5.7 → −25.4). That is a
  threshold effect of stat targeting on a flat roster, and a reason to treat single encounter cells
  with suspicion until authored encounters exist.

Beast-only split (stances on, fixtures as before this change), overall `elemental` / `neutral`:
Thunderbird +2.1 / +1.4, Griffin +14.0 / +8.9, Basilisk +2.6 / −2.2, Phoenix −2.3 / −0.8, Kirin
+0.1 / +4.7, Frost Wyrm +14.9 / +12.4, Tarasque +7.6 / +15.0, Leviathan −9.8 / −14.9, Treant −10.7 /
−10.8, Golem −18.6 / −13.6. No PvE battle stalemates in either run, and none in PvP. Re-tuning for stances (and
re-deriving `StrikePower`, or giving Ranged beasts a kit of their own) is the next tuning question
and is not part of this change.

## After variance and crits

Damage gained a uniform 90–110% variance roll and a per-beast critical-hit chance (x1.5), by user
decision (design doc, "Variance and critical hits"; research in
[`research-crit-variance-speed.md`](research-crit-variance-speed.md)). The only stat change is the
new, user-approved `CritChance` on every species (Thunderbird 15, Basilisk 12, Phoenix 10, Griffin 8,
Tarasque 6, Kirin 5, Frost Wyrm 5, Leviathan 3, Treant 3, Golem 2) and on the fixtures (5 each, the
direwolves 8); the six combat stats, kit, fixtures otherwise, and seed are unchanged, and nothing was
re-tuned. Battles are now random but seeded, so the simulator runs every team and fight 5 times
(`--samples 5`, 1050 battles per calibration step, about 130 s for the default run on 8 threads)
and calibrates on the sampled clear rate. `tuned-report.md` was regenerated; its new crit table
shows every beast's observed crit rate within 0.1 points of its authored chance and its average roll
multiplier on `1 + 0.5 x chance` (1.009 for Golem to 1.075 for Thunderbird).

Overall marginal clear rate, levels and encounters averaged (points; "stances" is the previous
section's "stances" column, one deterministic battle per team; "+ crits" is the mean over 5 seeded
samples per team):

| Beast | Stance | Crit | `elemental` stances | `elemental` + crits | `neutral` stances | `neutral` + crits |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Thunderbird | Skirmisher | 15% | +1.6 | +3.8 | −2.4 | +0.6 |
| Griffin | Skirmisher | 8% | +24.6 | +26.3 | +23.2 | +22.6 |
| Basilisk | Ranged | 12% | +3.8 | +4.1 | −0.2 | +0.1 |
| Phoenix | Ranged | 10% | −0.4 | −1.4 | +2.2 | +0.6 |
| Kirin | Ranged | 5% | +4.7 | +2.8 | +3.4 | +5.6 |
| Frost Wyrm | Vanguard | 5% | +13.3 | +11.5 | +11.7 | +9.3 |
| Tarasque | Vanguard | 6% | +0.1 | −0.4 | +5.8 | +3.5 |
| Leviathan | Vanguard | 3% | −7.2 | −9.7 | −12.7 | −12.3 |
| Treant | Vanguard | 3% | −15.6 | −14.1 | −15.2 | −11.6 |
| Golem | Vanguard | 2% | −25.0 | −22.8 | −15.8 | −18.5 |

Per encounter, levels averaged:

`elemental`:

| Beast | `boss` stances | `boss` + crits | `swarm` stances | `swarm` + crits | `pack` stances | `pack` + crits |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Thunderbird | +18.0 | +20.7 | +12.3 | +8.1 | −25.4 | −17.5 |
| Griffin | +15.3 | +18.1 | +16.3 | +19.7 | +42.1 | +41.1 |
| Basilisk | +8.7 | +9.6 | −1.6 | +0.7 | +4.4 | +2.1 |
| Phoenix | −0.5 | −0.4 | −2.9 | −2.4 | +2.4 | −1.2 |
| Kirin | +2.8 | −0.4 | −1.6 | −2.4 | +13.0 | +11.2 |
| Frost Wyrm | +12.0 | +3.8 | +16.9 | +19.3 | +11.0 | +11.3 |
| Tarasque | +11.4 | +7.0 | −9.5 | −5.6 | −1.6 | −2.5 |
| Leviathan | −24.3 | −22.5 | +11.0 | +4.2 | −8.2 | −10.9 |
| Treant | −25.7 | −22.0 | −15.5 | −13.4 | −5.6 | −6.9 |
| Golem | −17.7 | −13.7 | −25.4 | −28.1 | −32.0 | −26.7 |

`neutral`:

| Beast | `boss` stances | `boss` + crits | `swarm` stances | `swarm` + crits | `pack` stances | `pack` + crits |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Thunderbird | +18.0 | +17.7 | −11.5 | −6.3 | −13.6 | −9.5 |
| Griffin | +15.3 | +22.8 | +27.5 | +26.6 | +26.7 | +18.4 |
| Basilisk | +8.7 | +8.9 | −12.8 | −12.4 | +3.6 | +3.8 |
| Phoenix | −0.5 | −1.1 | −6.2 | −6.2 | +13.5 | +9.0 |
| Kirin | +2.8 | −0.2 | −1.6 | +1.7 | +8.9 | +15.4 |
| Frost Wyrm | +12.0 | +4.6 | +18.3 | +16.5 | +4.9 | +6.8 |
| Tarasque | +11.4 | +5.8 | −2.9 | −2.9 | +8.9 | +7.7 |
| Leviathan | −24.3 | −21.3 | +3.7 | +2.9 | −17.6 | −18.3 |
| Treant | −25.7 | −21.3 | −4.9 | −3.4 | −14.9 | −10.0 |
| Golem | −17.7 | −15.8 | −9.5 | −16.4 | −20.2 | −23.4 |

**Noise.** Rerunning the new build with a second base seed (`--seed 777`, same 5 samples) moves a
beast's overall marginal by 2.1 points on average and at most 3.7 (Treant, `elemental`) and 4.8
(Griffin, `neutral`); single encounter cells move by up to 8.9. (A different base seed also
reshuffles team slots and which half of the teams wins initiative ties, so this is an upper bound on
roll noise alone.) Every before → after change in the overall table is within that band (largest
3.6: Treant `neutral`, Golem `elemental` +2.2), so **variance and crits at these values do not
measurably move the roster's balance**; the ranking and the main flags (Griffin and Frost Wyrm high;
Golem, Treant and Leviathan low; Golem no niche, Griffin no weakness) are the same as with stances
alone. Only threshold flags changed: Frost Wyrm is no longer top 3 against every encounter in
`elemental` mode (its `boss` marginal +12.0 → +3.8), and in `neutral` mode Tarasque (+5.8 → +3.5)
drops under the ±5 flag while Kirin (+3.4 → +5.6) crosses it. Sampling also removed the one
calibration miss (`neutral` `pack` L1, 39.0% before): with 5 samples per team the clear-rate curve
has no step that a multiplier cannot split. That is the expected size: the highest crit chance adds 7.5% to Thunderbird's
average damage, about what one or two points of `Attack` share do, and the variance roll averages
out. The one visible effect is on the Thunderbird / Leviathan–Treant tails of single encounter cells
(for example Thunderbird's `pack` −25.4 → −17.5 `elemental`), which is within the per-cell noise.
PvP 1v1 moves no more than a few points per beast (`neutral`: Thunderbird 88.9% → 90.0%, Griffin
92.6% → 89.3%); no PvE or PvP battle stalemates. The standard kit's physical share is unchanged
(37–46%).

## After mixed encounters + CurrentHp targeting + fair ranged kit

Three changes, none of them a roster change (stats, stances and crit chances are as in the previous
section; nothing was re-tuned):

- **Runtime: `SkillTargetingCriterion.CurrentHp`** (appended, value 3). It compares the HP a unit has
  left, honours `SkillTargetingOrder`, breaks ties on the unit id and works in both the range-limited
  pick and `PickFocusIgnoringRange` (design doc, decision 4). `Stat` + `HP` compared the stat block's
  maximum, so a "lowest HP" enemy never tracked damage taken: with four beasts at exactly 100 max HP,
  Griffin (105) was focused only on the 4 of its 84 teams without them. Every "pick off the weakest"
  fixture skill now uses `CurrentHp` + `Lowest` (the fixed set's wisps and stingers; the generated
  stalker, caster and champion hex).
- **Fair standard kit for Ranged beasts.** A Ranged beast never walks into melee, so it only fired
  Strike at an enemy already adjacent, and the physical share of single-target power had fallen to
  37-47%. Ranged beasts now carry **Shot** (Physical, range 3, Blast's cooldown) instead of Strike,
  and both physical powers were re-derived from fire ratios measured over the new default run
  (Strike 55 → 57, Shot 41; see the simulator README). Kit parity, physical share of single-target
  power (every shape and level):

  | Kit mode | Vanguard (Strike) | Skirmisher (Strike) | Ranged (Shot) | All |
  | --- | ---: | ---: | ---: | ---: |
  | `elemental` | 50.2% | 50.2% | 49.7% | 50.0% |
  | `neutral` | 50.1% | 50.6% | 49.7% | 50.0% |

  Per shape it is 49.2-51.9%; on the fixed set (`--encounter-set fixed`) 49.9-52.0% per stance.
- **Mixed random encounters.** The default PvE run no longer fights the three fixed encounters but
  4 shapes x 8 generated compositions: `solo` (one giant), `elite` (a giant + 2 escorts, or 2
  champions + 1-2 escorts), `squad` (4-6 mixed standard enemies) and `horde` (16-24: 14-20 swarm +
  2-4 archers / casters), drawn from a pool of nine enemy types under a per-shape threat budget, with
  per-composition element schemes (one element, per type, per unit, none) dealt from a shuffled deck
  so all ten elements appear. Enemy speeds are 95-105. One sample per team and composition (8
  battles per team per shape, against 5 per encounter before); the default run takes about 150 s on
  8 threads. The simulator README has the pool, shapes and generator rules; the report lists every
  composition.

Overall marginal clear rate, levels and encounters/shapes averaged (points). **Previous** = the
previous section's "+ crits" column (fixed encounters, old kit, max-HP targeting). **Fixed, new** =
the fixed encounters with the Shot kit and `CurrentHp` targeting (a separate run, not committed), so
the first step isolates the kit and targeting and the second the encounters. **Generated** = the new
default, committed in `tuned-report.md`.

| Beast | Stance | `elemental` previous | `elemental` fixed, new | `elemental` generated | `neutral` previous | `neutral` fixed, new | `neutral` generated |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Phoenix | Ranged | −1.4 | +11.1 | **+16.2** | +0.6 | +14.1 | **+20.1** |
| Basilisk | Ranged | +4.1 | +17.0 | +4.1 | +0.1 | +13.6 | **+10.0** |
| Kirin | Ranged | +2.8 | −0.3 | +2.4 | +5.6 | +2.4 | +4.5 |
| Thunderbird | Skirmisher | +3.8 | +3.9 | **+6.9** | +0.6 | −1.1 | +2.1 |
| Griffin | Skirmisher | +26.3 | +12.8 | +3.1 | +22.6 | +10.0 | **+10.1** |
| Frost Wyrm | Vanguard | +11.5 | +7.9 | **+5.3** | +9.3 | +6.4 | +3.6 |
| Tarasque | Vanguard | −0.4 | −2.2 | +0.7 | +3.5 | −0.4 | +3.2 |
| Leviathan | Vanguard | −9.7 | −11.1 | **−9.7** | −12.3 | −14.7 | **−15.0** |
| Treant | Vanguard | −14.1 | −14.3 | **−13.8** | −11.6 | −12.3 | **−16.8** |
| Golem | Vanguard | −22.8 | −24.7 | **−15.2** | −18.5 | −18.0 | **−21.8** |

Bold = outside the ±5 flag in the committed report. Spread: `elemental` −15.2 … +16.2 (from −22.8 …
+26.3), `neutral` −21.8 … +20.1 (from −18.5 … +22.6).

Per shape, levels averaged (generated, committed report):

`elemental`:

| Beast | `solo` | `elite` | `squad` | `horde` | Overall |
| --- | ---: | ---: | ---: | ---: | ---: |
| Phoenix | +15.3 | +18.2 | +21.2 | +10.0 | +16.2 |
| Thunderbird | +19.5 | +8.9 | +4.3 | −5.0 | +6.9 |
| Frost Wyrm | −6.0 | +0.8 | +10.4 | +15.8 | +5.3 |
| Basilisk | +4.4 | +6.2 | +5.7 | +0.4 | +4.1 |
| Griffin | +7.7 | +5.1 | +3.7 | −4.2 | +3.1 |
| Kirin | +0.9 | +1.0 | +3.4 | +4.1 | +2.4 |
| Tarasque | −5.3 | −8.7 | +5.5 | +11.4 | +0.7 |
| Leviathan | −8.7 | −5.8 | −18.8 | −5.6 | −9.7 |
| Treant | −13.4 | −15.0 | −16.8 | −9.8 | −13.8 |
| Golem | −14.3 | −10.7 | −18.6 | −17.1 | −15.2 |

`neutral`:

| Beast | `solo` | `elite` | `squad` | `horde` | Overall |
| --- | ---: | ---: | ---: | ---: | ---: |
| Phoenix | +17.2 | +25.3 | +25.5 | +12.3 | +20.1 |
| Griffin | +22.1 | +5.8 | +11.2 | +1.3 | +10.1 |
| Basilisk | +14.4 | +13.7 | +6.7 | +5.4 | +10.0 |
| Kirin | +3.9 | +6.4 | +0.6 | +7.1 | +4.5 |
| Frost Wyrm | −5.4 | +0.4 | +11.5 | +7.7 | +3.6 |
| Tarasque | −4.1 | −3.6 | +4.4 | +16.2 | +3.2 |
| Thunderbird | +13.9 | +11.1 | −0.1 | −16.7 | +2.1 |
| Leviathan | −23.3 | −17.1 | −13.2 | −6.2 | −15.0 |
| Treant | −18.7 | −17.8 | −16.9 | −13.9 | −16.8 |
| Golem | −19.9 | −24.2 | −29.7 | −13.3 | −21.8 |

Flags in the committed report: Phoenix no weakness (top 3 in every shape) in both modes; Golem and
Treant no niche (bottom 3 in every shape) in both modes. No stalemates, no calibration misses, no
kit-parity miss.

What moved, and why:

- **The Ranged beasts regained what stances took from them, and Phoenix overshot.** With Shot,
  a Ranged beast's `Attack` counts again. Phoenix has the roster's highest combined offence (Atk 125,
  SpA 140) and was paying for Atk it could not use; it is now first overall in both modes and top 3
  in every shape. Basilisk (Atk 95, SpA 150) gains in both modes on the fixed set; on the generated
  set it gains in `neutral` only (+0.1 → +10.0); in `elemental` mode it is flat (+4.1), which this
  run does not explain (its Dark kit is resisted by nothing, and is super-effective against the
  dominant element of only one composition per level; the per-shape noise is up to 12.6 points). Kirin (Atk 50) gains nothing, as expected: the
  fix gives weight to `Attack`, and Kirin has little.
- **Griffin's lead was mostly the max-HP targeting threshold.** With `CurrentHp`, the wisps and
  stingers no longer skip Griffin for whichever beast was built with 100 HP, and its fixed-set
  overall falls +26.3 → +12.8 (`elemental`); against generated compositions it is +3.1 / +10.1.
  Thunderbird, its mirror image under max-HP targeting, recovers against the fixed pack (−17.5 →
  −13.2) but stays weak against the horde (−5.0 / −16.7): a fast, fragile Skirmisher that dives into
  twenty small melee enemies.
- **The slow Vanguards are still the problem.** Golem, Treant and Leviathan are bottom three almost
  everywhere; they have the lowest speeds (40-55 against 75-120) and, in `elemental` mode, lose most
  where their element is resisted. That is the Speed question the next deliverable (re-tuning with a
  speed spread of about 15%) addresses; this change does not touch it.
- **Shapes show niches.** Frost Wyrm and Tarasque lead against the horde and squad but are below zero
  against the giant; Thunderbird and Griffin lead against the giant and fall off against the horde.
  That is the direction's "different shapes reward different stat lines".
- **Elements now matter per battle.** The `elemental` element-matchup view (report, "Element
  matchups") shows every beast gaining a great deal where its element is strong against a
  composition's dominant element (Phoenix +59.9, Griffin +47.2, Thunderbird +45.1 on 3-5
  compositions per level and mode) and losing where it is resisted (Leviathan −33.0, Treant −33.8,
  Golem −30.1). The buckets are small, so this is direction rather than measurement, but it is the
  first time the simulator exercises the element chart against varied enemy elements: the fixed
  boss was elementless and the fixed swarm cycled all ten.

**Compositions.** Per shape and level, a single composition's clear rate at its shape's calibrated
multiplier ranges from about 15% to 80% (most within 30-70%); `solo` in `neutral` mode is 47-56%,
since its compositions differ only in the giant's element. The threat weights were fitted to
per-composition clear rates (logit fit on type counts, `neutral`, level 50, 24 compositions per
shape): the first pool had a weak stalker (about half a standard enemy) and champions far weaker than
their weight, so the stalker (HP 100 → 125, power 48 → 55) and champion (HP 600 → 800) were raised
and the brute's threat set to 2.75. The elite shape went from 2-3 escorts to 2 (a giant) or 1-2 (two
champions), since the third escort was worth about 0.8 logit on its own. The swarm is two single-skill
types (swarmling, physical; stingling, special) because a two-skill swarm unit doubled the
damage-floor hits and the level-1 horde could not be calibrated below 35% clear even at the minimum
multiplier.

**Noise.** Rerunning the default PvE run with `--seed 777` (which also draws different compositions,
so it measures composition sampling as well as roll noise) moves a beast's overall marginal by 2.0
points on average and at most 4.8 (Phoenix +16.2 → +21.0) in `elemental` mode, and by 1.2 on average
and at most 3.2 (Treant) in `neutral`. Single shape cells move by 4.2 on average and up to 12.6 in
`elemental` (where which elements are drawn matters most) and 2.0 / 5.8 in `neutral`. That is about
the previous fixed-encounter run's overall noise (2.1 mean, 4.8 max) with 1 sample per composition
instead of 5 per encounter; raise `--compositions` for tighter per-shape and element-matchup
numbers. Every change in the overall table above larger than about 5 points is outside the noise.

## Retune for ATB + stances + crits + mixed encounters

The second tuning pass. Only `beast-roster.json` base stats changed (HP, Atk, Def, SpA, SpD, Spe).
`MoveRange` and `CritChance` were available as levers but end unchanged, and so are stances, growth
curves, the standard kit, the encounter pool and shapes, the damage formula, the element chart and
all Runtime code. `tuned-report.md` is regenerated on the new stats (default arguments).

**The speed band (user decision).** Under the ATB gauge twice the Speed is twice the turns. The
first pass's 40–120 spread therefore gave Thunderbird three turns for every one of Golem's, and the
slow Vanguards were bottom three almost everywhere. The user expects the fastest and slowest beasts
to differ by about 10–15%, so base Speed now spans **92–105 (1.14×)**. This replaces the "~2.5–3×"
lead default in [`research-crit-variance-speed.md`](research-crit-variance-speed.md) §5, which that
research marked unsourced (§4: no source gives a target ratio) and left for the simulator to
validate. The archetypes' speed **order** is kept. `BeastRosterTests` now pins both the band
(fastest / slowest ≤ 1.15) and the order (Thunderbird ≥ Griffin ≥ Basilisk ≥ Phoenix ≥ Kirin ≥
Frost Wyrm ≥ Tarasque ≥ Leviathan ≥ Treant > Golem), so neither can widen silently.

### Targets

Primary measure: the generated encounter set (the default), **mean of three base seeds** (12345,
777, 4242). Each seed also redraws the 32 compositions, so the mean covers composition sampling as
well as battle rolls.

1. Overall marginal within ±5 for every beast in `elemental` mode; aim for ±7 in `neutral`.
2. Every beast top 3 in at least one shape (`elemental` primary).
3. No beast top 3 in every shape.
4. Speed spread ≤ 1.15× with the approved order; six-stat total 570–630; `MoveRange` 2–5;
   `CritChance` 0–25 with glass cannons and assassins high and tanks low; every stat ≥ 1; `medium`
   curve for all; archetypes preserved.

### Stats, before → after

Max-level base stats. Move and Crit are outside the six-stat budget and did not change.

| Beast | HP | Atk | Def | SpA | SpD | Spe | Total | Move | Crit |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Thunderbird | 100 → **110** | 125 → **117** | 80 → **88** | 120 → **114** | 85 → **91** | 120 → **105** | 630 → **625** | 4 | 15% |
| Griffin | 105 → **106** | 115 → **112** | 90 → **91** | 90 → **91** | 90 → **91** | 115 → **104** | 605 → **595** | 5 | 8% |
| Basilisk | 100 → **103** | 95 → **94** | 80 → **79** | 150 → **153** | 95 → **94** | 110 → **102** | 630 → **625** | 5 | 12% |
| Phoenix | 100 → **92** | 125 → **102** | 75 → **70** | 140 → **120** | 90 → **85** | 100 → **101** | 630 → **570** | 4 | 10% |
| Kirin | 100 → **115** | 50 → **51** | 80 → **90** | 135 → **150** | 120 → **124** | 95 → **100** | 580 → **630** | 4 | 5% |
| Frost Wyrm | 95 → **98** | 75 → **74** | 120 → **124** | 100 → **103** | 115 → **118** | 80 → **98** | 585 → **615** | 3 | 5% |
| Tarasque | 115 → **112** | 140 → **137** | 130 → **127** | 55 → **54** | 80 → **78** | 75 → **97** | 595 → **605** | 3 | 6% |
| Leviathan | 125 → **122** | 85 → **86** | 125 → **126** | 85 → **86** | 95 → **100** | 55 → **95** | 570 → **615** | 3 | 3% |
| Treant | 130 → **134** | 85 → **87** | 95 → **98** | 90 → **93** | 120 → **124** | 50 → **94** | 570 → **630** | 3 | 3% |
| Golem | 160 → **146** | 105 → **95** | 150 → **137** | 70 → **64** | 105 → **96** | 40 → **92** | 630 → **630** | 2 | 2% |

In one line each:

- **Speed collapsed into the band, and the slow beasts paid for it.** Golem, Treant and Leviathan
  gained 40–52 Speed each. Golem stayed at the 630 ceiling, so its other stats scaled down about 9%.
  Leviathan and Treant rose to 615 / 630 and their other stats barely moved.
- **Thunderbird took the freed Speed as bulk.** Proportional rescaling left it at −6.8 (`elemental`)
  and −42.8 against the horde, so 10 HP and about 7 in each defence came out of Atk and SpA. It is
  still the most fragile Skirmisher per point of offence, with the highest crit.
- **Phoenix went to the 570 floor.** It was +19.8 (`elemental`, 3-seed mean) and top 3 in every
  shape. Its offence fell 25 / 20 points and its bulk stayed the roster's lowest (HP 92, Def 70, and
  HP + Def + SpD the lowest of the ten). Even at the floor it remains a strong `elemental` beast
  (its Fire kit; see below).
- **Kirin, Frost Wyrm and Basilisk were buffed into the room left by Phoenix.** Kirin went to 630
  (HP, Def, SpA). Frost Wyrm went to 615 (Def, SpD, SpA, HP). Basilisk went to 625 (HP, SpA).

Archetypes, checked against the approved identities:

- **Phoenix:** lowest HP, Def and total bulk, with high combined offence (Atk 102 + SpA 120; only
  Basilisk and Thunderbird are higher).
- **Leviathan:** HP 122 (third after Golem and Treant), Def 126 (third after Golem and Tarasque).
- **Golem:** highest HP and Def, slowest, Move 2.
- **Griffin:** second-fastest, Move 5.
- **Thunderbird:** fastest, second-highest Atk, highest crit, its bulk below every Vanguard's.
- **Frost Wyrm:** high Def and SpD.
- **Treant:** second-highest HP, joint-highest SpD with Kirin.
- **Tarasque:** highest Atk, second-highest Def.
- **Kirin:** second-highest SpA, joint-highest SpD, lowest Atk.
- **Basilisk:** highest SpA, Move 5, low Def (79), second-highest crit.

The crit ordering is untouched.

### Marginal clear rate, before → after (mean of 3 seeds, generated set)

Points of clear rate, levels 1/50/100 averaged, then averaged over seeds 12345 / 777 / 4242; (n) =
rank within the shape on the mean. "Top-3 shapes" counts the four shapes. Before = the roster at
the start of this pass (HEAD before the retune), measured the same way.

**Before, `elemental`:**

| Beast | `solo` | `elite` | `squad` | `horde` | Overall | Top-3 shapes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Phoenix | +18.4 (2) | +22.0 (1) | +24.6 (1) | +14.3 (1) | +19.8 (1) | 4 |
| Thunderbird | +19.1 (1) | +8.6 (2) | +2.3 (6) | −10.2 (8) | +4.9 (2) | 2 |
| Basilisk | +4.9 (3) | +6.8 (3) | +5.2 (4) | +2.8 (5) | +4.9 (3) | 2 |
| Frost Wyrm | −5.5 (7) | −0.0 (6) | +9.3 (2) | +12.0 (2) | +4.0 (4) | 2 |
| Griffin | +2.0 (4) | +5.8 (4) | +9.1 (3) | −7.0 (7) | +2.5 (5) | 1 |
| Tarasque | −0.4 (6) | −3.6 (7) | +3.9 (5) | +9.5 (3) | +2.4 (6) | 1 |
| Kirin | +0.8 (5) | +0.6 (5) | −0.2 (7) | +5.5 (4) | +1.7 (7) | 0 |
| Leviathan | −13.7 (9) | −9.5 (8) | −12.8 (8) | −1.9 (6) | −9.5 (8) | 0 |
| Treant | −11.2 (8) | −16.2 (10) | −16.7 (9) | −11.6 (9) | −13.9 (9) | 0 |
| Golem | −14.6 (10) | −14.4 (9) | −24.9 (10) | −13.4 (10) | −16.8 (10) | 0 |

**After, `elemental`:**

| Beast | `solo` | `elite` | `squad` | `horde` | Overall | Top-3 shapes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Phoenix | +5.6 (2) | +1.9 (5) | +2.2 (3) | −0.9 (8) | +2.2 (1) | 2 |
| Frost Wyrm | −1.9 (7) | +3.2 (3) | +1.2 (4) | +3.3 (5) | +1.5 (2) | 1 |
| Basilisk | +1.4 (4) | +3.4 (2) | +0.7 (5) | +0.3 (7) | +1.5 (3) | 1 |
| Leviathan | −3.8 (8) | +1.9 (4) | −1.5 (8) | +8.9 (1) | +1.4 (4) | 1 |
| Tarasque | +1.2 (5) | −1.1 (8) | +7.0 (1) | −6.3 (9) | +0.2 (5) | 1 |
| Golem | −5.7 (9) | −0.6 (6) | −1.2 (7) | +6.5 (3) | −0.3 (6) | 1 |
| Kirin | +2.0 (3) | −1.0 (7) | −6.0 (10) | +1.2 (6) | −0.9 (7) | 1 |
| Griffin | −8.2 (10) | −4.5 (9) | +0.4 (6) | +8.2 (2) | −1.0 (8) | 1 |
| Treant | −0.3 (6) | −7.0 (10) | −5.0 (9) | +6.1 (4) | −1.5 (9) | 0 |
| Thunderbird | +9.8 (1) | +3.7 (1) | +2.2 (2) | −27.4 (10) | −2.9 (10) | 3 |

**Before, `neutral`:**

| Beast | `solo` | `elite` | `squad` | `horde` | Overall | Top-3 shapes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Phoenix | +15.9 (2) | +24.3 (1) | +23.8 (1) | +13.8 (2) | +19.4 (1) | 4 |
| Basilisk | +14.9 (4) | +12.8 (2) | +7.6 (4) | +6.8 (5) | +10.5 (2) | 1 |
| Griffin | +19.0 (1) | +7.3 (4) | +13.6 (2) | −3.3 (6) | +9.1 (3) | 2 |
| Kirin | +2.1 (5) | +4.1 (5) | +2.3 (6) | +7.5 (4) | +4.0 (4) | 0 |
| Tarasque | −3.7 (6) | −1.7 (7) | +3.7 (5) | +14.7 (1) | +3.2 (5) | 1 |
| Frost Wyrm | −8.2 (7) | −0.7 (6) | +11.0 (3) | +8.4 (3) | +2.6 (6) | 2 |
| Thunderbird | +15.0 (3) | +11.4 (3) | −1.0 (7) | −16.9 (10) | +2.2 (7) | 2 |
| Leviathan | −19.3 (9) | −16.7 (9) | −13.5 (8) | −5.0 (7) | −13.6 (8) | 0 |
| Treant | −15.1 (8) | −16.1 (8) | −16.1 (9) | −12.2 (8) | −14.9 (9) | 0 |
| Golem | −20.5 (10) | −24.7 (10) | −31.3 (10) | −13.7 (9) | −22.6 (10) | 0 |

**After, `neutral`:**

| Beast | `solo` | `elite` | `squad` | `horde` | Overall | Top-3 shapes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Basilisk | +7.5 (3) | +10.7 (1) | +2.8 (2) | +1.6 (5) | +5.7 (1) | 3 |
| Griffin | +4.4 (4) | −0.2 (6) | +5.5 (1) | +8.0 (4) | +4.4 (2) | 1 |
| Frost Wyrm | +2.8 (5) | +3.3 (3) | +1.9 (4) | +0.2 (7) | +2.0 (3) | 1 |
| Kirin | +8.6 (2) | +3.1 (4) | −6.0 (10) | +1.4 (6) | +1.8 (4) | 1 |
| Treant | −10.7 (9) | −3.0 (7) | −0.8 (6) | +14.1 (1) | −0.1 (5) | 1 |
| Leviathan | −5.6 (8) | −3.2 (8) | −1.4 (8) | +8.2 (3) | −0.5 (6) | 1 |
| Tarasque | −1.5 (7) | +0.9 (5) | +2.5 (3) | −4.9 (9) | −0.8 (7) | 1 |
| Phoenix | −0.5 (6) | −3.9 (9) | −4.0 (9) | −1.8 (8) | −2.5 (8) | 0 |
| Golem | −15.9 (10) | −12.9 (10) | −1.1 (7) | +12.9 (2) | −4.2 (9) | 1 |
| Thunderbird | +10.8 (1) | +5.2 (2) | +0.6 (5) | −39.7 (10) | −5.8 (10) | 2 |

Overall spread on the mean: `elemental` −2.9 … +2.2 (from −16.8 … +19.8), `neutral` −5.8 … +5.7
(from −22.6 … +19.4).

Overall marginal per seed (12345 / 777 / 4242). The first column is the committed report.

| Beast | `elemental` | `neutral` |
| --- | --- | --- |
| Thunderbird | −0.4 / −4.5 / −3.9 | −3.5 / −7.2 / −6.6 |
| Griffin | −1.8 / −0.3 / −0.9 | +1.8 / +6.3 / +5.1 |
| Basilisk | +0.6 / +2.2 / +1.6 | +6.4 / +5.8 / +4.8 |
| Phoenix | −2.1 / +2.2 / +6.4 | −3.0 / −3.0 / −1.6 |
| Kirin | −0.8 / −0.9 / −1.1 | +2.5 / +2.1 / +0.7 |
| Frost Wyrm | +2.9 / +0.4 / +1.1 | +1.8 / +2.2 / +2.1 |
| Tarasque | −2.5 / +1.6 / +1.5 | −0.3 / −1.8 / −0.2 |
| Leviathan | +2.6 / +1.6 / −0.1 | −1.2 / −0.6 / +0.3 |
| Treant | +0.5 / −4.2 / −0.9 | −0.2 / −0.4 / +0.3 |
| Golem | +1.0 / +2.0 / −3.8 | −4.5 / −3.3 / −4.9 |

The committed default-seed report has `elemental` within −2.5 … +2.9 (no flags). In `neutral` it
is −4.5 … +6.4, where Basilisk's +6.4 is the report's only flag (outside its ±5 flag, inside this
pass's ±7 `neutral` aim). Its per-shape ranks differ from the mean's, as the noise below predicts:
at seed 12345 Golem is first in `squad` and Leviathan first in `elite`. Niches are less stable
than the overall marginals. Per single seed, the beasts with no top-3 shape in `elemental` are
Basilisk, Kirin and Griffin (12345), Treant (777) and Basilisk, Griffin and Kirin (4242). In
`neutral` they are Phoenix (12345), Leviathan, Tarasque and Phoenix (777) and Leviathan and Phoenix
(4242). No seed has a beast top 3 in every shape.

### Targets: met and missed (on the 3-seed mean)

| # | Target | Result |
| --- | --- | --- |
| 1 | `elemental` overall within ±5 | **Met**: −2.9 … +2.2 (every seed: −4.5 … +6.4; Phoenix +6.4 at seed 4242 is the only single-seed miss). |
| 1 | `neutral` overall within about ±7 | **Met**: −5.8 … +5.7 (every seed: −7.2 … +6.4; Thunderbird −7.2 at seed 777 is the only single-seed miss). |
| 2 | Every beast top 3 in ≥ 1 shape (`elemental`) | **Missed by one beast** on the mean: nine of ten. Treant is 4th against the horde (+6.1 against Golem's +6.5, 0.4 points; a shape cell's seed-to-seed noise is several points). Per single seed, one to three beasts miss (see above), so this target is not robust at the current noise level. |
| 2 | Same, `neutral` (secondary) | Nine of ten; Phoenix has none (best: 6th against the giant). |
| 3 | No beast top 3 in every shape | **Met** in both modes. Thunderbird is top 3 in three shapes in `elemental` and last against the horde. |
| 4 | Speed ≤ 1.15×, order, budget, bands, archetypes | **Met**: 92–105 (1.141×), order as approved, totals 570–630, Move and Crit unchanged; the roster tests pin the band and order. |

### What binds

- **Target 2 is a counting problem, and the horde is the only seat for bulk.** Four shapes give
  twelve top-3 slots for ten beasts, so at most two beasts can hold a second slot. Thunderbird holds
  three: first against the giant, the elite and nearly the squad, and last by far against the horde,
  where the fastest fragile diver meets twenty melee enemies first. Every configuration tried that
  kept its overall marginal in range kept those three (at 620 it fell to one slot, but `neutral`
  fell to −8.1). That leaves exactly one slot per other beast with no slack. Four beasts compete
  for the horde's three slots and do not reach the others: Leviathan, Golem, Treant and Griffin
  (with Frost Wyrm close behind). Under nearest-enemy targeting and no taunt or threat mechanic, a
  Vanguard's bulk only pays when many enemies swing at it. Across iterations c3–c10 exactly one of
  those four missed each time, and which one depended on 1–5 points of budget. Getting a tank a
  niche against the giant or the elite needs a mechanic (taunt/guard, planned with authored skills),
  not stats. This is the design question for the user.
- **Phoenix is at the budget floor.** At 570 it is still the best `elemental` beast on the mean
  (+2.2; up to +6.4 on one seed), while it is −2.5 in `neutral`. The gap is the element chart and
  the element draw, not its stats; these runs do not isolate which matchups carry it (the report's
  element-matchup view has the direction, on small buckets). Pushing it
  lower would need the budget band widened or its offence cut below "high Atk/SpA".
- **HP moves are sharp.** +5 HP on Griffin (c3 → c4) moved its `neutral` overall by +5.4. A
  Skirmisher's HP decides whether it survives its first engagement, so small HP changes are coarse
  levers.

### Fixed encounter set (sanity check, not a target)

`--encounter-set fixed` (the three hand-authored encounters, 5 samples), default seed. Overall
before → after, and after per encounter:

| Beast | `elemental` overall | `elemental` boss / swarm / pack (after) | `neutral` overall | `neutral` boss / swarm / pack (after) |
| --- | ---: | --- | ---: | --- |
| Thunderbird | +3.9 → +13.0 | +16.8 / +2.0 / +20.2 | −1.1 → +7.8 | +15.8 / −3.5 / +10.9 |
| Leviathan | −11.1 → +10.0 | +6.6 / +11.3 / +12.0 | −14.7 → +3.7 | +6.3 / +4.8 / +0.1 |
| Treant | −14.3 → +9.0 | +5.4 / +12.0 / +9.6 | −12.3 → +10.2 | +7.0 / +19.0 / +4.6 |
| Basilisk | +17.0 → +7.4 | +9.6 / +7.0 / +5.8 | +13.6 → +6.0 | +10.0 / +4.7 / +3.4 |
| Griffin | +12.8 → +4.2 | +5.8 / +0.5 / +6.2 | +10.0 → +4.4 | +4.2 / +5.6 / +3.4 |
| Kirin | −0.3 → −3.6 | +4.5 / −2.8 / −12.6 | +2.4 → −5.0 | +2.9 / −2.3 / −15.5 |
| Phoenix | +11.1 → −7.2 | −3.9 / −19.9 / +2.2 | +14.1 → −0.5 | −0.8 / −17.1 / +16.5 |
| Golem | −24.7 → −7.4 | −11.5 / −11.0 / +0.2 | −18.0 → −9.7 | −11.5 / −20.6 / +3.0 |
| Frost Wyrm | +7.9 → −11.8 | −11.2 / +6.9 / −31.0 | +6.4 → −10.3 | −11.7 / +8.3 / −27.6 |
| Tarasque | −2.2 → −13.6 | −22.2 / −6.0 / −12.5 | −0.4 → −6.7 | −22.2 / +1.0 / +1.3 |

The spread narrows from −24.7 … +17.0 to −13.6 … +13.0 (`elemental`) and from −18.0 … +14.1 to
−10.3 … +10.2 (`neutral`). There is no wild outlier beyond what three encounters at 5 samples can
show. The largest single cell is Frost Wyrm against the fixed pack (−31.0 / −27.6): a Vanguard with
the roster's second-lowest Attack against an encounter that flanks with lowest-HP-targeting wisps
and stingers. The fixed set is not what the pass tuned for; if it is ever made primary again,
Tarasque and Frost Wyrm are the beasts to revisit.

### Iteration log

Every candidate was measured on the full default PvE run (3 levels, 4 shapes × 8 compositions,
both kit modes) at seeds 12345, 777 and 4242, about 8.5 minutes per candidate. Ranges are overall
marginals on the 3-seed mean; "no niche" = not top 3 in any shape (`elemental`).

| # | Change (from the previous candidate unless stated) | `elemental` | `neutral` | No niche (`elemental`) | Kept? |
| ---: | --- | --- | --- | --- | --- |
| 0 | HEAD (Speed 40–120) | −16.8 … +19.8 | −22.6 … +19.4 | Kirin, Leviathan, Treant, Golem (Phoenix top 3 everywhere) | baseline |
| 1 | Speed into 92–105 in the approved order; totals Phoenix 595, Leviathan 615, Golem 630, Griffin 600, Thunderbird 610, Frost Wyrm 595, Treant 630, Tarasque 605, Kirin 595, Basilisk 615; other five stats rescaled proportionally | −6.8 … +5.4 | −12.1 … +9.3 | Golem, Frost Wyrm, Kirin | yes |
| 2 | Thunderbird to 630, bulk up (HP 99 → 112, Def 79 → 90, SpD 84 → 92) and Atk / SpA down; Phoenix and Griffin Atk −5; Kirin +10 (HP, Def) | −2.8 … +4.2 | −4.5 … +3.0 | Golem, Basilisk, Frost Wyrm, Kirin | yes |
| 3 | Phoenix 580; Leviathan 605; Frost Wyrm 615; Kirin 620; Basilisk 625 | −2.0 … +2.5 | −3.9 … +4.3 | Treant, Kirin | yes |
| 4 | Kirin 630; Leviathan 600 (HP −5); Griffin 600 (HP +5) | −1.8 … +2.6 | −5.0 … +7.7 | Leviathan | Kirin only (Griffin +5 HP: `neutral` +2.3 → +7.7) |
| 5 | Griffin back to 595; Leviathan 610 (HP 120, Def 124, SpD 99) | −2.0 … +2.9 | −4.5 … +4.7 | Treant | yes |
| 6 | Griffin offence-heavy at 595 (HP 100, Atk 120, SpA 96), to move it from horde to squad; Phoenix 575 | −4.0 … +2.9 | −3.4 … +4.7 | Basilisk, Griffin | Phoenix only |
| 7 | Griffin back; Phoenix 570; Thunderbird 620 (HP 107), to take a slot from it | −4.0 … +2.3 | −8.1 … +6.1 | Treant | Phoenix only (Thunderbird `neutral` −8.1) |
| 8 | Thunderbird 625 (HP 110); Leviathan reshaped towards SpA / SpD (HP 112, SpA 96, SpD 106; 615), to take an elite slot | −2.8 … +2.1 | −5.4 … +5.8 | Leviathan | Thunderbird only |
| 9 | Phoenix Move 4 → 3 | same as 8 (±0.1) | same | Leviathan | no: a Ranged beast rarely walks |
| 10 | Leviathan HP 122, Def 126, SpD 100 (615; restores "very high HP / Def") | **−2.9 … +2.2** | **−5.8 … +5.7** | Treant (by 0.4) | **final** |

Ten candidates, under the cap of fifteen. Every step was a budget move of 5–20 points on one to four
beasts, and each was judged on the 3-seed mean, never a single seed.

### Caveats

- **Overfitting.** Iterations 3–10 chase top-3 ranks that differ by less than the per-shape noise
  (a shape cell moves about 4 points between seeds, so about 2.5 on a 3-seed mean). The overall
  marginals are robust. The exact niche assignment is not: which bulk beast misses the horde's top
  three flips with a few budget points or a new seed.
- **Thunderbird is polarized, not balanced.** It is near zero overall because +10 against the giant
  offsets −27 (`elemental`) to −40 (`neutral`) against the horde. Once encounters are authored, how
  often a horde appears decides whether it is weak or strong. A diver that goes in first and alone
  is exactly what a threat or taunt mechanic on its allies would change.
- **Kit parity moved for Skirmishers.** The physical share of single-target power is 53.8% / 54.0%
  for Skirmishers (Strike / Blast 0.82, up from 0.71), inside the ±5 flag but above the 50% the
  Strike power was derived for. With Speed compressed, the Skirmishers now reach melee about as
  often as the Vanguards. `StrikePower` was fitted on the old roster; re-derive it in the kit's own
  next pass (out of scope here: the kit was frozen).
- **Everything the simulator assumes still applies**: one standard kit, fixture enemies,
  nearest-enemy targeting for beasts and no skills, gear or avatar. PvP (secondary, 1v1) now favours
  the fast Skirmishers (Thunderbird and Griffin 77–87% win rates) and punishes Frost Wyrm and
  Leviathan. It was not a target.
- The `baseline-report.md` numbers and the first pass's tables above are historical. The committed
  `tuned-report.md` is this pass's default-seed run.
