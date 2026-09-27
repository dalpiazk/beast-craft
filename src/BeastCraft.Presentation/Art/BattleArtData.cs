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
    /// <item><see cref="SkillIcons"/>: the round frame and rarity ring every skill icon is drawn
    /// in (the skill strip, the skill detail card, the turn order's "now acting" badge).</item>
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

        /// <summary>Painted backdrops: any number per (RegionId, Arena), each ArtKey once.</summary>
        public BattleBackdropData[] Backdrops = new BattleBackdropData[0];

        /// <summary>The overlays over a backdrop (null: the defaults).</summary>
        public BoardOverlayData Board = new BoardOverlayData();

        /// <summary>How skill icons are framed in the HUD (null: bare icons).</summary>
        public SkillIconStyleData SkillIcons;

        /// <summary>
        /// The backdrop whose ArtKey is <paramref name="artKey"/> (a battle layout's: the painting
        /// its obstacles are painted on), or null.
        /// </summary>
        public BattleBackdropData BackdropByArtKey(string artKey)
        {
            if (string.IsNullOrEmpty(artKey) || Backdrops == null)
            {
                return null;
            }

            foreach (BattleBackdropData backdrop in Backdrops)
            {
                if (backdrop != null && string.Equals(backdrop.ArtKey, artKey, StringComparison.Ordinal))
                {
                    return backdrop;
                }
            }

            return null;
        }

        /// <summary>
        /// The first backdrop for <paramref name="regionId"/>'s <paramref name="arena"/>, or null when
        /// there is none (the viewer then draws the pixel-tile board). A region with battle layouts has
        /// several (one per painting): a battle draws its own layout's (<see cref="BackdropByArtKey"/>).
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

    /// <summary>Where a skill comes from, which picks its default rarity (<see cref="SkillIconStyleData.RarityFor"/>).</summary>
    public enum SkillIconSource
    {
        /// <summary>A beast skill (<c>skill-library.json</c> BeastSkills).</summary>
        Beast = 0,

        /// <summary>An avatar active or passive.</summary>
        Avatar = 1,

        /// <summary>An enemy-library skill.</summary>
        Enemy = 2
    }

    /// <summary>
    /// A skill icon's layers, back to front: its rarity's ring (<see cref="RingScale"/> of the box),
    /// the icon itself (<see cref="IconScale"/>), then the round frame over its edge
    /// (<see cref="FrameScale"/>), each centred in the box. The frame and the rings are manifest
    /// sprites (by ArtKey), so the art lane paints them; a rarity may tint its ring.
    /// </summary>
    [Serializable]
    public class SkillIconStyleData
    {
        /// <summary>The round frame drawn over every icon: a sprite's ArtKey (empty = none).</summary>
        public string Frame;

        /// <summary>The frame's diameter as a fraction of the icon's box (0.2-1.5).</summary>
        public float FrameScale = 0.9f;

        /// <summary>The icon's diameter as a fraction of the box (0.2-1.5).</summary>
        public float IconScale = 0.76f;

        /// <summary>The rarity ring's diameter as a fraction of the box (0.2-1.5).</summary>
        public float RingScale = 1f;

        /// <summary>The rarities, each with its ring.</summary>
        public SkillRarityData[] Rarities = new SkillRarityData[0];

        /// <summary>The rarity of a beast skill, an avatar skill and an enemy skill without an override.</summary>
        public string BeastRarity;

        public string AvatarRarity;

        public string EnemyRarity;

        /// <summary>A skill's own rarity, by its icon's ArtKey.</summary>
        public SkillRarityOverrideData[] Overrides = new SkillRarityOverrideData[0];

        /// <summary>The rarity named <paramref name="id"/>, or null.</summary>
        public SkillRarityData Rarity(string id)
        {
            if (string.IsNullOrEmpty(id) || Rarities == null)
            {
                return null;
            }

            foreach (SkillRarityData rarity in Rarities)
            {
                if (rarity != null && string.Equals(rarity.Rarity, id, StringComparison.Ordinal))
                {
                    return rarity;
                }
            }

            return null;
        }

        /// <summary>
        /// The rarity of the skill whose icon is <paramref name="skillArtKey"/>: its override, else
        /// its source's default; null when neither names a rarity.
        /// </summary>
        public SkillRarityData RarityFor(string skillArtKey, SkillIconSource source)
        {
            if (!string.IsNullOrEmpty(skillArtKey) && Overrides != null)
            {
                foreach (SkillRarityOverrideData entry in Overrides)
                {
                    if (entry != null && string.Equals(entry.ArtKey, skillArtKey, StringComparison.Ordinal))
                    {
                        return Rarity(entry.Rarity);
                    }
                }
            }

            return Rarity(source == SkillIconSource.Enemy ? EnemyRarity : source == SkillIconSource.Avatar ? AvatarRarity : BeastRarity);
        }
    }

    /// <summary>One rarity: its id and its ring.</summary>
    [Serializable]
    public class SkillRarityData
    {
        /// <summary>Lowercase snake_case id (<c>common</c>, <c>rare</c>, ...).</summary>
        public string Rarity;

        /// <summary>The ring: a sprite's ArtKey.</summary>
        public string Ring;

        /// <summary>A palette char the ring is multiplied by (empty = its own colours).</summary>
        public string Tint;
    }

    /// <summary>One skill's own rarity.</summary>
    [Serializable]
    public class SkillRarityOverrideData
    {
        /// <summary>The skill's icon ArtKey (<c>skill/&lt;id&gt;</c>, <c>skill/enemy/&lt;enemy&gt;/&lt;skill&gt;</c>).</summary>
        public string ArtKey;

        /// <summary>A <see cref="SkillRarityData.Rarity"/>.</summary>
        public string Rarity;
    }
}
