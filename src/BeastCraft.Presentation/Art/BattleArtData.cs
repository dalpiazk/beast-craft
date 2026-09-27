using System;
using BeastCraft.Battle.Grid;

namespace BeastCraft.Presentation.Art
{
    /// <summary>
    /// The plain-data shape of <c>content/data/Vfx/battle-art.json</c>: how the battle screen
    /// dresses the board. Presentation only: nothing here is read by a battle. Public fields, JSON
    /// keys are the field names (read with <c>FieldJson</c>); checked by
    /// <see cref="BattleArtValidator"/>.
    /// <list type="bullet">
    /// <item><see cref="Backdrops"/>: a painted backdrop per region and arena size (the
    /// <c>RegionArt</c> pattern of <c>enemy-library.json</c>: a <c>RegionId</c> and an
    /// <c>ArtKey</c> naming a sprite in the art manifest), with where the board sits inside the
    /// image (<see cref="BattleBackdropData.BoardRect"/>). A region and arena without one draws
    /// the pixel-tile board (the fallback).</item>
    /// <item><see cref="Board"/>: the overlays drawn over a backdrop: the soft hex grid and the
    /// two deployment-zone tints, and the scrim behind the HUD bands.</item>
    /// </list>
    /// </summary>
    [Serializable]
    public class BattleArtData
    {
        /// <summary>The file's path relative to the repository root.</summary>
        public const string ProjectRelativePath = "content/data/Vfx/battle-art.json";

        /// <summary>The only <see cref="SchemaVersion"/> this code reads.</summary>
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion;

        /// <summary>Painted backdrops, at most one per (RegionId, Arena).</summary>
        public BattleBackdropData[] Backdrops = new BattleBackdropData[0];

        /// <summary>The overlays over a backdrop (null: the defaults).</summary>
        public BoardOverlayData Board = new BoardOverlayData();

        /// <summary>
        /// The backdrop for <paramref name="regionId"/>'s <paramref name="arena"/>, or null when
        /// there is none (the viewer then draws the pixel-tile board).
        /// </summary>
        public BattleBackdropData Backdrop(string regionId, ArenaSize arena)
        {
            if (string.IsNullOrEmpty(regionId) || Backdrops == null)
            {
                return null;
            }

            string name = arena.ToString();
            foreach (BattleBackdropData backdrop in Backdrops)
            {
                if (backdrop != null && string.Equals(backdrop.RegionId, regionId, StringComparison.Ordinal) &&
                    string.Equals(backdrop.Arena, name, StringComparison.Ordinal))
                {
                    return backdrop;
                }
            }

            return null;
        }
    }

    /// <summary>One painted backdrop: a region's arena of one size.</summary>
    [Serializable]
    public class BattleBackdropData
    {
        /// <summary>A <c>regions.json</c> RegionId.</summary>
        public string RegionId;

        /// <summary>An <see cref="ArenaSize"/> name: <c>Small</c> (5 x 7), <c>Medium</c> (8 x 11) or <c>Large</c> (11 x 15).</summary>
        public string Arena;

        /// <summary>The image: a sprite's <c>ArtKey</c> in the art manifest (<c>backdrop/&lt;region&gt;/&lt;arena&gt;</c>).</summary>
        public string ArtKey;

        /// <summary>
        /// Where the board's tiles lie in the image, as fractions of the image (0-1 from its
        /// top-left): the rectangle <c>HexLayout.BoardBounds</c> (the box of every tile of the arena)
        /// maps onto. The rest of the image is decorative margin, which may run to the canvas edges.
        /// Fractions, so a repaint at another resolution (same aspect) keeps its data.
        /// </summary>
        public BattleArtRect BoardRect = new BattleArtRect();
    }

    /// <summary>A rectangle in fractions of an image (0-1 from its top-left).</summary>
    [Serializable]
    public class BattleArtRect
    {
        public float X;

        public float Y;

        public float Width;

        public float Height;
    }

    /// <summary>
    /// What is drawn over a backdrop, under the units: the hex grid, then the deployment-zone tints.
    /// Colours are palette chars of the art manifest, like the VFX library's.
    /// </summary>
    [Serializable]
    public class BoardOverlayData
    {
        /// <summary>The hex grid's line colour (a palette char).</summary>
        public string GridColor = "4";

        /// <summary>The grid lines' opacity (0-1; 0 = no grid).</summary>
        public float GridAlpha = 0.2f;

        /// <summary>The grid lines' thickness in canvas pixels (0.5-6), whatever the zoom.</summary>
        public float GridWidth = 1.5f;

        /// <summary>The player's deployment rows' tint (a palette char).</summary>
        public string PlayerZoneColor = "c";

        /// <summary>The player's zone opacity (0-1; 0 = none).</summary>
        public float PlayerZoneAlpha = 0.14f;

        /// <summary>The enemy's deployment rows' tint (a palette char).</summary>
        public string EnemyZoneColor = "r";

        /// <summary>The enemy's zone opacity (0-1; 0 = none).</summary>
        public float EnemyZoneAlpha = 0.14f;

        /// <summary>
        /// The opacity (0-1) of the dark scrim behind the HUD bands above and below the board, where
        /// the backdrop's margin runs under the header, turn order, skills and controls.
        /// </summary>
        public float HudScrimAlpha = 0.45f;
    }
}
