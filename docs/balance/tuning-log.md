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
