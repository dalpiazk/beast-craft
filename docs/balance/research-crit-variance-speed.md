# Research: damage variance, critical hits, ATB speed (2026-09-22)

Desk research gathered to inform Beast Craft's variance/crit design and the ATB speed retune. Numbers are as reported by the cited sources; items marked UNVERIFIED could not be confirmed.

## 1. Damage variance
- Pokémon: uniform integer roll 85–100 (÷100), applied last; 16 rolls, 15% max spread. Sources: https://pokekipe.com/guides/mechanics/damage-formula , https://www.poketools.com/damage-calculator
- Fire Emblem: no damage roll; damage deterministic, only hit%/crit% random. https://fireemblem.fandom.com/wiki/Battle_Formulas
- Darkest Dungeon: per-skill fixed min–max damage range; crit = 1.5× the range max plus status effects. https://darkestdungeon.wiki.gg/wiki/Combat_Mechanics_(Darkest_Dungeon) , https://darkestdungeon.fandom.com/wiki/Critical_Hit
- XCOM 2: small discrete damage range per weapon plus hit/miss; crit is a flat bonus gated by flanking (+40% crit chance). https://xcom.fandom.com/wiki/Critical_hit_(XCOM_2) , https://xcom.fandom.com/wiki/Flanking_(XCOM_2)
- Baldur's Gate 3 / 5e: dice-based damage; crit doubles dice — much higher relative variance. https://baldursgate3.wiki.fextralife.com/Critical+Hits , https://bg3.wiki/wiki/Dice_rolls
- Divinity: Original Sin 2: variance from a multiplicative bonus stack on weapon ranges. https://divinityoriginalsin2.wiki.fextralife.com/Damage
- Summoners War / Epic Seven (closest genre peers): no continuous damage roll; randomness comes from crit only. https://summonerswar.fandom.com/wiki/Equations
- Perception: negativity bias makes "fair" RNG feel unfair (XCOM 2 quietly boosts hit% after misses); narrow variance keeps lethal-threshold planning meaningful. https://www.timetoloot.com/gaming/rng-vs-player-agency/ , https://gameplayask.blog/are-xcom-hit-percentages-accurate

## 2. Critical hits
| Game | Base crit chance | Crit multiplier | Crit damage its own stat? | Cap |
|---|---|---|---|---|
| Pokémon (gen 6+) | 4.17% (1/24) | 1.5× | No (crit stages) | stage-limited |
| Fire Emblem | from stats | 3× | No | situational |
| Darkest Dungeon | weapon + skill, additive | 1.5× of max + effects | No | not explicit |
| Summoners War | 15% | +50% | Yes | 100% crit rate |
| Epic Seven | substat | substat | Yes | 100% |
| Genshin Impact | 5% | +50% | Yes ("1:2 CR:CD" gearing) | itemization |
| Diablo | ~5%, additive | 1.5× + additive bonuses | Yes | itemization |
| XCOM 2 | 0% + flanking | flat bonus | N/A | situational |

Sources: https://bulbapedia.bulbagarden.net/wiki/Critical_hit , https://summonerswar.fandom.com/wiki/Critical_Rate , https://www.sezgaming.com/post/epic-seven-stats , https://game8.co/games/Genshin-Impact/archives/318629 , https://diablo.fandom.com/wiki/Critical_Hit

Anti-snowball patterns: hard 100% crit-rate cap; crit damage priced about 2× crit rate per point in itemization (Genshin); crit resistance is uncommon.

## 3. Expected value and sample size
- Average damage multiplier from crits = 1 + CR × (CM − 1). At CR 15% and CM 1.5, that is 1.075 (+7.5% average damage).
- Standard error ∝ 1/√n. A few thousand simulated fights per matchup resolves means to about 1%. Uniform variance adds modestly to crit variance. (Generic statistics; no published game-studio methodology found.) https://medium.com/@sorellanamontini/sample-size-estimation-and-monte-carlo-simulations-e2ff4783664a

## 4. ATB / turn-meter speed
- Summoners War: speed widely considered dominant; no official cap. A community-proposed diminishing-returns rule exists but is UNVERIFIED as an official implementation. https://forum.com2us.com/forum/main-forum/summoner-s-war/general-ab/1626675-how-to-counter-speed-meta-in-arena , https://summonerswar.fandom.com/wiki/Attack_Bar
- SW/E7 mitigate with turn-meter push/pull skills and speed tuning, not caps.
- Final Fantasy X CTB: action rank (recovery delay) matters as much as Agility; Haste/Slow scale delay. https://finalfantasy.fandom.com/wiki/Final_Fantasy_X_battle_system
- Slow tanks stay valuable via taunt/threat, damage reduction and HP — not speed. https://blizzardwatch.com/2018/09/25/taunt-threat-inevitability-disaster/
- No source gives a target slowest:fastest speed ratio — UNVERIFIED; treat as a design call validated by simulation.

## 5. Decisions taken for Beast Craft
- Damage variance: uniform 90–110% (tighter than Pokémon; preserves lethal-threshold planning).
- Crit multiplier 1.5×; per-beast base crit chance (assassins/glass cannons ~10–15%, tanks ~2–5%); crit chance capped 0–100%; raisable by skills (BuffStat) and gear (StatModifier).
- Crit Damage as its own stat: deferred; if added, gear/skill-only and priced ~2× crit chance per point.
- Speed: target ~2.5–3× slowest:fastest, validated in the simulator. Consider FFX-style heavier-skill gauge costs once skills are authored.
- Tank value: taunt/threat with authored skills (planned), not speed.
