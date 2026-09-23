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
decision). See "Retune for ATB + stances + crits + mixed encounters" near the end; that section's
stats are the current roster. `tuned-report.md` has since been regenerated once more, on the same
roster, after the square-root speed gauge and the mitigation damage formula replaced the linear
gauge and the level-term formula; see "Sqrt speed + mitigation formula" at the end.

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

## Sqrt speed + mitigation formula

A Runtime formula change, **not a retune**. The roster (`beast-roster.json`) is unchanged, and so
are the stances, crit chances, move ranges, the encounter pool and shapes, and the element chart.
Two formulas were replaced. Both are adopted from Sword x Staff and user-approved; see
[`research-sword-x-staff.md`](research-sword-x-staff.md) and the design doc (decision 3, "Damage
formula"):

- **Turn order: square-root gauge fill.** `TurnManager` now fills each unit's gauge at
  `round(100 × sqrt(max(1, Speed)))` per tick against a threshold of 100000. The square root is an
  exact integer square root, so every platform computes the same order. The old fill was `Speed`
  against 1000. Turns now grow with sqrt(Speed): four times the Speed is twice the turns. One unit
  of normalized time is still one turn of a Speed-100 unit (100 ticks).
- **Damage: percent-of-stat power with `A / (A + D)` mitigation.**
  `damage = Power / 100 × A × A / (A + DefenseWeight × D) × GlobalScale × element × crit × roll`,
  truncated and floored at 1, with `DefenseWeight` = `GlobalScale` = 1. This replaces
  `((2 × Level / 5 + 2) × Power × A / D) / 50 + 2`. Level no longer enters the formula. Crit is
  `max(MinCritMultiplier 1.3, CritMultiplier 1.5)`; the 1.3 floor is there for a future crit-damage
  reduction.

`tuned-report.md` is regenerated with the default arguments. Because the roster is unchanged, the
report shows the balance shift the next retune has to absorb.

### Power rescale

`Power` now means a percent of the attacking stat. Every power was rescaled so that a neutral hit
between two average level-50 roster beasts takes the same share of HP as before. The average beast is
the mean base stats: HP 113.8, Atk 95.5, Def 103, SpA 102.8, SpD 100.1. The rescale matches the old
`0.44 × P + 2` (level 50, A ≈ D) against the new `P' / 100 × A / 2` at A ≈ D ≈ 58, which gives
`P' ≈ 1.52 × P + 7`.

| Skill | Old power | New power |
| --- | ---: | ---: |
| Blast (beast kit, special) | 40 | 68 |
| Strike (beast kit, physical, Vanguard / Skirmisher) | 57 | 93 (parity, below) |
| Shot (beast kit, physical, Ranged) | 41 | 70 (parity, below) |
| Burst halves (beast kit) | 20 | 37 |
| giant crush / gaze, colossus crush / gaze | 70 | 113 |
| cleave, hex, shadow claw | 55 | 90 |
| smash | 50 | 83 |
| maul (direwolf) and bolt (wisp), both in the fixed set | 45 | 75 |
| arrow, bolt (caster) | 42 | 71 |
| quake / roar, staff | 35 | 60 |
| shockwave, bite, sting (and the fixed-set swarm) | 30 | 52 |
| storm | 28 | 49 |

**HP share of one hit, average beast into average beast, 100% roll, no crit:**

| Level | Skill | HP | Old damage (share) | New damage (share) |
| --- | --- | ---: | ---: | ---: |
| 1 | Blast | 17 | 3 (17.6%) | 5 (29.4%) |
| 1 | Strike | 17 | 4 (23.5%) | 6 (35.3%) |
| 50 | Blast | 65 | 20 (30.8%) | 20 (30.8%) |
| 50 | Strike | 65 | 25 (38.5%) | 24 (36.9%) |
| 100 | Blast | 114 | 36 (31.6%) | 35 (30.7%) |
| 100 | Strike | 114 | 46 (40.4%) | 42 (36.8%) |

The table uses the average beast's Atk 55 vs Def 59 at level 50 for Strike, and SpA 59 vs SpD 57
for Blast.

- **Levels 50 and 100 match.** Level 1 now takes the same share as every other level, because the
  formula is level-invariant.
- **The old level term under-scaled level-1 damage.** You can see it in the calibration:
  - The per-shape difficulty multipliers are now nearly flat across levels. For example, `solo`
    `elemental` is x0.805 / x0.787 / x0.787 at levels 1 / 50 / 100, where it was x0.844 / x0.773 /
    x0.766 before.
  - Level-1 battles are much shorter: 8.6–12.6 normalized time, down from 29–46. Part of that is the
    square-root gauge giving level-1 units more turns per unit of time: a Speed-15 unit gets 0.39
    turns instead of 0.15.
- **Strike and Shot parity.** At Strike 97 / Shot 70, Strike fired 0.71–0.72× as often as Blast for
  Vanguards and 0.81–0.82× for Skirmishers, 0.733× pooled over both stances. Shot fired 0.96×. So
  Strike = 68 / 0.733 = **93**, and Shot stays 68 / 0.965 = **70**.
- **Kit parity at the defaults.** The physical share is 49.2% / 49.6% for Vanguards, 52.5% / 52.8%
  for Skirmishers and 49.8% for Ranged beasts (`elemental` / `neutral`), and 50.0% / 50.2% overall.
  At Strike 97 the Skirmishers were 53.5–53.8%.

### Turn rates by Speed

The table shows turns per unit of normalized time relative to a Speed-100 unit: sqrt(Speed / 100)
now, Speed / 100 before. **Turn share** is the beast's share of the turns in a hypothetical
all-roster fight: each of the ten beasts once, all standing.

| Beast | Base Speed | Turns vs Speed-100 (old → new) | Turn share (old → new) |
| --- | ---: | ---: | ---: |
| Thunderbird | 105 | 1.050 → 1.025 | 10.63% → 10.31% |
| Griffin | 104 | 1.040 → 1.020 | 10.53% → 10.26% |
| Basilisk | 102 | 1.020 → 1.010 | 10.32% → 10.16% |
| Phoenix | 101 | 1.010 → 1.005 | 10.22% → 10.11% |
| Kirin | 100 | 1.000 → 1.000 | 10.12% → 10.06% |
| Frost Wyrm | 98 | 0.980 → 0.990 | 9.92% → 9.96% |
| Tarasque | 97 | 0.970 → 0.985 | 9.82% → 9.91% |
| Leviathan | 95 | 0.950 → 0.975 | 9.62% → 9.81% |
| Treant | 94 | 0.940 → 0.970 | 9.51% → 9.76% |
| Golem | 92 | 0.920 → 0.959 | 9.31% → 9.65% |

**Fastest / slowest turn ratio.** The Speed ratio is 1.141 at level 100, 1.132 at level 50 and
1.143 at level 1 (Speed 16 / 14 after rounding). The turn ratio is now:

- **1.069** at level 100 (old 1.141).
- **1.065** at level 50 (old 1.132).
- **1.070** at level 1 (old 1.143).

**User decision:** the 10–15% target now applies to **turns**. The current band gives about 7%, so
the roster is under-spread for the new rule. To reach 1.10–1.15 in turns, the Speed spread must be
1.21–1.32×.

The report's per-beast **Turns / time** column averages levels 1, 50 and 100. It rises for every
beast; for example, Kirin goes from 0.440 to 0.570 and Frost Wyrm from 0.320 to 0.392, averaged over
shapes in `elemental` mode. That rise is mostly the level-1 effect above. The stat table at level 50
now lists each beast's turn rate: 0.728 for Golem up to 0.775 for Thunderbird.

### Marginal clear rate, before → after (default seed 12345, generated set)

Before is the committed report at `424dff3` (the second tuning pass under the old formulas). After is
this change on the same roster. Each shape column shows before → after; the change is in overall
points.

#### `elemental` (primary)

| Beast | solo | elite | squad | horde | Overall before | Overall after | Change |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Basilisk | -1.2 → +2.6 | +2.0 → +6.1 | +2.6 → +11.2 | -1.0 → +5.4 | +0.6 | +6.3 | +5.7 |
| Thunderbird | +12.1 → +22.9 | +0.5 → +1.5 | +3.0 → +4.6 | -17.2 → -16.4 | -0.4 | +3.1 | +3.5 |
| Griffin | -3.9 → +1.1 | -1.4 → -0.6 | -3.0 → -0.8 | +1.1 → +5.2 | -1.8 | +1.2 | +3.0 |
| Kirin | +1.8 → +4.4 | -1.7 → +0.3 | -4.1 → +1.2 | +0.6 → -1.5 | -0.8 | +1.1 | +1.9 |
| Phoenix | +2.2 → +1.4 | -2.9 → +0.8 | -0.6 → +4.5 | -7.1 → -3.2 | -2.1 | +0.9 | +3.0 |
| Frost Wyrm | -2.3 → -7.8 | +5.2 → +2.1 | +0.1 → -2.6 | +8.6 → +6.6 | +2.9 | -0.4 | -3.3 |
| Leviathan | +6.0 → +0.1 | +7.4 → +4.2 | -9.3 → -12.1 | +6.3 → +4.9 | +2.6 | -0.7 | -3.3 |
| Tarasque | -2.2 → -2.4 | -6.4 → -6.0 | +5.2 → +5.9 | -6.4 → -2.6 | -2.5 | -1.3 | +1.2 |
| Treant | -2.6 → -8.9 | -5.5 → -8.3 | -2.2 → -7.6 | +12.3 → +9.4 | +0.5 | -3.8 | -4.3 |
| Golem | -9.8 → -13.5 | +2.8 → -0.2 | +8.2 → -4.5 | +2.8 → -7.7 | +1.0 | -6.5 | -7.5 |

#### `neutral` (secondary)

| Beast | solo | elite | squad | horde | Overall before | Overall after | Change |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Basilisk | +8.5 → +9.4 | +13.6 → +17.6 | +2.0 → +12.0 | +1.4 → +7.5 | +6.4 | +11.6 | +5.2 |
| Griffin | +5.0 → +14.0 | -0.4 → +0.6 | +2.5 → +7.6 | +0.1 → +5.3 | +1.8 | +6.9 | +5.1 |
| Kirin | +10.2 → +7.8 | +5.0 → +7.4 | -5.6 → +0.4 | +0.5 → -1.7 | +2.5 | +3.5 | +1.0 |
| Thunderbird | +11.0 → +28.3 | +4.8 → +7.3 | 0.0 → +4.2 | -29.7 → -29.1 | -3.5 | +2.7 | +6.2 |
| Phoenix | +0.3 → -3.5 | -3.0 → +0.9 | -4.1 → +3.4 | -5.1 → +1.2 | -3.0 | +0.5 | +3.5 |
| Tarasque | -2.7 → +0.5 | +2.3 → +2.4 | +3.4 → 0.0 | -4.0 → -1.4 | -0.3 | +0.4 | +0.7 |
| Frost Wyrm | +2.1 → -4.2 | +2.6 → -1.5 | +2.4 → -4.7 | +0.2 → 0.0 | +1.8 | -2.6 | -4.4 |
| Treant | -12.0 → -18.3 | -3.6 → -7.9 | -0.2 → -6.4 | +15.0 → +11.2 | -0.2 | -5.3 | -5.1 |
| Leviathan | -5.8 → -16.3 | -5.0 → -9.3 | -0.4 → -6.4 | +6.4 → +5.4 | -1.2 | -6.7 | -5.5 |
| Golem | -16.7 → -17.7 | -16.4 → -17.5 | -0.1 → -10.1 | +15.2 → +1.6 | -4.5 | -10.9 | -6.4 |

**Flags after the change.** Before, only `neutral` Basilisk was flagged (+6.4).

- **`elemental`:** Basilisk +6.3 (HIGH, and no weakness: top 3 in every shape) and Golem -6.5 (LOW).
- **`neutral`:** Basilisk +11.6 (HIGH, no weakness), Griffin +6.9 (HIGH), Treant -5.3, Leviathan
  -6.7 and Golem -10.9 (all LOW).
- The `elemental` overall spread widens from -2.5 … +2.9 to -6.5 … +6.3.
- There are no stalemates and no calibration misses.

### Why balance moved

- **Defense is worth less and Attack more.** Under `A / D`, a 1% change in either stat moved damage
  by 1%. Under `A² / (A + D)` at A ≈ D, Defense moves damage by about 0.5% and Attack by about 1.5%.
  - The tanks lose the most. Golem, Leviathan and Treant have the highest Def / SpD and the
    lowest attacks.
  - The high-attack beasts gain: Basilisk (SpA 153), Thunderbird and Griffin.
  - This is the reference's intended "diminishing returns on defense". The retune has to price it:
    bulk now has to come more from HP than from Def / SpD.
- **Speed matters less.** The square root halves every speed gap in turns. That should help the
  slow Vanguards, but the effect is small next to the mitigation shift (their turn share rises by
  0.3 points at most).
- **Thunderbird's giant specialism deepened.** On `solo` it went from +12.1 to +22.9 (`elemental`),
  while the horde still punishes it. The mitigation term rewards its Atk 117 / SpA 114 split against
  the giant's high Def / SpD.

### TODO for the retune deliverable

Done in "Retune with authored kits, avatar passives, sqrt speed and mitigation" below (against the
library kits rather than the standard kit, so Strike / Shot parity was not re-derived).

- **Replace the speed-band roster test.** `BeastRosterTests` still pins base Speed to fastest /
  slowest ≤ 1.15 (the stat). Under the square-root gauge that constrains the wrong quantity. Replace
  it with a **turn-ratio** test: `FillRateForSpeed(fastest) / FillRateForSpeed(slowest)` in
  **1.10–1.15**, which is a Speed spread of about 1.21–1.32×. Keep the order test. This deliverable
  deliberately left the test and the roster unchanged.
- **Widen base Speed** to meet that turn ratio.
- **Re-tune the six-stat lines** for the new Attack / Defense weighting, then re-derive Strike and
  Shot parity once more.
- **Re-run the multi-seed check** (seeds 12345, 777 and 4242) as in the second pass.

## Skill library: exploratory run (pre-retune)

**Exploratory, not the tuned report.** The first authored kits (`skill-library.json`, see the design
doc's "Beast skill kits") were fielded once with `--kit library --avatar library --mode pve` (skill
level 1, default seed 12345, generated set, levels 1 / 50 / 100, both kit modes) on the **unchanged**
roster, beside a standard-kit PvE run of the same code for comparison. The committed
`tuned-report.md` still uses the standard kit; the next deliverable switches the default to the
library kits and retunes the roster against them.

Overall marginal clear rate (points), levels and shapes averaged:

| Beast | Stance | Standard kit, `elemental` | Library kit, `elemental` | Library kit, `neutral` |
| --- | --- | ---: | ---: | ---: |
| Basilisk | Ranged | +6.3 | **+10.8** | **+26.1** |
| Thunderbird | Skirmisher | +3.1 | **+5.4** | _-10.3_ |
| Tarasque | Vanguard | -1.3 | +0.4 | +1.5 |
| Phoenix | Ranged | +0.9 | -1.2 | -2.7 |
| Kirin | Ranged | +1.1 | -1.2 | +2.4 |
| Frost Wyrm | Vanguard | -0.4 | -1.4 | -2.7 |
| Griffin | Skirmisher | +1.2 | -1.7 | -0.3 |
| Leviathan | Vanguard | -0.7 | -1.8 | -3.4 |
| Golem | Vanguard | _-6.5_ | -3.6 | _-6.5_ |
| Treant | Vanguard | -3.8 | _-5.6_ | -4.0 |

Library `elemental` by shape (solo / elite / squad / horde): Basilisk +7.1 / +10.7 / +15.7 / +9.5;
Thunderbird +11.6 / -7.2 / +6.8 / +10.5 (its horde weakness, -16.4 on the standard kit, is gone:
Chain Lightning does what it was designed to); Leviathan +3.3 / +8.4 / -14.6 / -4.2; Golem
-3.2 / +2.9 / -4.0 / -10.2; Treant -2.4 / -4.6 / -10.1 / -5.5.

Read-outs for the retune (first impressions, one seed):

- **Basilisk is the outlier**, most of all in `neutral` (+26.1): Coup de Grace (execute +50%, picks
  the lowest-HP% enemy in range) plus stacking poison finishes targets the team has already softened.
  Its execute power or poison stack cap is the first knob.
- **Thunderbird's element carries it**: +5.4 in `elemental`, -10.3 in `neutral`. The kit is three
  sub-40-power hits per skill; without the Lightning multiplier its per-hit damage sinks under the
  mitigation curve. Worth checking with the retune rather than raising power blindly.
- **The tanks remain the weakest marginals** (Golem, Treant, Leviathan in squads): taunt and shields
  keep the team alive but a clear-rate metric at calibrated difficulty rewards damage. The library
  kits narrow Golem's gap (-6.5 → -3.6 elemental) but do not close it.
- **Calibration moved** because the library avatar is fielded: solo / elite multipliers ~x0.86–0.98,
  squad / horde ~x1.26–1.39 (standard kit, no avatar: see `tuned-report.md`). `neutral` / `solo` /
  level 1 missed its clear-rate target (36.7%), the only calibration miss.
- **Avatar passives:** Keen Eye and Opening Ward fire once per battle as designed; Last Stand fires
  2.98 times per battle on average (40% threshold, cooldown 2) — likely the strongest default
  passive per slot.

## Retune with authored kits, avatar passives, sqrt speed and mitigation

The third tuning pass, and the first against the real game setup: every beast fights with its
authored `DefaultLoadout` from `skill-library.json`, beside the library avatar (its three default
actives and passives), at skill level 1, under the square-root ATB gauge and the `A²/(A+D)`
mitigation formula. This is now the **simulator's default** (`--skill-kit library --avatar
library`), and `tuned-report.md` is regenerated from it. Both the roster's base stats and the skill
numbers were tuned. Stances, growth curves, elements, formulas, the encounter generator and enemy
pool, skill identities and default loadouts did not change.

Three engine and tooling changes came first:

- **Heals scale.** A `Heal` restores `Magnitude / 100 × caster SpecialAttack × HealScale`
  (`SkillEffectApplier.HealScale` = 1.0), rounded to whole HP, scaled by skill level, with no defense,
  crit or variance (and so no rng draws). Passive heals read the avatar's `SpecialAttack`. Every
  heal magnitude was first rescaled to restore what it did at level 50 for its caster
  (`new = old × 100 / caster's level-50 SpA`), then tuned (see "Heal scaling check" below).
- **The avatar fixture follows the growth curve.** The sim's avatar stat block was `10 + level` in
  every stat; it is now `round(100 × medium scale)` (15 / 57 / 100 at levels 1 / 50 / 100), so its
  shields and heals are the same share of a beast's HP at every level.
- **`--skill-kit standard|library`** replaces the overloaded `--kit standard|library`; `--kit` is
  only the element axis again. The defaults flipped to `--skill-kit library --avatar library`
  (`--skill-level 1`). The standard kit and its kit parity table remain available with
  `--skill-kit standard`.

**The speed rule is now in turns.** The fastest beast gets 10–15% more turns than the slowest
(user). Turns grow with `sqrt(Speed)`, so the old 1.14× Speed band (92–105) was only 1.069× turns.
Base Speed now spans **88–110 (1.25×), a 1.118× turn ratio** (`FillRateForSpeed` 1049 / 938), in
the approved order. `BeastRosterTests.Roster_TurnRateSpreadStaysInBand` replaces the Speed-stat band
test and pins the fill-rate ratio to 1.10–1.15; the order test is unchanged.

### Targets

As in the second pass, on the **mean of three base seeds** (12345, 777, 4242), generated set,
levels 1 / 50 / 100, both modes, library setup:

1. Overall marginal within ±5 for every beast in `elemental`; aim for ±7 in `neutral`.
2. Every beast top 3 in at least one shape (`elemental` primary).
3. No beast top 3 in every shape.
4. Tanks (Golem, Leviathan) should find a niche beyond hordes thanks to taunt.
5. At `--skill-level 10` the spread should not blow up.

Hard constraints: turn ratio 1.10–1.15 with the approved order; six-stat totals 570–630; Move 2–5;
Crit 0–25 (glass cannons and assassins high, tanks low); archetypes and signature skills kept; the
skill budget rule as the guide.

### Stats, before → after

Max-level base stats. **Turn rate** is `FillRateForSpeed(Spe) / 1000`, turns per unit of normalized
time relative to a Speed-100 unit. Move and Crit did not change.

| Beast | HP | Atk | Def | SpA | SpD | Spe | Total | Turn rate | Move | Crit |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Thunderbird | 110 | 117 | 88 | 114 | 91 | 105 → **110** | 625 → **630** | 1.025 → **1.049** | 4 | 15% |
| Griffin | 106 → **116** | 112 → **118** | 91 → **97** | 91 → **85** | 91 | 104 → **108** | 595 → **615** | 1.020 → **1.039** | 5 | 8% |
| Basilisk | 103 | 94 | 79 | 153 → **117** | 94 | 102 → **105** | 625 → **592** | 1.010 → **1.025** | 5 | 12% |
| Phoenix | 92 | 102 | 70 | 120 | 85 | 101 → **104** | 570 → **573** | 1.005 → **1.020** | 4 | 10% |
| Kirin | 115 | 51 | 90 | 150 → **149** | 124 | 100 → **101** | 630 | 1.000 → **1.005** | 4 | 5% |
| Frost Wyrm | 98 | 74 | 124 | 103 | 118 | 98 → **99** | 615 → **616** | 0.990 → **0.995** | 3 | 5% |
| Tarasque | 112 | 137 → **130** | 127 | 54 | 78 | 97 | 605 → **598** | 0.985 → **0.985** | 3 | 6% |
| Leviathan | 122 | 86 | 126 | 86 | 100 | 95 → **94** | 615 → **614** | 0.975 → **0.970** | 3 | 3% |
| Treant | 134 | 87 → **77** | 98 | 93 → **105** | 124 | 94 → **92** | 630 | 0.970 → **0.959** | 3 | 3% |
| Golem | 146 → **150** | 95 → **109** | 137 | 64 → **50** | 96 | 92 → **88** | 630 | 0.959 → **0.938** | 2 | 2% |

- **Speed widened** to reach the turn ratio: +5 Thunderbird, +4 Griffin, +3 Basilisk and Phoenix,
  +1 Kirin and Frost Wyrm, −1 Leviathan, −2 Treant, −4 Golem.
- **Basilisk −36 SpA** (153 → 117). It was +15.9 `elemental` / +30.1 `neutral` and top in every
  shape: execute, stacking poison and the highest SpA compounded. Its power now lives in its kit
  (execute, poison, a 45% petrify), so it is no longer the highest-SpA beast (Kirin is), and its
  total fell to 592. It keeps low Def (79), Move 5 and the second-highest crit.
- **Unused stats moved to used ones.** Golem's `SpecialAttack` (no special skill in its kit) went
  into Attack (95 → 109, SpA 64 → 50); Treant's Attack (no physical skill) into SpA (87 → 77,
  93 → 105), which now also powers its heals; Griffin's SpA into HP, Attack and Defense (total
  595 → 615).
- **Tarasque −7 Atk** (137 → 130): it was top 3 in three shapes; it is still the highest Atk.
- Archetypes hold: Golem highest HP and Def, slowest, Move 2; Tarasque highest Atk and
  second-highest Def; Leviathan third in HP and Def; Treant second-highest HP and joint-highest SpD;
  Thunderbird fastest with the highest crit; Griffin second-fastest, Move 5; Kirin highest SpA and
  lowest Atk; Phoenix lowest HP and Def and the lowest total.

### Skill changes

Magnitudes at skill level 1 (before level scaling). "Heal rescale" is the mechanical conversion to
`SpecialAttack`-scaled heals; the rest is this pass's tuning.

| Skill | Owner (slot) | Change | Why |
| --- | --- | --- | --- |
| Deep Shell `deep_shell` | Leviathan (3) | Heal 12 → 24 | Heal rescale |
| Tidal Renewal `tidal_renewal` | Leviathan (–) | Heal 14 → 29 | Heal rescale |
| Verdant Mend `verdant_mend` | Treant (2) | Heal 18 → 34 → **40** | Heal rescale, then tuned: Treant was −6.3 |
| Bark Ward `bark_ward` | Treant (3) | L10 bonus Heal 6 → 11 | Heal rescale |
| Lifebloom `lifebloom` | Treant (–) | Heal 10 → 19 | Heal rescale |
| Rebirth Flame `rebirth_flame` | Phoenix (3) | Heal 30 → 44 | Heal rescale |
| Sacred Spring `sacred_spring` | Kirin (1) | Heal 12 → 14 → **20** | Heal rescale, then tuned: Kirin had no top-3 shape |
| Purifying Ward `purifying_ward` | Kirin (–) | L10 bonus Heal 6 → 7 | Heal rescale |
| Halo `halo` | Kirin (–) | Heal 16 → 19 | Heal rescale |
| Mending Light `mending_light` | Avatar active (2) | Heal 8 → 13 | Heal rescale (the old fixture's SpA 60 at level 50) |
| Verdant Pulse `verdant_pulse` | Avatar passive (–) | Heal 3 → 5 | Heal rescale |
| Boulder Slam `boulder_slam` | Golem (1) | Damage 75 → **85** | Golem was bottom 3 everywhere; see the budget note in the design doc |
| Stone Challenge `stone_challenge` | Golem (2) | Range 2 → **3**, Taunt 2t → **3t**, 85% → **90%** | The taunt is the tank's niche; at radius 2 it caught too little of an encounter |
| Granite Bulwark `granite_bulwark` | Golem (3) | Range 1 → **2**, Shield 35% → **70%** Def | The Golem's biggest single lever (a sensitivity run at 70% and cooldown 2 took it from −12 to −1 `elemental`); settled at cooldown 3 |
| Serpent Bite `serpent_bite` | Leviathan (1) | Damage 75 → **82** | Leviathan's squad weakness |
| Iron Crush `iron_crush` | Tarasque (2) | Damage 180 → **155** | Tarasque held three top-3 shapes, +5 overall |
| Rime Bolt `rime_bolt` | Frost Wyrm (1) | Damage 50 → **48** | Frost Wyrm crowding the elite and horde slots |
| Frost Breath `frost_breath` | Frost Wyrm (3) | Damage 45 → **42** | Same |
| Thunder Talons `thunder_talons` | Thunderbird (1) | 32 → **33** × 3 hits | `neutral` −9.5 (DPT 99 = 1.1× the melee budget) |
| Chain Lightning `chain_lightning` | Thunderbird (2) | 22 → **25** × 3 hits | Same |
| Gale Talon `gale_talon` | Griffin (2) | Damage 88 → 92 → **89** | Griffin was −5.8; backed off once its bulk rose |
| Wind Lance `wind_lance` | Griffin (1) | Damage 105 → **110** | Griffin had no top-3 shape |
| Gust `gust` | Griffin (3) | Damage 45 → **55** | Griffin's horde −11 |
| Flame Wave `flame_wave` | Phoenix (2) | Damage 95 → **90** | Free a horde slot for Kirin |
| Radiant Bolt `radiant_bolt` | Kirin (3) | Damage 58 → **62** | Kirin had no top-3 shape |
| Venom Spit `venom_spit` | Basilisk (1) | DoT 18 → **15** | Basilisk +30 `neutral` |
| Coup de Grace `coup_de_grace` | Basilisk (2) | Damage 110 → 95 → **105**, execute +50% → **+60%** | Cut with the SpA, then partly restored so the assassin keeps a niche |
| Petrifying Gaze `petrifying_gaze` | Basilisk (3) | Stun 25% → **45%** | Basilisk's elite niche (a real petrify on the boss), paid for under the hard-control band |

Every unlimited damage skill stays at or below 1.2× its budget, and the rule's numbers did not move
(the budget test is unchanged). Golem's Boulder Slam (85, above a tank's ≈ 0.8×) and Thunder Talons
(99, exactly 1.1×) are the two defaults above their role-scaled guide; the design doc justifies
both. `SkillLibraryTests` pins that moved: Stone Challenge range 3 and chance 90, Coup de Grace
execute 60, and the Basilisk petrify bound (≤ 30% → ≤ 50%).

### Marginal clear rate, before → after (mean of 3 seeds, generated set, library setup)

Points of clear rate, levels 1 / 50 / 100 averaged, then averaged over seeds 12345 / 777 / 4242;
(n) = rank within the shape on the mean. **Before** = the roster and kits at the start of this pass
with heal scaling applied (and the old `10 + level` avatar fixture), library setup.

**Before, `elemental`:**

| Beast | `solo` | `elite` | `squad` | `horde` | Overall | Top-3 shapes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Basilisk | +14.2 (1) | +19.0 (1) | +17.8 (1) | +12.4 (1) | +15.9 (1) | 4 |
| Tarasque | +9.5 (2) | +2.0 (3) | +12.3 (2) | −7.9 (9) | +4.0 (2) | 3 |
| Thunderbird | +7.4 (3) | −7.7 (10) | +3.4 (4) | +2.7 (4) | +1.5 (3) | 1 |
| Phoenix | −6.2 (10) | −2.4 (6) | +7.6 (3) | +1.5 (5) | +0.1 (4) | 1 |
| Frost Wyrm | −6.1 (9) | +1.8 (4) | −3.6 (7) | +3.6 (3) | −1.1 (5) | 1 |
| Griffin | −4.5 (6) | −3.9 (7) | −1.7 (6) | +5.1 (2) | −1.2 (6) | 1 |
| Kirin | −3.6 (5) | −1.1 (5) | −1.0 (5) | −2.1 (7) | −1.9 (7) | 0 |
| Leviathan | −5.3 (7) | +2.0 (2) | −10.4 (8) | −0.5 (6) | −3.6 (8) | 1 |
| Treant | +0.4 (4) | −5.8 (9) | −13.4 (10) | −6.5 (8) | −6.3 (9) | 0 |
| Golem | −6.0 (8) | −3.9 (8) | −10.9 (9) | −8.2 (10) | −7.2 (10) | 0 |

**Before, `neutral`:**

| Beast | `solo` | `elite` | `squad` | `horde` | Overall | Top-3 shapes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Basilisk | +40.9 (1) | +36.3 (1) | +25.5 (1) | +17.9 (1) | +30.1 (1) | 4 |
| Griffin | +1.8 (2) | −2.4 (7) | +1.5 (4) | +4.6 (2) | +1.4 (2) | 2 |
| Tarasque | +0.2 (4) | +2.0 (2) | +9.7 (2) | −6.9 (10) | +1.2 (3) | 2 |
| Kirin | 0.0 (5) | +0.8 (4) | +1.1 (5) | +0.5 (4) | +0.6 (4) | 0 |
| Frost Wyrm | −4.2 (7) | −1.5 (6) | −2.0 (7) | −0.8 (5) | −2.1 (5) | 0 |
| Phoenix | −13.1 (9) | −8.7 (8) | +2.4 (3) | +3.7 (3) | −3.9 (6) | 2 |
| Leviathan | −2.7 (6) | +1.1 (3) | −11.4 (8) | −4.0 (7) | −4.3 (7) | 1 |
| Treant | +1.0 (3) | −1.3 (5) | −13.8 (10) | −5.4 (8) | −4.9 (8) | 1 |
| Golem | −7.8 (8) | −11.2 (9) | −12.3 (9) | −3.6 (6) | −8.7 (9) | 0 |
| Thunderbird | −15.9 (10) | −15.0 (10) | −0.8 (6) | −6.1 (9) | −9.5 (10) | 0 |

**After, `elemental` (primary):**

| Beast | `solo` | `elite` | `squad` | `horde` | Overall | Top-3 shapes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Tarasque | +4.6 (2) | −0.3 (7) | +10.1 (1) | −3.7 (8) | +2.7 (1) | 2 |
| Thunderbird | +8.5 (1) | −5.3 (10) | +2.9 (4) | +3.6 (2) | +2.4 (2) | 2 |
| Griffin | +2.5 (4) | +2.3 (4) | +3.7 (3) | −4.6 (10) | +1.0 (3) | 1 |
| Kirin | −2.0 (7) | +1.1 (5) | +1.5 (7) | +2.2 (3) | +0.7 (4) | 1 |
| Phoenix | −7.0 (10) | −3.7 (8) | +9.8 (2) | +1.5 (4) | +0.2 (5) | 1 |
| Basilisk | −0.2 (6) | +2.9 (2) | +1.8 (5) | −4.1 (9) | +0.1 (6) | 1 |
| Frost Wyrm | −6.7 (9) | +0.5 (6) | +1.6 (6) | +4.0 (1) | −0.1 (7) | 1 |
| Golem | 0.0 (5) | +2.4 (3) | −7.1 (8) | −0.6 (7) | −1.3 (8) | 1 |
| Leviathan | −3.8 (8) | +4.2 (1) | −12.5 (10) | +1.2 (5) | −2.7 (9) | 1 |
| Treant | +4.0 (3) | −3.9 (9) | −11.8 (9) | +0.3 (6) | −2.9 (10) | 1 |

**After, `neutral`:**

| Beast | `solo` | `elite` | `squad` | `horde` | Overall | Top-3 shapes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Basilisk | +16.9 (1) | +12.9 (1) | +7.9 (1) | −1.8 (6) | +9.0 (1) | 3 |
| Kirin | +2.6 (6) | +9.3 (2) | +6.3 (4) | +5.4 (2) | +5.9 (2) | 2 |
| Griffin | +7.5 (3) | +7.3 (3) | +4.9 (5) | −7.6 (10) | +3.0 (3) | 2 |
| Treant | +8.7 (2) | +3.0 (4) | −11.3 (9) | +4.0 (4) | +1.1 (4) | 1 |
| Golem | +6.0 (4) | −1.7 (6) | −7.9 (8) | +5.9 (1) | +0.6 (5) | 1 |
| Tarasque | −6.0 (7) | −2.2 (7) | +6.8 (2) | −2.6 (8) | −1.0 (6) | 1 |
| Leviathan | +5.2 (5) | +1.6 (5) | −14.9 (10) | 0.0 (5) | −2.0 (7) | 0 |
| Frost Wyrm | −7.2 (8) | −4.9 (8) | +3.8 (6) | −1.9 (7) | −2.6 (8) | 0 |
| Phoenix | −16.2 (9) | −10.2 (9) | +6.7 (3) | +4.3 (3) | −3.9 (9) | 2 |
| Thunderbird | −17.7 (10) | −15.0 (10) | −2.3 (7) | −5.6 (9) | −10.1 (10) | 0 |

Per single seed the `elemental` overall ranges −5.4 (Treant, seed 777) … +4.6 (Thunderbird,
12345), and `neutral` −13.1 (Thunderbird, 12345) … +9.5 (Basilisk, 4242). The committed
`tuned-report.md` is seed 12345 alone.

### Targets: met and missed (on the 3-seed mean)

| # | Target | Result |
| --- | --- | --- |
| 1 | `elemental` overall within ±5 | **Met**: −2.9 … +2.7 (spread 5.6, from 23.1). |
| 1 | `neutral` overall within about ±7 | **Missed by two beasts**: Basilisk +9.0 and Thunderbird −10.1; the other eight are −3.9 … +5.9 (spread 19.1, from 39.6). |
| 2 | Every beast top 3 in ≥ 1 shape (`elemental`) | **Met on the mean**, ten of ten, with no slack: Golem holds the elite's 3rd place at +2.4 against Griffin's +2.3. Per single seed two beasts miss each time (12345: Treant, Basilisk; 777: Kirin, Griffin; 4242: Kirin, Leviathan). |
| 2 | Same, `neutral` (secondary) | Eight of ten; Leviathan and Frost Wyrm have none. |
| 3 | No beast top 3 in every shape | **Met** in both modes (the most is Basilisk, three shapes in `neutral`). |
| 4 | Tanks find a niche beyond hordes | **Yes, the elite.** `elemental`: Leviathan is 1st against the elite (+4.2; it was already 2nd before this pass) and Golem 3rd (+2.4, from 8th). `neutral`: Golem is 1st against the horde and 4th against the giant (+6.0); Leviathan 5th against the giant. Taunt plus the Golem's area shield is what does it. Both stay in the bottom three against the squad (−7.1 and −12.5 `elemental`, with Treant), whose archers and casters stay out of taunt range. |
| 5 | Skill level 10 does not blow up | **Met**; see below. |
| – | Hard constraints | **Met**: turn ratio 1.118 (Speed 88–110), order as approved, totals 573–630, Move and Crit unchanged, signature skills kept. |

### Skill level 10 sanity check

The final data at `--skill-level 10` (every skill and passive at ×1.27 magnitude, the level-5 and
level-10 tier gates passed), same seeds:

**`elemental`:**

| Beast | `solo` | `elite` | `squad` | `horde` | Overall | Top-3 shapes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Tarasque | +8.3 (1) | −0.3 (7) | +9.5 (2) | −3.7 (8) | +3.5 (1) | 2 |
| Phoenix | −5.3 (9) | −0.9 (8) | +11.8 (1) | +2.8 (3) | +2.1 (2) | 2 |
| Thunderbird | +6.8 (2) | −3.7 (9) | +3.1 (4) | +2.0 (4) | +2.1 (3) | 1 |
| Kirin | +1.0 (4) | +1.5 (4) | +2.8 (5) | +2.8 (2) | +2.0 (4) | 1 |
| Frost Wyrm | −8.1 (10) | +1.4 (5) | +1.2 (6) | +8.3 (1) | +0.7 (5) | 1 |
| Griffin | −0.7 (7) | +2.2 (2) | +4.1 (3) | −5.2 (10) | +0.1 (6) | 2 |
| Basilisk | +0.7 (5) | +1.6 (3) | −0.3 (7) | −4.9 (9) | −0.7 (7) | 1 |
| Golem | −0.6 (6) | +3.3 (1) | −7.1 (8) | −0.6 (6) | −1.2 (8) | 1 |
| Leviathan | −4.1 (8) | +0.3 (6) | −12.4 (9) | −0.5 (5) | −4.2 (9) | 0 |
| Treant | +2.0 (3) | −5.3 (10) | −12.8 (10) | −1.1 (7) | −4.3 (10) | 1 |

**`neutral`:**

| Beast | `solo` | `elite` | `squad` | `horde` | Overall | Top-3 shapes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Basilisk | +10.1 (3) | +11.9 (1) | +5.6 (4) | −0.8 (6) | +6.7 (1) | 2 |
| Kirin | +5.7 (4) | +7.2 (2) | +6.6 (3) | +5.2 (2) | +6.2 (2) | 3 |
| Griffin | +19.0 (1) | +2.0 (4) | +7.1 (2) | −6.9 (10) | +5.3 (3) | 2 |
| Golem | +11.2 (2) | −0.5 (6) | −8.5 (8) | +4.9 (3) | +1.8 (4) | 2 |
| Frost Wyrm | −7.0 (8) | +0.6 (5) | +4.4 (6) | +8.4 (1) | +1.6 (5) | 1 |
| Treant | +4.2 (5) | +5.3 (3) | −11.9 (9) | +3.0 (5) | +0.1 (6) | 1 |
| Tarasque | −3.1 (6) | −3.3 (8) | +5.0 (5) | −5.9 (8) | −1.8 (7) | 0 |
| Phoenix | −14.3 (9) | −7.8 (9) | +7.3 (1) | +3.7 (4) | −2.8 (8) | 1 |
| Leviathan | −6.6 (7) | −2.2 (7) | −14.1 (10) | −4.7 (7) | −6.9 (9) | 0 |
| Thunderbird | −19.1 (10) | −13.2 (10) | −1.4 (7) | −6.8 (9) | −10.2 (10) | 0 |

The spread holds: `elemental` −4.3 … +3.5 (spread 7.8 against 5.6 at level 1) with every beast
still inside ±5. Leviathan loses its elite slot (to Golem, Griffin and Basilisk), so it has no
top-3 shape, the only target-2 miss. `neutral` is −10.2 … +6.7. The level-10 tier bonuses (mostly
small debuffs, shields and heals) favour Kirin and Phoenix slightly and cost Leviathan and Treant.

### Heal scaling check

A beast's heal cast on itself, as a share of its own HP at levels 1 / 50 / 100 (heal / HP in
parentheses); the avatar's heals against the roster's mean HP. **Before** (flat HP, start of the
pass):

| Heal | Caster | Magnitude | L1 | L50 | L100 |
| --- | --- | ---: | ---: | ---: | ---: |
| Deep Shell | leviathan | 12 | 67% (12/18) | 17% (12/70) | 10% (12/122) |
| Tidal Renewal | leviathan | 14 | 78% (14/18) | 20% (14/70) | 11% (14/122) |
| Verdant Mend | treant | 18 | 90% (18/20) | 24% (18/76) | 13% (18/134) |
| Lifebloom | treant | 10 | 50% (10/20) | 13% (10/76) | 7% (10/134) |
| Rebirth Flame | phoenix | 30 | 214% (30/14) | 57% (30/53) | 33% (30/92) |
| Sacred Spring | kirin | 12 | 71% (12/17) | 18% (12/66) | 10% (12/115) |
| Halo | kirin | 16 | 94% (16/17) | 24% (16/66) | 14% (16/115) |
| Mending Light | avatar (sim fixture), vs mean beast HP | 8 | 47% (8/17) | 12% (8/65) | 7% (8/114) |
| Verdant Pulse | avatar (sim fixture), vs mean beast HP | 3 | 18% (3/17) | 5% (3/65) | 3% (3/114) |

**After** (`SpecialAttack`-scaled, final numbers; the avatar is the curve-following fixture):

| Heal | Caster | Magnitude | L1 | L50 | L100 |
| --- | --- | ---: | ---: | ---: | ---: |
| Deep Shell | leviathan | 24 | 17% (3/18) | 17% (12/70) | 17% (21/122) |
| Tidal Renewal | leviathan | 29 | 22% (4/18) | 20% (14/70) | 20% (25/122) |
| Verdant Mend | treant | 40 | 30% (6/20) | 32% (24/76) | 31% (42/134) |
| Lifebloom | treant | 19 | 15% (3/20) | 14% (11/76) | 15% (20/134) |
| Rebirth Flame | phoenix | 44 | 57% (8/14) | 57% (30/53) | 58% (53/92) |
| Sacred Spring | kirin | 20 | 24% (4/17) | 26% (17/66) | 26% (30/115) |
| Halo | kirin | 19 | 24% (4/17) | 24% (16/66) | 24% (28/115) |
| Mending Light | avatar (sim fixture), vs mean beast HP | 13 | 12% (2/17) | 11% (7/66) | 11% (13/115) |
| Verdant Pulse | avatar (sim fixture), vs mean beast HP | 5 | 6% (1/17) | 5% (3/66) | 4% (5/115) |

Every heal is now flat across levels to within 2 points (whole-HP rounding at level 1, where HP is
14–22). `SkillLibraryTests.Library_BeastHealsRestoreAboutTheSameShareOfHpAtEveryLevel` holds a
beast heal's drift under 5 points. With the old `10 + level` avatar block, Mending Light would have
been 6% at level 1 against 12% at level 50.

### Iteration log

Four quick filters (one seed, four compositions per shape) and ten three-seed confirmations (about
12 minutes each):

- **q1–q4:** the speed spread and a first Basilisk cut (SpA 153 → 118, poison, execute power), then
  the unused-stat swaps for Golem and Treant. Golem barely moved (−13 → −12) until a sensitivity run
  of its shield (70% at cooldown 2, range 2) and a 3-turn taunt took it to −1 `elemental` and +14
  `neutral`: the tank's value is its team-wide shield, not its stats.
- **f1:** the shield back to 60% at cooldown 3. `elemental` spread 23 → 11; Tarasque +5.4, Griffin
  −5.8.
- **f2–f3:** Griffin rebuilt (SpA into bulk, Gust up), Basilisk at 115 SpA, Tarasque and Frost Wyrm
  trimmed, Kirin and Golem nudged: every beast inside ±4.4, three without a top-3 shape.
- **f4–f8:** chasing the last niches. Griffin's elite and squad slots came from +4 HP and +5 Def;
  Kirin's from a larger team heal; Basilisk's from execute +60% and a 45% petrify. Each move that
  gave one beast an elite slot took it from another (Golem, Basilisk and Griffin within 0.5 points).
- **f9:** Thunderbird's Static Charge speed buff 10% → 20% moved nothing and was reverted; Basilisk
  SpA 115 → 117.
- **f10 (final):** Granite Bulwark 65% → 70% gave Golem the elite slot back.

### Caveats

- **Target 2 is at noise level.** The elite's third slot is decided by 0.1 points on the mean, and
  every single seed has two beasts without a top-3 shape. A shape cell moves by several points
  between seeds, so fitting ranks this closely is partly fitting noise.
- **The `neutral` misses are the element chart.** Thunderbird is +2.4 `elemental` and −10.1
  `neutral` (Lightning is strong against Water and Air, and its sub-40-power hits lean on the 2×);
  Basilisk is +0.1 and +9.0 (Dark is strong only against Light, and weak defensively to Nature and
  Light). Closing one mode opens the other; `elemental` was held as the primary target.
- **Tanks are squad-weak by construction.** The taunt reaches 3 hexes and the squad's archers and
  casters stay out of it; nothing in the kits addresses that (a ranged taunt would).
- **Calibration miss:** `neutral` `solo` level 1 (62.9% at the closest multiplier): the single
  giant's level-1 stats round too coarsely to split, as in the exploratory run.
- **The avatar is a fixture:** 100 in every stat at max level on the medium curve. The real avatar's
  stats come from `AvatarStatsSO` and its gear, not authored yet.
- Only the default loadouts were measured, at skill levels 1 and 10. The 30 non-default skills and
  the seven non-default passives keep their first-draft numbers (heals rescaled only).

## Element chart v2

The element chart was replaced by a user-approved **v2** (see `docs/design/battle-system.md`,
"Element system"), and the roster and kits were then lightly re-fit to it. Setup as in the
previous section: library kits and avatar at skill level 1, generated encounters, levels 1 / 50 /
100, **mean of seeds 12345 / 777 / 4242**. `tuned-report.md` is regenerated from the default run
(seed 12345).

### The chart change

Attacker-side, as before. The main eight (Fire through Metal) are now **normalized**: every row is
2x against exactly two and 0.5x against exactly two of them, and every column takes 2x from exactly
two and 0.5x from exactly two (`ElementChartTests.MainEight_AreNormalized_TwoStrongTwoWeak_ByRowAndColumn`).
In v1, within the main eight, Earth and Nature each took 2x from three elements while Fire,
Lightning and Ice took 2x from one, and Metal's row had a single 2x target. Light and Dark become
generalists through a new **1.25x** tier (`ElementChart.Mild`).

| Matchup | v1 | v2 | Why |
| --- | ---: | ---: | --- |
| Air → Fire | 1x | 2x | A gust snuffs flame |
| Water → Metal | 1x | 2x | Rust |
| Earth → Ice | 1x | 2x | Rock shatters ice |
| Metal → Lightning | 0.5x | 2x | The lightning rod |
| Nature → Lightning | 1x | 0.5x | Wood insulates |
| Air → Nature | 2x | 0.5x | Forests withstand wind |
| Metal → Air | 1x | 0.5x | A blade can't cut wind |
| Metal → Dark | 1x | 0.5x | Dark resists Metal |
| Lightning → Light | 1x | 0.5x | Light resists Lightning |
| Light → Water, Air, Ice, Earth | 1x | 1.25x | Light as a generalist |
| Dark → Fire, Lightning, Nature, Metal | 1x | 1.25x | Dark as a generalist |
| Water → Earth | 2x | 1x | Now neutral |
| Earth → Metal | 2x | 1x | Now neutral |
| Air → Lightning | 0.5x | 1x | Now neutral |
| Nature → Fire | 0.5x | 1x | Now neutral |

### Element effect, v1 → v2

The **element effect** is a beast's `elemental` overall marginal minus its `neutral` overall
marginal (three-seed means): how much the element system helps or hurts it. The `neutral` mode
forces every skill to `Element.None`, so it does not depend on the chart. **v1** is the committed
roster under v1 (identical to the previous section's "after" tables); **v2, same roster** is only
the chart change; **v2, final** is after the light retune below (which moves both modes).

| Beast | Element | v1 `elemental` | v1 `neutral` | **v1 effect** | v2 `elemental` (same roster) | **v2 effect (same roster)** | Final `elemental` | Final `neutral` | **Final effect** |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Thunderbird | Lightning | +2.4 | −10.1 | **+12.5** | −7.2 | **+3.0** | −2.2 | −0.5 | **−1.6** |
| Phoenix | Fire | +0.2 | −3.9 | **+4.0** | −0.1 | **+3.8** | −1.2 | −5.1 | **+3.9** |
| Tarasque | Metal | +2.7 | −1.0 | **+3.7** | +1.5 | **+2.5** | +2.7 | −0.6 | **+3.3** |
| Frost Wyrm | Ice | −0.1 | −2.6 | **+2.4** | −1.7 | **+0.9** | −1.8 | −1.7 | **−0.1** |
| Leviathan | Water | −2.7 | −2.0 | **−0.7** | −3.2 | **−1.2** | +0.1 | +2.3 | **−2.3** |
| Golem | Earth | −1.3 | +0.6 | **−1.9** | −1.2 | **−1.7** | −0.1 | +3.4 | **−3.5** |
| Griffin | Air | +1.0 | +3.0 | **−2.1** | +3.6 | **+0.6** | +0.7 | −2.3 | **+3.0** |
| Treant | Nature | −2.9 | +1.1 | **−4.0** | +0.1 | **−1.1** | −0.4 | −0.7 | **+0.3** |
| Kirin | Light | +0.7 | +5.9 | **−5.2** | +3.4 | **−2.6** | +2.0 | +4.4 | **−2.4** |
| Basilisk | Dark | +0.1 | +9.0 | **−8.9** | +4.7 | **−4.3** | +0.1 | +0.8 | **−0.7** |

- **v1:** −8.9 … +12.5 (spread 21.4, mean absolute 4.5).
- **v2, same roster:** −4.3 … +3.8 (spread 8.1, mean absolute 2.2).
- **Final:** −3.5 … +3.9 (spread 7.4, mean absolute 2.1).

The chart alone cut the spread of element effects from 21.4 to 8.1 points. Lightning's +12.5 was
the outlier: in v1 Metal and Air attacks were 0.5x into it, in v2 Metal is 2x and Air 1x (and Dark
1.25x), with only Nature newly 0.5x. Dark's −8.9 and Light's −5.2 were the other end (each hit only
the other). Nature's loss shrinks (−4.0 → −1.1 on the same roster); Earth's barely moves (−1.9 →
−1.7). Fire (+3.8) and Metal (+2.5) keep a small edge; after the retune the largest effects are
Phoenix +3.9 and Golem −3.5. Each element is one beast, so an element's effect is also that beast's
kit and stance meeting the encounter pool (every enemy is dealt from a shuffled deck of all ten elements).

### Marginal clear rate under v2, same roster (before the retune)

Points of clear rate, levels averaged, then averaged over the three seeds; (n) = rank within the
shape on the mean. `neutral` is identical to v1 (it does not read the chart).

**v2, same roster, `elemental`:**

| Beast | `solo` | `elite` | `squad` | `horde` | Overall | Top-3 shapes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Basilisk | +5.5 (3) | +7.0 (1) | +8.0 (3) | −1.6 (8) | +4.7 (1) | 3 |
| Griffin | +5.9 (1) | +4.1 (2) | +5.7 (4) | −1.2 (7) | +3.6 (2) | 2 |
| Kirin | +1.3 (5) | +3.6 (3) | +5.0 (5) | +3.8 (2) | +3.4 (3) | 2 |
| Tarasque | +3.0 (4) | −0.5 (6) | +10.0 (1) | −6.4 (9) | +1.5 (4) | 1 |
| Treant | +5.7 (2) | −2.0 (8) | −9.4 (9) | +6.0 (1) | +0.1 (5) | 2 |
| Phoenix | −5.1 (8) | −4.0 (9) | +8.4 (2) | +0.4 (6) | −0.1 (6) | 1 |
| Golem | −0.6 (6) | −0.1 (5) | −5.8 (7) | +1.8 (4) | −1.2 (7) | 0 |
| Frost Wyrm | −7.0 (10) | −1.4 (7) | −1.7 (6) | +3.1 (3) | −1.7 (8) | 1 |
| Leviathan | −3.0 (7) | +1.2 (4) | −12.1 (10) | +1.1 (5) | −3.2 (9) | 0 |
| Thunderbird | −5.7 (9) | −7.9 (10) | −8.1 (8) | −7.0 (10) | −7.2 (10) | 0 |

Per single seed the overall ranges −9.9 (Thunderbird, 777) … +6.5 (Basilisk, 777).

**v2, same roster, `neutral`:**

| Beast | `solo` | `elite` | `squad` | `horde` | Overall | Top-3 shapes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Basilisk | +16.9 (1) | +12.9 (1) | +7.9 (1) | −1.8 (6) | +9.0 (1) | 3 |
| Kirin | +2.6 (6) | +9.3 (2) | +6.3 (4) | +5.4 (2) | +5.9 (2) | 2 |
| Griffin | +7.5 (3) | +7.3 (3) | +4.9 (5) | −7.6 (10) | +3.0 (3) | 2 |
| Treant | +8.7 (2) | +3.0 (4) | −11.3 (9) | +4.0 (4) | +1.1 (4) | 1 |
| Golem | +6.0 (4) | −1.7 (6) | −7.9 (8) | +5.9 (1) | +0.6 (5) | 1 |
| Tarasque | −6.0 (7) | −2.2 (7) | +6.8 (2) | −2.6 (8) | −1.0 (6) | 1 |
| Leviathan | +5.2 (5) | +1.6 (5) | −14.9 (10) | 0.0 (5) | −2.0 (7) | 0 |
| Frost Wyrm | −7.2 (8) | −4.9 (8) | +3.8 (6) | −1.9 (7) | −2.6 (8) | 0 |
| Phoenix | −16.2 (9) | −10.2 (9) | +6.7 (3) | +4.3 (3) | −3.9 (9) | 2 |
| Thunderbird | −17.7 (10) | −15.0 (10) | −2.3 (7) | −5.6 (9) | −10.1 (10) | 0 |

Per single seed the overall ranges −13.1 (Thunderbird, 12345) … +9.5 (Basilisk, 4242).

### Marginal clear rate after the light retune (final)

Same measurement, final roster and kits (the committed data).

**Final, `elemental`:**

| Beast | `solo` | `elite` | `squad` | `horde` | Overall | Top-3 shapes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Tarasque | +3.3 (3) | +1.1 (6) | +12.7 (1) | −6.2 (9) | +2.7 (1) | 2 |
| Kirin | +0.2 (7) | +2.3 (3) | +4.0 (3) | +1.6 (5) | +2.0 (2) | 2 |
| Griffin | +5.0 (1) | +1.6 (4) | +1.4 (5) | −5.3 (8) | +0.7 (3) | 1 |
| Basilisk | +2.3 (4) | +2.5 (2) | +3.4 (4) | −7.9 (10) | +0.1 (4) | 1 |
| Leviathan | +0.8 (5) | +3.6 (1) | −9.1 (9) | +4.9 (3) | +0.1 (5) | 2 |
| Golem | +0.7 (6) | +1.5 (5) | −5.2 (8) | +2.8 (4) | −0.1 (6) | 0 |
| Treant | +4.7 (2) | −2.1 (8) | −9.6 (10) | +5.4 (1) | −0.4 (7) | 2 |
| Phoenix | −5.5 (9) | −5.2 (10) | +6.1 (2) | −0.2 (7) | −1.2 (8) | 1 |
| Frost Wyrm | −7.0 (10) | −2.0 (7) | −3.0 (7) | +5.0 (2) | −1.8 (9) | 1 |
| Thunderbird | −4.4 (8) | −3.3 (9) | −0.7 (6) | −0.1 (6) | −2.2 (10) | 0 |

Per single seed the overall ranges −5.1 (Treant, 777) … +6.0 (Tarasque, 777).

**Final, `neutral`:**

| Beast | `solo` | `elite` | `squad` | `horde` | Overall | Top-3 shapes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Kirin | +3.0 (6) | +6.7 (1) | +5.6 (3) | +2.4 (5) | +4.4 (1) | 2 |
| Golem | +7.2 (3) | +4.3 (4) | −4.8 (8) | +7.1 (1) | +3.4 (2) | 2 |
| Leviathan | +11.2 (1) | +5.9 (2) | −11.1 (9) | +3.4 (2) | +2.3 (3) | 3 |
| Basilisk | +7.2 (2) | +4.8 (3) | +1.5 (6) | −10.4 (9) | +0.8 (4) | 2 |
| Thunderbird | −5.4 (7) | −7.0 (9) | +7.3 (2) | +3.0 (3) | −0.5 (5) | 2 |
| Tarasque | −9.8 (9) | −1.7 (7) | +9.6 (1) | −0.6 (8) | −0.6 (6) | 1 |
| Treant | +4.0 (5) | +3.4 (5) | −12.1 (10) | +2.0 (7) | −0.7 (7) | 0 |
| Frost Wyrm | −6.7 (8) | −4.8 (8) | +1.8 (5) | +2.9 (4) | −1.7 (8) | 0 |
| Griffin | +6.0 (4) | −1.3 (6) | −2.0 (7) | −12.2 (10) | −2.3 (9) | 0 |
| Phoenix | −16.6 (10) | −10.1 (10) | +4.2 (4) | +2.4 (6) | −5.1 (10) | 0 |

Per single seed the overall ranges −6.2 (Phoenix, 777) … +5.7 (Kirin, 12345).

### Why a retune, and its limits

On the same roster v2 missed the primary target once: **Thunderbird −7.2 `elemental`** (from +2.4).
Its v1 edge had been hiding the weakest `neutral` line on the roster (−10.1), the previous
section's caveat. Golem, Leviathan and Thunderbird also had no top-3 shape. A light retune followed
under the previous pass's rules: small stat and skill-number moves only, at most six three-seed
iterations, turn ratio 1.10–1.15 in the approved Speed order, six-stat totals 570–630, Move 2–5,
crit chances unchanged (they are user-approved values, pinned by `BeastRosterTests`), signature
skills kept, every unlimited damage skill at or below 1.2× its budget.

### Stats, before → after

| Beast | HP | Atk | Def | SpA | SpD | Spe | Total | Move | Crit |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Thunderbird | 110 → **116** | 117 | 88 | 114 → **108** | 91 | 110 | 630 | 4 → **3** | 15% |
| Leviathan | 122 → **132** | 86 | 126 | 86 | 100 | 94 | 614 → **624** | 3 | 3% |
| Kirin | 115 | 51 | 90 | 149 → **140** | 124 | 101 | 630 → **621** | 4 | 5% |
| Basilisk | 103 | 94 | 79 | 117 → **110** | 94 | 105 | 592 → **585** | 5 | 12% |

Speed did not change, so the turn ratio is still **1.118** (Speed 88–110) in the approved order.
Archetypes hold: Kirin is still the highest SpA (140, next Phoenix 120); Leviathan is still third in
HP (132, behind Golem 150 and Treant 134) and Defense; Thunderbird is still the fastest with the
highest crit.

- **Thunderbird Move 4 → 3** was the one lever that moved it. As the fastest beast with Move 4 it
  reached the enemy alone and took its focus (the first pass's "fastest fragile beast walks in
  first" finding). A quick filter (seed 12345, four compositions) put Move 3 at +5.3 `elemental` and
  +5.7 `neutral` against the same data at Move 4, and **+4 Speed (114, SpD −4) made it worse**
  (−8.3 `elemental` over three seeds, the horde −12.6). It still acts first; it now arrives with
  its team. **HP +6 / SpA −6** is a little more bulk from the stat its default kit reads less (+2.3
  in the same quick filter).
- **Leviathan +10 HP** (total 624): the elite and horde tank had lost its elite slot under v2.
- **Kirin −9 SpA** and **Basilisk −7 SpA**: after the Thunderbird and Leviathan buffs Kirin was
  first overall and top 3 in three shapes, Basilisk third and top 3 in two; the cuts free elite,
  squad and horde slots. Kirin's heals and shields scale with its SpA, so they fall by the same 6%.

### Skill changes

| Skill | Owner (slot) | Change | DPT / budget | Why |
| --- | --- | --- | --- | --- |
| Thunder Talons `thunder_talons` | Thunderbird (1) | 33 → **36** × 3 hits | 108 / 90 = 1.2× | Thunderbird −7.2 |
| Chain Lightning `chain_lightning` | Thunderbird (2) | 25 → **28** × 3 hits | 84 / 70 = 1.2× | Same |
| Static Charge `static_charge` | Thunderbird (3) | +15 → **+25** CritChance 3t | – | Same (in place of the approved base crit) |
| Serpent Bite `serpent_bite` | Leviathan (1) | 82 → **86** | 86 / 90 = 0.96× | Leviathan −3.2 |
| Undertow `undertow` | Leviathan (2) | Taunt 70% → **85%** | – | Same; moved `neutral` a little, not `elemental` |
| Boulder Slam `boulder_slam` | Golem (1) | 85 → **90** | 90 / 90 = 1.0× | Golem had no top-3 shape |
| Granite Bulwark `granite_bulwark` | Golem (3) | Shield 70% → **85%** Def | – | Same; the Golem's biggest lever again |
| Frost Breath `frost_breath` | Frost Wyrm (3) | 42 → **46** | 46 / 70 = 0.66× | Frost Wyrm lost its horde slot at r3–r4 |
| Iron Crush `iron_crush` | Tarasque (2) | 155 → **148** | 74 / 90 = 0.82× | Tarasque held three top-3 shapes at r5 |

Thunder Talons and Chain Lightning now sit exactly on the budget rule's 1.2× ceiling (they were
1.1× and 1.07×); nothing is above it, and the rule's numbers did not move. The design doc's kit
tables and budget note are updated.

### Targets: met and missed (on the 3-seed mean, final)

| # | Target | Result |
| --- | --- | --- |
| 1 | `elemental` overall within ±5 | **Met**: −2.2 … +2.7 (spread 4.9; v1 −2.9 … +2.7, spread 5.6; v2 before the retune −7.2 … +4.7). |
| 1 | `neutral` overall within about ±7 | **Met**: −5.1 … +4.4 (spread 9.5). v1 missed it by two beasts (Basilisk +9.0, Thunderbird −10.1; spread 19.1). |
| 2 | Every beast top 3 in ≥ 1 shape (`elemental`) | **Missed by two**: Thunderbird (best: 6th in `squad` and `horde`, 4.7 and 5.0 points short) and Golem (4th in `horde` by 2.1, 5th in `elite` by 0.8). Eight of ten; v1 was ten of ten with no slack, v2 before the retune seven of ten. |
| 2 | Same, `neutral` (secondary) | Six of ten (Griffin, Treant, Frost Wyrm and Phoenix have none; Thunderbird now has two). |
| 3 | No beast top 3 in every shape | **Met** in both modes (the most is Leviathan, three shapes in `neutral`). |
| – | Turn ratio 1.10–1.15 | **Met**: 1.118, unchanged. |
| – | Element effect: no element systematically helped or hurt | **Much improved, not zero**: spread 21.4 → 7.4 (8.1 from the chart alone). Fire keeps about +4 and Metal +3; Earth −3.5 after the retune (−1.7 from the chart alone). |
| – | Hard constraints | **Met**: totals 573–630, Move 2–5, crit unchanged, signature skills and default loadouts kept, every unlimited damage skill ≤ 1.2× its budget. |

### Iteration log

Six three-seed iterations (about 11 minutes each) and one set of quick filters:

- **r1:** Thunder Talons 35, Chain Lightning 28, Serpent Bite 86, Basilisk SpA 113. Thunderbird
  −7.2 → −6.3; skill numbers barely move it.
- **r2:** Talons 36, Static Charge +25 crit, Thunderbird Speed 114 / SpD 87, Bulwark 75, Undertow
  85%. Thunderbird **worse** (−8.3; horde −12.6): more Speed walks it in sooner. (A base crit of 20
  was tried first and withdrawn before the run: base crit chances are user-approved values.)
- **Quick filters q0–q3** (one seed, four compositions, paired on the same seed): Speed back to 110
  (q0); Move 3 (q1: +5.3 `elemental`, +5.7 `neutral` over q0); HP +6 / SpA −6 (q2: +2.3); HP +6 /
  Atk −6 (q3: +1.9).
- **r3:** Thunderbird Move 3, Leviathan HP 132. Every beast inside ±5 (−4.0 … +3.8) and ±7 in
  `neutral`; Golem, Thunderbird and Frost Wyrm without a top-3 shape.
- **r4:** Thunderbird HP 116 / SpA 108, Kirin SpA 143, Boulder Slam 90. `elemental` −2.9 … +2.7;
  Kirin top 3 in three shapes, Golem, Thunderbird and Frost Wyrm in none.
- **r5:** Kirin SpA 137, Bulwark 80, Frost Breath 46. Frost Wyrm took the horde's third place;
  Kirin lost all of its slots (overshoot); Tarasque held three.
- **r6 (final):** Kirin SpA 140, Basilisk SpA 110, Iron Crush 148, Bulwark 85. Eight of ten with a
  top-3 shape; the iteration budget is spent.

### Caveats

- **Target 2 is at noise level, as before.** Golem is 0.8 points from the elite's third place, and
  a shape cell moves by several points between seeds. Thunderbird is further off: its best shapes
  are 5 points short, its skills are on the budget ceiling, and Move 3 only partly fixed it being
  focused first; closing the rest likely needs a kit or behaviour change rather than numbers.
- **Undertow 70% → 85% is probably inert** for the primary mode (r2 showed no `elemental` change for
  Leviathan's elite); it stayed because each later iteration was measured with it.
- **Single seeds still stray**: per seed the final `elemental` overall runs −5.1 (Treant, 777) …
  +6.0 (Tarasque, 777), so a single-seed report such as the committed `tuned-report.md` can show a
  beast just outside ±5.
- **Skill level 10 was not re-measured** in this pass (the previous pass's sanity check is on the v1
  chart).
- The mild tier is only on Light and Dark attacks: the player side gets it through Kirin's and
  Basilisk's elemental skills, and the enemy side through Light and Dark enemies dealt from the same
  deck, so it cuts both ways for every beast.

## Thunderbird range vs move

The element chart v2 retune cut the Thunderbird's Move 4 → 3 because, as the fastest beast, it
reached the enemy alone and took its focus. The alternative (user suggestion): give it reach
instead, so it does not have to walk deep into the fight. As a Skirmisher it retreats after firing
with its leftover movement, capped to its longest enemy-side `SingleTarget` / `Line` range — with
Thunder Talons at range 1 it could never actually retreat. Setup as in "Element chart v2": library
kits and avatar at skill level 1, generated encounters, levels 1 / 50 / 100, **mean of seeds
12345 / 777 / 4242**; only the Thunderbird's move range and Thunder Talons change. Chain Lightning
(28 × 3, radius 2) is unchanged.

| Variant | Move | Talons range | Talons power | DPT / budget |
| --- | ---: | ---: | --- | --- |
| **A** (element chart v2 final) | 3 | 1 | 36 × 3 = 108 | 108 / 90 = 1.2× (the ceiling) |
| **B** (the budget rule's figure) | 4 | 2 | 26 × 3 = 78 | 78 / 70 = 1.11× (burst striker ≈ 1.1× → 77, rounded to whole hits) |
| **C** (B at the ceiling) | 4 | 2 | 28 × 3 = 84 | 84 / 70 = 1.2× (the ceiling, like A) |

### Results (3-seed means)

| | A | B | **C (chosen)** |
| --- | ---: | ---: | ---: |
| Thunderbird `elemental` overall | −2.2 | −3.5 | **−2.9** |
| Thunderbird `elemental` solo / elite / squad / horde | −4.4 (8) / −3.3 (9) / −0.7 (6) / −0.1 (6) | −4.3 (8) / −6.1 (10) / +1.6 (6) / −5.1 (8) | −3.4 (8) / −5.4 (10) / +2.2 (6) / −4.8 (8) |
| Thunderbird `neutral` overall | −0.5 | −3.5 | −2.6 |
| Thunderbird `neutral` solo / elite / squad / horde | −5.4 (7) / −7.0 (9) / +7.3 (2) / +3.0 (3) | −13.5 (8) / −8.4 (9) / +9.8 (1) / −2.1 (7) | −11.9 (8) / −7.3 (9) / +10.9 (1) / −1.9 (7) |
| Thunderbird survival, `elemental` / `neutral` (mean of shapes) | 21.3% / 18.3% | 22.1% / 21.3% | 22.4% / 22.1% |
| Thunderbird damage share, `elemental` / `neutral` | 26.4% / 27.5% | 26.5% / 29.2% | 27.1% / 30.0% |
| All beasts, `elemental` overall (target ±5) | −2.2 … +2.7 (spread 4.9) | −3.5 … +3.0 (6.5) | −2.9 … +3.1 (6.0) |
| All beasts, `neutral` overall (target ±7) | −5.1 … +4.4 (9.5) | −5.5 … +4.5 (10.0) | −5.8 … +4.3 (10.1) |
| Top 3 in ≥ 1 shape, `elemental` | 8 / 10 (not Thunderbird, Golem) | 9 / 10 (not Thunderbird) | **9 / 10** (not Thunderbird) |
| Top 3 in ≥ 1 shape, `neutral` | 6 / 10 | 8 / 10 | 7 / 10 |
| Top 3 in every shape (either mode) | none | none | none |
| Turn ratio | 1.118 | 1.118 | 1.118 |

(n) = rank within the shape on the mean. Per single seed the Thunderbird's `elemental` overall runs
−4.7 … +0.4 (A), −7.1 … −1.1 (B) and −6.6 … −0.4 (C); variant A reproduces the "Element chart v2"
final tables exactly.

**C, full `elemental`:**

| Beast | `solo` | `elite` | `squad` | `horde` | Overall | Top-3 shapes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Griffin | +5.4 (1) | +3.2 (2) | +5.0 (2) | −1.1 (7) | +3.1 | 3 |
| Kirin | +1.0 (4) | +2.7 (3) | +3.6 (4) | +1.5 (5) | +2.2 | 1 |
| Tarasque | +0.9 (5) | −0.3 (6) | +11.9 (1) | −6.2 (10) | +1.6 | 1 |
| Basilisk | +3.1 (3) | +2.6 (4) | +3.0 (5) | −6.2 (9) | +0.6 | 1 |
| Golem | +0.8 (6) | +1.7 (5) | −5.7 (8) | +4.1 (3) | +0.2 | 1 |
| Treant | +5.1 (2) | −2.5 (8) | −10.6 (9) | +6.1 (1) | −0.5 | 2 |
| Phoenix | −5.1 (9) | −4.1 (9) | +4.8 (3) | −0.3 (6) | −1.2 | 1 |
| Leviathan | +0.1 (7) | +3.6 (1) | −10.7 (10) | +2.2 (4) | −1.2 | 1 |
| Frost Wyrm | −7.7 (10) | −1.5 (7) | −3.6 (7) | +4.7 (2) | −2.0 | 1 |
| Thunderbird | −3.4 (8) | −5.4 (10) | +2.2 (6) | −4.8 (8) | −2.9 | 0 |

**C, full `neutral`:**

| Beast | `solo` | `elite` | `squad` | `horde` | Overall | Top-3 shapes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Kirin | +4.5 (6) | +6.2 (2) | +3.7 (3) | +2.8 (3) | +4.3 | 3 |
| Golem | +6.9 (4) | +4.2 (4) | −5.4 (8) | +8.6 (1) | +3.6 | 1 |
| Leviathan | +15.3 (1) | +7.4 (1) | −12.8 (10) | +1.6 (6) | +2.9 | 2 |
| Basilisk | +9.7 (2) | +4.9 (3) | +1.6 (6) | −6.1 (9) | +2.5 | 2 |
| Treant | +9.7 (3) | +3.6 (5) | −11.8 (9) | +3.2 (2) | +1.2 | 2 |
| Griffin | +5.8 (5) | +1.4 (6) | +1.9 (5) | −9.3 (10) | −0.1 | 0 |
| Frost Wyrm | −6.8 (7) | −5.6 (8) | +0.4 (7) | +2.7 (4) | −2.3 | 0 |
| Thunderbird | −11.9 (8) | −7.3 (9) | +10.9 (1) | −1.9 (7) | −2.6 | 1 |
| Tarasque | −16.8 (10) | −2.4 (7) | +8.4 (2) | −4.0 (8) | −3.7 | 1 |
| Phoenix | −16.3 (9) | −12.3 (10) | +3.1 (4) | +2.3 (5) | −5.8 | 0 |

### Decision: C

The decision rule: keep the variant that best meets the targets, and prefer a range-2 variant
(it keeps the Thunderbird's "fast" identity: the fastest beast with Move 4 again) if it is within
about 1.5 points of A on the Thunderbird's `elemental` overall and breaks no target A meets.

- **Both B and C qualify.** Every beast stays inside ±5 `elemental` and ±7 `neutral`, nobody is
  top 3 everywhere, and the turn ratio does not move. The Thunderbird is 1.3 (B) and 0.7 (C) points
  below A. Both **improve** the top-3 coverage in `elemental` from 8 to 9 of 10: the Golem takes the
  horde's third place (+2.8 → +4.1, as Leviathan falls from +4.9 to +2.2), and Griffin moves up
  to 2nd in `elite` and `squad` (from 4th and 5th).
- **C over B:** closer to A on the Thunderbird (−2.9 vs −3.5 `elemental`, −2.6 vs −3.5 `neutral`),
  a narrower `elemental` spread (6.0 vs 6.5) and a higher damage share. C puts Talons exactly where A
  had it relative to its budget (1.2×, the ceiling); B's 1.1× would have been a quiet cut on top of
  the reach change. B has one more `neutral` top-3 beast (8 vs 7; the secondary target).
- **No follow-up tweak.** +2 per hit (30 × 3 = 90) would break the 1.2× ceiling (84) at range 2,
  and −2 is B. Nothing else changed.

What range 2 does: the Thunderbird survives more often (21.3% → 22.4% `elemental`, 18.3% → 22.1%
`neutral`) and deals a larger share of its team's damage, and it becomes the best `neutral` squad
beast (+10.9, 1st). It gets worse against the single big target and the horde (`elite` −3.3 → −5.4,
`horde` −0.1 → −4.8 `elemental`): 84 per turn instead of 108 is less single-target damage, which
the giant fight feels most; why the horde got worse was not isolated (this experiment only changed
the two numbers). It still has **no `elemental`
top-3 shape** (best 6th in `squad`, 2.6 points short), as in A.

### Changes

- `beast-roster.json`: Thunderbird `MoveRange` 3 → **4** (Speed unchanged, turn ratio 1.118).
- `skill-library.json`: Thunder Talons `Range` 1 → **2**, power 36 → **28** × 3 hits (84 / 70 =
  1.2× the range-2+ budget); description no longer says "rakes". Chain Lightning unchanged.
- `SkillLibraryTests.Builder_MapsOpenersAndMultiHits` pins Talons' range 2 and 28 power;
  `Library_UnlimitedDamageSkillsStayWithinTheFirstDraftPowerBudget` now checks Talons against the
  range-2+ budget (84 ≤ 84).
- `docs/design/battle-system.md`: roster table (Move 4), move-range note, budget note and the
  Thunderbird kit table. `tuned-report.md` is regenerated from the default run (seed 12345).
