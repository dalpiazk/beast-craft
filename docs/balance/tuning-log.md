# Roster tuning log — first simulator pass

The first tuning pass of the starter roster's base stats against the headless balance simulator's
PvE mode. Only `content/data/Creatures/beast-roster.json` base stats (six stats and
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

## Niche pass: Thunderbird opener, Phoenix/Frost Wyrm lifts, remaining negatives

A read-only analysis of the "Thunderbird range vs move" result over five seeds (12345 / 777 /
4242 / 2024 / 99) found every beast inside ±5 `elemental` but several without a shape they are
reliably good at. The Thunderbird had none: its third default slot, Static Charge (+25 crit
chance, +10% Speed), was dead weight, and at range 2 it parked inside the enemy's radius-2 area
attacks. Phoenix, Frost Wyrm, Leviathan and Thunderbird were the four negatives. This pass
applies three user-approved changes, then makes small skill-only moves for the rest. **No roster
stat changes**: six-stat totals, Move, Speed (turn ratio 1.118) and crit are untouched, and every
signature skill is kept.

Setup as in "Element chart v2": library kits and avatar at skill level 1, generated encounters,
levels 1 / 50 / 100, both element modes. Iterations used seeds 12345 / 777 / 4242; the final
tables are the **mean of five seeds** (the three plus 2024 and 99). A beast has a **robust niche**
in a shape when it is top 3 there in at least 4 of the 5 seeds.

### Changes

User-approved:

| Skill | Before | After | DPT / budget (70 at range 2+, 90 at range 1) |
| --- | --- | --- | --- |
| Thunderbird default loadout | Talons, Chain Lightning, Static Charge | Talons, Chain Lightning, **Storm Dive** | Static Charge stays learnable (level 3) |
| Storm Dive (learn level) | 8 | **5** | defaults must be learnable by level 5 |
| Storm Dive (power, once per battle, first turn) | 230 | **120** | opener, exempt (30 per cooldown turn) |
| Thunder Talons | 28 × 3 | **26 × 3** | 78 / 70 = 1.11× (was 1.2×) |
| Phoenix Ember Shot | 55; DoT 14 3t | **60**; DoT 14 3t | 81 / 70 = 1.16× (was 1.09×) |
| Frost Wyrm Rime Bolt | 48; −8% Speed | **52**; −8% Speed | 52 / 70 = 0.74× |

Measured follow-ups (this pass):

| Skill | Before | After | DPT / budget | Why |
| --- | --- | --- | --- | --- |
| Chain Lightning | 28 × 3 | **22 × 3** | 66 / 70 = 0.94× (was 1.2×) | the planned lever when Talons 26 left the Thunderbird's `neutral` above +7 (+9.1); 24 still measured +7.0–7.1 |
| Leviathan Undertow (taunt + slow) | radius 2 | **radius 3** | utility | the Golem's Stone Challenge fix: the taunt reaches the squad's archers and casters |
| Leviathan Deep Shell | Shield 50% Def 3t; Heal 24 | Shield **65%** Def 3t; Heal 24 | utility | sustain lift for the tank with the lowest damage share |
| Tarasque Iron Crush | 148 | **160** | 80 / 90 = 0.89× | the Thunderbird's rise pushed the Tarasque's `neutral` to −6.5; a heavier single hit lifts it against the big targets |
| Basilisk Coup de Grace | 105, execute +60% | **115**, execute +60% | 75 / 70 = 1.07× | its assassin role: a better finisher in `solo` / `elite` |
| Kirin Radiant Bolt | 62 | **66** | 66 / 70 = 0.94× | the Kirin had fallen out of every `elemental` top 3 (4th in `elite` and `horde`) |

Descriptions follow the numbers (Undertow "three hexes", Deep Shell "about two-thirds of its
Defense", Storm Dive "one heavy hit"; Coup de Grace's stale "+50%" now says +60%). Every
unlimited damage skill is still at or below 1.2× its budget.

### Iteration log (3-seed means unless noted)

| Run | Change on top of the previous | TB `elem` / `neutral` | Range `elemental` | Range `neutral` | `elemental` top-3 coverage | Notes |
| --- | --- | --- | --- | --- | ---: | --- |
| Baseline | "Thunderbird range vs move" C | −2.9 / −2.6 | −2.9 … +3.1 | −5.8 … +4.3 | 9 / 10 | Thunderbird has no shape |
| V1 | the approved changes | +2.3 / **+9.1** | −2.6 … +2.3 | −6.5 … +9.1 | 9 / 10 | TB `neutral` over +7 (squad +21.1); Tarasque `neutral` −6.5 |
| V2 | V1 + Undertow radius 3 | +2.3 / +8.8 | −2.5 … +2.3 | −6.6 … +8.8 | 10 / 10 | Leviathan +0.5 (squad +0.9, horde +1.2) |
| V3 | V2 + Chain Lightning 24 | +1.4 / +7.0 | −2.2 … +1.7 | −6.3 … +7.0 | 10 / 10 | still on the +7 line |
| V4 | V2 + Chain Lightning 26 + Deep Shell 65 | +1.7 / +8.2 | −2.1 … +1.7 | −6.7 … +8.2 | 10 / 10 | Deep Shell: Leviathan +0.8 / +1.4 |
| V5 | V2 + CL 24 + Deep Shell 65 + Iron Crush 160 + Coup 115 | +1.2 / +7.1 | −2.3 … +1.5 | −5.9 … +7.1 | 9 / 10 | Kirin drops out (4th in `elite`) |
| V6 | V5 with Chain Lightning 22 | +0.5 / +6.0 | −2.2 … +1.5 | −5.7 … +6.0 | 9 / 10 | 5 seeds: Kirin still no top 3 (`elite` +1.9 vs Golem +3.1) |
| **V7** | V6 + Radiant Bolt 66 | +0.5 / +6.1 | −2.3 … +2.0 | −6.0 … +6.1 | 9 / 10 | **5 seeds: 10 / 10** (below); chosen |

Four three-seed rounds (two variants each in the first three) and the five-seed confirmation of V7.

### Results (mean of 5 seeds: 12345 / 777 / 4242 / 2024 / 99)

Before = "Thunderbird range vs move" C; after = V7. Overall is the marginal clear rate (levels and
shapes averaged) ± its standard deviation across the five seeds; (n) is the rank within the shape on
the mean, `=n` a tie. The last column counts the seeds in which the beast is top 3 in `solo` /
`elite` / `squad` / `horde`.

#### `elemental` (primary): overall and per shape, before → after (rank in shape)

| Beast | Overall before | Overall after | `solo` | `elite` | `squad` | `horde` | Top-3 seeds s/e/q/h, after |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| Kirin | +2.6 ± 1.2 | **+2.6** ± 1.2 | +0.4 (5) → +0.3 (5) | +2.9 (3) → +2.9 (=3) | +4.3 (4) → +4.7 (4) | +2.7 (4) → +2.4 (4) | 1/1/2/1 |
| Tarasque | +2.3 ± 3.3 | **+1.5** ± 3.1 | −0.3 (6) → −0.5 (7) | +4.3 (1) → +3.6 (2) | +10.2 (1) → +9.5 (1) | −4.8 (9) → −6.3 (10) | 1/3/4/0 |
| Thunderbird | −2.1 ± 2.7 | **+1.3** ± 2.9 | −3.7 (8) → −0.4 (6) | −5.1 (9) → −2.7 (=8) | +2.6 (5) → +7.6 (2) | −2.2 (7) → +0.8 (5) | 2/1/4/2 |
| Griffin | +2.3 ± 1.8 | **+0.8** ± 2.1 | +6.0 (1) → +3.8 (1) | +0.9 (6) → −1.4 (6) | +5.2 (3) → +4.4 (5) | −3.1 (8) → −3.8 (8) | 4/1/2/2 |
| Basilisk | +0.2 ± 1.3 | **−0.1** ± 1.1 | +3.5 (3) → +2.6 (3) | +1.4 (5) → +1.1 (5) | +2.4 (6) → +1.8 (6) | −6.4 (10) → −6.1 (9) | 1/2/0/0 |
| Golem | +0.5 ± 2.3 | **−0.3** ± 2.7 | −1.3 (7) → −1.2 (8) | +3.9 (2) → +2.9 (=3) | −5.0 (8) → −6.1 (8) | +4.3 (3) → +3.2 (3) | 1/4/0/2 |
| Phoenix | −1.3 ± 3.1 | **−0.7** ± 3.5 | −5.3 (10) → −4.7 (10) | −5.4 (10) → −5.2 (10) | +6.3 (2) → +6.8 (3) | −0.7 (5) → +0.3 (7) | 0/0/3/1 |
| Leviathan | −2.1 ± 2.0 | **−1.2** ± 2.4 | +0.8 (4) → +0.9 (4) | +2.1 (4) → +4.0 (1) | −10.2 (9) → −10.1 (9) | −1.1 (6) → +0.4 (6) | 2/3/0/1 |
| Frost Wyrm | −1.8 ± 1.6 | **−1.8** ± 1.3 | −4.6 (9) → −3.8 (9) | −3.3 (8) → −2.5 (7) | −4.5 (7) → −5.7 (7) | +5.4 (2) → +4.7 (1) | 0/0/0/4 |
| Treant | −0.6 ± 2.5 | **−2.1** ± 2.1 | +4.5 (2) → +3.0 (2) | −1.7 (7) → −2.7 (=8) | −11.2 (10) → −12.9 (10) | +5.9 (1) → +4.4 (2) | 3/0/0/2 |

Range: −2.1 … +2.6 → −2.1 … +2.6.

Niche map `elemental` (after): mean top 3 per shape; **bold** = top 3 in ≥ 4 of 5 seeds

| Shape | Top 3 (ties included) |
| --- | --- |
| `solo` | **Griffin** +3.8 (4/5), Treant +3.0 (3/5), Basilisk +2.6 (1/5) |
| `elite` | Leviathan +4.0 (3/5), Tarasque +3.6 (3/5), Kirin +2.9 (1/5), **Golem** +2.9 (4/5) |
| `squad` | **Tarasque** +9.5 (4/5), **Thunderbird** +7.6 (4/5), Phoenix +6.8 (3/5) |
| `horde` | **Frost Wyrm** +4.7 (4/5), Treant +4.4 (2/5), Golem +3.2 (2/5) |

Beasts with a robust niche, before: 4 (Griffin `solo`; Golem `elite` and `horde`; Tarasque `squad`; Treant `horde`); after: **5** (Griffin, Golem, Tarasque, Thunderbird, Frost Wyrm).

Survival / damage share `elemental` (mean of shapes), before → after

| Beast | Damage share | Survival |
| --- | ---: | ---: |
| Thunderbird | 27.6% → 32.6% | 23.6% → 30.9% |
| Phoenix | 32.5% → 32.8% | 43.4% → 43.8% |
| Frost Wyrm | 22.8% → 22.4% | 31.0% → 30.2% |
| Leviathan | 16.2% → 15.4% | 30.7% → 30.6% |
| Tarasque | 26.5% → 26.2% | 27.5% → 27.0% |
| Basilisk | 30.3% → 29.5% | 38.2% → 37.5% |
| Kirin | 32.5% → 32.4% | 46.0% → 45.6% |

#### `neutral` (secondary): overall and per shape, before → after (rank in shape)

| Beast | Overall before | Overall after | `solo` | `elite` | `squad` | `horde` | Top-3 seeds s/e/q/h, after |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| Thunderbird | −2.5 ± 1.4 | **+5.8** ± 1.5 | −11.0 (8) → +2.5 (6) | −6.2 (9) → +0.0 (6) | +10.0 (1) → +16.5 (1) | −2.6 (7) → +4.3 (2) | 0/0/5/3 |
| Kirin | +4.0 ± 0.9 | **+4.0** ± 0.7 | +4.4 (6) → +4.3 (5) | +6.1 (2) → +6.4 (2) | +3.0 (5) → +3.0 (=4) | +2.6 (3) → +2.0 (5) | 0/5/1/0 |
| Leviathan | +2.4 ± 0.6 | **+3.6** ± 0.7 | +13.9 (1) → +14.2 (1) | +7.0 (1) → +9.6 (1) | −12.3 (9) → −12.6 (9) | +1.2 (5) → +3.3 (3) | 5/5/0/2 |
| Golem | +3.4 ± 1.3 | **+2.2** ± 2.4 | +6.9 (5) → +6.8 (4) | +3.7 (4) → +2.2 (4) | −4.5 (8) → −6.3 (8) | +7.6 (1) → +6.1 (1) | 2/2/0/5 |
| Basilisk | +2.3 ± 0.3 | **+1.6** ± 0.3 | +8.9 (3) → +7.9 (2) | +5.0 (3) → +5.1 (3) | +0.7 (6) → +0.1 (6) | −5.3 (10) → −6.8 (9) | 4/3/0/0 |
| Treant | +0.6 ± 1.1 | **−0.9** ± 0.9 | +9.9 (2) → +7.6 (3) | +3.2 (5) → +1.8 (5) | −13.4 (10) → −14.6 (10) | +2.7 (2) → +1.5 (6) | 4/0/0/3 |
| Griffin | +1.8 ± 3.4 | **−2.0** ± 3.0 | +7.3 (4) → +0.1 (7) | +1.0 (6) → −3.5 (7) | +4.0 (3) → +3.0 (=4) | −5.1 (9) → −7.5 (10) | 0/0/1/1 |
| Frost Wyrm | −2.4 ± 1.0 | **−3.4** ± 0.7 | −6.0 (7) → −8.7 (8) | −4.5 (8) → −5.1 (9) | −0.0 (7) → −0.3 (7) | +1.1 (6) → +0.5 (7) | 0/0/0/0 |
| Phoenix | −6.0 ± 0.7 | **−4.7** ± 1.2 | −16.4 (9) → −13.7 (9) | −13.2 (10) → −11.6 (10) | +3.9 (4) → +4.2 (3) | +1.8 (4) → +2.2 (4) | 0/0/4/1 |
| Tarasque | −3.9 ± 0.9 | **−6.1** ± 0.8 | −17.9 (10) → −21.1 (10) | −2.1 (7) → −4.9 (8) | +8.7 (2) → +7.1 (2) | −4.2 (8) → −5.6 (8) | 0/0/4/0 |

Range: −6.0 … +4.0 → −6.1 … +5.8.

Niche map `neutral` (after): mean top 3 per shape; **bold** = top 3 in ≥ 4 of 5 seeds

| Shape | Top 3 (ties included) |
| --- | --- |
| `solo` | **Leviathan** +14.2 (5/5), **Basilisk** +7.9 (4/5), **Treant** +7.6 (4/5) |
| `elite` | **Leviathan** +9.6 (5/5), **Kirin** +6.4 (5/5), Basilisk +5.1 (3/5) |
| `squad` | **Thunderbird** +16.5 (5/5), **Tarasque** +7.1 (4/5), **Phoenix** +4.2 (4/5) |
| `horde` | **Golem** +6.1 (5/5), Thunderbird +4.3 (3/5), Leviathan +3.3 (2/5) |

Beasts with a robust niche, before: 7; after: 8 (Leviathan, Kirin, Basilisk, Treant, Thunderbird, Tarasque, Phoenix, Golem).

Survival / damage share `neutral` (mean of shapes), before → after

| Beast | Damage share | Survival |
| --- | ---: | ---: |
| Thunderbird | 29.6% → 36.2% | 22.0% → 33.0% |
| Phoenix | 32.3% → 32.5% | 41.1% → 41.7% |
| Frost Wyrm | 23.3% → 22.6% | 29.2% → 27.5% |
| Leviathan | 16.1% → 15.3% | 30.9% → 30.0% |
| Tarasque | 24.5% → 24.0% | 22.8% → 21.1% |
| Basilisk | 29.8% → 28.7% | 40.5% → 39.0% |
| Kirin | 31.0% → 30.9% | 47.2% → 45.9% |

### Targets: met and missed (5-seed means, final)

| Target | Before | After | |
| --- | --- | --- | --- |
| Every beast `elemental` overall within ±4 | −2.1 … +2.6 | −2.1 … +2.6 | met |
| Every beast `neutral` overall within ±7 | −6.0 … +4.0 | −6.1 … +5.8 | met (Tarasque −6.1 and Thunderbird +5.8 are the edges) |
| Every beast top 3 in ≥ 1 shape, `elemental` mean | 8 / 10 (not Leviathan, Thunderbird) | **10 / 10** | met, but Kirin only on a tie: `elite` +2.9 = Golem +2.9 |
| Robust niches (top 3 in ≥ 4 / 5 seeds), `elemental` | 4 beasts | **5 beasts** | Griffin `solo`, Golem `elite`, Tarasque and Thunderbird `squad`, Frost Wyrm `horde` |
| No beast top 3 in every shape | none | none | met (most: two shapes, Tarasque, Treant and Golem counting the tie) |
| Turn ratio 1.10–1.15, six-stat totals 570–630, Move 2–5, crit | unchanged | unchanged | met (no roster edits) |
| Budget rule (≤ 1.2× for unlimited damage skills) | met | met | highest now Ember Shot 1.16× |

`neutral` top-3 coverage is 8 / 10 (not Griffin, Frost Wyrm; before: 8 / 10, not Frost Wyrm,
Phoenix), with 8 robust niches (7 before).

### What moved, and what did not

- **Thunderbird**: the opener works. `elemental` −2.1 → +1.3, `neutral` −2.5 → +5.8, survival
  23.6% → 30.9% and damage share 27.6% → 32.6% (`elemental`). It is now robustly 2nd in `squad`
  (4 / 5 seeds) and best in `neutral` `squad` (5 / 5). Storm Dive at 120 on turn one removes a
  target or most of one before the Thunderbird is focused; with both multi-hit skills still at the
  1.2× ceiling (V1) it became the strongest `neutral` beast, hence Talons 26 and Chain Lightning 22.
  It is the most positive `neutral` beast still, and its `neutral` solo / elite are only 6th.
- **Leviathan**: `elemental` −2.1 → −1.2, `neutral` +2.4 → +3.6, now 1st on the mean in `elite`
  (3 / 5 seeds, not robust). The squad hole is **not** fixed (−10.2 → −10.1): the radius-3 taunt
  catches more of the squad but the Leviathan's own damage share (15%) is the lowest in the roster,
  so what it contributes against 4–6 spread enemies is the taunt window. Closing it would need a
  damage change (e.g. Tidal Wave in the defaults), which the signature tests rule out without
  dropping Deep Shell or Serpent Bite; not attempted.
- **Phoenix**: −1.3 → −0.7 `elemental`, −6.0 → −4.7 `neutral`; still robust only in `neutral`
  `squad`. **Frost Wyrm**: −1.8 → −1.8 `elemental` (the Rime Bolt lift absorbed the predicted
  drop when the Thunderbird improved) and now a robust `horde` niche (4 / 5, was 3 / 5); `neutral`
  −2.4 → −3.4 with no top-3 shape.
- **Basilisk**: overall unchanged (+0.2 → −0.1 `elemental`) and 3rd in `solo` on the mean, but
  top 3 there in only 1 of 5 seeds, so its `elemental` niche stays weak; in `neutral` it is robust
  in `solo` (4 / 5). The Coup de Grace lift roughly offset the Thunderbird's gain.
- **Tarasque** `neutral` −3.9 → −6.1 even with Iron Crush 160 (`neutral` `solo` −21.1, last): the
  slow melee brick loses most to the Thunderbird's stronger opener. Still inside ±7.
- **Griffin** and **Treant** lost ground to the Thunderbird (Griffin `neutral` +1.8 → −2.0,
  Treant `elemental` −0.6 → −2.1) but keep their niches (Griffin `solo` robust; Treant 2nd in
  `solo` and `horde` on the mean).

### Caveats

- Kirin's `elemental` top-3 is a tie with the Golem in `elite` (both +2.9 over five seeds, Kirin
  top 3 in 1 of 5 seeds). A sixth seed could drop it to 4th; the Kirin is otherwise the most
  consistent beast (overall +2.6 ± 1.2).
- Per-shape standard deviations across seeds run 1–11 points (Tarasque `elite` 10.6, Griffin
  `horde` 9.8); the overall means are within about ±1.5 of the truth.
- Everything is skill level 1 with default loadouts; Storm Dive's opener matters most at low skill
  levels, before the other slots grow.
- `tuned-report.md` is regenerated from the default run (seed 12345); its PvE marginals equal the
  V7 seed-12345 run.

## Tooling: a faster simulator and one-process multi-seed runs (no balance change)

No roster, skill library, fixture or rule changed, and `tuned-report.md` is byte-identical. The
default run went from 210 s to about 50 s on the 8-core tuning machine. Path finding had been 60%
of the CPU; the Runtime now does the same searches, with the same results, on flat arrays and a
heap, and the simulator uses Server GC. `Tooling/BalanceSim/README.md`, "Performance", has the
profile and the before/after table. Every pass above judged candidates on the mean of 3-5 seeds,
run as one process per seed and parsed back out of the reports. `--seeds` does that in one command:

```sh
dotnet run --project Tooling/BalanceSim -c Release -- --mode pve --seeds 12345,777,4242,2024,99 --out out/candidate.md
```

`out/candidate.md` is the aggregate: per kit mode and beast, the per-shape mean with its rank and
the number of seeds with the beast in the top 3 (the "robust niche" count used above), and the
overall mean, standard deviation, range and per-seed values. Each seed's full report is written
beside it as `out/candidate.seed<n>.md`, identical to a `--seed <n>` run. Five seeds take about
four minutes. For a first screen of many candidates, `--calibrate-sample 30` runs the difficulty
search on 30 of the 210 teams and is about 3.5x faster again. It moves the overall marginals by
at most 0.7 points, against about 2 points for a seed change. Confirm the final candidate without
it, since the committed report never uses it.

## Team composition analysis (no balance change)

Question: is there enough variation that the player's team composition matters, or does every
lineup win regardless? The simulator already fielded all 210 four-beast teams against every
composition; it now also reports whole teams (`Tooling/BalanceSim/README.md`, "Team composition";
the "PvE team composition" section of `tuned-report.md` and of the `--seeds` aggregate). Measured
on the current tuned data, default arguments, `--seeds 12345,777,4242`. Every number is at the
calibrated difficulty, where the average team clears 50%, so "every lineup wins" cannot happen by
construction; the question is how far apart the lineups are there.

**Noise first.** A team's clear rate in one shape is 24 battles per seed (8 compositions x 3
levels), so one team moves 12-14 points between seeds (damage rolls and each seed's composition
draw). One seed's best / worst lists are therefore mostly noise. The **persistent SD** below is the
teams' spread within a seed with that seed-to-seed noise removed: the part that is the lineup's own.

| Kit mode | Shape | Persistent team SD | Per-seed SD | Seed-to-seed SD | Seed means min … max | p10 … p90 |
| --- | --- | ---: | ---: | ---: | --- | --- |
| `elemental` | `solo` | 5.7 | 13.6 | 12.3 | 25.0% … 70.8% | 38.9% … 62.5% |
| `elemental` | `elite` | 4.4 | 12.6 | 11.8 | 25.0% … 72.2% | 38.9% … 59.7% |
| `elemental` | `squad` | 11.0 | 16.1 | 11.7 | 11.1% … 83.3% | 33.3% … 66.7% |
| `elemental` | `horde` | 7.5 | 15.8 | 14.0 | 29.2% … 86.1% | 37.4% … 65.3% |
| `elemental` | overall | 4.4 | 7.8 | 6.5 | 31.6% … 63.5% | 43.0% … 57.3% |
| `neutral` | `solo` | 18.7 | 24.7 | 16.2 | 1.4% … 94.4% | 22.2% … 80.6% |
| `neutral` | `elite` | 11.1 | 16.1 | 11.7 | 20.8% … 77.8% | 30.6% … 65.3% |
| `neutral` | `squad` | 14.5 | 17.4 | 9.6 | 8.3% … 87.5% | 30.4% … 70.8% |
| `neutral` | `horde` | 11.3 | 16.8 | 12.5 | 22.2% … 86.1% | 33.3% … 69.4% |
| `neutral` | overall | 8.2 | 10.9 | 7.2 | 28.5% … 71.9% | 37.5% … 61.2% |

(Min … max and p10 … p90 are of the 3-seed means, which still carry about seed-to-seed SD / sqrt(3)
= 7-8 points of noise per shape, so they overstate the true extremes; the persistent SD does not.)

- **Composition matters, per encounter more than overall.** With elements on, the lineup's own
  spread is an SD of 4-6 points against a solo giant or an elite, 7.5 against a horde and 11 against
  a squad: a true p10-to-p90 gap of roughly 11-28 points of clear rate at the same difficulty. Over
  every shape together it is only 4.4 (about 11 points p10-p90), because the lineups that are best
  in one shape are not best in another. Leviathan + Golem + Frost Wyrm + Treant is the best horde
  team (86.1%) and second-best elite team (72.2%) but the third-worst squad team (23.6%); the
  squad leaders are built around Griffin + Thunderbird (Griffin + Thunderbird + Tarasque + Kirin
  83.3%, + Frost Wyrm + Tarasque 81.9%). So picking the team for the encounter pays; one team for
  everything is roughly as good as another (seed means 31.6% … 63.5% overall, 89% of the teams
  between 40% and 60%).
- **The element chart narrows the gap between lineups.** Without elements (`neutral`) the
  persistent spread is 1.5-3x larger (solo 18.7: some teams almost never beat a solo giant, others
  almost always). With elements, a team's value swings with each composition's elements and
  averages out over the shape's mixed compositions, so the report cannot see it: the simulator
  fields a fixed team against every composition, whereas a player who sees the enemy's elements
  before choosing would get more out of the lineup than these numbers show.
- **Best and worst lineups (`elemental`, 3-seed means, SD over seeds in brackets).** Overall best:
  Phoenix + Griffin + Thunderbird + Kirin 63.5% (11.0), Leviathan + Golem + Treant + Kirin 63.5%
  (6.3), Phoenix + Golem + Treant + Kirin 63.5% (2.8), Griffin + Thunderbird + Tarasque + Kirin
  62.8% (4.2), Golem + Griffin + Thunderbird + Basilisk 61.8% (2.2). Overall worst: Thunderbird +
  Frost Wyrm + Treant + Kirin 31.6% (8.5), Phoenix + Thunderbird + Frost Wyrm + Treant 36.1% (8.9),
  Leviathan + Golem + Thunderbird + Treant 37.5% (7.5), Thunderbird + Frost Wyrm + Treant + Basilisk
  37.5% (4.5), Leviathan + Golem + Griffin + Treant 37.5% (4.5). Per shape: solo best Leviathan +
  Treant + Tarasque + Kirin 70.8%, worst Phoenix + Griffin + Kirin + Basilisk 25.0%; elite best
  Phoenix + Leviathan + Kirin + Basilisk 72.2%, worst Phoenix + Thunderbird + Frost Wyrm + Treant
  25.0%; squad best Griffin + Thunderbird + Tarasque + Kirin 83.3%, worst Leviathan + Golem +
  Thunderbird + Treant 11.1%; horde best Leviathan + Golem + Frost Wyrm + Treant 86.1%, worst
  Golem + Thunderbird + Treant + Tarasque 29.2%. Neighbouring ranks are within noise.
- **Pairs are close to additive, but the few real interactions are as big as any single beast.**
  Synergy = the clear rate of the 28 teams holding a pair minus what the two marginals predict
  (baseline + 0.675 x (mA + mB)). Over every shape (`elemental`, 3-seed mean, noise 0.5-1.0, every
  seed the same sign): Griffin + Thunderbird **+3.7** (+5.1 squad, +6.2 horde), Golem + Treant
  +2.2, Thunderbird + Basilisk +1.8, Griffin + Basilisk +1.7 (+5.3 horde), Golem + Frost Wyrm +1.6
  (+3.9 horde); anti-synergies Thunderbird + Treant **-3.5** (-4.4 solo, -4.0 horde), Leviathan +
  Griffin **-3.3** (-4.1 solo, -3.3 squad, -3.1 horde), Golem + Tarasque -1.9 (-4.6 solo),
  Thunderbird + Frost Wyrm -1.7, Leviathan + Golem -1.6. The beasts' own overall marginals span
  only -2.3 … +2.0 in `elemental`, so Griffin + Thunderbird and the two anti-synergies are the
  largest single composition effects in the roster. Every other pair is within about +/-1.5
  overall.
- **Reading for design.** Nobody wins regardless of lineup at a fair difficulty, and the choice is
  worth the most per encounter (squad and horde) and least against a lone giant or elite, where
  the four-beast spread is only 4-6 points. If the game should reward building a team more, the
  levers are the interactions above (pair skills or passives that deliberately create synergies)
  and encounters whose elements the player can see and counter before the fight.

Reproduce: `dotnet run --project Tooling/BalanceSim -c Release -- --seeds 12345,777,4242 --out out/teams.md`
(about 130 s); the default run's "PvE team composition" section shows seed 12345 alone.

## Team bonds

Follow-up to "Team composition analysis": lineups mattered per encounter but almost additively, so
the game now has **team bonds**, composition-triggered team effects applied at battle start to the
player's team (never enemies). Mechanism and data: `docs/design/battle-system.md`, "Team bonds";
content: the `TeamBonds` array of `skill-library.json` (three stance bonds, five element pairs
covering all ten elements, so every beast is in exactly two bonds). The simulator applies them by
default (`--bonds on|off`, library kit only) and reports them in "PvE team bonds". No beast stats
or skills changed; only bond magnitudes were tuned.

**Guard.** Bonds must widen the spread between lineups without breaking per-beast balance: every
beast's 3-seed mean overall marginal within +/-4 (`elemental`) and +/-7 (`neutral`), tuning bond
magnitudes, never beast stats. All numbers are `--seeds 12345,777,4242`, default arguments.
`--bonds off` reproduces the pre-bond aggregate byte for byte (the Runtime change is inert without
bonds).

**Iterations** (full 3-seed runs; the element-pair bonds are on 28 teams each, `shield_wall` on 155,
`crossfire` on 70, `pack_hunters` on 28):

| Run | Change | Out of band |
| --- | --- | --- |
| 1 | First draft: `shield_wall` 40/60% Defense shield, `crossfire` +6/+10%, `pack_hunters` +8/+12 crit, `wildfire` +8% Speed, `storm_front` +6% Atk/SpA, `bedrock` +10% Def/SpD, `winter_grove` 50% shield, `twilight` +5% Def/SpD (team) | Tarasque `elemental` +5.6, Golem `neutral` +7.6, Phoenix `neutral` -7.6 (the Vanguard shield and `bedrock` stacked on Golem / Tarasque; Fire + Air too weak) |
| 2 | `shield_wall` 30/45, `crossfire` 8/12, `bedrock` 5, `wildfire` +15% Speed, `winter_grove` 70 | none, but the overall spread did not move (`elemental` 4.4 -> 4.4) |
| 3 | Pair bonds up: `pack_hunters` 12/16 crit, `wildfire` +20% Speed, `storm_front` 10, `bedrock` 6, `winter_grove` 90 | none; spread 4.4 -> 4.7; `wildfire` still a loss (Speed alone buys little under sqrt speed) |
| 4 (final) | `wildfire` = +15% Speed, +8% Atk, +8% SpA | none |

**Per-beast marginals, final** (3-seed mean overall, bonds off -> on):

| Beast | `elemental` | `neutral` |
| --- | ---: | ---: |
| Kirin | +2.0 -> +2.8 | +4.1 -> +3.8 |
| Tarasque | +0.8 -> +1.4 | -6.0 -> -3.7 |
| Golem | -0.2 -> +0.5 | +2.5 -> +5.3 |
| Leviathan | -0.1 -> -0.2 | +3.9 -> +1.4 |
| Basilisk | 0.0 -> -0.2 | +1.7 -> -0.3 |
| Phoenix | -0.3 -> -0.5 | -4.8 -> -4.2 |
| Frost Wyrm | -2.3 -> -0.5 | -3.2 -> 0.0 |
| Griffin | +1.4 -> -0.7 | -3.4 -> -4.9 |
| Thunderbird | +0.5 -> -1.1 | +6.1 -> +3.3 |
| Treant | -1.9 -> -1.3 | -0.9 -> -0.7 |

Every beast is inside the guard (`elemental` -1.3 … +2.8, tighter than before bonds' -2.3 … +2.0
in range terms; `neutral` -4.9 … +5.3, was -6.0 … +6.1).

**Composition spread** (persistent team SD, the lineup's own spread with seed-to-seed noise removed;
bonds off -> on):

| Kit mode | `solo` | `elite` | `squad` | `horde` | overall |
| --- | ---: | ---: | ---: | ---: | ---: |
| `elemental` | 5.7 -> 5.4 | 4.4 -> 4.7 | 11.0 -> 10.7 | 7.5 -> 10.0 | 4.4 -> 4.6 |
| `neutral` | 18.7 -> 18.1 | 11.1 -> 11.9 | 14.5 -> 14.8 | 11.3 -> 14.0 | 8.2 -> 9.2 |

**Bond marginal** (`elemental`, 3-seed mean; Δ = teams with the bond minus without, excess = over
the additive prediction from the members' marginals, the part the lineup earns):

| Bond | Teams | `solo` | `elite` | `squad` | `horde` | Overall Δ (SD) | Overall excess |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `pack_hunters` | 28 | +1.1 / +0.8 | -0.2 / +2.6 | +11.6 / +5.8 | +1.4 / +7.8 | +3.5 (2.4) | +4.2 |
| `shield_wall` | 155 | -2.0 / -0.5 | -1.4 / -0.8 | -13.4 / -0.9 | +3.6 / -1.1 | -3.3 (0.1) | -0.8 |
| `crossfire` | 70 | 0.0 / +0.2 | +0.6 / +0.1 | +8.2 / +0.8 | -2.1 / +0.4 | +1.7 (1.5) | +0.4 |
| `wildfire` | 28 | -0.6 / +0.8 | -1.2 / +1.4 | +8.2 / +0.8 | -9.6 / -2.5 | -0.8 (1.0) | +0.1 |
| `storm_front` | 28 | -0.2 / +1.8 | +2.2 / +1.6 | -4.3 / +0.6 | +2.2 / -0.3 | 0.0 (2.7) | +0.9 |
| `bedrock` | 28 | +1.0 / -2.7 | +1.7 / +0.5 | +4.1 / +1.1 | 0.0 / +2.1 | +1.7 (4.5) | +0.3 |
| `winter_grove` | 28 | -4.6 / -1.2 | 0.0 / +2.0 | -4.2 / +4.4 | +13.6 / +3.9 | +1.2 (2.5) | +2.3 |
| `twilight` | 28 | +4.8 / +1.7 | +5.5 / +1.6 | +3.9 / -0.1 | -2.6 / -0.1 | +2.9 (2.9) | +0.8 |

In `neutral` the excesses are larger (`bedrock` +5.3, `pack_hunters` +4.3, `winter_grove` +4.2,
`storm_front` +1.9, `wildfire` +1.7).

**Pair synergy, overall (`elemental`, 3-seed mean).** Before: Griffin + Thunderbird +3.7, Golem +
Treant +2.2 … Thunderbird + Treant -3.5, Leviathan + Griffin -3.3. After: Griffin + Thunderbird
**+4.2** (`pack_hunters`), Frost Wyrm + Treant **+2.3** (`winter_grove`, new), Leviathan + Tarasque
+2.0, Golem + Frost Wyrm +1.8, Golem + Treant +1.7; anti-synergies Thunderbird + Treant -3.8,
Leviathan + Griffin -3.2, Thunderbird + Frost Wyrm -2.0, Frost Wyrm + Kirin -2.0, Tarasque +
Basilisk -1.7.

**Reading.**

- **Composition matters more where encounters are crowded, not overall.** The horde spread grows
  by a third (7.5 -> 10.0 `elemental`, 11.3 -> 14.0 `neutral`) and the elite a little; solo and
  squad do not move, and the overall spread only 4.4 -> 4.6 (`neutral` 8.2 -> 9.2). The bonds with
  real interaction are `pack_hunters` (excess +4.2 overall, +7.8 horde, +5.8 squad) and
  `winter_grove` (+2.3; +4.4 squad, +3.9 horde); they are what make "field these two together" a
  choice.
- **Why the overall spread barely moves.** A pair bond is active on 28 of 210 teams, so even a
  5-point bond adds only about 0.34 x 5 = 1.7 points of SD in quadrature, and the balance guard
  forbids the bigger magnitudes that would move it more: most of a pair bond's value lands on its
  two beasts' marginals (each gains about a third of it), which is exactly what the guard caps.
  The calibrated difficulty also absorbs any bond that nearly every team has: `shield_wall` (155
  teams) raises the multiplier for everyone and reads as -3.3 Δ, -0.8 excess.
- **Weak spots.** `wildfire` (Phoenix + Griffin) is still roughly neutral in `elemental` after two
  buffs (Δ -0.8, excess +0.1; the horde -9.6 is the two beasts' own horde weakness). `bedrock` and
  `twilight` are mostly additive (excess under +1 in `elemental`), i.e. buffs to their beasts
  rather than to the pairing. `pack_hunters`' second tier (3 Skirmishers) cannot occur with this
  roster.
- **If composition should matter more still,** the levers are bonds whose value depends on the
  lineup rather than on the beasts (effects that scale with the count, or target the non-members,
  e.g. a Vanguard bond that shields the Ranged beasts), fewer but stronger bonds on pairs that are
  currently anti-synergies (Leviathan + Griffin, Thunderbird + Treant), and letting the player see
  the encounter before choosing (the simulator fields a fixed lineup against every composition).

Reproduce: `dotnet run --project Tooling/BalanceSim -c Release -- --seeds 12345,777,4242 --out out/bonds.md`
and the same with `--bonds off` for the baseline (about 140 s each).

## Scouting and counter-picking (no balance change)

Follow-up to "Team composition analysis", which found that picking the team for the encounter
pays but the simulator only ever fielded a fixed lineup. The game now has an `EncounterPreview`
(`docs/design/battle-system.md`, "Encounter preview": enemy groups with element, stance and count,
`ScoutingDetail.Full` by default), and the simulator reports what seeing it is worth: "PvE scouted
picking" (`Tooling/BalanceSim/README.md`, "Scouted picking"), on by default. It re-reads the
battles already run (every team against every composition), so the report gains a section and no
battle, stat or calibration changes: the difficulty is still calibrated against the *average*
team, so the baseline is about 50% and every strategy's gain reads as uplift. `--scouted none`
reproduces the previous report byte for byte.

Strategies: **random** (a seeded random team per composition; the noise control), **heuristic**
(each beast scores 1 x its chart multiplier into the enemies - 0.5 x theirs into it, per enemy; best
four, at least one Vanguard), **heuristic + bonds** (the team maximising the members' scores plus
0.5 per active bond tier), **best team** (the one lineup that did best in the shape at the other
levels, scored at this level: strong-team knowledge without the encounter, held out so not
luck-inflated) and **oracle** (per composition, the team that did best against it: an upper bound,
about 100% by construction with 210 teams and one battle each).

**Results**, `--mode pve --seeds 12345,777,4242`, default arguments (8 compositions per shape;
clear rate, uplift over the baseline in brackets; SD = the heuristic uplift's SD over seeds):

| Kit mode | Shape | Baseline | Random | Heuristic | SD | Heuristic + bonds | Best team | Oracle |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `elemental` | `solo` | 49.8% | 48.6% (-1.2) | 79.2% (+29.3) | 3.9 | 83.3% (+33.5) | 70.8% (+21.0) | 100.0% (+50.2) |
| `elemental` | `elite` | 51.2% | 56.9% (+5.8) | 70.8% (+19.7) | 3.8 | 76.4% (+25.2) | 70.8% (+19.7) | 100.0% (+48.8) |
| `elemental` | `squad` | 49.9% | 47.2% (-2.7) | 76.4% (+26.5) | 4.9 | 73.6% (+23.7) | 76.4% (+26.5) | 100.0% (+50.1) |
| `elemental` | `horde` | 49.6% | 38.9% (-10.7) | 54.2% (+4.6) | 20.8 | 54.2% (+4.6) | 83.3% (+33.7) | 100.0% (+50.4) |
| `elemental` | overall | 50.1% | 47.9% (-2.2) | 70.1% (+20.0) | 3.8 | 71.9% (+21.7) | 75.3% (+25.2) | 100.0% (+49.9) |
| `neutral` | overall | 50.3% | 50.0% (-0.3) | 50.3% (0.0) | 7.4 | 56.6% (+6.3) | 83.3% (+33.0) | 100.0% (+49.7) |

A shape's figure per seed is 24 battles (binomial SE about 10 points), so the same three seeds were
also run with `--compositions 32` (96 battles per shape per seed; SE about 3 points on the 3-seed
mean) as the tighter check:

| Kit mode | Shape | Baseline | Random | Heuristic | SD | Heuristic + bonds | Best team |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `elemental` | `solo` | 50.1% | +1.6 | 79.5% (+29.4) | 4.5 | 87.5% (+37.4) | 68.1% (+17.9) |
| `elemental` | `elite` | 49.5% | -2.2 | 74.3% (+24.8) | 8.6 | 78.5% (+29.0) | 62.5% (+13.0) |
| `elemental` | `squad` | 50.2% | +1.6 | 68.8% (+18.6) | 7.0 | 67.7% (+17.5) | 80.2% (+30.0) |
| `elemental` | `horde` | 49.6% | +0.4 | 65.6% (+16.0) | 0.3 | 66.7% (+17.1) | 79.2% (+29.6) |
| `elemental` | overall | 49.8% | +0.3 | 72.0% (+22.2) | 0.9 | 75.1% (+25.2) | 72.5% (+22.6) |
| `neutral` | overall | 50.1% | -1.1 | 49.3% (-0.8) | 2.4 | 57.0% (+6.9) | 82.3% (+32.2) |

- **Scouting plus a plain element counter-pick is worth about +20 points** of clear rate at the
  calibrated difficulty (+20.0 at 8 compositions, +22.2 at 32; about 50% -> 70%). The `neutral`
  control, where the chart does nothing, gives the same picks 0.0 / -0.8: the whole gain is the
  element chart, not the picks happening to be strong lineups.
- **By shape**, the counter-pick is strongest against a lone giant (+29) and an elite group
  (+20 to +25), where one or two elements dominate. Against squads and hordes, whose mixed elements
  dilute any counter, it is +16 to +19 on the tighter run (the default run's +26.5 squad / +4.6 horde
  are within its noise; horde's seed SD is 20.8). There the held-out **best team** (+30) beats it:
  bringing a strong lineup matters more than countering the elements, while against solo and elite
  encounters countering beats the strong lineup (+29 vs +18, +25 vs +13).
- **Bonds help the counter-pick a little**: +1.7 (8 compositions) / +3.0 (32) overall in
  `elemental`, mostly in solo and elite; in `neutral` they are the only thing the bond-aware pick
  knows, and it gains +6 to +7 there, consistent with the bond marginals.
- **Oracle** is 100% in every shape: with 210 teams and one battle per team and composition,
  some team always wins, so it is a luck bound rather than a skill ceiling. The honest ceiling for
  "knowing what works" is between the best-team and oracle columns.
- **Pick rates: no beast is always or never picked by the counter-pick.** In every shape each beast
  is fielded in between 13% (Frost Wyrm, solo) and 63% (Leviathan solo, Phoenix elite / squad) of
  heuristic picks (8 compositions; 18-59% at 32), and the bond-aware pick likewise spans 4-71% with
  no 0 or 100. Phoenix drops to 4-21% under the bond-aware pick (its `wildfire` partner Griffin is
  rarely the counter) and Tarasque / Golem rise (they reach `bedrock` and `shield_wall` together).
  The **oracle** does have extremes in single shapes, but they are about the lineups that happened to
  win, not the counter-pick: `horde` Leviathan always and Griffin never (8 compositions), `squad`
  Thunderbird always and `horde` Basilisk never (32).

**Implications (not acted on).** Visible elements make the chart a real decision: the same beasts
clear about 20 points more often when picked for the encounter, with no must-pick beast. If the game
expects players to scout, difficulty tuned to 50% for the average team is about 70% for a scouting
player; whether authored encounters should be calibrated against the average or the counter-picked
team, and whether the partial `ScoutingDetail` levels should gate some encounters, are open design
questions. Horde and squad encounters reward lineup strength more than countering, which is where
bonds and stances carry the choice.

Reproduce: `dotnet run --project Tooling/BalanceSim -c Release -- --mode pve --seeds 12345,777,4242 --out out/scout.md`
(about 150 s) and the same with `--compositions 32` (about 10 min).

## Material economy (stage 5): drop tables tuned for skill pacing

First pass of `content/data/Skills/drop-tables.json` against the new pacing model
(`--mode pacing`; report [`pacing-report.md`](pacing-report.md)). The XP constants (100 x level^1.5,
10 XP a use, 20 uses a battle cap, materials 250 / 1,000 / 4,000) are unchanged; only drop chances,
quantities, bands and first-clear bonuses moved. The default PvE / PvP report is unaffected.

- **Targets** (median battles for one focused skill): L5 15-20, L10 ~80, L15 ~180, L20 ~300-320.
- **The design's starting table overshot every gate** (L5 in 6 battles, L10 in 46, L15 in 126 with
  4-10 uses a battle): with every drop fed to one skill, 90% shard drops from level 1 are ~140 XP a
  battle on their own.
- **The targets are inconsistent with a 20-uses-a-battle practice rate.** At the per-battle cap
  practice alone reaches L5 in 9 battles, below the 15-20 target, so practice must be about 5-10 uses
  a battle; the model uses 3-9 (the PvE report's 3-10 beast turns per cleared battle). At that rate
  practice alone takes ~1,100 battles to L20, so materials must supply ~3/4 of the XP to hit ~300,
  not the 10-15% the design sketch assumed. Kept the XP constants; flagged for design.
- **Structure**: a tutorial band 1-3 with no regular drops (its four first-clear shards pay for L5
  and the gate), shards 4-20, crystals from band 21 (first clears open the L10 gate at ~101), cores
  from band 41 (first clears open the L15 gate at ~201); regular drops scaled so the levels between
  gates land on target; the rich late bands (61+) feed the rest of the team.

| Level | Target | p10 / p50 / p90 (1000 campaigns, seed 12345) |
| ---: | --- | --- |
| 5 | 15-20 | 15 / 16 / 17 |
| 10 | ~80 | 69 / 77 / 85 |
| 15 | ~180 | 157 / 171 / 184 |
| 20 | ~300-320 | 283 / 301 / 302 |

Seeds 1, 2, 3 (300 campaigns each) agree within a battle. Material income per 500-battle campaign:
~106 shards, ~120 crystals, ~27 cores; 74% of the focus skill's XP to L20 is material XP. The L20
median is anchored at ~301 by the first band-61 core, so it is robust to small changes in the
regular drops but moves with the band boundaries and the level ramp.

Reproduce: `dotnet run --project Tooling/BalanceSim -c Release -- --mode pacing --self-check --out docs/balance/pacing-report.md`
(about 1.5 s).

## Avatar level (stage 3a): pacing against the encounter level

`AvatarProgression` (new): a level costs `200 + 16 × level`; a battle pays 8 XP, plus `40 + 4 ×
enemy level` on a clear. Derived, not searched: at encounter level L and an 80% clear rate a battle
pays `8 + 0.8 × (40 + 4L) = 40 + 3.2L`, and the pacing campaign spends 5 battles per encounter level,
so a level should cost `5 × (40 + 3.2L) = 200 + 16L`. Target: median avatar level within 3 of the
encounter level at every 50-battle checkpoint. Measured (`--mode pacing`, 1000 campaigns): median
within 0-1 everywhere (battle 100: 21 vs 20; 250: 50 vs 50; 400: 81 vs 80; 500: 100), p10-p90 at
most 3 levels wide. A player who clears less than 80% falls behind the content (at a 50% clear
rate a battle pays about two thirds as much XP), which is the intended pressure; the curve's two
constants move the whole track.

## Avatar gauge (stage 3b): the avatar on its own ATB gauge

The avatar used to tick once per player-beast turn, so a four-beast team cycled it about four times
as often as one beast acts. It now fills its own ATB gauge from its own Speed
(`BattleTurnExecutor.ExecuteAvatarTurn`; design doc, decision 6, timing amendment): its actives and
its passives' internal cooldowns run on its own turns, `AllyTurnStart` passives stay per beast turn,
and a beast's turn no longer ticks it. The simulator's library avatar gets Speed 100 at max level on
the medium curve (`AvatarStatsSO.GetStatsAtLevel`, 15 at level 1, the same scale as its other stats),
at the encounter level (`--avatar-level`, default). No beast data, skills, bonds or avatar numbers
changed.

**Cadence** (default run, seed 12345, 40,320 battles at the calibrated difficulty):

| | Before (per player-beast turn) | After (own gauge) |
| --- | ---: | ---: |
| Avatar turns (ticks) per battle | 17.26 | 5.79 (5.66-5.79 over the 3 seeds) |
| ... per unit of normalized time | 1.65 | 0.56 |
| Avatar active casts per battle | 13.29 | 3.74 (3.62-3.74) |
| `last_stand` firings per battle (cooldown 2) | 2.98 (2.97-3.00) | 2.24 (2.20-2.24) |
| `keen_eye` (aura) / `opening_ward` (battle start) | 1.00 / 1.00 | 1.00 / 1.00 |

So the avatar acts about a third as often and casts about 72% less; `last_stand`, gated by its
cooldown in avatar turns, fires a quarter less.

**Guard** (as for team bonds: every beast's 3-seed mean overall marginal within +/-4 `elemental`
and +/-7 `neutral`; `--seeds 12345,777,4242`, default arguments), before -> after:

| Beast | `elemental` | `neutral` |
| --- | ---: | ---: |
| Golem | +0.5 -> +1.8 | +5.3 -> +4.7 |
| Tarasque | +1.4 -> +1.5 | -3.7 -> -2.6 |
| Basilisk | -0.2 -> +1.4 | -0.3 -> +3.8 |
| Frost Wyrm | -0.5 -> +0.2 | 0.0 -> -1.5 |
| Kirin | +2.8 -> +0.1 | +3.8 -> -0.3 |
| Leviathan | -0.2 -> 0.0 | +1.4 -> +1.7 |
| Phoenix | -0.5 -> -0.2 | -4.2 -> -4.7 |
| Treant | -1.3 -> -0.7 | -0.7 -> -0.4 |
| Thunderbird | -1.1 -> -1.1 | +3.3 -> +6.0 |
| Griffin | -0.7 -> -2.9 | -4.9 -> -6.7 |

Every beast is inside the guard (`elemental` -2.9 ... +1.8, `neutral` -6.7 ... +6.0), so **no tuning
iteration was made**. Griffin `neutral` (-6.7) and Thunderbird `neutral` (+6.0) sit closest to the
edge; Kirin (Light, the team healer) lost the most in `elemental` (-2.7), as the avatar's Mending
Light and Aegis now cover less of what it covered.

**Composition matters more.** The avatar's frequent team-wide casts had been flattening lineups: the
persistent team SD (lineup spread with seed-to-seed noise removed) rose `elemental` overall 4.6 ->
6.1 (squad 10.7 -> 12.0, horde 10.0 -> 13.5) and `neutral` overall 9.2 -> 11.3.

**Open (design, not acted on).** The avatar's actives (Rallying Cry, Mending Light, Aegis) and
passive cooldowns were authored for the old cadence; at roughly 0.56 turns per unit of time they are
a much smaller share of a battle. Retuning them (shorter cooldowns, larger magnitudes, or a faster
authored avatar Speed) is a design choice about how big the avatar's role should be, not a balance
repair: per-beast balance holds without it.

Reproduce: `dotnet run --project Tooling/BalanceSim -c Release -- --seeds 12345,777,4242 --out out/gauge.md`
(about 140 s).

## Large enemies (footprints)

The bosses now cover several hexes (design doc, "Unit footprints"): the giant and the fixed-set
colossus are `Hex7` (seven tiles), the champion `Triangle` (three). Ranges to and from them are
measured between nearest tiles, an area hits them once, a large caster's area grows from all of its
tiles, a giant cannot be knocked back and a champion moves at most one tile. Beasts, skills, bonds and
the avatar are unchanged. **Parity retune of the fixtures** so the giant's reach does not silently
grow by the footprint's radius: giant and colossus gaze range 3 -> 2, quake and roar area radius
2 -> 1 (a radius-1 burst from a `Hex7` is exactly the old radius-2 disc around its centre); the
champion's shockwave stays at 2. No other enemy numbers changed.

**One-tile battles are byte-identical.** Before the fixtures were given footprints, the new code
reproduced the committed default report byte for byte (the full run, every shape). With the
footprints, every `squad` and `horde` result is unchanged on all three seeds (they have no large
units), as are the fixed set's `swarm` and `pack`; only the enemy-type descriptions and the scouting
lines that pool shapes (the held-out best team's tie-break, the oracle summary) move.

**Calibration.** The bosses are easier to reach (twelve tiles around a giant), so the calibrated
difficulty rose: `solo` x0.918-0.936 -> x0.953-0.961 `elemental` and x0.863-0.883 -> x0.898-0.904
`neutral`; `elite` x0.826-0.840 -> x0.863-0.875 and x0.805-0.813 -> x0.826-0.836 (seed 12345;
average times unchanged within 0.2). The fixed `boss` (colossus) moved from x0.93-0.95 to x0.96-0.97.

**Guard** (every beast's 3-seed mean overall marginal within +/-4 `elemental` and +/-7 `neutral`;
`--seeds 12345,777,4242`, default arguments), before -> after, with the two boss shapes (the other
two are unchanged):

| Beast | Stance | `elemental` `solo` | `elemental` `elite` | `elemental` overall | `neutral` `solo` | `neutral` `elite` | `neutral` overall |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Golem | Vanguard | +2.4 -> +0.5 | +4.1 -> +5.2 | +1.8 -> +1.6 | +13.4 -> +13.8 | +1.8 -> +5.8 | +4.7 -> +5.8 |
| Phoenix | Ranged | -4.7 -> -3.3 | -2.5 -> +1.2 | -0.2 -> +1.1 | -15.7 -> -5.6 | -10.3 -> -3.4 | -4.7 -> -0.4 |
| Tarasque | Vanguard | +2.8 -> +4.0 | +0.8 -> -2.2 | +1.5 -> +1.0 | -11.5 -> -12.4 | -2.2 -> -5.0 | -2.6 -> -3.6 |
| Frost Wyrm | Vanguard | -7.3 -> -5.9 | -0.7 -> -0.2 | +0.2 -> +0.7 | -12.8 -> -11.9 | -5.0 -> -6.0 | -1.5 -> -1.5 |
| Treant | Vanguard | +4.3 -> +5.5 | -5.2 -> -0.8 | -0.7 -> +0.7 | +5.2 -> +6.2 | -0.8 -> +5.2 | -0.4 -> +1.3 |
| Kirin | Ranged | -2.6 -> -2.8 | +0.9 -> +2.6 | +0.1 -> +0.5 | -3.7 -> -7.6 | +2.1 -> +5.0 | -0.3 -> -0.5 |
| Basilisk | Ranged | +5.6 -> +2.5 | +4.5 -> +3.9 | +1.4 -> +0.4 | +15.8 -> +8.8 | +9.0 -> +8.3 | +3.8 -> +1.9 |
| Leviathan | Vanguard | +2.5 -> -0.3 | +2.9 -> +1.8 | 0.0 -> -0.9 | +14.3 -> +8.6 | +2.8 -> +3.9 | +1.7 -> +0.6 |
| Thunderbird | Skirmisher | -1.0 -> -2.7 | -3.0 -> -6.7 | -1.1 -> -2.4 | +0.2 -> -4.5 | +5.0 -> -6.3 | +6.0 -> +2.0 |
| Griffin | Skirmisher | -1.9 -> +2.5 | -1.8 -> -4.8 | -2.9 -> -2.6 | -5.3 -> +4.5 | -2.4 -> -7.6 | -6.7 -> -5.6 |

Every beast is inside the guard (`elemental` -2.6 ... +1.6, `neutral` -5.6 ... +5.8), and the spread
narrowed (`neutral` -6.7 ... +6.0 before), so **no enemy tuning iteration was made**.

**What moved, per shape.**

- *Melee Vanguards against the bosses:* a giant now has twelve tiles around it rather than six, so
  more of a team reaches it at once. Treant gains most (`elite` +4.4 / +6.0), Golem gains in `elite`
  (+1.1 / +4.0) and holds its `solo` lead in `neutral`; Leviathan loses some of its `solo` edge
  (-2.8 / -5.7) now that it is no longer one of the few beasts in contact.
- *Knockback against the bosses:* a giant cannot be pushed and a champion moves one tile at most.
  At skill level 1 the only default-loadout knockback is Griffin's Gust (area, 2 hexes); Griffin
  loses in `elite` (-3.0 / -5.2), where the champions it used to shove are now pinned, but gains in
  `solo` (+4.4 / +9.8), plausibly because the old one-tile giant was pushed out of its teammates' reach and the
  seven-tile one stays in it. Thunderbird (no knockback) loses most in `elite` (-3.7 / -11.3; its
  `neutral` overall +6.0 -> +2.0); the cause is not isolated (its kit is short-range single-target
  plus a Skirmisher's crowd preference, both of which the footprint changes).
- *Ranged beasts:* the parity retune keeps the giant's reach from its centre, but a standoff unit's
  range now counts to the giant's ring, so Ranged beasts stand one tile further from its centre and
  its area slams: Phoenix recovers (`neutral` `solo` -15.7 -> -5.6, overall -4.7 -> -0.4), Basilisk
  loses its `solo` lead (+15.8 -> +8.8 `neutral`).

**Open.** Knockback is weaker against bosses by design; whether Griffin (and Thunderbird, whose
`elite` drop is unexplained) should get boss value back is a beast retune (a later deliverable), not
an enemy one. Enemy multi-hex
units other than the three bosses, and footprints in hand-authored encounters, are not designed.

Reproduce: `dotnet run --project Tooling/BalanceSim -c Release -- --seeds 12345,777,4242 --out out/footprints.md`
(about 150 s).

## Scouting-based calibration

Design decision (milestone 2): encounter difficulty assumes the player **scouts and counter-picks**.
The simulator used to calibrate each (shape, level, kit mode) so the *average* of the 210 teams
cleared 50%; "Scouting and counter-picking" showed a scouting player then clears about 70%. It now
calibrates so the team the **bond-aware scouted picker** (heuristic + bonds) fields against each
composition clears 50% (`--calibrate-on bonds`, the default; README "Difficulty calibration"), and
reports the average team's rate beside it as the **no-scouting** rate. No beast, skill, bond, avatar
or enemy data changed.

How: the picks depend on the preview alone, so they are worked out once per shape
(`ScoutedPicker.PicksFor`). Each search step runs only the picked team per composition,
`--calibrate-samples` (16) times: 8 x 16 = 128 battles a step (SE about 4.4 points) instead of 1680.
The picked battles are seeded like the every-team ones (sample 0 is exactly the picked team's
every-team battle; the self-check verifies it). Same bracket and 8 bisections. At the chosen
multiplier every team runs once, and every metric, the composition and bond sections, scouting and
the no-scouting rate come from that run. `--calibrate-on heuristic` aims the plain counter-pick;
`--calibrate-on mean` is the old calibration and **reproduces the committed report byte for byte**
(`cmp` against `git show HEAD:docs/balance/tuned-report.md`: identical, SHA-256 `d14cda26...`).

**Normalized marginals.** At a cell clear rate p a beast's marginal scales with p (1 - p), so the
lower average-team rate shrinks raw marginals (most in the boss shapes, p about 10-20%). The report
and the aggregate add **normalized** = marginal x 0.25 / (p (1 - p)) per cell (p = the no-scouting
rate), averaged like the raw one; the flags and the balance guard (3-seed mean within +/-4
`elemental`, +/-7 `neutral`) now read it. It amplifies noise by the same factor (about 2x at 13%),
so the normalized SDs over seeds run 1.3-4.8 in `elemental`, against 0.8-4.3 raw before.

**Calibration** (`--seeds 12345,777,4242`, default arguments; levels averaged, then seeds):

| Kit mode | Shape | Multiplier before (mean) | Multiplier now | Scouted | No scouting | Gap |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `elemental` | `solo` | x0.941 | x1.153 | 49.6% | 10.3% | +39.3 |
| `elemental` | `elite` | x0.864 | x0.982 | 50.1% | 21.9% | +28.2 |
| `elemental` | `squad` | x1.224 | x1.340 | 50.3% | 26.0% | +24.3 |
| `elemental` | `horde` | x1.138 | x1.176 | 48.7% | 42.7% | +6.0 |
| `elemental` | overall | x1.042 | x1.163 | 49.7% | 25.2% | +24.4 |
| `neutral` | `solo` | x0.902 | x0.930 | 48.4% | 28.6% | +19.8 |
| `neutral` | `elite` | x0.843 | x0.841 | 50.9% | 50.1% | +0.7 |
| `neutral` | `squad` | x1.215 | x1.233 | 50.3% | 43.6% | +6.8 |
| `neutral` | `horde` | x1.165 | x1.173 | 49.7% | 46.9% | +2.8 |
| `neutral` | overall | x1.031 | x1.044 | 49.8% | 42.3% | +7.5 |

- **Not scouting costs about 25 points** in `elemental`: the average team clears about a quarter
  of encounters calibrated for the scouting player, a lone giant about one in ten. The gap is the
  counter-pick's value, as before (+20 to +25 on the old calibration), now read from the other side
  (and somewhat larger). Hordes, whose mixed elements dilute any counter, barely move (+6).
- **`neutral` is the control**: with the chart off the bond-aware pick knows only bonds, and the
  gap is +0.7 (elite) to +20 (solo); +7.5 overall.
- **Misses are level-1 steps.** Four of 72 cells over the three seeds end more than 10 points off
  (seed 12345 `neutral` `solo` L1 35.9%; 777 `neutral` `elite` L1 66.4%; 4242 `elemental` `horde`
  L1 39.1% and `neutral` `solo` L1 63.3%): in each the picked teams' rate jumps across 50% between
  two multipliers less than 1% apart (a single stat rounding at level 1, which the few picked
  teams react to alike, where 210 teams averaged it out). They are flagged in the report and pass
  the self-check, which fails only a miss that is not such a step.
- In the scouting section the **Heuristic + bonds** column now sits at the target (48.3%
  `elemental` overall over seeds, on one battle per composition and level against the
  calibration's 16), and the plain heuristic reaches 51.4%: the two pickers are worth about the
  same, as before.

**Guard** (3-seed mean overall marginal; before = `--calibrate-on mean`, the continuity run, which
reproduces the "Large enemies (footprints)" after-column exactly; after = the new default, raw and
normalized; the guard reads the normalized figure):

| Beast | Stance | `elemental` before | raw | normalized | `neutral` before | raw | normalized |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Golem | Vanguard | +1.6 | +1.6 | +3.4 | +5.8 | +4.1 | +4.8 |
| Phoenix | Ranged | +1.1 | +1.7 | +2.1 | -0.4 | -0.8 | -1.3 |
| Tarasque | Vanguard | +1.0 | +0.3 | +2.3 | -3.6 | -2.9 | -3.4 |
| Frost Wyrm | Vanguard | +0.7 | +1.2 | -0.7 | -1.5 | +0.2 | 0.0 |
| Treant | Vanguard | +0.7 | +1.3 | +2.7 | +1.3 | +1.7 | +2.2 |
| Kirin | Ranged | +0.5 | +0.5 | +0.8 | -0.5 | -0.6 | -0.9 |
| Basilisk | Ranged | +0.4 | +0.2 | +1.5 | +1.9 | +1.6 | +2.0 |
| Leviathan | Vanguard | -0.9 | -0.9 | -2.3 | +0.6 | +0.1 | +0.4 |
| Thunderbird | Skirmisher | -2.4 | -2.9 | **-5.0** | +2.0 | +1.3 | +1.0 |
| Griffin | Skirmisher | -2.6 | -3.1 | **-4.8** | -5.6 | -4.8 | -4.9 |

`neutral` is inside the guard (-4.9 ... +4.8). **`elemental` is not: Thunderbird -5.0 and Griffin
-4.8 are outside +/-4** (the raw means, -2.9 and -3.1, would pass). Both Skirmishers already sat at
the bottom on the old calibration (-2.4 / -2.6); the normalization weights the boss shapes up (x2.7
`solo`, x1.5 `elite` at their mean p), and both are weakest there (`elite` -4.1 / -5.6 raw, Griffin `horde` -8.9).
**Not tuned here**: the retune under the new calibration is stage D (Griffin / Thunderbird are on
its list already).

**Runtime.** The default run fell from about 52 s to 11 s (PvE about 10 s), `--self-check` to 22 s,
three seeds from about 150 s to 33 s; `--calibrate-on mean` costs what the default did (3 seeds 166 s).

Reproduce: `dotnet run --project Tooling/BalanceSim -c Release -- --seeds 12345,777,4242 --out out/scoutcal.md`
(about 35 s) and the continuity run with `--calibrate-on mean` added (about 170 s).

## Scaling bonds

Design decision (milestone 2): add **lineup-scaling** team bonds beside the pair and tiered stance
bonds, so every team's stance mix counts. A scaling bond (`PerCount`, skill library schema 2) has
one tier whose effects are per stack: once its count reaches the tier's `MinCount` it applies
stacks = min(count, `MaxCount`), every magnitude x stacks as one application. A new scope,
`Others`, gives the effect to the teammates that are *not* members. The validator caps the worst
case (magnitude x `MaxCount`: 20% of a stat, 15 flat crit, a 60% shield, 1 move); carriers are
cached per tier and stack count with cloned effects. The bond-aware scouted picker weighs a scaling
bond at 0.125 per stack (`ScoutedPicker.ScalingBondWeight`; nothing for an `Others` bond no
teammate receives). See battle-system.md, "Team bonds".

**Bonds** (the design's starting magnitudes, kept; roster 5 Vanguard, 3 Ranged, 2 Skirmisher):

| Bond | Condition | Scope | Per stack | Max | Teams at x1 / x2 / x3 (of 210) |
| --- | --- | --- | --- | ---: | --- |
| `bulwark` Bulwark | Vanguard beasts | Others | +4% Defense, +4% SpecialDefense | 3 | 50 / 100 / 55 |
| `overwatch` Overwatch | Ranged beasts | Team | +3 CritChance | 3 | 105 / 63 / 7 |
| `flanking` Flanking | Skirmisher beasts | Team | +4% Speed | 2 | 112 / 28 / - |

Every one of the 210 lineups **resolves** a scaling bond (a test pins it for the roster), but the
five all-Vanguard teams resolve only `bulwark`, which has no non-Vanguard to land on: 205 of 210
teams **apply** one. (Any stance-count bond on a Vanguard-only team would need Members or Team scope.)

**Normalized marginals** (3-seed mean, `--seeds 12345,777,4242`; before = the "Scouting-based
calibration" default, after = this change; the guard reads normalized):

| Beast | Stance | `elemental` before | after | Δ | `neutral` before | after | Δ |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Phoenix | Ranged | +2.1 | +3.3 | +1.2 | -1.3 | -1.4 | -0.1 |
| Treant | Vanguard | +2.7 | +2.4 | -0.3 | +2.2 | +2.6 | +0.4 |
| Kirin | Ranged | +0.8 | +1.7 | +0.9 | -0.9 | -0.9 | 0.0 |
| Tarasque | Vanguard | +2.3 | +1.5 | -0.8 | -3.4 | -3.6 | -0.2 |
| Golem | Vanguard | +3.4 | +1.5 | -1.9 | +4.8 | +3.3 | -1.5 |
| Basilisk | Ranged | +1.5 | -0.2 | -1.7 | +2.0 | -0.7 | **-2.7** |
| Frost Wyrm | Vanguard | -0.7 | -1.0 | -0.3 | 0.0 | 0.0 | 0.0 |
| Leviathan | Vanguard | -2.3 | -2.0 | +0.3 | +0.4 | +1.7 | +1.3 |
| Thunderbird | Skirmisher | **-5.0** | -3.6 | +1.4 | +1.0 | +1.6 | +0.6 |
| Griffin | Skirmisher | **-4.8** | -3.6 | +1.2 | -4.9 | -2.7 | **+2.2** |

- **The guard now holds in both modes** (`elemental` -3.6 ... +3.3 within +/-4, `neutral` -3.6 ...
  +3.3 within +/-7): `flanking` lifts the two Skirmishers, which were outside it, by about 1.3 each.
  Top 3 in some shape: 9 of 10 in each mode (not Thunderbird `elemental`, Kirin `neutral`).
- **Stance means moved at most 1.4 points** (`elemental` Skirmisher +1.3, Ranged +0.1, Vanguard
  -0.6; `neutral` Skirmisher +1.4, Ranged -0.9, Vanguard 0.0), so no bond was rescaled. Two single
  beasts moved more than 2 (Basilisk `neutral` -2.7, Griffin `neutral` +2.2) while their stance
  partners did not (Kirin 0.0, Phoenix -0.1; Thunderbird +0.6), so a bond magnitude, which moves a
  stance as a whole, is not the lever. A second seed set (`--seeds 1,2,3`) repeats the pattern
  (Basilisk -2.0 / -2.1, Griffin +2.2 / +1.8, Golem -1.5 / -2.2, stance means within 1.4): real but
  beast-specific, for the stage D retune.
- **Calibration** barely moves: the overall multiplier rises x1.163 -> x1.177 `elemental`, x1.044 ->
  x1.059 `neutral` (the picked team gets the bonds too); no-scouting rate 25.2% -> 23.8% and 42.3%
  -> 40.3%.

**Scaling bonds by stacks** (3-seed means, levels pooled, overall; **excess** over the additive
prediction from the members' marginals; **slope** = least-squares points of clear rate per applied
stack over every team, mean (SD) over seeds):

| Bond | Count | Stacks | Teams | `elemental` clear | excess | `neutral` clear | excess |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `bulwark` (slope 0.0 (0.7) / +0.9 (0.4)) | 0 | 0 | 5 | 29.7% | +7.6 | 46.3% | +8.2 |
|  | 1 | 1 | 50 | 23.9% | +1.0 | 41.4% | +2.2 |
|  | 2 | 2 | 100 | 21.9% | -1.9 | 36.8% | -3.5 |
|  | 3 | 3 | 50 | 26.3% | +1.6 | 45.4% | +4.0 |
|  | 4 | 0 | 5 | 29.7% | +4.3 | 41.5% | -1.0 |
| `overwatch` (slope +0.9 (1.1) / -0.8 (1.2)) | 0 | 0 | 35 | 24.2% | +1.5 | 47.1% | +5.8 |
|  | 1 | 1 | 105 | 23.0% | -0.6 | 37.4% | -3.1 |
|  | 2 | 2 | 63 | 24.2% | -0.3 | 40.2% | +0.5 |
|  | 3 | 3 | 7 | 30.6% | +5.1 | 50.9% | +12.1 |
| `flanking` (slope -2.5 (0.2) / -0.6 (1.1)) | 0 | 0 | 70 | 26.9% | +1.1 | 42.6% | +1.9 |
|  | 1 | 1 | 112 | 21.9% | -1.4 | 37.8% | -2.3 |
|  | 2 | 2 | 28 | 23.7% | +2.9 | 44.2% | +4.7 |

The slope mixes the bond with its stance's own strength (only Skirmisher teams can hold `flanking`,
and Skirmishers are the weakest beasts: its slope is negative although the bond helps), and the
excess of a Team-scope stance bond is flat by construction (its stacks are a sum of memberships the
additive model already fits). What the excess does show is the stance-extreme lineups beating the
additive prediction: no Vanguard, 3 Ranged or 2 Skirmishers run +2.9 to +12.1 (4 Vanguards +4.3 /
-1.0), the 2-Vanguard / 1-Ranged / 1-Skirmisher counts -0.6 to -3.5.

**Composition spread: the goal is missed.** The target was a persistent team SD (`elemental`
overall, 3 seeds) at least 1.5 points above the value before this change.

| Kit mode | Scope | Persistent SD before | after | Per-seed SD before | after | Seed-to-seed SD before | after |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `elemental` | overall | 0.0 | 0.0 | 6.3 | 6.2 | 7.8 | 6.4 |
| `elemental` | `solo` | 3.5 | 3.5 | 8.0 | 8.2 | 7.2 | 7.4 |
| `elemental` | `elite` | 0.0 | 0.0 | 10.8 | 9.4 | 15.6 | 10.4 |
| `elemental` | `squad` | 9.5 | 9.1 | 13.0 | 13.5 | 8.9 | 10.0 |
| `elemental` | `horde` | 0.0 | 5.0 | 17.3 | 17.4 | 18.9 | 16.7 |
| `neutral` | overall | 3.1 | 3.8 | 11.8 | 11.5 | 11.4 | 10.8 |

- **Before is already 0.0**, not the 6.1 the design assumed: under the scouted-pick calibration
  each seed's own composition draw decides most of a team's `elemental` rate, so the
  seed-to-seed SD (7.8) exceeds the within-seed spread (6.3) and the persistent estimate floors at
  0. The within-seed spread did not move (6.3 -> 6.2; `--seeds 1,2,3`: 6.3 -> 6.4, persistent 1.0
  -> 0.0).
- **Why the bonds do not spread teams:** a Team-scope stance bond adds a fixed amount per stance
  member, i.e. it shifts that stance's beasts' marginals, and the stance it helps most here
  (Skirmishers, via `flanking`) is the weakest, so it narrows the spread; `bulwark` (Others) is
  concave in the Vanguard count (stacks x recipients = 3, 4, 3 for 1, 2, 3 Vanguards) and helps the
  balanced middle most, which also narrows it.
- **Probes (not committed):** larger magnitudes at the validator caps' edge (bulwark 6%, overwatch
  5, flanking 8%) gave persistent 2.1 but only because the seed-to-seed SD fell (per-seed 6.3 ->
  6.1), and moved the Skirmishers +3.4 / +4.5 (over the 2-point rule). Convex stacks (overwatch
  +5 crit and flanking +8% Speed with Members scope, so value ~ count²) left it at 0.0 (per-seed
  6.4). Stat bonds of this size do not make the `elemental` lineup matter more across seeds; the
  lever is probably element or kit synergy (e.g. bonds that change what a beast does), or measuring
  composition against fixed compositions rather than per-seed draws. Left for the lead.

**Runtime** unchanged (default run about 11 s, three seeds about 30 s). `--self-check` and
`--mode pacing --self-check` pass.

Reproduce: `dotnet run --project Tooling/BalanceSim -c Release -- --seeds 12345,777,4242 --out out/scaling.md`
(about 30 s); before = the same at the previous commit.

## Level-difference modifier

Design decision (milestone 2, user): **an under-levelled team must not clear content** ("that makes
levelling pointless; the curve levels out too much"). Chosen fix: a level-difference multiplier on
every damage hit, caster level minus target level, both directions, the avatar at its own level
(`DamageFormula.GetLevelMultiplier`; battle-system.md, "Damage formula"):

```
level multiplier = clamp(1 + k d + q d |d|, 1 - cap, 1 + cap),  d = caster level - target level
k = 0.025 (LevelDifferencePerLevel), q = 0.005 (LevelDifferenceConvex), cap = 0.4 (LevelDifferenceCap)
```

It is the last multiplier before the single truncation (after element, crit, variance and execute),
exactly 1 between equal levels and then not applied at all, so **every equal-level battle is
bit-identical**: the committed `tuned-report.md` is byte-identical with the modifier in (`cmp`
against the fresh default run), and so are all three seeds' reports and the aggregate of
`--mode pve --seeds 12345,777,4242` against the previous commit (**balance guard: normalized
per-beast marginals unchanged, identity**). Damage over time inherits it; heals, shields, stat
changes, status chance and knockback do not; no random draws. No roster, skill, bond, avatar or
enemy data changed.

**Measuring it: `--level-gap`.** Every (kit mode, shape, level) cell is calibrated at equal levels
as before (scouted pick at 50%), then replayed at the same multiplier with the enemies `g` levels
above the team: the picked team 16 times per composition (the **scouted** rate) and a seeded 42 of
210 teams once per composition (the **no-scouting** rate). The seed ignores the gap (common random
numbers), so gap 0 is the calibration itself. The team and the avatar stay at the row's level; the
enemies' stats follow their curve to their level. Report section "PvE level gap" (and "over seeds"
with `--seeds`); committed as `docs/balance/level-gap-report.md` (`--mode pve --levels
10,30,50,70,90 --level-gap -5..10`, seed 12345, about 58 s). Targets for the scouted rate: gap 0
50 +/- 5, +2 and +3 in 20-35%, +5 and beyond under 10%, at every level band.

**Sweep** (3 seeds 12345 / 777 / 4242, `elemental` (the game's mode), every shape averaged,
scouted rate; `!` = misses its target; k = 0 is the stats alone, i.e. before this change). The
design's grid is k in {0.025, 0.03, 0.035, 0.04} x cap in {0.3, 0.4}; the cap only binds past
cap / k levels (7.5-16), so the cap-0.3 rows repeat the cap-0.4 ones exactly up to +7 (common random
numbers) and are listed once:

| k | cap | q | +2 at L10 / 30 / 50 / 70 / 90 | +3 | +5 | +7 | Targets met L30-90 | L10 |
| ---: | ---: | ---: | --- | --- | --- | --- | ---: | ---: |
| 0 | - | 0 | 33.7 / 39.0 ! / 37.7 ! / 41.9 ! / 41.9 ! | 25.3 / 36.4 ! / 35.4 ! / 41.5 ! / 38.6 ! | 19.0 ! / 25.8 ! / 32.2 ! / 36.1 ! / 34.6 ! | 10.7 / 22.7 / 26.7 / 35.0 / 34.9 | 0/12 | 2/3 |
| 0.025 | 0.3, 0.4 | 0 | 26.6 / 29.9 / 29.8 / 31.3 / 30.0 | 18.4 ! / 26.8 / 24.9 / 28.8 / 26.8 | 10.5 ! / 11.6 ! / 16.3 ! / 19.7 ! / 18.6 ! | 2.7 / 8.5 / 10.5 / 13.7 / 11.5 | 8/12 | 1/3 |
| 0.03 | 0.3, 0.4 | 0 | 25.2 / 28.9 / 27.9 / 29.8 / 27.9 | 16.7 ! / 24.6 / 23.6 / 27.0 / 25.4 | 8.9 / 11.1 ! / 13.8 ! / 17.1 ! / 15.9 ! | 2.2 / 5.9 / 8.9 / 10.5 / 9.6 | 8/12 | 2/3 |
| 0.035 | 0.3, 0.4 | 0 | 23.8 / 27.2 / 26.2 / 27.9 / 26.8 | 15.6 ! / 23.0 / 22.3 / 25.3 / 23.8 | 7.0 / 9.4 / 12.2 ! / 15.4 ! / 12.9 ! | 1.2 / 4.4 / 7.2 / 8.0 / 7.2 | 9/12 | 2/3 |
| 0.04 | 0.3, 0.4 | 0 | 22.7 / 26.0 / 24.7 / 27.1 / 25.8 | 14.3 ! / 20.6 / 21.0 / 24.4 / 22.3 | 5.5 / 8.3 / 11.0 ! / 13.2 ! / 10.7 ! | 1.0 / 3.1 / 6.2 / 7.0 / 6.1 | 9/12 | 2/3 |
| 0.03 | 0.4 | 0.004 | 23.4 / 26.6 / 25.1 / 27.2 / 26.5 | 13.5 ! / 20.0 / 20.6 / 24.0 / 21.2 | 3.8 / 6.2 / 8.3 / 9.2 / 8.3 | 0.3 / 0.8 / 2.0 / 3.1 / 2.8 | 12/12 | 2/3 |
| **0.025** | **0.4** | **0.005** | 23.8 / 27.2 / 26.2 / 27.9 / 26.8 | 14.3 ! / 20.6 / 21.0 / 24.4 / 22.3 | 3.8 / 6.2 / 8.3 / 9.2 / 8.3 | 0.3 / 0.8 / 2.0 / 3.1 / 2.8 | **12/12** | 2/3 |

- **Stats alone (k = 0) do not do it**: 5 levels under, the scouted team still clears 26-36% at
  levels 30-90 (at level 10, where a level is about 4% of stats rather than 1-2%, 19%).
- **No linear k meets both ends.** "Under 10% at +5" needs a multiplier of about 1.25 at 5 levels,
  which linearly (k = 0.05) would put 3 under at about 1.15 and below 20%. Every linear k from
  0.025 to 0.04 misses +5 at levels 50-90 (11-20%) while +2 / +3 still sit comfortably inside the band.
- **The convex term q (design fallback) fixes it**: 1 + k d + q d |d| is mild near 0 and steep
  further out. k = 0.025, q = 0.005 gives x1.07 at 2 levels (= linear 0.035), x1.12 at 3
  (= linear 0.04) and x1.25 at 5. **Chosen: k = 0.025, q = 0.005, cap = 0.4**, which meets all 12
  targets at levels 30-90; k = 0.03 / q = 0.004 is equivalent except +3 at level 30 sits exactly
  on the 20% edge. Cap 0.4 (not 0.3): with q the multiplier reaches 1.4 at 7 levels, where a clear
  is already 1-3%; 0.3 would bind at 6 and flatten +6 / +7.
- **Chosen setting, full table** (3-seed means, every shape averaged, `scouted (no scouting)` %):

| Mode | Level | -5 | -4 | -3 | -2 | -1 | 0 | +1 | +2 | +3 | +4 | +5 | +6 | +7 | +8 | +9 | +10 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `elemental` | 10 | 100.0 (98.7) | 98.5 (92.5) | 93.3 (77.9) | 83.3 (59.3) | 66.9 (37.5) | 49.3 (21.9) | 34.3 (17.0) | 23.8 (9.5) | 14.3 (3.9) ! | 10.2 (2.1) | 3.8 (0.6) | 1.8 (0.1) | 0.3 (0.0) | 0.0 (0.0) | 0.0 (0.0) | 0.0 (0.0) |
| `elemental` | 30 | 98.8 (91.0) | 93.3 (75.9) | 87.8 (60.5) | 71.7 (43.5) | 60.4 (31.7) | 50.3 (22.7) | 34.0 (16.9) | 27.2 (10.4) | 20.6 (6.1) | 12.5 (2.7) | 6.2 (1.3) | 2.7 (0.5) | 0.8 (0.1) | 0.7 (0.0) | 0.6 (0.0) | 0.6 (0.0) |
| `elemental` | 50 | 96.9 (84.9) | 92.4 (70.1) | 80.9 (56.1) | 70.5 (40.9) | 64.4 (29.7) | 49.3 (23.4) | 35.6 (16.7) | 26.2 (11.0) | 21.0 (6.7) | 15.9 (3.8) | 8.3 (1.4) | 5.9 (0.8) | 2.0 (0.2) | 2.1 (0.2) | 2.1 (0.2) | 1.6 (0.1) |
| `elemental` | 70 | 95.8 (81.5) | 90.6 (66.6) | 77.2 (52.1) | 72.7 (39.0) | 64.3 (28.6) | 50.1 (23.3) | 42.9 (19.7) | 27.9 (13.3) | 24.4 (9.2) | 16.9 (4.4) | 9.2 (1.9) | 6.7 (0.9) | 3.1 (0.4) | 2.9 (0.4) | 2.7 (0.3) | 2.4 (0.3) |
| `elemental` | 90 | 93.9 (76.4) | 86.9 (62.0) | 74.7 (48.0) | 69.9 (35.6) | 57.2 (25.8) | 49.8 (21.9) | 38.5 (18.0) | 26.8 (11.5) | 22.3 (7.5) | 13.9 (4.2) | 8.3 (1.8) | 4.7 (0.9) | 2.8 (0.3) | 3.6 (0.4) | 2.9 (0.3) | 2.1 (0.2) |
| `neutral` | 10 | 100.0 (100.0) | 99.9 (99.8) | 99.2 (98.8) | 95.6 (95.0) | 78.7 (73.0) | 50.5 (41.7) | 30.4 (24.7) | 16.1 (12.0) ! | 4.8 (3.3) ! | 2.1 (1.5) | 0.2 (0.1) | 0.0 (0.0) | 0.0 (0.0) | 0.0 (0.0) | 0.0 (0.0) | 0.0 (0.0) |
| `neutral` | 30 | 99.5 (99.4) | 98.9 (98.9) | 97.8 (94.9) | 90.4 (81.6) | 71.9 (62.0) | 51.6 (42.0) | 31.5 (27.1) | 15.8 (14.2) ! | 9.1 (7.4) ! | 2.5 (2.3) | 0.4 (0.7) | 0.2 (0.1) | 0.0 (0.0) | 0.0 (0.0) | 0.0 (0.0) | 0.0 (0.0) |
| `neutral` | 50 | 99.7 (99.3) | 98.0 (97.4) | 94.4 (91.0) | 83.7 (74.5) | 71.2 (56.9) | 49.7 (40.9) | 26.9 (24.9) | 16.3 (12.1) ! | 7.2 (5.7) ! | 2.5 (2.1) | 0.1 (0.4) | 0.0 (0.0) | 0.0 (0.0) | 0.1 (0.0) | 0.1 (0.0) | 0.0 (0.0) |
| `neutral` | 70 | 99.2 (99.2) | 98.2 (96.9) | 94.1 (89.4) | 84.0 (74.3) | 70.8 (55.2) | 50.3 (41.6) | 32.4 (30.2) | 16.0 (17.1) ! | 10.1 (8.2) ! | 3.8 (3.8) | 0.4 (0.9) | 0.3 (0.1) | 0.0 (0.1) | 0.0 (0.0) | 0.0 (0.0) | 0.1 (0.0) |
| `neutral` | 90 | 98.8 (99.0) | 98.0 (94.7) | 90.6 (85.9) | 81.2 (69.5) | 67.2 (53.0) | 50.1 (40.8) | 31.1 (30.8) | 15.8 (16.2) ! | 7.6 (8.3) ! | 5.9 (3.2) | 1.1 (1.0) | 0.1 (0.1) | 0.0 (0.0) | 0.0 (0.0) | 0.0 (0.0) | 0.0 (0.0) |

**Targets, honestly:**
- `elemental`, levels 30-90: **met everywhere** (gap 0 49-50%; 2 under 26-28%; 3 under 21-24%;
  5 under 6-9%). The no-scouting player is at 10-13% two levels under and under 2% at five.
- `elemental`, **level 10: 3 under is 14% (target 20-35%)** — the stats already move 4% per level
  there, so the modifier stacks on a gap that is steep on its own (k = 0 gives 25%). 2 under (24%) and
  5 under (4%) are in target. A level-dependent k could soften it; not done (level 10 is early game,
  where levelling is fast).
- `neutral` (the element-free control) is **steeper**: 2 under 16%, 3 under 5-10% at every level.
  Without the chart a fight is decided by stats and the multiplier alone, and the picked team has no
  counter-pick edge to spend; the targets are set on `elemental`, the game's mode.
- **Per shape**, the boss shapes (`solo`, `elite`) fall fastest (3 under 3-22%, 5 under 0-7%),
  `squad` and `horde` slowest (5 under 12-15% and 7-17% at levels 50-90, mostly over target; `horde`
  keeps 6-9% even 8-10 under at levels 50-90). The All-shapes mean meets the targets; 156 of 180
  shape cells do. A per-shape look (why hordes keep a residue) is left for stage D.
- Above-level fights are, symmetrically, easy: 3 levels over clears 75-93%, 5 over 94-100%.
- The committed single-seed `level-gap-report.md` (seed 12345) shows the same picture with seed noise
  (All shapes, `elemental`: 41 of 45 targets met; misses L10 +3 11.9%, L30 +3 16.2%, L50 +5 10.9%,
  L70 +5 10.7%).

**Pacing** (`--mode pacing --self-check`): passes, report byte-identical (the pacing model draws
clear rates, it does not simulate battles; the avatar stays within 0-1 levels of the encounter
level, so the avatar's own level difference is small in practice).

**Runtime.** Default run unchanged (about 11 s; no level-gap section by default: the default levels
1 / 50 / 100 cannot show gaps above level 100, and the report stays byte-identical). Each nonzero
gap adds about 128 + 336 battles per cell: `--mode pve --levels 10,30,50,70,90 --level-gap -5..10`
takes about 58 s, three seeds about 155 s.

Reproduce: `dotnet run --project Tooling/BalanceSim -c Release -- --mode pve --levels 10,30,50,70,90
--level-gap -5..10 --seeds 12345,777,4242 --out out/levelgap.md` (about 155 s); each sweep row is the
same run with the three `DamageFormula.LevelDifference*` constants edited.

## Avatar retune (milestone 2, stage D1)

Design decision (milestone 2): after the avatar moved onto its own ATB gauge ("Avatar gauge") it acts
about a third as often, and its actives and passive cooldowns were still authored for the old
cadence. This retune restores its role, **passives first** (the user's vision: the passives are the
avatar's main role). Data only, plus a measurement: `--avatar-value` (README, "Avatar value").

**Measure.** At each cell's calibrated multiplier the picked teams' battles (8 compositions x 16) are
replayed **without the avatar**, seed for seed; the avatar's **value** is the scouted rate with it
(the calibrated ~50%) minus without, in points. Its **direct share** is its damage + healing + shield
soak (a shield's soak credited to its caster) as a percent of the whole team's, with the part its
passives produced. **Reference**: the same measurement on `cdf48ec` (the last commit on the
per-beast-turn cadence), in a scratch worktree with the scouted-pick calibration (`035000f`, sim only)
cherry-picked onto it and the no-avatar replay added (never checked out in the main tree). 3 seeds
(12345 / 777 / 4242), levels 1 / 50 / 100, both kit modes.

Targets: value at least 75% of the reference; direct share 10-15% (`elemental`).

| Run | Change | `elemental` value (% of ref) | no-avatar % | direct share (passives) | `neutral` value | share |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Reference `cdf48ec` | old cadence | **+41.6** (100%) | 8.4 | - | +48.1 | - |
| A0 | before (gauge, old numbers) | +31.6 (76%) | 18.4 | 13.6% (10.8) | +41.4 | 15.8% |
| A1 | design start: actives cd 4/3/4 -> 2, durations 2 -> 3, Mending Light 13 -> 20, Aegis 30 -> 45, Last Stand icd 2 -> 1 | +42.7 (103%) | 8.0 | **21.1%** (11.7) | +47.9 | 24.6% |
| A2 | A1 with Mending Light 13, Aegis back to cd 4 / 30 | +38.3 (92%) | 11.5 | **16.1%** (12.8) | +45.2 | 19.0% |
| **A3** | A2 with Last Stand icd back to 2 | **+37.0 (89%)** | 13.6 | **13.8%** (10.5) | +44.1 | 16.0% |
| A3b | A2 with Last Stand 80 -> 60 (icd 1) | +35.2 (85%) | 14.8 | 13.9% (10.6) | +45.6 | 15.9% |

Per shape, A0 -> A3 (`elemental`, value in points): `solo` +29.9 -> +38.2, `elite` +39.9 -> +43.8,
`squad` +25.6 -> +27.2, `horde` +31.2 -> +38.9 (reference +44.4 / +47.3 / +32.2 / +42.6). Avatar
turns per battle are unchanged (5.6-5.7).

- **The before state already sat on the value line (76%)**, with the share inside its band. The
  design's starting set overshoots the share (21%): Aegis at 45% of Defense every second avatar turn
  soaks as much as Opening Ward and Last Stand together, which would turn the avatar into a shield
  caster. The value can come from the buff instead: **Rallying Cry at cooldown 2 for three turns**
  is up almost all the time and has no direct output, so it moves the value without moving the share.
- **Chosen: A3.** Value +37.0 (89% of the reference; target met), direct share 13.8% (target met),
  **three-quarters of it from the passives** (10.5 of 13.8: Opening Ward's opening shield and Last
  Stand's emergency shield). Last Stand stays at internal cooldown 2: at 1 (A2) its extra firings
  push the share over 15%, and trading its size for frequency (A3b, 60%) is worth less.
- `neutral` (the control) is 44.1 points against 48.1 (92%) with a 16.0% share; it was 15.8% before,
  the element-free fights last longer and shields soak more of them.

**Changes** (skill library; the three optional actives and two optional passives follow the design so
the whole avatar catalogue is on the new cadence; they are not in the default loadout the simulator
fields):

| Skill | Before | After |
| --- | --- | --- |
| Rallying Cry (default) | cd 4; +10% Attack / SpecialAttack 2t | **cd 2**; +10% Attack / SpecialAttack **3t** |
| Mending Light (default) | cd 3; Heal 13 | **cd 2**; Heal 13 |
| Aegis (default) | cd 4; Shield 30% Def 2t | unchanged |
| Hex of Frailty | cd 4; -10% Def / SpD 2t | **cd 2**; **3t** |
| Battle Focus | cd 5; +8 crit 2t | **cd 3**; **3t** |
| Slowing Field | cd 5; -12% Speed 2t | **cd 3**; **3t** |
| Keen Eye, Opening Ward, Last Stand (default passives) | - | unchanged (Last Stand icd 2) |
| Bloodlust | icd 2 | **icd 1** |
| Storm Call | icd 3 | **icd 1** |

Each active's tier-15 `CooldownReduction` (1) now takes a cooldown-2 active to 1, never below; the
library test that allowed a reduction only on cooldowns of 3 or more still holds for beast skills and
now requires the avatar's actives (which deal no damage) to keep a cooldown of at least 1.

**Guard** (3-seed normalized means, before -> after): the stronger avatar raises every cell's
multiplier, so beasts shift. `elemental`: Treant +2.4 -> +2.6, Phoenix +3.3 -> +2.5, Kirin +1.7 ->
+2.1, Golem +1.5 -> +2.1, Tarasque +1.5 -> +1.2, Basilisk -0.2 -> 0.0, Frost Wyrm -1.0 -> -0.2,
Leviathan -2.0 -> -1.5, **Griffin -3.6 -> -4.2, Thunderbird -3.6 -> -4.5** (outside +/-4, the stage D3
retune below). `neutral` stays inside +/-7 (-3.9 ... +5.8; Golem +3.3 -> +5.8, Tarasque -3.6 -> -0.9).

Reproduce: `dotnet run --project Tooling/BalanceSim -c Release -- --mode pve --seeds 12345,777,4242
--avatar-value --out out/avatar.md` (about 40 s).

## Thunderbird in `elite` (milestone 2, stage D2)

The Thunderbird fell from about 0 to -4 in `elite` when the bosses became multi-hex ("Large enemies
(footprints)"; seed 12345's committed reports, `46ce852` -> `b7ae1d0`: `elite` -0.3 -> -4.4, `solo`
-2.2 -> -3.2, its `elite` survival 37.0% -> 34.5% at an unchanged damage share of 35%). The design's
hypothesis was a Skirmisher that **holds** (the fewer-adjacent stop-tile preference) next to a
Triangle champion with its short-range kit. `--turn-detail` (new; README, "Avatar value") measures it
directly: per beast, the turns on which no skill fired and why, and the enemy HP it took off large
enemies versus the rest.

**Diagnosis** (3 seeds, after the avatar retune, every team's battles):

- **No holding.** The Thunderbird fires on 100% of its turns in `solo` and `elite` (0.0% held by its
  stance, 0.0% out of reach); so do the other Ranged and Skirmisher beasts. The hypothesis is rejected.
- **It spends its damage on the boss.** 208 enemy HP per battle in `elite` (second only to the Phoenix),
  but **84.8% of it off the giant or champions** (Griffin 97.8%, the Ranged beasts 80-83%). With a
  Hex7 giant or two Triangle champions filling the front, the nearest enemy (Thunder Talons'
  `Distance` targeting) is almost always the boss, and the escort's archers and casters keep firing;
  its `elite` survival is 7.6-13% per seed, the lowest but the Griffin's.
- **Range is not the lever.** Probes (3 seeds, `elemental` normalized overall / `elite` raw): Storm
  Dive range 3 -> 4 (the design's candidate) -4.5 -> -3.7 / -3.8 -> -4.2; Thunder Talons range 2 -> 3
  **-6.1** / -3.5 (worse: at range 3 it hangs back and hits less); Talons targeting the lowest HP
  fraction -3.7 / -3.2 (it still picks the wounded boss); Talons targeting the **lowest current HP**
  **+0.8** / **+1.0 (2nd)**.

**Fix: Thunder Talons targets the enemy with the least HP left in reach** (`TargetingCriterion
CurrentHp`, `Lowest`), a Skirmisher hunter picking off the escort (and, in `squad` / `horde`, finishing
the weakest), where it used to pour its hits into the boss's HP pool. In `elite` its share of damage off
bosses falls 84.8% -> 63.2% and its turns per battle rise 3.53 -> 3.70. It is a one-field data change,
not one of the two candidates the design named (neither addresses the cause the measurement found);
power was then trimmed in the retune below (26 -> 25 x 3) because the change is strong in `neutral`
`squad`.

Reproduce: `--mode pve --seeds 12345,777,4242 --turn-detail` ("PvE beast turns over seeds").

## Beast retune under the scouted calibration (milestone 2, stage D3)

Targets: every beast's normalized overall marginal within +/-4 `elemental` and +/-7 `neutral` (3 seeds,
then confirmed over 5: 12345 / 777 / 4242 / 2024 / 99); **Griffin and Thunderbird `neutral` within +/-5**;
every beast top 3 in some shape; no beast top 3 in every shape. Hard constraints kept: **no roster edits**
(turn ratio, six-stat totals, Move, crit untouched), every signature skill kept, unlimited damage skills
at most 1.2x their budget. Start: the avatar retune above plus the Thunder Talons targeting fix.

| Skill | Before | After | DPT / budget | Why |
| --- | --- | --- | --- | --- |
| Thunder Talons | 26 x 3, nearest | **25 x 3**, **lowest current HP** | 75 / 70 = 1.07x | D2 fix; 26 left the Thunderbird's `neutral` at +5.3 (`squad` +25) |
| Wind Lance (Griffin) | 110 | **122** | 79 / 70 = 1.13x | Griffin `elemental` -4.8 (`elite` 10th, `horde` 10th) |
| Gale Talon (Griffin) | 89 | **95** | 95 / 90 = 1.06x | same |
| Gust (Griffin) | 55; Knockback 2 | **75**; Knockback 2 | 38 / 70 = 0.54x | Griffin still -3.7; lifts `solo` / `elite` |
| Radiant Bolt (Kirin) | 66 | **70** | 70 / 70 = 1.0x | Kirin had no `elemental` top-3 shape (4th in `elite` and `squad`) |
| Serpent Bite (Leviathan) | 86 | **94** | 94 / 90 = 1.04x | Leviathan dropped out of every top 3 over 5 seeds |

### Iteration log (normalized overall, 3-seed means unless noted)

| Run | Change on top of the previous | TB `elem` / `neutral` | Griffin `elem` / `neutral` | Range `elemental` | Range `neutral` | `elemental` top-3 coverage | Notes |
| --- | --- | --- | --- | --- | --- | ---: | --- |
| Start | avatar retune | -4.5 / -0.6 | -4.2 / -3.9 | -4.5 … +2.6 | -3.9 … +5.8 | 9 / 10 | TB, Griffin outside +/-4; TB no top 3 |
| D2 | Talons lowest current HP | +0.8 / **+5.3** | -4.8 / **-5.2** | -4.8 … +2.0 | -5.2 … +5.5 | 9 / 10 | Kirin drops out (4th) |
| R1 | Talons 25, Wind Lance 122, Gale Talon 95, Radiant Bolt 70 | -0.2 / +3.6 | -3.7 / -2.2 | -3.7 … +1.7 | -2.5 … +5.5 | 10 / 10 | Griffin on the edge |
| R2 | R1 + Gust 75 | -0.3 / +3.6 | -3.2 / -1.0 | -3.2 … +1.8 | -2.9 … +5.4 | 10 / 10 | 5 seeds: Leviathan no top 3 (4th `horde`) |
| **R3** | R2 + Serpent Bite 94 | -0.3 / +3.5 | -3.3 / -1.1 | -3.3 … +1.4 | -3.0 … +5.4 | 10 / 10 | **5 seeds: 10 / 10**; chosen |

Three three-seed iterations (of eight allowed) and two five-seed confirmations (R2, R3).

### Results (mean of 5 seeds: 12345 / 777 / 4242 / 2024 / 99)

Before = after the avatar retune (stage D1), after = R3. Normalized overall = the guard's figure (mean
± SD over seeds); per shape the raw marginal and its rank on the mean; the last column counts the seeds
in which the beast is top 3 in `solo` / `elite` / `squad` / `horde`.

#### `elemental`: normalized overall and raw per shape, before -> after (rank in shape)

| Beast | Normalized before | Normalized after | `solo` | `elite` | `squad` | `horde` | Top-3 seeds s/e/q/h, after |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| Kirin | +2.9 | **+2.5** ± 2.6 | -0.1 (4) -> +0.1 (4) | +2.9 (2) -> +2.2 (2) | +3.5 (4) -> +2.9 (4) | +1.6 (5) -> +1.4 (5) | 0/4/2/1 |
| Treant | +2.5 | **+1.8** ± 2.9 | +3.6 (1) -> +3.5 (2) | +1.9 (3) -> +0.5 (4) | -8.4 (10) -> -8.5 (10) | +7.7 (2) -> +7.2 (2) | 4/1/0/4 |
| Golem | +1.6 | **+0.9** ± 2.9 | -2.1 (9) -> -2.0 (8) | +5.1 (1) -> +4.2 (1) | -5.1 (8) -> -7.0 (9) | +6.0 (3) -> +5.7 (3) | 0/3/0/3 |
| Thunderbird | -2.8 | **+0.8** ± 2.4 | -1.8 (8) -> -2.1 (9) | -4.6 (10) -> -0.1 (6) | +4.2 (3) -> +8.4 (2) | -2.8 (6) -> -1.1 (6) | 0/0/4/0 |
| Phoenix | +1.5 | **+0.5** ± 4.7 | -0.4 (6) -> -0.6 (6) | -1.1 (8) -> -1.8 (9) | +9.7 (1) -> +9.2 (1) | -3.0 (7) -> -3.2 (7) | 0/1/3/0 |
| Tarasque | +0.5 | **0.0** ± 3.5 | -0.8 (7) -> -1.1 (7) | -0.1 (6) -> -0.8 (7) | +6.6 (2) -> +7.0 (3) | -4.7 (8) -> -4.9 (8) | 2/2/4/0 |
| Frost Wyrm | -0.3 | **-1.1** ± 3.3 | -3.9 (10) -> -3.9 (10) | -0.4 (7) -> -1.5 (8) | -3.4 (7) -> -3.6 (7) | +12.6 (1) -> +12.0 (1) | 0/1/0/5 |
| Basilisk | -0.5 | **-1.3** ± 1.1 | +2.9 (3) -> +2.4 (3) | -0.1 (5) -> +0.0 (5) | -0.9 (6) -> -2.3 (6) | -8.2 (9) -> -8.4 (9) | 3/0/0/0 |
| Leviathan | -2.1 | **-1.5** ± 3.5 | -0.2 (5) -> -0.3 (5) | +0.3 (4) -> +0.6 (3) | -7.9 (9) -> -6.8 (8) | +1.9 (4) -> +2.0 (4) | 2/2/0/2 |
| Griffin | -3.3 | **-2.6** ± 1.8 | +2.9 (2) -> +4.0 (1) | -4.0 (9) -> -3.2 (10) | +1.7 (5) -> +0.6 (5) | -11.1 (10) -> -10.6 (10) | 4/1/2/0 |

Normalized range: -3.3 … +2.9 -> -2.6 … +2.5.

Niche map `elemental` (after): mean top 3 per shape; **bold** = top 3 in ≥ 4 of 5 seeds

| Shape | Top 3 |
| --- | --- |
| `solo` | **Griffin +4.0 (4/5)**, **Treant +3.5 (4/5)**, Basilisk +2.4 (3/5) |
| `elite` | Golem +4.2 (3/5), **Kirin +2.2 (4/5)**, Leviathan +0.6 (2/5) |
| `squad` | Phoenix +9.2 (3/5), **Thunderbird +8.4 (4/5)**, **Tarasque +7.0 (4/5)** |
| `horde` | **Frost Wyrm +12.0 (5/5)**, **Treant +7.2 (4/5)**, Golem +5.7 (3/5) |

Top 3 in some shape: 10 / 10.

#### `neutral`: normalized overall and raw per shape, before -> after (rank in shape)

| Beast | Normalized before | Normalized after | `solo` | `elite` | `squad` | `horde` | Top-3 seeds s/e/q/h, after |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | :---: |
| Golem | +5.6 | **+5.1** ± 1.3 | +10.6 (1) -> +10.9 (2) | +6.2 (1) -> +5.1 (1) | -8.0 (8) -> -8.0 (8) | +8.7 (2) -> +8.4 (2) | 5/4/0/5 |
| Thunderbird | +0.2 | **+3.9** ± 1.8 | -5.7 (7) -> -8.1 (9) | -5.8 (10) -> +3.1 (4) | +17.3 (1) -> +22.8 (1) | -2.9 (8) -> -1.5 (7) | 0/4/5/0 |
| Treant | +1.6 | **+0.4** ± 1.1 | +4.8 (3) -> +4.8 (3) | +4.6 (3) -> +2.3 (5) | -12.2 (9) -> -12.5 (10) | +7.1 (3) -> +6.0 (3) | 3/1/0/5 |
| Griffin | -2.9 | **-0.1** ± 2.3 | +9.5 (2) -> +16.0 (1) | -5.3 (9) -> -1.7 (7) | +1.4 (6) -> -0.6 (6) | -17.6 (10) -> -16.3 (10) | 5/0/0/0 |
| Leviathan | -0.4 | **-0.2** ± 1.0 | +3.6 (4) -> +2.1 (4) | +3.8 (5) -> +5.1 (2) | -12.4 (10) -> -10.9 (9) | +2.7 (4) -> +3.0 (4) | 1/3/0/0 |
| Frost Wyrm | +1.0 | **-0.3** ± 1.1 | -5.7 (8) -> -6.1 (8) | -3.4 (6) -> -5.8 (8) | +1.8 (5) -> +0.7 (5) | +12.5 (1) -> +11.4 (1) | 0/0/1/5 |
| Kirin | -0.3 | **-0.9** ± 0.7 | -6.8 (9) -> -5.2 (7) | +4.4 (4) -> +1.3 (6) | +1.9 (4) -> +1.1 (4) | +0.4 (6) -> +0.2 (6) | 0/1/0/0 |
| Basilisk | -1.4 | **-2.4** ± 2.6 | +3.1 (5) -> +0.9 (5) | +5.4 (2) -> +4.5 (3) | -6.0 (7) -> -5.4 (7) | -9.1 (9) -> -9.7 (9) | 1/2/0/0 |
| Tarasque | -1.5 | **-2.5** ± 1.6 | -4.2 (6) -> -4.3 (6) | -5.3 (8) -> -7.1 (10) | +7.4 (3) -> +5.5 (3) | -2.6 (7) -> -2.9 (8) | 0/0/4/0 |
| Phoenix | -1.9 | **-3.1** ± 0.5 | -9.2 (10) -> -11.0 (10) | -4.6 (7) -> -6.9 (9) | +8.9 (2) -> +7.4 (2) | +0.9 (5) -> +1.4 (5) | 0/0/5/0 |

Normalized range: -2.9 … +5.6 -> -3.1 … +5.1.

Niche map `neutral` (after): mean top 3 per shape; **bold** = top 3 in ≥ 4 of 5 seeds

| Shape | Top 3 |
| --- | --- |
| `solo` | **Griffin +16.0 (5/5)**, **Golem +10.9 (5/5)**, Treant +4.8 (3/5) |
| `elite` | **Golem +5.1 (4/5)**, Leviathan +5.1 (3/5), Basilisk +4.5 (2/5) |
| `squad` | **Thunderbird +22.8 (5/5)**, **Phoenix +7.4 (5/5)**, **Tarasque +5.5 (4/5)** |
| `horde` | **Frost Wyrm +11.4 (5/5)**, **Golem +8.4 (5/5)**, **Treant +6.0 (5/5)** |

Top 3 in some shape: 9 / 10 (not: Kirin).

### Targets: met and missed (5-seed means, final)

| Target | Before | After | |
| --- | --- | --- | --- |
| Every beast `elemental` normalized within +/-4 | -3.3 … +2.9 (3 seeds: -4.5 … +2.6) | -2.6 … +2.5 (3 seeds: -3.3 … +1.4) | met |
| Every beast `neutral` normalized within +/-7 | -2.9 … +5.6 | -3.1 … +5.1 | met (Golem +5.1 the edge) |
| Griffin / Thunderbird `neutral` within +/-5 | -2.9 / +0.2 | -0.1 / +3.9 | met |
| Every beast top 3 in some shape, `elemental` | 9 / 10 (not Leviathan) | **10 / 10** | met; Leviathan only on the mean (`elite` +0.6 against Treant +0.5, 2 / 5 seeds) |
| No beast top 3 in every shape | none | none | met (most: two shapes `elemental`, Treant and Golem; three `neutral`, Golem) |
| Turn ratio, six-stat totals, Move, crit, signatures | unchanged | unchanged | met (no roster edits; every signature test passes) |
| Budget rule (<= 1.2x, unlimited damage skills) | met | met | highest Ember Shot 1.16x, Wind Lance 1.13x |

`neutral` top-3 coverage is 9 / 10 (not Kirin, 4th in `squad`), with 9 robust niches (top 3 in at
least 4 of 5 seeds) across 7 beasts. `elemental` robust niches: Griffin and Treant `solo`, Kirin
`elite`, Thunderbird and Tarasque `squad`, Frost Wyrm and Treant `horde` (7 niches, 6 beasts).

### What moved

- **Thunderbird** -2.8 -> +0.8 `elemental` (`elite` 10th -> 6th, `squad` +4.2 -> +8.4, robustly 2nd),
  +0.2 -> +3.9 `neutral` (`squad` +22.8, 1st in every seed; the edge the Talons trim holds). Its `solo`
  is unchanged (-2.1, 9th): against a lone giant there is nothing weaker to pick.
- **Griffin** -3.3 -> -2.6 `elemental`, -2.9 -> -0.1 `neutral`: the lifts land mostly in `solo` (1st in
  both modes, +4.0 / +16.0), its duelist niche; `elite` (-3.2, 10th) and `horde` (-10.6, 10th) stay its
  weak shapes (97.8% of its `elite` damage goes into the boss, like the Thunderbird's did; a targeting
  change was not needed to meet the guard, and would blur the two Skirmishers).
- **Kirin** keeps a robust `elite` niche (2nd, 4 / 5 seeds) through Radiant Bolt 70.
- **Leviathan** -2.1 -> -1.5 `elemental`; Serpent Bite 94 buys back a top 3 (`elite` 3rd on the mean).
- Everyone else gave up 0.4-1.0 points `elemental` to the two Skirmishers' gains; the `neutral`
  losers are the Ranged damage dealers (Phoenix -1.9 -> -3.1, Basilisk -1.4 -> -2.4), whose `squad`
  kills the Thunderbird now takes first.

### Avatar after the beast retune

The avatar's value stays on target with the final data (3 seeds, `--avatar-value`): **+36.5 points**
`elemental` (88% of the `cdf48ec` reference 41.6; `solo` +36.5, `elite` +42.7, `squad` +27.2, `horde`
+39.8), **direct share 13.6%**, 10.2 of it from its passives (`neutral` +45.4, 15.8%).

### Caveats

- Leviathan's `elemental` niche is thin (`elite` 3rd by 0.1 point, top 3 in 2 of 5 seeds); a sixth
  seed could drop it to 4th. Its `horde` (4th, +2.0) is the other near miss.
- Golem `neutral` +5.1 is the most positive beast in either mode (top 3 in three `neutral` shapes); it
  was +5.6 before and is inside +/-7.
- Normalized SDs over seeds run 1.1-4.7 `elemental` (Phoenix 4.7); the 5-seed means are within about
  +/-2 of the truth.
- `tuned-report.md` is regenerated from the default run (seed 12345).

Reproduce: `dotnet run --project Tooling/BalanceSim -c Release -- --mode pve --seeds
12345,777,4242,2024,99 --out out/retune.md` (about 55 s).

## Level-gap re-check (milestone 2, stage D4)

With the final data (the stronger avatar, the beast retune) the level-gap targets were re-measured
(`--mode pve --levels 10,30,50,70,90 --level-gap -5..10 --seeds 12345,777,4242`, the "Level-difference
modifier" setup). At k = 0.025, q = 0.005 two `elemental` targets at levels 30-90 broke, narrowly:
**3 under at level 50: 20.0% (just under 20)** and **5 under at level 70: 10.6%** (10 of 12 met; level
10's 3 under 17.3%, already a known miss). The stronger avatar flattens the curve a little (its shields
scale with its own level, the team's).

**Re-sweep** (the fix has to make 5 under harder without making 3 under harder: more convexity at the
same 3-level multiplier; `elemental`, every shape averaged, scouted %):

| k | q | multiplier at 2 / 3 / 5 | 2 under L30 / 50 / 70 / 90 | 3 under | 5 under | Met L30-90 | L10 2 / 3 / 5 under |
| ---: | ---: | --- | --- | --- | --- | ---: | --- |
| 0.025 | 0.005 | 1.07 / 1.12 / 1.25 | 28.8 / 27.8 / 29.8 / 29.9 | 22.9 / **20.0 !** / 25.1 / 22.7 | 7.1 / 9.8 / **10.6 !** / 8.5 | 10 / 12 | 24.0 / 17.3 ! / 4.2 |
| 0.017 | 0.007 | 1.06 / 1.11 / 1.26 | 30.2 / 29.6 / 31.0 / 30.6 | 23.2 / 20.2 / 25.8 / 23.2 | 6.3 / 9.4 / **10.2 !** / 8.0 | 11 / 12 | 24.9 / 17.4 ! / 4.0 |
| **0.012** | **0.009** | 1.06 / 1.12 / 1.29 | 30.8 / 29.8 / 31.3 / 31.1 | 23.0 / 20.3 / 25.3 / 22.7 | 5.7 / 7.9 / 9.0 / 7.4 | **12 / 12** | 25.0 / 17.4 ! / 3.3 |

**Chosen: k = 0.012, q = 0.009, cap = 0.4** (`DamageFormula.LevelDifferencePerLevel` /
`LevelDifferenceConvex`): x1.02 / 1.06 / 1.12 / 1.29 / 1.4 (cap, from 7; 6 is x1.396) at 1 / 2 / 3 / 5 / 7
levels over, x0.98 / 0.94 / 0.88 / 0.72 / 0.6 under. Every equal-level battle is untouched (the default
report does not depend on k or q).

- `elemental`, levels 30-90: all 12 targets met; 3 under at level 50 (20.3%) is the thin one. Gap 0
  49.4-50.3%; 2 under 29.8-31.3%; 3 under 20.3-25.3%; 5 under 5.7-9.0%.
- Level 10: 3 under 17.4% (target 20-35%), unchanged by k / q at this scale: the stats move about 4%
  per level there. 2 under (25.0%) and 5 under (3.3%) are in target.
- `neutral` (the control) stays steeper: 3 under 8.5-13.6% at levels 30-90 (misses by design, as
  before), 5 under 0.2-0.8%.
- The committed single-seed `level-gap-report.md` (seed 12345) meets 40 of 45 `elemental` All-shapes
  targets; its misses are 3 under at levels 10 / 30 / 50 / 90 (13.1-19.5%) and 2 under at level 10
  (19.7%), seed noise around the 3-seed means above.

Reproduce: as above (about 160 s); each sweep row is the same run with the two constants edited.

## Encounters as game content

No balance change. The simulator's enemy types, encounter shapes and element-scheme weights moved out
of `Tooling/BalanceSim/encounters.json` into game content,
`content/data/Encounters/enemy-library.json` and `encounter-library.json`, by a
one-off scripted conversion (skills into the skill library's `SkillData` shape, `ThreatBudget` into
`ThreatMin` / `ThreatMax`, the scheme weights out of `SimOptions`); the generator moved into the
Runtime (`EncounterGenerator`) and the simulator now draws through it and fields enemies through the
game's `EnemyCatalog`. `encounters.json` keeps only the legacy fixed set.

**The gate:** the fresh default report differs from the committed one only in its three provenance
lines (the encounter files' paths and "game content" wording, "from the roster and the enemies"); so
do the fixed-set (`--encounter-set fixed`, one line) and a level-gap run (`--levels 30,70 --level-gap
-2,0,3`). Every number, composition, multiplier and clear rate is byte-identical. `tuned-report.md` and
`level-gap-report.md` are regenerated with exactly those lines changed.

The default run's calibrated multipliers are now also written for the game
(`--write-difficulty`, `encounter-difficulty.json`):

| Kit mode | Shape | L1 | L50 | L100 |
| --- | --- | ---: | ---: | ---: |
| `elemental` | `solo` | x1.152 | x1.168 | x1.160 |
| `elemental` | `elite` | x1.133 | x1.141 | x1.094 |
| `elemental` | `squad` | x1.348 | x1.320 | x1.367 |
| `elemental` | `horde` | x1.367 | x1.250 | x1.285 |
| `neutral` | `solo` | x0.969 | x0.984 | x0.988 |
| `neutral` | `elite` | x0.867 | x0.891 | x0.902 |
| `neutral` | `squad` | x1.277 | x1.266 | x1.313 |
| `neutral` | `horde` | x1.367 | x1.313 | x1.289 |

The game reads the `elemental` rows (linear between levels, clamped outside) times `DifficultyScale`
1.0. **Pending producer review:** this is calibrated so a scouting, counter-picking player (the
bond-aware heuristic pick) clears 50%; an unscouted team clears about 10-38% at these multipliers.
The campaign's intended difficulty is not decided.

Reproduce: `dotnet run --project Tooling/BalanceSim -c Release -- --out docs/balance/tuned-report.md
--write-difficulty content/data/Encounters/encounter-difficulty.json`.

## Behaviour bonds and tiered difficulty

Two lead-designed changes, approved by the user: the stat bonds become **behaviour bonds** (a member
of the bond acts in battle when its trigger happens), and the difficulty calibration aims each
encounter shape at its own **tiered target** instead of a flat 50%. Built in six steps, each
committed on a green local gate.

**1. Engine, byte for byte.** `BondReaction` on each bond tier (trigger, action, target, trigger
filter, reactor order, chance, cooldown, caps, range, effects), `SkillEffectType.Cleanse`,
`TeamBondCondition.DistinctStances`, the executor's `BattleHooks` (passives and/or reacting bonds)
with bonds passed into every turn, `BondReactionRecord` on each turn result. The design rules: one
reaction per bond per event, the chance rolled last and only below 100 (at most one draw), no
chaining (a reaction's hits, crits and defeats trigger nothing; `PassiveLoadout.SyncDefeatedSilently`
keeps its kills from firing defeat passives). With the old content (no reacting tier) no hook is
built, and the gate held exactly: the default report, `encounter-difficulty.json` and the level-gap
report (`--levels 10,30,50,70,90 --level-gap -5..10`) were byte-identical to the committed ones
(line endings aside). 27 EditMode tests cover the intercept, crit follow-ups, the threshold latch,
the turn-start cleanse, silent reaction defeats, draw-for-draw identity and the validator.

**2. The composition panel** (`--panel 16x4`, "Composition panel" in the BalanceSim README): 16 fixed
compositions per shape from a constant seed, every team 4 times each at the level-50 cell's
multiplier; a two-way ANOVA splits team clear rates into a team main effect and a team x composition
interaction (the value of counter-picking). It supersedes the multi-seed "persistent SD". Baseline on
the old stat bonds, seeds 12345, 777, 4242, 2024, 99, target 50%:

| Kit mode | Main-effect SD | Interaction SD |
| --- | ---: | ---: |
| `elemental` | 11.0 | 28.1 |
| `neutral` | 20.5 | 19.2 |

**3. Content swap.** The eleven stat bonds became nine behaviour bonds (see battle-system.md, "Team
bonds"); every beast is in one stance bond and one element bond, plus `combined_arms`. Enemies now
apply statuses so `twilight` has something to cleanse: Quake stuns 20%, Bolt burns 30%, Sting poisons
25%. The bond-aware picker weighs each bond per tier (`ScoutedPicker.BondWeights`) and gives the
cleanse bond nothing against an encounter that cannot afflict.

**4. Tiered targets.** `TargetClear` per shape in `encounter-library.json`: `squad` 80, `horde` 80,
`elite` 60, `solo` 50. `encounter-difficulty.json` schema 2 carries them; `--target-clear N` is the
legacy uniform target (the balance guard's setting), `shape=N` pairs override one shape. The level-gap
bands are now relative to the cell's target T: gap 0 T +/- 5; +2/+3 0.4 T-0.7 T; +5 under 0.2 T; and,
new, over-levelled -2/-3 at least T + 0.4 (100 - T), -5 at least T + 0.8 (100 - T).

**5. Retune** at the uniform 50% target, five seeds (as the guard is defined). Straight after the
swap the guard failed: `elemental` Thunderbird +7.1 and Leviathan -5.4 (normalized), 8/10 niches;
`neutral` Thunderbird +14.2 and Tarasque -7.9, 6/10 niches; stance means Skirmisher +3.8 / Vanguard
-1.8. Two structural causes, found by removing one bond at a time: `combined_arms` needs three stances,
and with only two Skirmishers in the roster every such team holds one, so its value lands on them
(without it the `elemental` stance means fell to Skirmisher +0.5, Ranged 0.0, Vanguard -0.2); and the Vanguards had lost
`shield_wall` and `bulwark` while the adjacent-only guardian fired 0.2 times a battle. Enemy statuses
were not the cause (the same content without them moved the two outliers by under a point: Thunderbird +6.5 -> +6.3, Leviathan -4.8 -> -4.2).

Reaction chances and powers first:

| Bond | Draft | Tuned |
| --- | --- | --- |
| `guardian` | 40% / 60%, adjacent, Shield 15 | 60% / 80%, within 2 hexes, healthiest Vanguard first, Shield 40 |
| `pack_hunters` | +6 crit, follow-up power 45 | +3 crit, power 25 |
| `crossfire` | 40% / 55%, power 40 | 45% / 60%, power 50 |
| `storm_front` | stun 40% | stun 30% |
| `bedrock` | below 50%, taunt within 2 for 2 turns | below 60%, within 3 for 3 turns |
| `winter_grove` | Shield 60 + Heal 20 | Shield 50 + Heal 15 |
| `twilight` | cleanse, 2 per member | cleanse + Heal 15, 3 per member |
| `combined_arms` | +15% Atk / SpA | +10% |

Beasts only where still outside the guard, crit chances (a user decision) and the six-stat budget
(600 +/- 5%) untouched: Thunderbird Atk 117 -> 108, SpA 108 -> 100; Leviathan HP 132 -> 138, Def
126 -> 120, SpD 100 -> 106; Golem Atk 109 -> 118, SpA 50 -> 41; Kirin SpA 140 -> 137, SpD 124 -> 118.

Picker weights were fitted to the panel's pooled `elemental` excess at 0.1 per point (never below 0):
`winter_grove` 0.34, `bedrock` 0.1, the rest 0.02 or less. At 0.25 per point the bond-aware pick leaned
on Winter Grove over better element matchups and trailed the plain heuristic; at 0.1 it leads it.

Result, five seeds at 50%:

| | `elemental` | `neutral` |
| --- | --- | --- |
| Normalized marginals (guard) | -3.1 … +3.8 (+/-4: all inside) | -5.7 … +5.7 (+/-7: all inside) |
| Top 3 in some shape (niches) | 9 of 10 (not Leviathan) | 9 of 10 (not Kirin) |
| Stance means (Ranged / Skirmisher / Vanguard) | +1.1 / -0.5 / -0.5 | -1.2 / +5.3 / -1.4 |
| Scouting uplift: heuristic / heuristic + bonds | +28.8 / +31.1 | -1.0 / +5.5 |
| Panel main-effect / interaction SD | 8.6 / 24.5 | 19.8 / 18.5 |

Reactions per battle of an active team and panel excess over the additive prediction (`elemental`,
five-seed means):

| Bond | Reactions / battle | Panel excess |
| --- | ---: | ---: |
| `guardian` | 0.62 | 0.0 |
| `pack_hunters` | 1.12 | +0.1 |
| `crossfire` | 0.88 | -0.1 |
| `wildfire` | 4.61 | +0.1 |
| `storm_front` | 1.26 | +0.2 |
| `bedrock` | 2.36 | +1.0 |
| `winter_grove` | 1.87 | +3.5 |
| `twilight` | 0.60 | +0.1 |
| `combined_arms` | 1.88 | 0.0 |

**The panel target was not met.** The design asked for the `elemental` main-effect SD to rise 1.5 or
the interaction SD 2 over the old bonds; both fell (11.0 -> 8.6, 28.1 -> 24.5). The drop is mostly the
guard retune compressing the beasts, and stronger bonds do not buy it back: a probe with every
reaction near its validator cap and stronger enemy statuses (guardian 80/100%, crossfire power 60,
wildfire 60% burns, winter grove below 50% for Shield 80 + Heal 30, combined arms +20%) broke the
guard (Treant +12.8, Golem -7.2) and still gave only 9.9 / 22.3, because the calibration raises the
difficulty with the picked team's strength. On this roster the lineup's spread comes from the
element chart and the beasts; the bonds mostly ride on their members (panel excess near 0 for every
bond but Winter Grove and Bedrock). Open for the lead: a bond-driven spread needs bonds whose value
depends on the composition far more sharply than these do, or a different yardstick.

**Shipping table** (the default run, seed 12345, at the tiered targets; the game reads `elemental`):

| Kit mode | Shape | Target | L1 | L50 | L100 |
| --- | --- | ---: | ---: | ---: | ---: |
| `elemental` | `solo` | 50 | x1.246 | x1.266 | x1.234 |
| `elemental` | `elite` | 60 | x1.094 | x1.141 | x1.117 |
| `elemental` | `squad` | 80 | x1.207 | x1.211 | x1.211 |
| `elemental` | `horde` | 80 | x1.250 | x1.219 | x1.238 |
| `neutral` | `solo` | 50 | x0.922 | x0.930 | x0.953 |
| `neutral` | `elite` | 60 | x0.844 | x0.848 | x0.848 |
| `neutral` | `squad` | 80 | x1.141 | x1.184 | x1.191 |
| `neutral` | `horde` | 80 | x1.188 | x1.250 | x1.242 |

Per shape, levels averaged (`elemental`): the scouted rate is on target (51.6 / 59.4 / 79.4 / 80.5);
the unscouted average team clears 7.1% of `solo`, 10.0% of `elite`, 58.1% of `squad` and 50.9% of
`horde`; the plain heuristic (no bonds) 50.0 / 66.7 / 70.8 / 62.5 (24 battles a shape, noisy); the
scouted team without its avatar 4.7 / 7.6 / 45.8 / 29.9. The avatar is worth 40-50 points on the
bosses: flagged for the producer with the unscouted rates.

Level gap (`docs/balance/level-gap-report.md`, `elemental`, all shapes, mean target 67.5%), scouted
rate at gaps -5, -3, -2, 0, +2, +3, +5, level 50: 94.1, 85.7, 80.1, 68.2, 31.3, 23.0, 13.5. Being
over-levelled makes every shape easier at every level; 25 of 35 all-shape cells sit inside their
bands. The misses: at +3 the scouted rate falls below 0.4 T at L10-L50 (17-23% against 27%: a
couple of levels under costs more than the band allows early on), and -2, -5 and +5 miss by 0.4-2.6
points at a few levels.

Reproduce: the guard, `dotnet run --project Tooling/BalanceSim -c Release -- --mode pve --seeds
12345,777,4242,2024,99 --target-clear 50 --panel 16x4 --out out/guard.md`; the shipping table and
report, `dotnet run --project Tooling/BalanceSim -c Release -- --panel 16x4 --avatar-value --out
docs/balance/tuned-report.md --write-difficulty content/data/Encounters/encounter-difficulty.json`;
the level gap, `dotnet run --project Tooling/BalanceSim -c Release -- --mode pve --levels
10,30,50,70,90 --level-gap -5,-3,-2,0,2,3,5 --gap-mix 0 --out docs/balance/level-gap-report.md`.

## Level-gap mix and the team suggester

No balance change: the roster, the skill library and the difficulty table are unchanged
(`encounter-difficulty.json` is byte-identical). Three user decisions:

1. **Balance is judged over a mix of level gaps** (`--gap-mix`, on by default; Tooling README,
   "Level-gap mix"). The per-beast marginals, niches, flags, element matchups, team composition and
   bond sections now read battles at enemy level minus team level -3 / -2 / -1 / 0 / +1 / +2 / +3
   with shares 5 / 10 / 15 / 40 / 15 / 10 / 5% (each every-team battle is dealt one gap in exact
   proportion; the gap-0 share is the calibration's own battles). The calibration, the table,
   scouting and the plumbing checks stay at gap 0, and `--gap-mix 0` reproduces the pre-mix report
   byte for byte. Cost: the default run 13 s -> 16 s, the tuned-report command about 30 s -> 33 s.
2. **Enemy elements are shown free before every fight** (the `Full` preview is the pre-fight
   screen; `docs/design/battle-system.md`, "Encounter preview"). The scouted calibration targets are
   kept.
3. **The bond-aware picker is the game's `TeamSuggester`** (Runtime, `BeastCraft.Battle.Scouting`):
   the simulator calls it, and every run checks that `Suggest` with the whole roster names the
   simulator's pick (32 of 32 compositions in the default run, 160 of 160 with `--compositions 40`,
   also with bonds off, a team of 3 with two Vanguards, and `dominant-element` detail). The game
   shows the suggestion only after three losses on the same battle and when the player has not
   turned it off (`TeamSuggestionPolicy`, `PlayerSettings`).

The guard under the mix (five seeds 12345 / 777 / 4242 / 2024 / 99, `--target-clear 50`; the gap-0
column is the same run's equal-level battles and reproduces "Behaviour bonds and tiered
difficulty"):

| | `elemental` mix | `elemental` gap 0 | `neutral` mix | `neutral` gap 0 |
| --- | --- | --- | --- | --- |
| Normalized marginals (guard +/-4 / +/-7) | -3.4 … +2.1 (all inside) | -3.1 … +3.8 | -4.2 … +5.2 (all inside) | -5.7 … +5.7 |
| Top 3 in some shape | 8 of 10 (not Kirin, Leviathan) | 9 of 10 (not Leviathan) | 8 of 10 (not Leviathan, Kirin) | 9 of 10 (not Kirin) |

The mix narrows the normalized spread in both modes and costs one niche per mode, each by less than
1.3 points: Kirin is 4th in `elemental` `elite` (+0.6 against Treant's +1.4) and
Leviathan 4th in `neutral` `elite` (+2.7 against Basilisk's +4.0). Retune attempts on the same five
seeds (scratch rosters, `--roster`), none adopted:

| Candidate | `elemental` mix | `neutral` mix | Gap 0 |
| --- | --- | --- | --- |
| Leviathan Def 120 -> 117, Speed 94 -> 97; Kirin HP 115 -> 121 | 8 / 10 (not Kirin, Leviathan) | 8 / 10 (not Treant, Kirin) | `elemental` 9 / 10, `neutral` 8 / 10 |
| Kirin HP 115 -> 125 | 8 / 10 (not Kirin, Leviathan) | 8 / 10 (not Kirin, Leviathan) | Kirin +4.1 normalized: outside the `elemental` guard |

(Three earlier candidates were run on a wrong seed set, 1 / 2 in place of 2024 / 99, and are not
counted: Leviathan's offence up and defences down made it worse, Leviathan Speed +6 for Defense -6
pushed Phoenix to -4.1.) The top-3 boundary is within the five-seed noise of the shape means (SD over
seeds 1-2 points): each change moved a niche from one beast to another rather than adding one. The
normalized guard holds under the mix in both modes; the niche count is flagged for the next balance
pass rather than chased here.

Reproduce: the guard, `dotnet run --project Tooling/BalanceSim -c Release -- --mode pve --seeds
12345,777,4242,2024,99 --target-clear 50 --out out/guard.md` (its tables carry the gap-0 column);
the shipping report and table, the unchanged command in the Tooling README.

## Region campaign: XP falloff, bench, level cap (`--mode campaign`)

The campaign-progression design (region node maps, level-gap falloff, bench XP, seals and the beast
level cap) is paced by a new Monte Carlo model, `--mode campaign`
([campaign-pacing-report.md](campaign-pacing-report.md)): the real save, `regions.json`, node maps,
`CampaignRules`, cap and XP code, with a tiered clear-chance model (squad / horde 80%, elite and
gates 60%, solo and bosses 50% at equal level). The design's numbers, applied literally, missed
several of its own gates; what changed and why:

| Knob | Design | Now | Why |
| --- | --- | --- | --- |
| Clear bonus, beast and avatar | `40 + 4 L` | `50 + 5 L` | The campaign clears ~70% of battles (harder elites, gates, bosses; losses retried), not the pacing model's 80%; at the old rate the team fell 1-2 levels behind each stage and into a loss spiral (1,074 battles with 11-row maps). |
| Bench share | 50% + 7.5% per level below, max 100% | 10% + 9% per level below, max 100% | The design predicted "reserves ~6-7 behind" assuming fielded beasts earn their full XP; under the falloff (~14% lost) and knockouts they earn ~70%, and the literal rule kept the bench 1-2 behind. **Pending lead/user review.** |
| Map rows | 14 (rest row 12, elites from 4) | 11 (rest row 9, elites from 3) | 14 rows gave 689 battles p50 (target 400-600) and the focus-skill gates missed (L20 at 376). |
| Gate level | row + 1 | row + 0 | An elite-tier gate one level up clears ~36%: ~3 attempts per gate. |
| r01 battle shapes | squad 60 / horde 40 | squad 45 / horde 40 / solo 15 | Solos drop the early shards the focus skill's L10 gate needs (86 battles, target 70-90). |

Result, 1,000 campaigns: 541 battles p50 (517-569); fielded team within 0.3 levels of every gate and
boss, avatar on it; bench 5.0-6.0 behind from region 3; recruit (level 1 at region 5) 7.7 behind at
the end of region 6; cap never exceeded; nothing banked at a seal (the cap never binds on the
content); grind probe 0.00 levels; focus skill 17 / 86 / 190 / 323 battles to L5 / 10 / 15 / 20.

`--mode pacing` moves only through the falloff and the clear bonus: its avatar and beast now track
one level above the encounter level (p10-p90 within one level), every other number unchanged.
`tuned-report.md` is byte-identical (the PvE simulation does not use progression; the ten DRAFT boss
templates are validated but not fought by the default run).

The boss templates' `DifficultyOverride`s were calibrated with the fixed set (`--encounter-set fixed
--kit elemental --calibrate-samples 64`, one boss at its level per run) to ~50% bond-aware scouted
clear: x1.180, x1.156, x1.203, x1.043, x0.992, x0.938, x0.803, x0.844, x0.934, x0.805 (r01-r10; r04
landed at 59% scouted, the bisection's closest step).

Reproduce: `dotnet run --project Tooling/BalanceSim -c Release -- --mode campaign --self-check --out
docs/balance/campaign-pacing-report.md` (about 2 s) and `-- --mode pacing --self-check --out
docs/balance/pacing-report.md`.

## Economy: gear budget, consumables, Trader prices (`--economy-probe`, `--mode campaign`)

The economy (gold, the Trader, gear, consumables, cosmetics; docs/design/economy-and-shop.md) was
tuned with two new tools: `--economy-probe` (every PvE cell replayed at its calibrated multiplier with
each gear profile and each consumable, in levels-equivalent against the team one level above the
enemies) and the economy model inside `--mode campaign`.

| Knob | Design | Now | Why |
| --- | --- | --- | --- |
| Gear budget | common 5% / rare 8% / epic 12% of a stat at every band | the same in band 1, scaled per band by `(T(11) / T(min + 10))^0.65` | A level adds less of a stat the higher it is: flat-5% commons measured 0.69 / 1.30 / 1.64 LE at L1 / 50 / 100 (target ~0.7); scaled: 0.69 / 0.74 / 0.69. |
| Epics | one per slot, mixed stats | one per piece (six per band from 41), focused | Mixed-stat epics measured below the rares (0.91 LE at L50); focused: 1.78. |
| Consumables | +10% stats, +8 crit, shield 25%, -10% enemy Speed, 30% DoT | +4% stats, +12 crit, -5% enemy Attack / SpecialAttack, 50% DoT 8; no speed, no shield | At most one per battle, each at or under ~0.3 LE (measured 0.17-0.26 mean, 0.20-0.39 at L50). Speed buffs / debuffs measured negative (-0.4 to -0.7 LE); a consumable shield displaced bond shields (negative). |
| Trader visits | every ~12 battles | a trading post (unchanged maps) plus a travelling trader at every camp: every ~9 battles | Trading posts alone were met every ~36 battles (1.5 per region); more trading posts in the maps pushed the recruit past its gate (fewer battles). |
| Prices | design units | x0.7 | Visits every ~9 battles bring ~7 price units each, not ~11: affordability p50 45% at the design's prices, 69% now (target 55-80%). |

Result, 1,000 campaigns: want-list affordability p50 69%; no visit without an affordable essential;
gold held at every boss 1.0-1.9 visits' income; gold earned 977 / 4,559 / 9,026 in regions 1 / 5 / 10,
49,953 over the campaign (+~15,000 from gear sales); focus skill 17 / 86 / 190 / 323; every earlier
campaign gate unchanged (545 battles p50). The multi-seed balance guard holds under `--gear rare`.
Typical gear raises the calibrated difficulty multipliers by 0.4-8.6% (not yet applied; see the
economy doc).

Reproduce: `dotnet run --project Tooling/BalanceSim -c Release -- --mode campaign --self-check --out
docs/balance/campaign-pacing-report.md`; `-- --mode pve --economy-probe --out <scratch>`; `-- --mode
pve --seeds 12345,777,4242 --gear rare --out <scratch>`.

### Pass rewards and gear prices (user decisions)

User decisions: the camp's travelling trader and prices x0.7 stay (approved); a stage pass's first
clear now guarantees a **common** of its band (from the drop pool; commons are not boss-tagged), and
only region lairs guarantee rare / epic gear. With passes granting rares, selling replaced gear was
23% of all gold (15,122 of ~65,000); the user target is 10-15%. Commons at passes alone brought it to
19% (11,647); gear prices then went from 4.2 / 10.5 units (common / rare; the design's x0.7) to
3 / 8 (x0.5), and the never-sold epic's sellback valuation from 21 to 10.5 units: **8,340 gold from
sales, 14% of all gold**. Every gate still met: affordability p50 63%, nothing-affordable visits 0%,
gold held at every boss 1.0-1.6 visits' income, focus skill 17 / 86 / 190 / 323, 545 battles; gold
earned unchanged (977 / 4,559 / 9,026 / 49,953). The report's "Per campaign" line now prints the
sales share.

## Boss re-calibration after the combat merge

The ten DRAFT boss templates' `DifficultyOverride`s (Region campaign, above) were calibrated on the
combat rules before behaviour bonds, enemy statuses (the giant's Quake stuns, the caster's Bolt and
the stingling's Sting burn / poison) and the tiered targets. Re-run on the merged rules, same recipe
(the fixed set, `--kit elemental`, bond-aware scouted pick, 64 samples per step, target 50%, one boss
at its level per run), with and without typical gear. The boss replaces the shape table's multiplier
at its node, so it ships on the table's assumption: **typical gear** (user decision for the shipping
table, below).

| Boss | Level | Before | Gearless | Typical gear (shipped) | Scouted clear at shipped |
| --- | ---: | ---: | ---: | ---: | ---: |
| r01 Hollow Warden | 10 | x1.180 | x1.270 | **x1.430** | 51.6% |
| r02 Ember Twins | 20 | x1.156 | x0.986 | **x1.031** | 46.9% |
| r03 Tide Colossus | 30 | x1.203 | x1.227 | **x1.219** | 51.6% |
| r04 Storm Titan | 40 | x1.043 | x1.066 | **x1.078** | 50.0% |
| r05 Rust Knights | 50 | x0.992 | x0.953 | **x0.963** | 51.6% |
| r06 Frost Matriarch | 60 | x0.938 | x1.031 | **x1.063** | 50.0% |
| r07 Thunder Court | 70 | x0.803 | x0.867 | **x0.883** | 48.4% |
| r08 Heart of the Deepwild | 80 | x0.844 | x1.023 | **x1.055** | 51.6% |
| r09 Cinder King | 90 | x0.934 | x0.867 | **x0.922** | 50.0% |
| r10 Apex Pair | 100 | x0.805 | x0.734 | **x0.863** | 50.0% |

Against the old overrides the gearless multipliers move -15% to +21%: most bosses got easier to
beat (a higher multiplier holds them at 50%; the most at r08, whose stinglings' poison the Twilight
bond cleanses), r02, r05, r09 and r10 harder. Typical gear then adds up to 17.6% (r10; r01 12.6%,
the rest under 7%; r03's typical multiplier is 0.7% under its gearless one, inside the noise: 64
samples per step put about +/-6 points on each scouted clear).

Reproduce: write each template as a fixed encounter (every group's enemy copied from
`enemy-library.json` with its `Count` and `Elements`, arena as authored; a scratch
`encounters.json`), then per boss `dotnet run --project Tooling/BalanceSim -c Release -- --mode pve
--kit elemental --encounter-set fixed --encounters-file <scratch> --encounters <id> --levels <level>
--calibrate-samples 64 --scouted bonds --gear typical` (drop `--gear typical` for the gearless
column); the "Difficulty" row's multiplier is the override. `--mode campaign` is unchanged (it models
bosses at a flat 50%).

## Shipping difficulty table in typical gear

User decision: the shipping `encounter-difficulty.json` assumes the gear a player normally wears
(`--gear typical`: commons in band 1-20, then rare and epic pieces by band; see the economy doc). The
committed tuned report stays gearless (the per-beast guard's setting), so the two now come from two
commands, and the table's `_readme` names its gear:

- report: `dotnet run --project Tooling/BalanceSim -c Release -- --panel 16x4 --avatar-value --out
  docs/balance/tuned-report.md` (unchanged bytes);
- table: `dotnet run --project Tooling/BalanceSim -c Release -- --panel 16x4 --avatar-value --gear
  typical --write-difficulty content/data/Encounters/encounter-difficulty.json`
  (the panel and the avatar-value replay do not touch the calibration; `--mode pve --gear typical
  --write-difficulty <path>` writes the same bytes in half the time).

| `elemental` | L1 | L50 | L100 |
| --- | ---: | ---: | ---: |
| `solo` (50%) | x1.246 -> x1.297 (+4.1%) | x1.266 -> x1.273 (+0.6%) | x1.234 -> x1.297 (+5.1%) |
| `elite` (60%) | x1.094 -> x1.141 (+4.3%) | x1.141 -> x1.156 (+1.4%) | x1.117 -> x1.156 (+3.5%) |
| `squad` (80%) | x1.207 -> x1.250 (+3.6%) | x1.211 -> x1.211 (0.0%) | x1.211 -> x1.223 (+1.0%) |
| `horde` (80%) | x1.250 -> x1.313 (+5.0%) | x1.219 -> x1.250 (+2.6%) | x1.238 -> x1.188 (-4.1%) |

Typical gear lets the enemies be up to 5% stronger at the same scouted clear, the size the economy
probe predicted (0.4-8.6% before the combat merge). The one decrease, `horde` at level 100, is not
explained by the gear: the picked team clears 82.0% there at x1.188 in gear and 81.3% at x1.238
without, so the geared calibration landed a step lower on 128 battles per search step (+/-3.5 points
at 80%); flagged for the next balance pass rather than hand-edited (the file is written, never
edited). The `neutral` cells move the same way (0-5.5%, `horde` L100 -4.4%).

## Idle (AFK) rewards (`--mode campaign`)

New system (lead-approved design; user decisions: 8-hour cap, idle at most ~15% of gold and materials
and ~10% of beast XP over a campaign, looks from the battle-drop pool, XP to the party and the bench
through the catch-up rule, clock tampering clamped silently). `idle-rewards.json` rates were tuned in
the campaign model with the game's `IdleRewardCalculator` claiming on the model's save at the default
cadence: 25 battles a day, away 16 hours a day, two claims a day (8 hours each, exactly the cap).

Shape of the rates: 10 bands of 10 progress levels; gold/hour = `kG x (10 + 2L)` and XP/hour =
`kX x (34 + 2.8L)` at each band's middle level (the squad gold curve; a standing beast's clear XP net of
losses); one material roll an hour of the `squad` cell at `m` x its chances; looks 0.08% per full claim.

| kG | kX | m | Idle gold | Idle materials | Idle XP | Want-list affordability p50 | Verdict |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 0.25 | 0.13 | 0.25 | 13.2% | 9.0% | 6.4% | 100% | **MISS** (affordability) |
| 0.20 | 0.13 | 0.25 | 10.9% | | | 100% | **MISS** |
| 0.15 | 0.13 | 0.25 | 8.4% | | | 100% | **MISS** |
| 0.12 | 0.13 | 0.25 | 6.9% | | | 100% | **MISS** |
| 0.11 | 0.13 | 0.25 | 6.4% | | | 75% | ok |
| 0.10 | 0.13 | 0.35 | 5.8% | 12.1% | 6.4% | | ok |
| 0.10 | 0.18 | 0.35 | 5.7% | 12.0% | 8.5% | | ok |
| 0.10 | 0.21 | 0.45 | 5.4% | 14.9% | 9.8% (p90 10.3%) | 76% | ok (too close) |
| **0.10** | **0.20** | **0.40** | **5.6%** | **13.4%** (p90 16.8%) | **9.3%** (p90 9.8%) | **74%** | **ok (shipped)** |
| 0 (no idle gold) | 0.13 | 0.25 | 0% | | | 64% | ok |

**Finding: the Trader caps idle gold, not the 15% ceiling.** The want-list affordability gate (p50
55-80%; 63% without idle) flips to 100% once idle gold passes about 6.5% of all gold: the per-visit
distribution is bimodal, and a little more gold makes most visits fully affordable. Idle gold ships at
0.1 x G(L) (5.6%); reaching ~15% needs higher Trader prices or another gold sink first (producer item).
XP and materials sit just under their ceilings.

Effect on the campaign (every gate still met): battles 545 -> 507 p50 (idle XP keeps the team a
fraction of a level ahead, so fewer losses: ~17 -> ~13 retries a region); fielded and avatar within 1
of every gate and boss; the falloff's cut of fielded battle XP 13.7% -> 22.3%; bench 5.0-5.7 behind
from region 3 (was 5.0-6.0; r03, r04 and r10 sit exactly at the 5.0 floor); recruit 7.7 -> 6.3 behind;
gold held at a boss 1.1-1.7 visits' income (was 1.0-1.6); focus skill L10 86 -> 77, L15 190 -> 171,
L20 323 -> 297 battles (idle materials). The cap never binds (banked levels at a seal 0; the falloff
keyed on the progress level stops idle XP first). With `--idle-hours-per-day 0` the report is
byte-identical to the pre-idle one apart from an "Idle rewards: off" line and section.

Cadence sensitivity (shipped rates): 15 battles a day -> idle 8.8% of gold, 20.2% of materials, 14.4%
of XP (over the ceilings, and affordability 100%); 40 a day -> 3.7%, 8.9%, 6.2%; one claim a day (16
hours away, capped at 8) -> 3.0%, 7.4%, 5.0%; four claims a day -> 5.6%, 13.5%, 9.2% (claiming more
often gains nothing). The ceilings hold for the modelled player, not for a light one; a producer call.

Reproduce: `dotnet run --project Tooling/BalanceSim -c Release -- --mode campaign --self-check --out
docs/balance/campaign-pacing-report.md` (add `--battles-per-day`, `--idle-hours-per-day`,
`--idle-claims-per-day` for the sensitivity rows; the kG / kX / m rows by editing `idle-rewards.json`).

### Avatar idle XP (user decision)

The avatar now earns idle XP at the party's rate, through the falloff on its own level against the
progress level, with no cap. Campaign re-run: the beasts' shares are unchanged (gold 5.6%, materials
13.4%, beast XP 9.3%, p50); the avatar's idle XP is 8.7% of its XP; its p50 level on arriving at each
stage-2 pass moves from the node level to one above (within 1 everywhere, target +/-3); the falloff's
cut of the avatar's battle XP rises 14.7% -> 21.7%. Every gate is still met. User decisions recorded
with it: a lighter player's larger idle share is accepted, and paying at the progress level at claim
time is kept; both rely on local-only play (docs/design/progression-and-saves.md, "Idle rewards").

## Post-game region r11 (Duskmeridian): Normal and Hard calibration

The post-game region r11 (DRAFT; battle-system.md, "Post-game region") is fought at a flat level 100
on Normal or Hard. Its six post-game shapes copy the mainline recipes with lower targets (Normal:
squad / horde 65%, elite 45%; Hard: 50%, 50%, 30%) and are calibrated by the shipping table's command
(`--panel 16x4 --avatar-value --gear typical --write-difficulty`) at level 100 only, each on its
mainline shape's own compositions (a first run drew eight fresh lineups per post-game shape and put
the Hard squad and horde *below* Normal, x1.277 < x1.313 and x1.094 < x1.207: eight lineups are too
few to order two nearby targets; on one shared set the three targets sit on one curve). Their cells
are appended after the mainline cells; the mainline cells, targets and `_readme` are byte-identical
(the diff adds a JSON comma to the last mainline target and cell), and `tuned-report.md` is
byte-identical (the report run never sees a post-game shape).

| Shape (level 100) | Mainline | Normal | scouted | Hard | scouted |
| --- | ---: | ---: | ---: | ---: | ---: |
| squad, elemental | x1.223 | x1.266 | 65.6% | x1.359 | 50.0% |
| horde, elemental | x1.188 | x1.242 | 64.1% | x1.281 | 51.6% |
| elite, elemental | x1.156 | x1.219 | 45.3% | x1.242 | 30.5% |
| squad, neutral | x1.207 | x1.246 | 63.3% | x1.281 | 50.0% |
| horde, neutral | x1.188 | x1.227 | 64.8% | x1.262 | 48.4% |
| elite, neutral | x0.867 | x0.904 | 44.5% | x0.934 | 29.7% |

**Boss.** The twin giants (Light + Dark, the r10 apex pair's structure) with the boss recipe above
(fixed set, `--kit elemental --scouted bonds --gear typical`, level 100), both targets on the Normal
template's battles (the Hard template fields the same lineup, so one seed stream gives one curve; the
first try on each template's own seeds put Hard at x0.723 and Normal at x0.711, near-equal, 18.8% vs
35.9%). The recipe reproduces r10's shipped x0.863 at 50.0% exactly. The curve has a cliff: at 256
samples a step, x0.703 51.6%, x0.711 41.0%, x0.713 28.9%, x0.715 27.3%, x0.719 27.7%, x0.723 18.4%,
x0.750 3.1%.

| Boss | Target | Shipped | Scouted at 64 samples | Scouted at 256 samples |
| --- | ---: | ---: | ---: | ---: |
| boss_r11_dusk_and_dawn (Normal) | 35% | **x0.711** | 35.9% | 41.0% |
| boss_r11_dusk_and_dawn_hard (Hard) | 20% | **x0.723** | 25.0% (its 64-sample search stopped at x0.719) | 18.4% |

Pacing (`--mode campaign`, the appended "Post-game" section; clear chance by target at gap 0):
Normal 56.3% of battles cleared, 66 battles to clear r11 (p50; 56-77), 2 boss attempts (p50); Hard
40.8%, 90 battles (75-108), 3 boss attempts. The mainline report is byte-identical above it.

Reproduce: the table command above; the boss rows as in "Boss re-calibration after the combat merge"
with `--encounters boss_r11_dusk_and_dawn --levels 100 --target-clear 35` (and `20`), `--calibrate-samples
64` (and `256`); `-- --mode campaign --self-check --out docs/balance/campaign-pacing-report.md`.

## Rectangular arenas: shape effect

Producer decision: the arenas become portrait-fit rectangles of pointy-top hexes (an odd-r offset
rectangle; battle-system.md, "Arenas"): Small 5 x 7 (35 tiles; the radius-3 hexagon had 37), Medium
8 x 11 (88; 91), Large 11 x 15 (165; 169). Deployment stays 2 / 3 / 4 rows deep (10 / 24 / 44 tiles a
side, was 9 / 21 / 38) with a 3 / 5 / 7-row neutral band between. Content uses Medium (`solo`,
`elite`, `squad` and their post-game copies; bosses r01-r03) and Large (`horde`s; bosses r04-r11);
nothing is fought on Small. This section measures what the shape alone does, at the **old**
calibration; the next re-calibrates.

**Method.** `--pin-difficulty` (new; BalanceSim README) fights every cell at a given table's
multipliers with no calibration search. The tables are main's (a360358), each written by main's own
run with `--write-difficulty`: the gearless report's (`--panel 16x4 --avatar-value`), each guard
seed's (`--mode pve --seed N --target-clear 50`), and the shipping table itself for typical gear.
Pinned to its own table, main reproduces its report byte for byte apart from the header line and the
"Evaluations" column, so every difference below is the arena. The battle seeds are the same on both
sides, but a battle whose units move differently plays out differently, so each cell keeps its
sampling noise (128 picked-team battles, about +/-4 points).

**Medium barely moves; Large hordes get easier.** Each side's front row is the very row the hexagon
had on both presets (Medium 8 tiles, Large 11, the same screen columns), so a lineup that fits the
front row deploys on the same tiles as before. What changed is the middle: the hexagons' centre rows
were 11 (Medium) and 15 (Large) tiles wide, the rectangles' are 8 and 11. A squad or a boss fight
rarely spreads that wide; a 16-24-swarm horde does, and on the narrower Large board it surrounds
fewer beasts at once.

Clear rate at the old multipliers, levels 1 / 50 / 100 averaged; scouted = the bond-aware pick (the
calibrated number), no scouting = every team:

| Kit mode | Shape | Arena | Scouted (gearless, report) | No scouting | Scouted (typical gear, shipping table) |
| --- | --- | --- | ---: | ---: | ---: |
| `elemental` | `solo` | Medium | 51.6 -> 49.7 (-1.8) | 7.1 -> 6.9 (-0.2) | 49.7 -> 49.5 (-0.3) |
| `elemental` | `elite` | Medium | 59.4 -> 59.4 (0.0) | 10.0 -> 10.3 (+0.2) | 58.9 -> 59.1 (+0.3) |
| `elemental` | `squad` | Medium | 79.4 -> 79.2 (-0.2) | 58.1 -> 58.5 (+0.3) | 79.2 -> 79.2 (0.0) |
| `elemental` | `horde` | Large | 80.5 -> **84.9 (+4.4)** | 50.9 -> 55.0 (+4.1) | 80.2 -> **85.2 (+5.0)** |
| `neutral` | `solo` | Medium | 50.5 -> 45.3 (-5.2) | 40.6 -> 40.0 (-0.7) | 51.6 -> 49.2 (-2.4) |
| `neutral` | `elite` | Medium | 59.1 -> 56.5 (-2.6) | 59.6 -> 59.4 (-0.2) | 62.8 -> 62.2 (-0.6) |
| `neutral` | `squad` | Medium | 79.4 -> 79.4 (0.0) | 75.0 -> 75.1 (+0.1) | 80.5 -> 80.5 (0.0) |
| `neutral` | `horde` | Large | 79.4 -> **83.9 (+4.5)** | 57.8 -> 65.1 (+7.3) | 79.4 -> **83.9 (+4.4)** |

The post-game cells (level 100, typical gear, the shipping table's multipliers): `horde_postgame`
64.1 -> **76.6%** (target 65) and `horde_postgame_hard` 51.6 -> **58.6%** (target 50) `elemental`
(`neutral` 64.8 -> 78.1 and 48.4 -> 71.9); every `squad` and `elite` post-game cell within 1.6 points
of main. The widest single mainline cells: `elemental` `horde` L1 78.9 -> 89.1, `neutral` `horde`
L100 77.3 -> 89.1 and `neutral` `solo` L100 51.6 -> 40.6 (gearless).

**The per-beast guard does not move.** Five seeds (12345 / 777 / 4242 / 2024 / 99), `--target-clear
50`, over the level-gap mix, each seed pinned to its own main table
(`--pin-difficulty "<dir>/main-guard-pin-{seed}.json"`):

| Beast | `elemental` normalized, main -> rect | `neutral` normalized, main -> rect |
| --- | ---: | ---: |
| Kirin | +2.0 -> +2.3 (+0.3) | -1.1 -> -1.0 (+0.1) |
| Treant | +2.1 -> +2.2 (+0.1) | -0.5 -> -0.9 (-0.4) |
| Basilisk | +1.7 -> +1.3 (-0.4) | +0.6 -> +0.5 (-0.1) |
| Thunderbird | +0.8 -> +1.0 (+0.2) | +5.2 -> +5.4 (+0.2) |
| Griffin | +0.4 -> +0.5 (+0.1) | +3.8 -> +4.4 (+0.6) |
| Golem | +0.5 -> +0.4 (-0.1) | +1.0 -> +1.3 (+0.3) |
| Frost Wyrm | -0.9 -> -1.0 (-0.1) | -0.3 -> -0.8 (-0.5) |
| Tarasque | -1.2 -> -1.2 (0.0) | -3.6 -> -3.6 (0.0) |
| Leviathan | -1.9 -> -2.1 (-0.2) | -0.8 -> -0.7 (+0.1) |
| Phoenix | -3.4 -> -3.4 (0.0) | -4.2 -> -4.5 (-0.3) |

- `elemental`: -3.4 … +2.1 -> -3.4 … +2.3, every beast inside +/-4; top 3 in some shape 8 of 10 ->
  8 of 10 (the same two out: Kirin, Leviathan); gap 0 -3.1 … +3.8 -> -3.4 … +3.9.
- `neutral`: -4.2 … +5.2 -> -4.5 … +5.4, inside +/-7; top 3 in some shape 8 of 10 -> 7 of 10 (Phoenix
  drops to 4th in `squad`, +2.2 against Griffin's +2.3); gap 0 -5.7 … +5.7 -> -5.5 … +6.1.
- Guard calibration at 50% (seed means): `horde` scouted 50.2 -> 54.2 `elemental`, 48.7 -> 55.5
  `neutral`; `solo` 50.4 -> 49.1 and 49.4 -> 46.8; `elite` and `squad` within 0.8.

Every beast moves by 0.6 points or less, well inside the five-seed noise (SD 0.4-4.3). The shape is a
difficulty change, concentrated in the Large hordes, not a balance change: re-calibrating the table is
enough, and no beast needs retuning.

Reproduce: on main, `-- --panel 16x4 --avatar-value --write-difficulty <dir>/main-tuned-pin.json`
and, per seed, `-- --mode pve --seed N --target-clear 50 --write-difficulty
<dir>/main-guard-pin-N.json`; then on the rectangles `-- --panel 16x4 --avatar-value --pin-difficulty
<dir>/main-tuned-pin.json`, `-- --mode pve --gear typical --pin-difficulty
content/data/Encounters/encounter-difficulty.json --write-difficulty <scratch>` (at main's shipping
table; the post-game rates are on stderr) and `-- --mode pve --seeds 12345,777,4242,2024,99
--target-clear 50 --pin-difficulty "<dir>/main-guard-pin-{seed}.json"`.

## Rectangular arenas: re-calibration

The documented commands on the rectangular arenas, no roster, skill or content change: the shipping
table (`--panel 16x4 --avatar-value --gear typical --write-difficulty
content/data/Encounters/encounter-difficulty.json`, post-game cells included), the gearless
`tuned-report.md`, `level-gap-report.md` (`--gap-mix 0`), `pacing-report.md` and
`campaign-pacing-report.md` (both byte-identical: neither fights a battle), and the guard (five
seeds, `--target-clear 50`).

**Shipping table** (typical gear; the game reads `elemental`). Only the Large hordes move, as the
shape-effect run predicted; every Medium cell keeps its multiplier bar four `neutral` ones (one
bisection step each; the game does not read `neutral`):

| Cell | Target | Old | New | Scouted at new |
| --- | ---: | ---: | ---: | ---: |
| `elemental` `horde` L1 | 80 | x1.313 | **x1.359** (+3.6%) | 85.2% |
| `elemental` `horde` L50 | 80 | x1.250 | **x1.266** (+1.3%) | 79.7% |
| `elemental` `horde` L100 | 80 | x1.188 | x1.188 | 80.5% |
| `elemental` `horde_postgame` L100 (Normal) | 65 | x1.242 | **x1.258** (+1.3%) | 63.3% |
| `elemental` `horde_postgame_hard` L100 (Hard) | 50 | x1.281 | **x1.344** (+4.9%) | 48.4% |
| `neutral` `horde` L50 / L100 | 80 | x1.250 / x1.188 | x1.273 / x1.219 | 80.5% / 78.9% |
| `neutral` `horde_postgame` / `_hard` | 65 / 50 | x1.227 / x1.262 | x1.266 / x1.313 | 64.8% / 50.8% |
| `neutral` `solo` L50, `elite` L50, `squad_postgame_hard`, `elite_postgame_hard` | | x0.980, x0.887, x1.281, x0.934 | x0.977, x0.885, x1.273, x0.938 | 49.2%, 59.4%, 50.0%, 29.7% |

Every other cell, `elemental` `solo` / `elite` / `squad` and their post-game copies included, is
unchanged. The hordes needed only 1-5% more because the curve is steep there: at level 100 the old
x1.188 still clears 80.5%. **Targets:** every mainline cell's scouted clear is within 5 points of its
tier (squad / horde 80, elite 60, solo 50) except `elemental` `horde` L1 at 85.2%, the search's
closest evaluated step (128 battles a step, +/-3.5 points at 80%; main's `horde` L100 landed a
step off the same way, "Shipping difficulty table in typical gear"), flagged, not hand-edited; every
post-game cell within 1.7 points of its Normal / Hard target (Normal 65 / 65 / 45: 65.6, 63.3,
45.3%; Hard 50 / 50 / 30: 50.0, 48.4, 30.5%, `elemental`).

**The committed report** (gearless, tiered targets): multipliers move by at most one or two search
steps (`elemental` `horde` x1.250 / x1.219 / x1.238 -> x1.313 / x1.238 / x1.242; `solo` L50 x1.266
-> x1.250; `squad` L1 x1.207 -> x1.219; `neutral` `horde` L50 / L100 +0.6%, `solo` L100 -1.6%,
`elite` L1 / L100 -0.5%), and every cell's scouted clear is within 5 points of its target (widest:
`neutral` `solo` L1 45.3%, `neutral` `elite` L1 63.3%, `elemental` `horde` L1 82.8%).

**Guard** (five seeds, `--target-clear 50`, over the level-gap mix), main -> rectangles:

| Beast | `elemental` normalized | `neutral` normalized |
| --- | ---: | ---: |
| Treant | +2.1 -> +2.0 | -0.5 -> -0.8 |
| Kirin | +2.0 -> +1.8 | -1.1 -> -0.7 |
| Basilisk | +1.7 -> +1.3 | +0.6 -> +0.6 |
| Thunderbird | +0.8 -> +1.1 | +5.2 -> +5.3 |
| Griffin | +0.4 -> +0.9 | +3.8 -> +4.4 |
| Golem | +0.5 -> +0.6 | +1.0 -> +1.1 |
| Tarasque | -1.2 -> -1.1 | -3.6 -> -3.9 |
| Frost Wyrm | -0.9 -> -1.3 | -0.3 -> -1.0 |
| Leviathan | -1.9 -> -2.3 | -0.8 -> -0.8 |
| Phoenix | -3.4 -> -3.1 | -4.2 -> -4.4 |

- `elemental`: **-3.1 … +2.0**, every beast inside +/-4 (was -3.4 … +2.1); top 3 in some shape
  **8 of 10**, the same two out as on main (Kirin, Leviathan); gap 0 -3.0 … +3.4, 9 of 10.
- `neutral`: **-4.4 … +5.3**, inside +/-7 (was -4.2 … +5.2); top 3 in some shape **8 of 10** (was 8):
  Kirin now takes `horde` 3rd (+2.4) and Treant drops to 4th there (+1.9), so the two out are
  Leviathan and Treant (were Leviathan and Kirin); gap 0 -5.8 … +5.7, 8 of 10 (was 9).
- The top 3 of every shape is the same three beasts as on main in `elemental` (`solo` Griffin,
  Treant, Basilisk; `elite` Golem, Treant, Basilisk; `squad` Thunderbird, Tarasque, Phoenix; `horde`
  Frost Wyrm, Treant, Thunderbird; only the order within a shape moves) and in `neutral` but for
  `horde`'s third place (Kirin for Treant).
- Every beast moves by 0.7 normalized points or less; no beast retune.

**Level gap** (`level-gap-report.md`, all-shape cells meeting their band): `elemental` 25 -> 24 of
35 (L70 -2 now 79.1% against at least 80.5, and +3 at L70 / L90 about 1-3 points under 0.4 T, while L70
and L90 +5 now meet theirs), `neutral` 27 -> 27; shape cells 90 -> 87 and 106 -> 104 of 140. The
same misses as before (a couple of levels under costs more than the band allows; over-levelled
`elemental` falls just short of T + 0.4 (100 - T) at -2), not new ones in kind.

**Boss overrides** (the templates' `DifficultyOverride`s are not in the table; recipe as in "Boss
re-calibration after the combat merge": fixed set, `--kit elemental --scouted bonds --gear typical
--calibrate-samples 64`, target 50%, r11 35% / 20%). Run on main the recipe reproduces all twelve
shipped overrides exactly; on the rectangles ten land on the same multiplier and two a step lower,
r01 Hollow Warden x1.430 -> x1.414 (-1.1%) and r06 Frost Matriarch x1.063 -> x1.047 (-1.5%), inside
the recipe's step and noise (64 samples, about +/-6 points). A boss fight is decided on the front
rows, which did not change. The overrides are kept.

Reproduce: the commands above, as in the Tooling README (the table, the report, `--mode pve --levels
10,30,50,70,90 --level-gap -5,-3,-2,0,2,3,5 --gap-mix 0`, `--mode pacing --self-check`, `--mode
campaign --self-check`); the guard `-- --mode pve --seeds 12345,777,4242,2024,99 --target-clear 50`;
the bosses per template `-- --mode pve --kit elemental --encounter-set fixed --encounters-file
<scratch> --encounters <id> --levels <level> --calibrate-samples 64 --scouted bonds --gear typical
--target-clear <50|35|20>`, the scratch file being the templates written as fixed encounters.
