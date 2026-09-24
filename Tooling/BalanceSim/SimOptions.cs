using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Battle.Scouting;
using BeastCraft.Creatures;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>Which element the standard kit is authored in for a run.</summary>
    public enum KitMode
    {
        /// <summary>Every kit skill carries the unit's own (first) element.</summary>
        Elemental = 0,

        /// <summary>Every kit skill is <c>Element.None</c>: isolates stat distribution from the chart.</summary>
        Neutral = 1
    }

    /// <summary>Which skills the beasts fight with (<c>--skill-kit standard|library</c>).</summary>
    public enum KitSource
    {
        /// <summary>The standard kit (<see cref="Kit.BuildBeastKit"/>): every beast the same, so stats are what is measured.</summary>
        Standard = 0,

        /// <summary>Each beast's authored default loadout from <c>skill-library.json</c> (<see cref="SkillLibraryKits"/>). The default: the real game setup.</summary>
        Library = 1
    }

    /// <summary>What the PvE difficulty calibration aims at the target clear rate (<c>--calibrate-on</c>).</summary>
    public enum CalibrationTarget
    {
        /// <summary>The mean over every team (the unscouted player): the calibration before scouting existed.</summary>
        Mean = 0,

        /// <summary>The team the element counter-pick heuristic fields per composition (<see cref="ScoutedPicker.Pick"/>).</summary>
        Heuristic = 1,

        /// <summary>The team the bond-aware picker fields per composition (<see cref="ScoutedPicker.PickWithBonds"/>); the default.</summary>
        Bonds = 2
    }

    /// <summary>
    /// Every tunable the simulator has, in one place. The constants are the defaults; the CLI
    /// overrides the subset exposed by <see cref="Parse"/>.
    /// </summary>
    public class SimOptions
    {
        // ------------------------------------------------------------------------------------
        // Standard beast kit. The roster has no authored skills yet, so every beast fights with
        // this; every beast has the same kit so the stat lines are what gets measured.
        //
        // Fairness between Attack and SpecialAttack (the baseline's bias: Strike at cooldown 1
        // against Blast at cooldown 2 weighted Attack about twice as heavily):
        //   - Strike (Physical) and Blast (Special) have the same cooldown, 1, so once a beast is
        //     in melee both fire every turn.
        //   - Range is the one asymmetry the brief fixes (Strike 1, Blast 3): Strike needs the
        //     beast to reach a free tile next to its target, so it fires less often — while
        //     closing, and when the target is crowded. A Ranged beast never walks into melee, so
        //     it carries Shot (Physical, range 3, Blast's cooldown) instead of Strike; before Shot,
        //     Ranged beasts only fired Strike at an enemy already adjacent and the physical share
        //     fell to 37-47%. Each physical skill's power is Blast's divided by how often it fires
        //     relative to Blast in its stances, measured over the default PvE run (generated
        //     compositions, 3 levels, both kit modes) after the sqrt-speed / mitigation-formula
        //     change, at Strike 97 / Shot 70: Strike fired 0.71-0.72x as often as Blast for Vanguard
        //     beasts and 0.81-0.82x for Skirmisher beasts (0.733x pooled over both, weighted by
        //     fires), Shot 0.96x for Ranged beasts (it fires after Blast and loses the odd target
        //     Blast just felled). So Strike is 93 (= 68 / 0.733) and Shot 70 (= 68 / 0.965): fires x
        //     power, and with it the weight of Attack vs SpecialAttack, is even per stance. One
        //     Strike serves two stances, so Vanguards sit a little under 50% and Skirmishers a little
        //     over (they reach melee more often since the speed band was compressed). (Under the A / (A + D) formula damage is
        //     exactly proportional to power, with no +2 offset, so power ratios are damage ratios.) The report's kit parity table shows the
        //     physical share of single-target power per stance and per shape, and flags a stance
        //     outside 50 +/- 5. Re-derive StrikePower / ShotPower if the kit, the enemies or the
        //     movement rules change.
        //   - The AoE ("Burst") is split into a Physical half and a Special half with the same
        //     power, radius and cooldown, fired together, physical first. A single-category Burst
        //     would re-open the bias this kit exists to close. Firing order does not bias the
        //     outcome: a target dies iff the two halves' damage together reaches its HP, whichever
        //     half lands the blow.
        // Powers are percent of the attacking stat (DamageFormula: Power / 100 * A * A / (A + D)).
        // They were rescaled from the old level-term formula (Blast 40, Strike 57, Shot 41,
        // Burst 20) so a neutral hit between two average level-50 roster beasts takes the same
        // share of HP as before: Blast 40 -> 68, Burst 20 -> 37 (old damage 0.44 P + 2 against
        // new P' / 100 * A / 2 at A = D ~ 58), and Strike 93 / Shot 70 re-derived from Blast for
        // parity (see above).
        //
        // Burst (2 x power 37, radius 2, cooldown 2) is lower power and longer cooldown than the
        // single-target pair: a periodic spike that only pays off when several enemies are close,
        // i.e. against swarms and packs. Cooldown 2 rather than 3 so it comes up on the beast's
        // second turn — fights at calibrated difficulty last a handful of turns per beast, and at
        // cooldown 3 the swarm was mostly dead before the first Burst (measured under the old
        // round-based turn order; cooldowns count the beast's own turns under ATB as well).
        //
        // AreaBurst semantics (SkillTargetResolver): the disc of radius Range around the caster's
        // own tile at the moment it fires; it never moves the caster. A Burst that catches nobody
        // still fires and re-arms (battle-system.md, decision 7). It is last in the fire order so
        // it goes off from the tile Strike just walked the beast to (a Ranged beast's Burst goes
        // off from wherever it stands, which is rarely next to anyone).
        // ------------------------------------------------------------------------------------
        public const string BlastId = "sim_blast";
        public const DamageCategory BlastCategory = DamageCategory.Special;
        public const float BlastPower = 68f;
        public const int BlastRange = 3;
        public const int BlastCooldown = 1;

        public const string StrikeId = "sim_strike";
        public const DamageCategory StrikeCategory = DamageCategory.Physical;
        public const float StrikePower = 93f;
        public const int StrikeRange = 1;
        public const int StrikeCooldown = BlastCooldown;

        /// <summary>
        /// A Ranged beast's physical single-target skill, in place of Strike: a Ranged unit never
        /// walks into melee, so Strike only ever fired at an enemy that was already adjacent and
        /// Ranged beasts under-used Attack. Same category as Strike, same range and cooldown as
        /// Blast; its power is re-derived for parity like Strike's (see above).
        /// </summary>
        public const string ShotId = "sim_shot";
        public const DamageCategory ShotCategory = DamageCategory.Physical;
        public const float ShotPower = 70f;
        public const int ShotRange = BlastRange;
        public const int ShotCooldown = BlastCooldown;

        public const string BurstPhysicalId = "sim_burst_physical";
        public const string BurstSpecialId = "sim_burst_special";
        public const float BurstPower = 37f;
        public const int BurstRadius = 2;
        public const int BurstCooldown = 2;

        // ------------------------------------------------------------------------------------
        // 1v1 (PvP) setup.
        // ------------------------------------------------------------------------------------
        public const ArenaSize PvpArena = ArenaSize.Medium;

        /// <summary>Unit ids per side. Initiative ties break on ordinal id, so every pairing is also run side-swapped.</summary>
        public const string PlayerUnitId = "p";
        public const string EnemyUnitId = "e";

        // ------------------------------------------------------------------------------------
        // PvE (team vs encounter) setup.
        // ------------------------------------------------------------------------------------
        public const int DefaultTeamSize = 4;
        public const int MaxTeamSize = 6;
        public const double DefaultTargetClearRate = 50.0;

        /// <summary>Calibration: the multiplier starts at 1 and doubles/halves until it brackets the target, within these bounds.</summary>
        public const double MinMultiplier = 1.0 / 64.0;
        public const double MaxMultiplier = 64.0;

        /// <summary>Calibration: bisection steps once the target is bracketed.</summary>
        public const int CalibrationBisections = 8;

        /// <summary>A calibrated clear rate further than this from the target is reported as a calibration miss.</summary>
        public const double CalibrationTolerance = 10.0;

        /// <summary>
        /// <c>--calibrate-samples</c> default: battles per composition the picked team fights at each
        /// step of a scouted-pick calibration (8 compositions x 16 = 128 battles per step, a
        /// binomial standard error of about 4.4 points at 50%).
        /// </summary>
        public const int DefaultCalibrateSamples = 16;

        /// <summary>
        /// <c>--level-gap-teams</c> default: how many teams (a seeded subset) the no-scouting rate at
        /// each nonzero level gap is measured over (42 of 210 x 8 compositions = 336 battles).
        /// </summary>
        public const int DefaultLevelGapTeams = 42;

        /// <summary>The highest level a beast or enemy can be: a level gap that would put the enemies past it is not run.</summary>
        public const int MaxLevel = 100;

        /// <summary>Level-gap targets (<see cref="LevelGapReport"/>): at gap 0 the scouted rate is the calibration target, within +/- this.</summary>
        public const double LevelGapEvenTolerance = 5.0;

        /// <summary>Level-gap targets: the smallest and largest gap (enemies above the team) of the "a couple of levels under" band.</summary>
        public const int LevelGapNearMin = 2;

        /// <summary>See <see cref="LevelGapNearMin"/>.</summary>
        public const int LevelGapNearMax = 3;

        /// <summary>Level-gap targets: the scouted rate a couple of levels under should sit in [low, high]...</summary>
        public const double LevelGapNearLow = 20.0;

        /// <summary>... see <see cref="LevelGapNearLow"/>.</summary>
        public const double LevelGapNearHigh = 35.0;

        /// <summary>Level-gap targets: from this many levels under...</summary>
        public const int LevelGapFarMin = 5;

        /// <summary>... the scouted rate should be below this.</summary>
        public const double LevelGapFarHigh = 10.0;

        /// <summary>
        /// The balance guard on the multi-seed mean of each beast's normalized overall marginal
        /// (<see cref="PveReport.NormalizationFactor"/>): within +/- this in <c>elemental</c>...
        /// </summary>
        public const double GuardElemental = 4.0;

        /// <summary>... and within +/- this in <c>neutral</c>.</summary>
        public const double GuardNeutral = 7.0;

        // ------------------------------------------------------------------------------------
        // Generated encounters (EncounterGenerator). Compositions per shape; the element-scheme
        // weights are game content (encounter-library.json, SchemeWeights).
        // ------------------------------------------------------------------------------------
        public const int DefaultCompositions = 8;

        /// <summary>
        /// Element-matchup view: a composition counts as dominated by an element when enemies of
        /// that element carry at least this share of its threat.
        /// </summary>
        public const double DominantElementShare = 0.5;

        /// <summary>How many places count as "top" / "bottom" for the niche flags.</summary>
        public const int NicheBand = 3;

        // ------------------------------------------------------------------------------------
        // Report thresholds (PvP).
        // ------------------------------------------------------------------------------------
        public const double HighWinRate = 60.0;
        public const double LowWinRate = 40.0;
        public const double MaxLevelSwing = 25.0;

        // ------------------------------------------------------------------------------------
        // CLI defaults.
        // ------------------------------------------------------------------------------------
        public static readonly int[] DefaultLevels = { 1, 50, 100 };
        public const int DefaultMatrixLevel = 50;
        public const int DefaultSeed = 12345;

        /// <summary>
        /// Battles per side-swapped pairing in PvP, and per (team, fixed encounter, level, kit mode)
        /// in PvE with <c>--encounter-set fixed</c>, each with its own seed. Damage variance and
        /// crits make a battle random, so one battle per team is a single draw.
        /// </summary>
        public const int DefaultSamples = 5;

        /// <summary>
        /// Battles per (team, generated composition, level, kit mode): the default run's PvE. The
        /// compositions already vary the fight, so each team fights each of a shape's
        /// <see cref="DefaultCompositions"/> compositions once: 8 battles per team per shape, more
        /// than the fixed set's 5.
        /// </summary>
        public const int DefaultGeneratedSamples = 1;
        public const double DefaultMarginalThreshold = 5.0;

        public List<int> Levels = new List<int>(DefaultLevels);
        public List<KitMode> Modes = new List<KitMode> { KitMode.Elemental, KitMode.Neutral };
        public bool RunPve = true;
        public bool RunPvp = true;
        public int TeamSize = DefaultTeamSize;
        public List<string> EncounterFilter;
        public double MarginalThreshold = DefaultMarginalThreshold;
        public double TargetClearRate = DefaultTargetClearRate;

        /// <summary>Null = the elements as generated (or authored, fixed set); otherwise every enemy gets this element.</summary>
        public Element? EnemyElementOverride;

        public int MaxTime = BattleTurnExecutor.DefaultMaxTime;
        public int Seed = DefaultSeed;

        /// <summary>
        /// <c>--seeds</c>: run every one of these base seeds in one process and report the
        /// aggregate (see <see cref="SeedAggregate"/>); null for the ordinary single-seed run.
        /// </summary>
        public List<int> Seeds;
        public int Samples = DefaultSamples;

        /// <summary>PvE battles per team per composition: <c>--samples</c> when given, else the set's default.</summary>
        public int PveSamples = DefaultGeneratedSamples;

        public EncounterSet EncounterSet = EncounterSet.Generated;
        public int Compositions = DefaultCompositions;
        public int MatrixLevel = DefaultMatrixLevel;
        public string OutPath;
        public string RosterPath;

        /// <summary><c>--encounters-file</c>: the legacy fixed encounters (<c>Tooling/BalanceSim/encounters.json</c>), or null to find it.</summary>
        public string EncountersPath;

        /// <summary>
        /// <c>--write-difficulty</c>: also write the calibrated multipliers as the game's
        /// <c>encounter-difficulty.json</c> to this path (never changes the report). Null = don't.
        /// </summary>
        public string WriteDifficultyPath;

        /// <summary><c>--enemy-library</c>: the game's enemy library, or null to find it by walking up.</summary>
        public string EnemyLibraryPath;

        /// <summary><c>--encounter-library</c>: the game's encounter library, or null to find it by walking up.</summary>
        public string EncounterLibraryPath;
        public bool SelfCheck;
        public bool ShowHelp;

        /// <summary><c>--timings</c>: print a per-phase wall-clock breakdown to stderr (never into the report).</summary>
        public bool Timings;

        /// <summary>
        /// <c>--calibrate-sample n</c>: the difficulty search evaluates only a seeded subset of n
        /// teams, then the chosen multiplier runs once with every team. 0 (the default) = every
        /// team at every step. Opt-in because it changes the calibrated multipliers, so the report.
        /// </summary>
        public int CalibrateSample;

        /// <summary>
        /// <c>--calibrate-on</c>: whose clear rate the difficulty search aims at the target. The
        /// bond-aware scouted pick by default (see <see cref="EffectiveCalibrateOn"/> for its
        /// fallback); <see cref="CalibrationTarget.Mean"/> is the calibration before scouting.
        /// </summary>
        public CalibrationTarget CalibrateOn = CalibrationTarget.Bonds;

        /// <summary><c>--calibrate-samples</c>: battles per composition per step of a scouted-pick calibration.</summary>
        public int CalibrateSamples = DefaultCalibrateSamples;

        /// <summary>
        /// <c>--level-gap</c>: the level gaps (enemy level minus team level; positive = the enemies
        /// are above the team) the "PvE level gap" section replays every cell at, ascending; null
        /// (the default) = no section and nothing extra run.
        /// </summary>
        public List<int> LevelGaps;

        /// <summary><c>--level-gap-teams</c>: teams (a seeded subset) the no-scouting rate at a nonzero gap is measured over.</summary>
        public int LevelGapTeams = DefaultLevelGapTeams;

        /// <summary>
        /// <c>--avatar-value</c>: also replay every cell's picked-team battles with no avatar at the
        /// calibrated multiplier and add the "PvE avatar value" section (the avatar's worth in points
        /// of clear rate, and its direct share of the team's output). Off by default.
        /// </summary>
        public bool AvatarValue;

        /// <summary>
        /// <c>--turn-detail</c>: count every beast's no-fire turns (held by its stance, out of reach)
        /// and its damage to large enemies versus the rest, and add the "PvE beast turns" section.
        /// Off by default; never changes a battle.
        /// </summary>
        public bool TurnDetail;

        /// <summary>The PvE avatar preset (<c>--avatar</c>); the library avatar by default, <see cref="AvatarPresets.None"/> fields none.</summary>
        public string AvatarPreset = AvatarPresets.Library;

        /// <summary>
        /// <c>--avatar-level</c>: the fielded avatar's level (its stats on the medium curve and its
        /// damage-formula level), 1-100; 0, the default, means each battle's encounter level.
        /// </summary>
        public int AvatarLevel;

        /// <summary>Which skills the beasts fight with (<c>--skill-kit standard|library</c>); library is the committed report's setting.</summary>
        public KitSource KitSource = KitSource.Library;

        /// <summary>
        /// <c>--bonds on|off</c> (default on): whether the skill library's team bonds apply to the
        /// player teams. Only with <c>--skill-kit library</c> (see <see cref="BondsActive"/>).
        /// </summary>
        public bool Bonds = true;

        /// <summary>
        /// <c>--scouted</c>: which scouted-picking strategies the "PvE scouted picking" section
        /// reports (<see cref="ScoutedPicker"/>). Every strategy by default; none = no section.
        /// </summary>
        public ScoutStrategies Scouted = ScoutStrategies.All;

        /// <summary><c>--scouted-detail</c>: how much of each composition the heuristic pickers see (default full).</summary>
        public ScoutingDetail ScoutedDetail = ScoutingDetail.Full;

        /// <summary><c>--scouted-vanguard-min</c>: the fewest Vanguards a heuristic pick fields (default 1).</summary>
        public int ScoutedVanguardMin = ScoutedPicker.DefaultVanguardMin;

        /// <summary>The skill level library skills and passives are fielded at (<c>--skill-level</c>).</summary>
        public int SkillLevel = 1;

        /// <summary><c>--skill-library</c>: the library file, or null to find it by walking up.</summary>
        public string SkillLibraryPath;

        /// <summary><c>--mode pacing</c>: run the skill-progression pacing model (<see cref="PacingSimulator"/>) instead of PvE / PvP.</summary>
        public bool RunPacing;

        /// <summary><c>--battles</c>: battles per pacing campaign.</summary>
        public int PacingBattles = PacingSimulator.DefaultBattles;

        /// <summary><c>--runs</c>: pacing campaigns per base seed.</summary>
        public int PacingRuns = PacingSimulator.DefaultRuns;

        /// <summary><c>--drop-tables</c>: the drop-table file, or null to find it by walking up.</summary>
        public string DropTablesPath;

        /// <summary>
        /// The loaded skill library, when the run needs it (<c>--skill-kit library</c> or
        /// <c>--avatar library</c>). Set by <see cref="Program"/> after parsing, not a CLI option.
        /// </summary>
        public SkillLibraryKits Library;

        /// <summary>A copy of these options for one seed of a <c>--seeds</c> run: <see cref="Seed"/> set, <see cref="Seeds"/> cleared.</summary>
        public SimOptions ForSeed(int seed)
        {
            SimOptions copy = (SimOptions)MemberwiseClone();
            copy.Seed = seed;
            copy.Seeds = null;
            return copy;
        }

        /// <summary>
        /// Whether team bonds apply this run: <c>--bonds on</c> (the default) with the library kit
        /// (bonds belong to the authored game setup; the standard kit measures stat lines alone).
        /// </summary>
        public bool BondsActive
        {
            get { return Bonds && KitSource == KitSource.Library && Library != null && Library.TeamBonds.Count > 0; }
        }

        /// <summary>
        /// The calibration target this run actually uses: <see cref="CalibrateOn"/>, except that the
        /// bond-aware pick falls back to the plain heuristic when bonds are not active
        /// (<c>--bonds off</c> or <c>--skill-kit standard</c>), where it has no bonds to weigh.
        /// </summary>
        public CalibrationTarget EffectiveCalibrateOn
        {
            get { return CalibrateOn == CalibrationTarget.Bonds && !BondsActive ? CalibrationTarget.Heuristic : CalibrateOn; }
        }

        /// <summary>Whether the difficulty is calibrated on a scouted pick (anything but <c>--calibrate-on mean</c>).</summary>
        public bool CalibratesOnPick
        {
            get { return CalibrateOn != CalibrationTarget.Mean; }
        }

        /// <summary>The <c>--calibrate-on</c> spelling of <paramref name="target"/>.</summary>
        public static string CalibrationName(CalibrationTarget target)
        {
            switch (target)
            {
                case CalibrationTarget.Mean:
                    return "mean";
                case CalibrationTarget.Heuristic:
                    return "heuristic";
                default:
                    return "bonds";
            }
        }

        /// <summary>The guard on a beast's normalized overall marginal (multi-seed mean) in <paramref name="mode"/>.</summary>
        public static double Guard(KitMode mode)
        {
            return mode == KitMode.Elemental ? GuardElemental : GuardNeutral;
        }

        /// <summary>Whether this run fields anything from the skill library.</summary>
        public bool NeedsLibrary
        {
            get { return KitSource == KitSource.Library || AvatarPreset == AvatarPresets.Library; }
        }

        public const string Usage =
            "Beast Craft headless balance simulator (local-only tooling).\n" +
            "\n" +
            "Usage: dotnet run --project Tooling/BalanceSim -c Release -- [options]\n" +
            "\n" +
            "  --mode <m>                 pve | pvp | both | pacing (default both). pve = team vs encounter (primary);\n" +
            "                             pvp = the 1v1 round-robin (secondary); pacing = the skill-progression / material\n" +
            "                             economy model (Monte Carlo campaigns; see README.md, \"Pacing\").\n" +
            "  --battles <n>              pacing: battles per campaign (default 500).\n" +
            "  --runs <n>                 pacing: campaigns per base seed (default 1000; --seeds pools every seed's).\n" +
            "  --drop-tables <path>       drop-tables.json (default: found by walking up from the working directory). Pacing\n" +
            "                             rolls it; PvE checks the encounter library's shape ids against it.\n" +
            "  --kit <k>                  elemental | neutral | both (default both): the element axis. neutral forces every\n" +
            "                             beast and enemy skill's element to None.\n" +
            "  --skill-kit <k>            library | standard (default library): the skill axis. library = each beast's\n" +
            "                             DefaultLoadout from skill-library.json (the real game setup); standard = the same\n" +
            "                             kit for every beast, so stat lines are what is measured (adds the kit parity table).\n" +
            "  --skill-level <n>          Skill level for library skills and avatar passives, 1-20 (default 1); the tier is\n" +
            "                             the gates below that level (16+ = every gate passed).\n" +
            "  --skill-library <path>     skill-library.json (default: found by walking up from the working directory).\n" +
            "  --bonds <on|off>           Team bonds (default on): the library's TeamBonds apply at battle start to every player\n" +
            "                             team that meets their condition. Library kit only; ignored with --skill-kit standard.\n" +
            "  --scouted <list>           Scouted picking, reported as \"PvE scouted picking\" (default all): comma-separated\n" +
            "                             random, heuristic, bonds, oracle, or all / none. Post-processing of the battles\n" +
            "                             already run: each strategy fields one of the simulated teams per composition, chosen\n" +
            "                             from what it can see (README, \"Scouted picking\"). none = no section.\n" +
            "  --scouted-detail <d>       full | elements-only | dominant-element (default full): the preview detail the\n" +
            "                             heuristic pickers see (ScoutingDetail).\n" +
            "  --scouted-vanguard-min <n> Fewest Vanguards a heuristic pick fields, 0 to the team size (default 1).\n" +
            "  --levels <list>            Comma-separated levels (default 1,50,100).\n" +
            "  --encounter-set <s>        generated | fixed (default generated). generated = random compositions of the game's\n" +
            "                             enemy library per encounter-library shape (solo, elite, squad, horde); fixed = the\n" +
            "                             legacy hand-authored boss, swarm and pack simulator fixtures (encounters.json).\n" +
            "  --compositions <n>         Generated compositions per shape (default 8).\n" +
            "  --encounters <list>        Comma-separated shape ids (generated) or encounter ids (fixed) (default all).\n" +
            "  --team-size <n>            Beasts per player team, 1-6 (default 4); every combination is fielded.\n" +
            "  --target-clear <pct>       Clear rate the difficulty calibration aims for (default 50).\n" +
            "  --marginal-threshold <x>   Flag a beast whose overall marginal clear rate is outside +/-x points (default 5).\n" +
            "  --enemy-element <e>        authored | None | <Element> (default authored = as generated or authored):\n" +
            "                             override every enemy's element.\n" +
            "  --max-time <n>             Battle-time cap before a battle is a stalemate, in turns of a Speed-100 unit (default 2000).\n" +
            "  --seed <n>                 Base seed; each battle derives its own (default 12345).\n" +
            "  --seeds <list>             Comma-separated base seeds, run one after another in one process. Each seed's run is\n" +
            "                             exactly a --seed run; stdout (and --out) get the multi-seed aggregate (mean +/- sd per\n" +
            "                             beast), and with --out each seed's full report is also written as <name>.seed<n>.md.\n" +
            "  --samples <n>              Battles per team and composition (PvE) and per pairing (PvP), each with its own\n" +
            "                             seed: damage variance and crits make battles random (default: PvP 5; PvE 1 per\n" +
            "                             generated composition, 5 per fixed encounter).\n" +
            "  --matrix-level <n>         Level the PvP win matrix and stat table are drawn at (default 50, else the highest level).\n" +
            "  --roster <path>            beast-roster.json (default: found by walking up from the working directory).\n" +
            "  --enemy-library <path>     enemy-library.json (default: BeastCraft/Assets/_Project/Data/Encounters/, found the same way).\n" +
            "  --encounter-library <path> encounter-library.json (default: beside enemy-library.json, found the same way).\n" +
            "  --encounters-file <path>   the fixed set's encounters.json (default: Tooling/BalanceSim/encounters.json, found the same way).\n" +
            "  --avatar <preset>          library | support | none (default library). PvE only: field an avatar beside the\n" +
            "                             player team (see AvatarPresets): library = the library's default loadout (first 3\n" +
            "                             actives + AvatarDefaultPassives) at --skill-level, the committed report's setting;\n" +
            "                             support = a passive-only fixture; none = no avatar.\n" +
            "  --avatar-level <n>         The avatar's level, 1-100 (default: each battle's encounter level). Scales its\n" +
            "                             stats on the medium curve (Speed included: 100 at level 100, so its ATB gauge\n" +
            "                             keeps pace with the beasts') and is its damage-formula level.\n" +
            "  --out <path>               Also write the Markdown report to this file.\n" +
            "  --write-difficulty <path>  PvE, generated set, one seed: also write the calibrated multipliers as the game's\n" +
            "                             encounter-difficulty.json (BeastCraft/Assets/_Project/Data/Encounters/). Report unchanged.\n" +
            "  --self-check               Run everything twice and fail unless both reports are identical; also checks the\n" +
            "                             PvE battle loop against BattleTurnExecutor.RunBattle.\n" +
            "  --timings                  Print a wall-clock breakdown (per PvE cell and calibration step, PvP, report, GC)\n" +
            "                             to stderr. Never changes the report.\n" +
            "  --calibrate-on <t>         bonds | heuristic | mean (default bonds): whose PvE clear rate the difficulty is\n" +
            "                             calibrated to --target-clear. bonds / heuristic = the team that scouted picker fields\n" +
            "                             per composition (the player is assumed to scout and counter-pick; bonds falls back to\n" +
            "                             heuristic when bonds are off or with --skill-kit standard); mean = the mean of every\n" +
            "                             team (the unscouted player; the calibration before scouting).\n" +
            "  --calibrate-samples <n>    Scouted-pick calibration: battles per composition the picked team fights at each\n" +
            "                             search step (default 16). The chosen multiplier then runs once with every team.\n" +
            "  --level-gap <list>         PvE: also replay every cell with the enemies this many levels above the team (negative =\n" +
            "                             below), at the cell's calibrated multiplier, and report \"PvE level gap\". Comma-separated\n" +
            "                             gaps and ranges, e.g. -5..10 or 0,2,3,5. The team (and the avatar, unless --avatar-level)\n" +
            "                             stays at the row's level; each battle's seed ignores the gap, so gap 0 is the calibration\n" +
            "                             itself. A gap that puts the enemies outside 1-100 is not run. Default: off.\n" +
            "  --level-gap-teams <n>      --level-gap: teams (a seeded subset) the no-scouting rate at a nonzero gap is measured\n" +
            "                             over (default 42; the scouted rate always uses the picked team, --calibrate-samples\n" +
            "                             battles per composition).\n" +
            "  --avatar-value             PvE: also replay each cell's picked-team battles with no avatar at the calibrated\n" +
            "                             multiplier and report \"PvE avatar value\": the avatar's worth in points of the scouted\n" +
            "                             clear rate, and its direct share of the team's damage, healing and shield soak. Needs an\n" +
            "                             avatar (--avatar library|support) and a scouted-pick calibration. Default: off.\n" +
            "  --turn-detail              PvE: report \"PvE beast turns\": each beast's no-fire turns (held by its stance, out of\n" +
            "                             reach, stunned) and its damage to large enemies (bosses) versus the rest. Default: off.\n" +
            "  --calibrate-sample <n>     --calibrate-on mean only. Opt-in speed-up that CHANGES results: the difficulty search\n" +
            "                             evaluates a seeded subset of n teams (e.g. 50 of 210), then the chosen multiplier runs\n" +
            "                             once with every team; the report's numbers all come from that full run (default: off,\n" +
            "                             every team at every step).\n" +
            "  --help                     Show this text.\n";

        /// <summary>Parses the command line. Returns null and fills <paramref name="error"/> on bad input.</summary>
        public static SimOptions Parse(string[] args, out string error)
        {
            SimOptions options = new SimOptions();
            bool matrixLevelGiven = false;
            bool samplesGiven = false;
            bool seedGiven = false;
            error = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string text;

                switch (arg)
                {
                    case "--help":
                    case "-h":
                        options.ShowHelp = true;
                        break;
                    case "--self-check":
                        options.SelfCheck = true;
                        break;
                    case "--timings":
                        options.Timings = true;
                        break;
                    case "--avatar-value":
                        options.AvatarValue = true;
                        break;
                    case "--turn-detail":
                        options.TurnDetail = true;
                        break;
                    case "--calibrate-sample":
                        if (!TryNextInt(args, ref i, arg, 1, out options.CalibrateSample, out error))
                        {
                            return null;
                        }

                        break;
                    case "--calibrate-samples":
                        if (!TryNextInt(args, ref i, arg, 1, out options.CalibrateSamples, out error))
                        {
                            return null;
                        }

                        break;
                    case "--level-gap":
                        if (!TryNext(args, ref i, arg, out text, out error) || !TryParseGaps(text, out options.LevelGaps, out error))
                        {
                            return null;
                        }

                        break;
                    case "--level-gap-teams":
                        if (!TryNextInt(args, ref i, arg, 1, out options.LevelGapTeams, out error))
                        {
                            return null;
                        }

                        break;
                    case "--calibrate-on":
                        if (!TryNext(args, ref i, arg, out text, out error))
                        {
                            return null;
                        }

                        switch (text.ToLowerInvariant())
                        {
                            case "mean":
                                options.CalibrateOn = CalibrationTarget.Mean;
                                break;
                            case "heuristic":
                                options.CalibrateOn = CalibrationTarget.Heuristic;
                                break;
                            case "bonds":
                                options.CalibrateOn = CalibrationTarget.Bonds;
                                break;
                            default:
                                error = "--calibrate-on expects bonds, heuristic or mean, got '" + text + "'.";
                                return null;
                        }

                        break;
                    case "--levels":
                        if (!TryNext(args, ref i, arg, out text, out error) || !TryParseLevels(text, options.Levels, out error))
                        {
                            return null;
                        }

                        break;
                    case "--mode":
                        if (!TryNext(args, ref i, arg, out text, out error) || !TryParseSimMode(text, options, out error))
                        {
                            return null;
                        }

                        break;
                    case "--kit":
                        if (!TryNext(args, ref i, arg, out text, out error) || !TryParseKit(text, options.Modes, out error))
                        {
                            return null;
                        }

                        break;
                    case "--skill-kit":
                        if (!TryNext(args, ref i, arg, out text, out error))
                        {
                            return null;
                        }

                        if (string.Equals(text, "standard", StringComparison.OrdinalIgnoreCase))
                        {
                            options.KitSource = KitSource.Standard;
                        }
                        else if (string.Equals(text, "library", StringComparison.OrdinalIgnoreCase))
                        {
                            options.KitSource = KitSource.Library;
                        }
                        else
                        {
                            error = "--skill-kit expects library or standard, got '" + text + "'.";
                            return null;
                        }

                        break;
                    case "--skill-level":
                        if (!TryNextInt(args, ref i, arg, 1, out options.SkillLevel, out error))
                        {
                            return null;
                        }

                        if (options.SkillLevel > 20)
                        {
                            error = "--skill-level must be between 1 and 20 (the authored skills' max level).";
                            return null;
                        }

                        break;
                    case "--battles":
                        if (!TryNextInt(args, ref i, arg, 1, out options.PacingBattles, out error))
                        {
                            return null;
                        }

                        break;
                    case "--runs":
                        if (!TryNextInt(args, ref i, arg, 1, out options.PacingRuns, out error))
                        {
                            return null;
                        }

                        break;
                    case "--drop-tables":
                        if (!TryNext(args, ref i, arg, out options.DropTablesPath, out error))
                        {
                            return null;
                        }

                        break;
                    case "--skill-library":
                        if (!TryNext(args, ref i, arg, out options.SkillLibraryPath, out error))
                        {
                            return null;
                        }

                        break;
                    case "--encounters":
                        if (!TryNext(args, ref i, arg, out text, out error))
                        {
                            return null;
                        }

                        options.EncounterFilter = new List<string>();
                        foreach (string part in text.Split(','))
                        {
                            string id = part.Trim();
                            if (id.Length > 0 && !options.EncounterFilter.Contains(id))
                            {
                                options.EncounterFilter.Add(id);
                            }
                        }

                        if (options.EncounterFilter.Count == 0)
                        {
                            error = "--encounters needs at least one encounter id.";
                            return null;
                        }

                        break;
                    case "--team-size":
                        if (!TryNextInt(args, ref i, arg, 1, out options.TeamSize, out error))
                        {
                            return null;
                        }

                        if (options.TeamSize > MaxTeamSize)
                        {
                            error = "--team-size must be between 1 and " + MaxTeamSize + " (the largest battle format).";
                            return null;
                        }

                        break;
                    case "--target-clear":
                        if (!TryNextDouble(args, ref i, arg, out options.TargetClearRate, out error))
                        {
                            return null;
                        }

                        if (options.TargetClearRate <= 0.0 || options.TargetClearRate >= 100.0)
                        {
                            error = "--target-clear expects a percentage strictly between 0 and 100.";
                            return null;
                        }

                        break;
                    case "--marginal-threshold":
                        if (!TryNextDouble(args, ref i, arg, out options.MarginalThreshold, out error))
                        {
                            return null;
                        }

                        if (options.MarginalThreshold < 0.0)
                        {
                            error = "--marginal-threshold expects a non-negative number.";
                            return null;
                        }

                        break;
                    case "--enemy-element":
                        if (!TryNext(args, ref i, arg, out text, out error) || !TryParseEnemyElement(text, options, out error))
                        {
                            return null;
                        }

                        break;
                    case "--max-time":
                        if (!TryNextInt(args, ref i, arg, 1, out options.MaxTime, out error))
                        {
                            return null;
                        }

                        break;
                    case "--seed":
                        if (!TryNextInt(args, ref i, arg, int.MinValue, out options.Seed, out error))
                        {
                            return null;
                        }

                        seedGiven = true;
                        break;
                    case "--seeds":
                        if (!TryNext(args, ref i, arg, out text, out error))
                        {
                            return null;
                        }

                        options.Seeds = new List<int>();
                        foreach (string part in text.Split(','))
                        {
                            if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
                            {
                                error = "--seeds expects comma-separated integers, got '" + text + "'.";
                                return null;
                            }

                            if (options.Seeds.Contains(seed))
                            {
                                error = "--seeds lists " + seed + " twice.";
                                return null;
                            }

                            options.Seeds.Add(seed);
                        }

                        break;
                    case "--samples":
                        if (!TryNextInt(args, ref i, arg, 1, out options.Samples, out error))
                        {
                            return null;
                        }

                        samplesGiven = true;
                        break;
                    case "--compositions":
                        if (!TryNextInt(args, ref i, arg, 1, out options.Compositions, out error))
                        {
                            return null;
                        }

                        break;
                    case "--encounter-set":
                        if (!TryNext(args, ref i, arg, out text, out error))
                        {
                            return null;
                        }

                        if (string.Equals(text, "generated", StringComparison.OrdinalIgnoreCase))
                        {
                            options.EncounterSet = EncounterSet.Generated;
                        }
                        else if (string.Equals(text, "fixed", StringComparison.OrdinalIgnoreCase))
                        {
                            options.EncounterSet = EncounterSet.Fixed;
                        }
                        else
                        {
                            error = "--encounter-set expects generated or fixed, got '" + text + "'.";
                            return null;
                        }

                        break;
                    case "--matrix-level":
                        if (!TryNextInt(args, ref i, arg, 1, out options.MatrixLevel, out error))
                        {
                            return null;
                        }

                        matrixLevelGiven = true;
                        break;
                    case "--out":
                        if (!TryNext(args, ref i, arg, out options.OutPath, out error))
                        {
                            return null;
                        }

                        break;
                    case "--write-difficulty":
                        if (!TryNext(args, ref i, arg, out options.WriteDifficultyPath, out error))
                        {
                            return null;
                        }

                        break;
                    case "--roster":
                        if (!TryNext(args, ref i, arg, out options.RosterPath, out error))
                        {
                            return null;
                        }

                        break;
                    case "--encounters-file":
                        if (!TryNext(args, ref i, arg, out options.EncountersPath, out error))
                        {
                            return null;
                        }

                        break;
                    case "--enemy-library":
                        if (!TryNext(args, ref i, arg, out options.EnemyLibraryPath, out error))
                        {
                            return null;
                        }

                        break;
                    case "--encounter-library":
                        if (!TryNext(args, ref i, arg, out options.EncounterLibraryPath, out error))
                        {
                            return null;
                        }

                        break;
                    case "--bonds":
                        if (!TryNext(args, ref i, arg, out text, out error))
                        {
                            return null;
                        }

                        text = text.ToLowerInvariant();
                        if (text != "on" && text != "off")
                        {
                            error = "--bonds expects on or off, got '" + text + "'.";
                            return null;
                        }

                        options.Bonds = text == "on";
                        break;
                    case "--scouted":
                        if (!TryNext(args, ref i, arg, out text, out error) || !TryParseScouted(text, options, out error))
                        {
                            return null;
                        }

                        break;
                    case "--scouted-detail":
                        if (!TryNext(args, ref i, arg, out text, out error) || !TryParseScoutingDetail(text, options, out error))
                        {
                            return null;
                        }

                        break;
                    case "--scouted-vanguard-min":
                        if (!TryNextInt(args, ref i, arg, 0, out options.ScoutedVanguardMin, out error))
                        {
                            return null;
                        }

                        break;
                    case "--avatar":
                        if (!TryNext(args, ref i, arg, out text, out error))
                        {
                            return null;
                        }

                        text = text.ToLowerInvariant();
                        if (!AvatarPresets.IsKnown(text))
                        {
                            error = "--avatar expects " + string.Join(" or ", AvatarPresets.Names) + ", got '" + text + "'.";
                            return null;
                        }

                        options.AvatarPreset = text;
                        break;
                    case "--avatar-level":
                        if (!TryNextInt(args, ref i, arg, 1, out options.AvatarLevel, out error))
                        {
                            return null;
                        }

                        if (options.AvatarLevel > 100)
                        {
                            error = "--avatar-level must be between 1 and 100.";
                            return null;
                        }

                        break;
                    default:
                        error = "Unknown argument '" + arg + "'. Use --help for usage.";
                        return null;
                }
            }

            if (seedGiven && options.Seeds != null)
            {
                error = "--seed and --seeds cannot be combined.";
                return null;
            }

            if (options.WriteDifficultyPath != null && (!options.RunPve || options.EncounterSet != EncounterSet.Generated || options.Seeds != null))
            {
                error = "--write-difficulty needs PvE, the generated encounter set and a single seed (not --seeds).";
                return null;
            }

            if (options.CalibrateSample > 0 && options.CalibratesOnPick)
            {
                error = "--calibrate-sample only applies with --calibrate-on mean (a scouted-pick calibration already searches on the picked teams alone).";
                return null;
            }

            if (options.AvatarValue && (!options.RunPve || options.AvatarPreset == AvatarPresets.None || !options.CalibratesOnPick))
            {
                error = "--avatar-value needs PvE, an avatar (--avatar library or support) and a scouted-pick calibration (not --calibrate-on mean).";
                return null;
            }

            if (options.ScoutedVanguardMin > options.TeamSize)
            {
                error = "--scouted-vanguard-min " + options.ScoutedVanguardMin + " is larger than --team-size " + options.TeamSize + ".";
                return null;
            }

            options.PveSamples = samplesGiven ? options.Samples : options.EncounterSet == EncounterSet.Generated ? DefaultGeneratedSamples : DefaultSamples;

            if (!options.Levels.Contains(options.MatrixLevel))
            {
                if (matrixLevelGiven)
                {
                    error = "--matrix-level " + options.MatrixLevel + " is not one of the simulated levels.";
                    return null;
                }

                options.MatrixLevel = options.Levels[options.Levels.Count - 1];
            }

            return options;
        }

        private static bool TryNext(string[] args, ref int i, string flag, out string value, out string error)
        {
            if (i + 1 >= args.Length)
            {
                value = null;
                error = flag + " needs a value.";
                return false;
            }

            i++;
            value = args[i];
            error = null;
            return true;
        }

        private static bool TryNextInt(string[] args, ref int i, string flag, int minimum, out int value, out string error)
        {
            value = 0;
            if (!TryNext(args, ref i, flag, out string text, out error))
            {
                return false;
            }

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < minimum)
            {
                error = flag + " expects an integer >= " + minimum + ", got '" + text + "'.";
                return false;
            }

            return true;
        }

        private static bool TryNextDouble(string[] args, ref int i, string flag, out double value, out string error)
        {
            value = 0.0;
            if (!TryNext(args, ref i, flag, out string text, out error))
            {
                return false;
            }

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.IsNaN(value) || double.IsInfinity(value))
            {
                error = flag + " expects a number, got '" + text + "'.";
                return false;
            }

            return true;
        }

        private static bool TryParseLevels(string text, List<int> levels, out string error)
        {
            levels.Clear();
            foreach (string part in text.Split(','))
            {
                if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) || level < 1)
                {
                    error = "--levels expects comma-separated integers >= 1, got '" + text + "'.";
                    return false;
                }

                if (!levels.Contains(level))
                {
                    levels.Add(level);
                }
            }

            levels.Sort();
            error = null;
            return true;
        }

        /// <summary>
        /// <c>--level-gap</c>: comma-separated integers and inclusive ranges <c>a..b</c> (either end
        /// may be negative), each within +/-(<see cref="MaxLevel"/> - 1); duplicates are dropped and
        /// the result is sorted ascending.
        /// </summary>
        private static bool TryParseGaps(string text, out List<int> gaps, out string error)
        {
            gaps = new List<int>();
            error = "--level-gap expects comma-separated integers or ranges a..b between -" + (MaxLevel - 1) + " and " + (MaxLevel - 1) + ", got '" + text + "'.";
            foreach (string raw in text.Split(','))
            {
                string part = raw.Trim();
                int split = part.IndexOf("..", StringComparison.Ordinal);
                string first = split < 0 ? part : part.Substring(0, split);
                string last = split < 0 ? part : part.Substring(split + 2);
                if (!int.TryParse(first.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int from) ||
                    !int.TryParse(last.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int to) ||
                    from > to || from < -(MaxLevel - 1) || to > MaxLevel - 1)
                {
                    return false;
                }

                for (int gap = from; gap <= to; gap++)
                {
                    if (!gaps.Contains(gap))
                    {
                        gaps.Add(gap);
                    }
                }
            }

            gaps.Sort();
            error = null;
            return true;
        }

        private static bool TryParseSimMode(string text, SimOptions options, out string error)
        {
            error = null;
            switch (text.ToLowerInvariant())
            {
                case "pve":
                    options.RunPve = true;
                    options.RunPvp = false;
                    return true;
                case "pvp":
                    options.RunPve = false;
                    options.RunPvp = true;
                    return true;
                case "both":
                    options.RunPve = true;
                    options.RunPvp = true;
                    return true;
                case "pacing":
                    options.RunPve = false;
                    options.RunPvp = false;
                    options.RunPacing = true;
                    return true;
                default:
                    error = "--mode expects pve, pvp, both or pacing, got '" + text + "'.";
                    return false;
            }
        }

        private static bool TryParseKit(string text, List<KitMode> modes, out string error)
        {
            modes.Clear();
            error = null;

            switch (text.ToLowerInvariant())
            {
                case "elemental":
                    modes.Add(KitMode.Elemental);
                    return true;
                case "neutral":
                    modes.Add(KitMode.Neutral);
                    return true;
                case "both":
                    modes.Add(KitMode.Elemental);
                    modes.Add(KitMode.Neutral);
                    return true;
                default:
                    error = "--kit expects elemental, neutral or both, got '" + text + "'" +
                            (text == "standard" || text == "library" ? " (the skill axis is now --skill-kit)." : ".");
                    return false;
            }
        }

        private static bool TryParseScouted(string text, SimOptions options, out string error)
        {
            error = null;
            ScoutStrategies strategies = ScoutStrategies.None;
            foreach (string raw in text.Split(','))
            {
                switch (raw.Trim().ToLowerInvariant())
                {
                    case "none":
                        break;
                    case "all":
                        strategies |= ScoutStrategies.All;
                        break;
                    case "random":
                        strategies |= ScoutStrategies.Random;
                        break;
                    case "heuristic":
                        strategies |= ScoutStrategies.Heuristic;
                        break;
                    case "bonds":
                        strategies |= ScoutStrategies.BondAware;
                        break;
                    case "oracle":
                        strategies |= ScoutStrategies.Oracle;
                        break;
                    default:
                        error = "--scouted expects a comma-separated list of random, heuristic, bonds, oracle (or all / none), got '" + text + "'.";
                        return false;
                }
            }

            options.Scouted = strategies;
            return true;
        }

        private static bool TryParseScoutingDetail(string text, SimOptions options, out string error)
        {
            error = null;
            switch (text.ToLowerInvariant())
            {
                case "full":
                    options.ScoutedDetail = ScoutingDetail.Full;
                    return true;
                case "elements-only":
                    options.ScoutedDetail = ScoutingDetail.ElementsOnly;
                    return true;
                case "dominant-element":
                    options.ScoutedDetail = ScoutingDetail.DominantElementOnly;
                    return true;
                default:
                    error = "--scouted-detail expects full, elements-only or dominant-element, got '" + text + "'.";
                    return false;
            }
        }

        /// <summary>The <c>--scouted-detail</c> spelling of <paramref name="detail"/>.</summary>
        public static string DetailName(ScoutingDetail detail)
        {
            switch (detail)
            {
                case ScoutingDetail.ElementsOnly:
                    return "elements-only";
                case ScoutingDetail.DominantElementOnly:
                    return "dominant-element";
                default:
                    return "full";
            }
        }

        private static bool TryParseEnemyElement(string text, SimOptions options, out string error)
        {
            error = null;
            if (string.Equals(text, "authored", StringComparison.OrdinalIgnoreCase))
            {
                options.EnemyElementOverride = null;
                return true;
            }

            if (Enum.TryParse(text, true, out Element element) && Enum.IsDefined(typeof(Element), element))
            {
                options.EnemyElementOverride = element;
                return true;
            }

            error = "--enemy-element expects authored or an element name (None, Fire, Water, ...), got '" + text + "'.";
            return false;
        }

        public static string ModeName(KitMode mode)
        {
            return mode == KitMode.Elemental ? "elemental" : "neutral";
        }

        public static string Format(double value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>Signed, one decimal: "+3.2", "-0.4", "0.0".</summary>
        public static string Signed(double value)
        {
            string text = Format(value);
            if (text == "0.0" || text == "-0.0")
            {
                return "0.0";
            }

            return value > 0 ? "+" + text : text;
        }

        public static string FormatMultiplier(double value)
        {
            return value.ToString("0.000", CultureInfo.InvariantCulture);
        }

        public static string Join(IEnumerable<int> values)
        {
            List<string> parts = new List<string>();
            foreach (int value in values)
            {
                parts.Add(value.ToString(CultureInfo.InvariantCulture));
            }

            return string.Join(", ", parts);
        }

        public static BattleFormat FormatForTeamSize(int size)
        {
            if (size <= BattleFormat.Solo.MaxPartySize())
            {
                return BattleFormat.Solo;
            }

            return size <= BattleFormat.SmallGroup.MaxPartySize() ? BattleFormat.SmallGroup : BattleFormat.LargeGroup;
        }
    }
}
