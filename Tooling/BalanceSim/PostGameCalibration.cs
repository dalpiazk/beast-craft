using System;
using System.Collections.Generic;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// <c>--write-difficulty</c>'s post-game cells: every post-game shape
    /// (<see cref="EncounterCatalog.PostGameShapes"/>) calibrated exactly as a mainline cell
    /// (<see cref="PveSimulator.RunCell"/>: the run's calibration target, samples and gear) to its
    /// own <c>TargetClear</c>, per kit mode, at <see cref="Level"/> only — post-game regions are
    /// flat level 100. Runs after the mainline run and its report, on the run's own simulator, so
    /// the report and every mainline cell are untouched; the cells are appended to the table after
    /// the mainline ones (<see cref="DifficultyWriter"/>). Each cell's picked-team clear rate goes
    /// to stderr.
    /// </summary>
    public static class PostGameCalibration
    {
        /// <summary>The only level post-game shapes are calibrated at (post-game regions are validated to MinLevel = MaxLevel = 100).</summary>
        public const int Level = SimOptions.MaxLevel;

        public static List<PveCell> Run(SimOptions options, PveSimulator pve, EncounterCatalog encounters)
        {
            List<PveCell> cells = new List<PveCell>();
            if (pve == null || encounters == null || encounters.PostGameShapes.Count == 0)
            {
                return cells;
            }

            foreach (KitMode mode in options.Modes)
            {
                foreach (EncounterShape shape in encounters.PostGameShapes)
                {
                    PveCell cell = pve.RunCell(mode, Level, shape);
                    cells.Add(cell);
                    Console.Error.WriteLine("Post-game " + SimOptions.ModeName(mode) + "/" + shape.Id + "/L" + Level + ": x" + SimOptions.FormatMultiplier(cell.Multiplier) +
                                            ", calibrated clear " + SimOptions.Format(cell.CalibratedRate) + "% (target " + SimOptions.Format(options.TargetFor(shape)) +
                                            "%), no-scouting " + SimOptions.Format(cell.ClearRate) + "%.");
                }
            }

            return cells;
        }
    }
}
