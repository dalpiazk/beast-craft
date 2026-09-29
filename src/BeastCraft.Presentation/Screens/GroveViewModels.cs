using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Common;
using BeastCraft.Creatures;
using BeastCraft.Economy;
using BeastCraft.Expeditions;
using BeastCraft.Garden;
using BeastCraft.Grove;
using BeastCraft.Npc;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Tutorial;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>The Grove hub's inner tabs (docs/design/grove.md, D4): Glade (Beast Grove), Garden (Wildgarden), Board (Expeditions), Npc (Grove Keeper and friends).</summary>
    public enum GroveTab
    {
        Glade = 0,
        Garden = 1,
        Board = 2,
        Npc = 3
    }

    /// <summary>
    /// A deterministic, subtle "whole-sprite tint" fallback for a worn colour form
    /// (docs/design/grove.md, "Colour evolutions", "Rendering"): no accent-mask art exists yet for any
    /// roster species, so a beast wearing a Grove colour form is drawn with its plain sprite multiplied
    /// by a soft, hue-shifted near-white colour hashed from the form's own id (stable across sessions,
    /// no authoring needed) — readable at a glance without a second art pass.
    /// </summary>
    public static class ColourFormPresentation
    {
        /// <summary>
        /// The colour form <paramref name="beastId"/> (of <paramref name="speciesId"/>) currently wears,
        /// as a subtle <c>#RRGGBB</c> multiply tint, or null when it wears its natural look or a free
        /// starter colour (neither is a Grove colour form) or the species has none authored.
        /// </summary>
        public static string WornTint(PlayerSave save, string beastId, string speciesId, GroveLibrary grove, CosmeticLibrary cosmetics)
        {
            if (save == null || grove == null || cosmetics == null || string.IsNullOrEmpty(speciesId))
            {
                return null;
            }

            foreach (ColourFormData form in grove.ColourFormsFor(speciesId))
            {
                if (form == null || string.IsNullOrEmpty(form.CosmeticCategoryId))
                {
                    continue;
                }

                CosmeticOption worn = CosmeticRules.Worn(save, beastId, form.CosmeticCategoryId, cosmetics);
                if (worn != null && worn.OptionId == form.CosmeticOptionId)
                {
                    return TintHex(form.ColourFormId);
                }
            }

            return null;
        }

        /// <summary>A stable, subtle pastel tint (high value, moderate saturation) hashed from <paramref name="id"/>: the same id always yields the same colour.</summary>
        public static string TintHex(string id)
        {
            int hash = 17;
            foreach (char c in id ?? string.Empty)
            {
                hash = unchecked((hash * 31) + c);
            }

            double hue = (Math.Abs(hash) % 360) / 360.0;
            (byte r, byte g, byte b) = HsvToRgb(hue, 0.32, 0.93);
            return "#" + r.ToString("X2", CultureInfo.InvariantCulture) + g.ToString("X2", CultureInfo.InvariantCulture) + b.ToString("X2", CultureInfo.InvariantCulture);
        }

        private static (byte, byte, byte) HsvToRgb(double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs((h * 6.0) % 2.0 - 1));
            double m = v - c;
            double r, g, b;
            int sector = (int)(h * 6.0) % 6;
            switch (sector)
            {
                case 0:
                    r = c;
                    g = x;
                    b = 0;
                    break;
                case 1:
                    r = x;
                    g = c;
                    b = 0;
                    break;
                case 2:
                    r = 0;
                    g = c;
                    b = x;
                    break;
                case 3:
                    r = 0;
                    g = x;
                    b = c;
                    break;
                case 4:
                    r = x;
                    g = 0;
                    b = c;
                    break;
                default:
                    r = c;
                    g = 0;
                    b = x;
                    break;
            }

            return ((byte)Math.Round((r + m) * 255.0), (byte)Math.Round((g + m) * 255.0), (byte)Math.Round((b + m) * 255.0));
        }
    }

    // ================================================================================================
    // Glade
    // ================================================================================================

    /// <summary>One habitat, as the Glade lists it.</summary>
    public sealed class HabitatRow
    {
        public string HabitatId;
        public string DisplayName;
        public bool Unlocked;
        public int SlotCount;
        public int PlacedCount;
    }

    /// <summary>One decor slot of the selected habitat's grid (a simple slot grid, not free drag — docs/design/grove.md, D4).</summary>
    public sealed class DecorSlotRow
    {
        /// <summary>Position in the grid (row-major).</summary>
        public int Index;

        /// <summary>Null for an empty slot.</summary>
        public string DecorId;

        public string DisplayName;
    }

    /// <summary>An owned decor piece not currently placed anywhere, offered for the selected habitat.</summary>
    public sealed class DecorAvailableRow
    {
        public string DecorId;
        public string DisplayName;
    }

    /// <summary>One owned beast, as the Glade lists it: affinity bar/tier, feed/play cooldown state, pending gifts, its worn colour form's tint (if any).</summary>
    public sealed class GladeBeastRow
    {
        public string BeastId;
        public string SpeciesId;
        public string Name;
        public string ArtKey;
        public Element Element;

        /// <summary>0 (never fed/played with) to <see cref="GroveLibrary.MaxTier"/>.</summary>
        public int Tier;

        public int Xp;

        /// <summary>The XP the next tier needs (0 = already at the top tier).</summary>
        public int NextTierXp;

        public bool CanFeed;
        public bool CanPlay;
        public int PendingGifts;

        /// <summary>The worn colour form's <c>#RRGGBB</c> multiply tint, or null (natural/free starter look).</summary>
        public string TintHex;
    }

    /// <summary>What a Glade action reported, for a toast.</summary>
    public sealed class GladeActionOutcome
    {
        public bool Success;
        public string Message;

        internal static GladeActionOutcome Of(bool success, string message)
        {
            return new GladeActionOutcome { Success = success, Message = message };
        }
    }

    /// <summary>
    /// The Glade tab (docs/design/grove.md, D4): habitats (their decor slot grid), beasts (tap one to
    /// feed/play, watch the affinity bar/tier, collect gifts). A pure view over <see cref="GroveRules"/>
    /// plus the session's actions, re-read on <see cref="Refresh"/>.
    /// </summary>
    public sealed class GladeViewModel
    {
        private readonly GameSession _session;

        public GladeViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Refresh();
        }

        public List<HabitatRow> Habitats { get; } = new List<HabitatRow>();

        public string SelectedHabitatId { get; private set; }

        public List<DecorSlotRow> Slots { get; } = new List<DecorSlotRow>();

        public List<DecorAvailableRow> AvailableDecor { get; } = new List<DecorAvailableRow>();

        public List<GladeBeastRow> Beasts { get; } = new List<GladeBeastRow>();

        public void SelectHabitat(string habitatId)
        {
            if (Habitats.Exists(h => h.HabitatId == habitatId))
            {
                SelectedHabitatId = habitatId;
                Refresh();
            }
        }

        public void Refresh()
        {
            PlayerSave save = _session.Save;
            GroveLibrary library = _session.Content.GroveLibrary;
            save.EnsureInitialized();

            Habitats.Clear();
            foreach (HabitatData habitat in library.Data.Habitats ?? new HabitatData[0])
            {
                if (habitat == null)
                {
                    continue;
                }

                Habitats.Add(new HabitatRow
                {
                    HabitatId = habitat.HabitatId,
                    DisplayName = habitat.DisplayName ?? habitat.HabitatId,
                    Unlocked = GroveRules.IsHabitatUnlocked(save, habitat),
                    SlotCount = habitat.SlotCount,
                    PlacedCount = save.Grove.PlacedCountIn(habitat.HabitatId)
                });
            }

            if (string.IsNullOrEmpty(SelectedHabitatId) || !Habitats.Exists(h => h.HabitatId == SelectedHabitatId && h.Unlocked))
            {
                SelectedHabitatId = Habitats.Find(h => h.Unlocked)?.HabitatId;
            }

            Slots.Clear();
            AvailableDecor.Clear();
            HabitatData selected = library.Habitat(SelectedHabitatId);
            if (selected != null)
            {
                int index = 0;
                foreach (PlacedDecorEntry entry in save.Grove.PlacedDecor)
                {
                    if (entry == null || entry.HabitatId != SelectedHabitatId)
                    {
                        continue;
                    }

                    DecorData data = library.Decor(entry.DecorId);
                    Slots.Add(new DecorSlotRow { Index = index++, DecorId = entry.DecorId, DisplayName = data?.DisplayName ?? entry.DecorId });
                }

                for (; index < selected.SlotCount; index++)
                {
                    Slots.Add(new DecorSlotRow { Index = index });
                }

                foreach (string decorId in save.Grove.UnlockedDecorIds)
                {
                    if (save.Grove.IsPlaced(decorId))
                    {
                        continue;
                    }

                    DecorData data = library.Decor(decorId);
                    if (data == null || (!string.IsNullOrEmpty(data.HabitatScope) && data.HabitatScope != SelectedHabitatId))
                    {
                        continue;
                    }

                    AvailableDecor.Add(new DecorAvailableRow { DecorId = decorId, DisplayName = data.DisplayName ?? decorId });
                }
            }

            Beasts.Clear();
            DateTime nowUtc = _session.Clock.UtcNow;
            TimeSpan nowMono = _session.Clock.Monotonic;
            long nowTicks = OfflineClock.UtcTicks(nowUtc);
            long nowMonoMs = OfflineClock.MonotonicMs(nowMono);
            long cooldownMs = (long)(Math.Max(1, library.Data.DailyCooldownHours) * 3600000.0);
            foreach (OwnedBeast beast in save.Beasts)
            {
                CreatureSpeciesSO species = _session.Content.Battle.GetSpecies(beast.Progress.SpeciesId);
                if (species == null)
                {
                    continue;
                }

                BeastAffinityState state = save.Grove.FindAffinity(beast.BeastId);
                int tier = state?.Tier ?? 0;
                AffinityTierData next = library.Tier(species.SpeciesId, tier + 1);
                bool canFeed = state == null || state.LastFeedUtcTicks <= 0 ||
                               OfflineClock.ElapsedMs(state.LastFeedUtcTicks, state.LastFeedMonotonicMs, nowTicks, nowMonoMs, out bool _) >= cooldownMs;
                bool canPlay = state == null || state.LastPlayUtcTicks <= 0 ||
                               OfflineClock.ElapsedMs(state.LastPlayUtcTicks, state.LastPlayMonotonicMs, nowTicks, nowMonoMs, out bool _) >= cooldownMs;
                Beasts.Add(new GladeBeastRow
                {
                    BeastId = beast.BeastId,
                    SpeciesId = species.SpeciesId,
                    Name = species.DisplayName ?? species.SpeciesId,
                    ArtKey = species.ArtKey,
                    Element = RosterViewModel.PrimaryElement(species),
                    Tier = tier,
                    Xp = state?.Xp ?? 0,
                    NextTierXp = next?.XpThreshold ?? 0,
                    CanFeed = canFeed,
                    CanPlay = canPlay,
                    PendingGifts = state?.PendingGifts.Count ?? 0,
                    TintHex = ColourFormPresentation.WornTint(save, beast.BeastId, species.SpeciesId, library, _session.Content.Economy?.Cosmetics)
                });
            }
        }

        public GladeActionOutcome Feed(string beastId)
        {
            GroveActionResult result = GroveRules.Feed(_session.Save, _session.Content.GroveLibrary, beastId, _session.Clock.UtcNow, _session.Clock.Monotonic,
                                                       _session.Content.Economy?.Cosmetics);
            return Apply(result.Success, result.Success ? "Fed! +" + result.XpGained + " affinity XP." + (result.TierUp ? " Tier " + result.Tier + "!" : string.Empty) : result.Error);
        }

        public GladeActionOutcome Play(string beastId)
        {
            GroveActionResult result = GroveRules.Play(_session.Save, _session.Content.GroveLibrary, beastId, _session.Clock.UtcNow, _session.Clock.Monotonic,
                                                       _session.Content.Economy?.Cosmetics);
            return Apply(result.Success, result.Success ? "Played! +" + result.XpGained + " affinity XP." + (result.TierUp ? " Tier " + result.Tier + "!" : string.Empty) : result.Error);
        }

        public GladeActionOutcome CollectGift(string beastId)
        {
            GiftInstance gift = GroveRules.CollectGift(_session.Save, beastId, _session.Content.Economy?.Cosmetics);
            return Apply(gift != null, gift != null ? "Gift collected: " + gift.ItemId + "." : "No gift waiting.");
        }

        public GladeActionOutcome CollectAllGifts(string beastId)
        {
            List<GiftInstance> collected = new List<GiftInstance>();
            int count = GroveRules.CollectAllGifts(_session.Save, beastId, collected, _session.Content.Economy?.Cosmetics);
            return Apply(count > 0, count > 0 ? "Collected " + count + " gift" + (count == 1 ? string.Empty : "s") + "." : "No gifts waiting.");
        }

        public GladeActionOutcome PlaceDecor(string decorId)
        {
            if (string.IsNullOrEmpty(SelectedHabitatId))
            {
                return GladeActionOutcome.Of(false, "No habitat unlocked yet.");
            }

            GroveActionResult result = GroveRules.PlaceDecor(_session.Save, _session.Content.GroveLibrary, SelectedHabitatId, decorId, 0f, 0f, 0);
            return Apply(result.Success, result.Success ? "Placed." : result.Error);
        }

        public GladeActionOutcome RemoveDecor(string decorId)
        {
            bool removed = GroveRules.RemoveDecor(_session.Save, decorId);
            return Apply(removed, removed ? "Put away (still owned)." : "That is not placed.");
        }

        private GladeActionOutcome Apply(bool success, string message)
        {
            if (success)
            {
                _session.Autosave(AutosaveReason.PlayerEdit);
            }

            Refresh();
            return GladeActionOutcome.Of(success, message);
        }
    }

    // ================================================================================================
    // Garden
    // ================================================================================================

    /// <summary>One Wildgarden plot: empty, growing, or ready.</summary>
    public sealed class PlotRow
    {
        public int PlotId;

        /// <summary>Null for an empty plot.</summary>
        public string SeedId;

        public string SeedName;

        /// <summary>0-1; 1 once ready.</summary>
        public double Progress;

        public bool Ready;

        /// <summary>The variety a lone harvest of this plot would yield (its self-pair).</summary>
        public string SelfVarietyId;
    }

    /// <summary>A seed the Garden's plant picker can offer.</summary>
    public sealed class SeedOptionRow
    {
        public string SeedId;
        public string DisplayName;
        public int GrowthHours;
    }

    /// <summary>One herbarium entry.</summary>
    public sealed class HerbariumRow
    {
        public string VarietyId;
        public string DisplayName;
        public string Entry;
        public bool Discovered;
    }

    /// <summary>One crafting recipe, and whether it can be made right now.</summary>
    public sealed class RecipeRow
    {
        public string RecipeId;
        public string OutputDisplay;
        public string Output;
        public List<string> InputsText = new List<string>();
        public bool CanCraft;
    }

    /// <summary>One counted Grove item in the inventory view.</summary>
    public sealed class GroveItemRow
    {
        public string ItemId;
        public string DisplayName;
        public int Quantity;
    }

    /// <summary>What a Garden action reported, for a toast.</summary>
    public sealed class GardenActionOutcome
    {
        public bool Success;
        public string Message;

        internal static GardenActionOutcome Of(bool success, string message)
        {
            return new GardenActionOutcome { Success = success, Message = message };
        }
    }

    /// <summary>
    /// The Garden tab (docs/design/grove.md, D4): plots (empty -> plant picker; growing -> progress;
    /// ready -> harvest, or pick two ready plots to cross-pollinate), the herbarium, the crafting
    /// recipes and the Grove item inventory. A pure view over <see cref="GardenRules"/> plus the
    /// session's actions, re-read on <see cref="Refresh"/>.
    /// </summary>
    public sealed class GardenViewModel
    {
        private readonly GameSession _session;
        private int _crossFirstPlotId = -1;

        public GardenViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Refresh();
        }

        public List<PlotRow> Plots { get; } = new List<PlotRow>();

        public List<SeedOptionRow> AvailableSeeds { get; } = new List<SeedOptionRow>();

        public List<HerbariumRow> Herbarium { get; } = new List<HerbariumRow>();

        public List<RecipeRow> Recipes { get; } = new List<RecipeRow>();

        public List<GroveItemRow> Inventory { get; } = new List<GroveItemRow>();

        /// <summary>The first of two ready plots picked for cross-pollination, or -1 (none picked yet).</summary>
        public int CrossFirstPlotId
        {
            get { return _crossFirstPlotId; }
        }

        public void Refresh()
        {
            PlayerSave save = _session.Save;
            GardenLibrary library = _session.Content.GardenLibrary;
            save.EnsureInitialized();
            DateTime nowUtc = _session.Clock.UtcNow;
            TimeSpan nowMono = _session.Clock.Monotonic;

            Plots.Clear();
            for (int plotId = 0; plotId < library.Data.PlotCount; plotId++)
            {
                PlotState plot = save.Garden.FindPlot(plotId);
                if (plot == null)
                {
                    Plots.Add(new PlotRow { PlotId = plotId });
                    continue;
                }

                SeedSpeciesData seed = library.Seed(plot.SeedId);
                CrossPollinationData self = library.Cross(plot.SeedId, plot.SeedId);
                Plots.Add(new PlotRow
                {
                    PlotId = plotId,
                    SeedId = plot.SeedId,
                    SeedName = seed?.DisplayName ?? plot.SeedId,
                    Progress = GardenRules.GrowthProgress(library, plot, nowUtc, nowMono),
                    Ready = GardenRules.IsReady(library, plot, nowUtc, nowMono),
                    SelfVarietyId = self?.ResultVarietyId
                });
            }

            if (_crossFirstPlotId >= 0 && !Plots.Exists(p => p.PlotId == _crossFirstPlotId && p.Ready))
            {
                _crossFirstPlotId = -1;
            }

            AvailableSeeds.Clear();
            foreach (SeedSpeciesData seed in library.Data.Seeds ?? new SeedSpeciesData[0])
            {
                if (seed != null && GardenRules.IsSeedUnlocked(save, seed))
                {
                    AvailableSeeds.Add(new SeedOptionRow { SeedId = seed.SeedId, DisplayName = seed.DisplayName ?? seed.SeedId, GrowthHours = seed.GrowthHours });
                }
            }

            Herbarium.Clear();
            foreach (VarietyData variety in library.Data.Varieties ?? new VarietyData[0])
            {
                if (variety == null)
                {
                    continue;
                }

                bool found = save.Garden.VarietiesDiscovered.Contains(variety.VarietyId);
                Herbarium.Add(new HerbariumRow
                {
                    VarietyId = variety.VarietyId,
                    DisplayName = found ? variety.DisplayName ?? variety.VarietyId : "???",
                    Entry = found ? variety.HerbariumEntry : "Not yet grown.",
                    Discovered = found
                });
            }

            Recipes.Clear();
            foreach (RecipeData recipe in library.Data.Recipes ?? new RecipeData[0])
            {
                if (recipe == null)
                {
                    continue;
                }

                RecipeRow row = new RecipeRow { RecipeId = recipe.RecipeId, Output = recipe.Output, OutputDisplay = OutputDisplay(library, recipe), CanCraft = true };
                foreach (RecipeInputData input in recipe.Inputs ?? new RecipeInputData[0])
                {
                    if (input == null)
                    {
                        continue;
                    }

                    int held = save.Grove.Items.GetCount(input.VarietyId);
                    row.InputsText.Add((library.Variety(input.VarietyId)?.DisplayName ?? input.VarietyId) + " " + held + "/" + input.Count);
                    row.CanCraft &= held >= input.Count;
                }

                Recipes.Add(row);
            }

            Inventory.Clear();
            foreach (GroveItemStack stack in save.Grove.Items.Items)
            {
                if (stack == null || string.IsNullOrEmpty(stack.ItemId))
                {
                    continue;
                }

                Inventory.Add(new GroveItemRow { ItemId = stack.ItemId, DisplayName = library.Variety(stack.ItemId)?.DisplayName ?? Humanize(stack.ItemId), Quantity = stack.Quantity });
            }
        }

        private static string OutputDisplay(GardenLibrary library, RecipeData recipe)
        {
            switch (recipe.Output)
            {
                case "decor":
                    return "Decor: " + recipe.OutputId;
                case "dye":
                    return "Dye: " + Humanize(recipe.OutputId);
                default:
                    return recipe.OutputId;
            }
        }

        internal static string Humanize(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return id;
            }

            string[] words = id.Split('_');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
                }
            }

            return string.Join(" ", words);
        }

        public GardenActionOutcome Plant(int plotId, string seedId)
        {
            GardenActionResult result = GardenRules.Plant(_session.Save, _session.Content.GardenLibrary, plotId, seedId, _session.Clock.UtcNow, _session.Clock.Monotonic);
            return Apply(result.Success, result.Success ? "Planted." : result.Error);
        }

        public GardenActionOutcome Harvest(int plotId)
        {
            GardenHarvestResult result = GardenRules.Harvest(_session.Save, _session.Content.GardenLibrary, plotId, _session.Clock.UtcNow, _session.Clock.Monotonic);
            if (result.Success && _crossFirstPlotId == plotId)
            {
                _crossFirstPlotId = -1;
            }

            return Apply(result.Success, result.Success ? "Harvested: " + Grown(result) + "." : result.Error);
        }

        /// <summary>Picks or completes a cross-pollination pair: the first tap remembers the plot, the second (a different, ready plot) harvests both.</summary>
        public GardenActionOutcome PickForCross(int plotId)
        {
            if (_crossFirstPlotId < 0)
            {
                _crossFirstPlotId = plotId;
                return GardenActionOutcome.Of(true, "Pick a second ready plot to cross-pollinate.");
            }

            if (_crossFirstPlotId == plotId)
            {
                _crossFirstPlotId = -1;
                return GardenActionOutcome.Of(true, "Unpicked.");
            }

            int first = _crossFirstPlotId;
            _crossFirstPlotId = -1;
            GardenHarvestResult result = GardenRules.HarvestPair(_session.Save, _session.Content.GardenLibrary, first, plotId, _session.Clock.UtcNow, _session.Clock.Monotonic);
            return Apply(result.Success, result.Success ? "Cross-pollinated: " + Grown(result) + "." : result.Error);
        }

        private string Grown(GardenHarvestResult result)
        {
            string name = _session.Content.GardenLibrary.Variety(result.VarietyId)?.DisplayName ?? result.VarietyId;
            return name + (result.Discovered ? " (new!)" : string.Empty);
        }

        public GardenActionOutcome Craft(string recipeId)
        {
            GardenActionResult result = GardenRules.Craft(_session.Save, _session.Content.GardenLibrary, recipeId, _session.Content.Economy?.Cosmetics);
            return Apply(result.Success, result.Success ? "Crafted." : result.Error);
        }

        private GardenActionOutcome Apply(bool success, string message)
        {
            if (success)
            {
                _session.Autosave(AutosaveReason.PlayerEdit);
            }

            Refresh();
            return GardenActionOutcome.Of(success, message);
        }
    }

    // ================================================================================================
    // Board
    // ================================================================================================

    /// <summary>A destination's state on the Board.</summary>
    public enum DestinationState
    {
        Locked,
        Available,
        Away,
        Ready
    }

    /// <summary>One Board destination card.</summary>
    public sealed class DestinationRow
    {
        public string DestinationId;
        public string DisplayName;
        public DestinationState State;
        public int DurationHours;
        public int PartySize;

        /// <summary>Hours left while <see cref="State"/> is <see cref="DestinationState.Away"/>.</summary>
        public double HoursRemaining;

        public List<string> AwayBeastNames = new List<string>();
    }

    /// <summary>One owned beast in the Board's send picker. Sending never locks it — it stays fully available for battle (producer decision).</summary>
    public sealed class BoardBeastOptionRow
    {
        public string BeastId;
        public string Name;
        public string ArtKey;
    }

    /// <summary>What a Board action reported, for a toast.</summary>
    public sealed class BoardActionOutcome
    {
        public bool Success;
        public string Message;

        internal static BoardActionOutcome Of(bool success, string message)
        {
            return new BoardActionOutcome { Success = success, Message = message };
        }
    }

    /// <summary>
    /// The Board tab (docs/design/grove.md, D4): destination cards (locked/available/away/ready), send
    /// with a party picker (beasts stay available for battle — no lock), collect with a result toast.
    /// A pure view over <see cref="ExpeditionRules"/> plus the session's actions, re-read on
    /// <see cref="Refresh"/>.
    /// </summary>
    public sealed class BoardViewModel
    {
        private readonly GameSession _session;

        public BoardViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Refresh();
        }

        public List<DestinationRow> Destinations { get; } = new List<DestinationRow>();

        public List<BoardBeastOptionRow> Beasts { get; } = new List<BoardBeastOptionRow>();

        public void Refresh()
        {
            PlayerSave save = _session.Save;
            ExpeditionLibrary library = _session.Content.ExpeditionLibrary;
            save.EnsureInitialized();
            DateTime nowUtc = _session.Clock.UtcNow;
            TimeSpan nowMono = _session.Clock.Monotonic;

            Destinations.Clear();
            foreach (DestinationData destination in library.Data.Destinations ?? new DestinationData[0])
            {
                if (destination == null)
                {
                    continue;
                }

                bool unlocked = ExpeditionRules.IsDestinationUnlocked(save, destination);
                ActiveExpedition active = save.Expeditions.FindActive(destination.DestinationId);
                DestinationRow row = new DestinationRow
                {
                    DestinationId = destination.DestinationId,
                    DisplayName = destination.DisplayName ?? destination.DestinationId,
                    DurationHours = destination.DurationHours,
                    PartySize = destination.PartySize
                };

                if (!unlocked)
                {
                    row.State = DestinationState.Locked;
                }
                else if (active == null)
                {
                    row.State = DestinationState.Available;
                }
                else
                {
                    bool ready = ExpeditionRules.IsReturned(library, active, nowUtc, nowMono);
                    row.State = ready ? DestinationState.Ready : DestinationState.Away;
                    row.HoursRemaining = ExpeditionRules.TimeRemainingHours(library, active, nowUtc, nowMono);
                    foreach (string beastId in active.BeastIds ?? new List<string>())
                    {
                        row.AwayBeastNames.Add(_session.BeastName(save.FindBeast(beastId)));
                    }
                }

                Destinations.Add(row);
            }

            Beasts.Clear();
            foreach (OwnedBeast beast in save.Beasts)
            {
                CreatureSpeciesSO species = _session.Content.Battle.GetSpecies(beast.Progress.SpeciesId);
                if (species != null)
                {
                    Beasts.Add(new BoardBeastOptionRow { BeastId = beast.BeastId, Name = species.DisplayName ?? species.SpeciesId, ArtKey = species.ArtKey });
                }
            }
        }

        public BoardActionOutcome Send(string destinationId, IReadOnlyList<string> beastIds)
        {
            ExpeditionActionResult result = ExpeditionRules.Send(_session.Save, _session.Content.ExpeditionLibrary, destinationId, beastIds, _session.Clock.UtcNow,
                                                                 _session.Clock.Monotonic);
            return Apply(result.Success, result.Success ? "Sent." : result.Error);
        }

        public BoardActionOutcome Collect(string destinationId)
        {
            ExpeditionCollectResult result = ExpeditionRules.Collect(_session.Save, _session.Content.ExpeditionLibrary, destinationId, _session.Clock.UtcNow,
                                                                     _session.Clock.Monotonic, _session.Content.Economy?.Cosmetics);
            if (!result.Success)
            {
                return Apply(false, result.Error);
            }

            string what;
            switch (result.Kind)
            {
                case "story":
                    what = result.NewStory ? "a new story" : "a story you already know";
                    break;
                case "trinket":
                    what = _session.Content.GardenLibrary.Variety(result.Id)?.DisplayName ?? GardenViewModel.Humanize(result.Id);
                    break;
                default:
                    what = "a look";
                    break;
            }

            return Apply(true, "Expedition back: " + what + ".");
        }

        private BoardActionOutcome Apply(bool success, string message)
        {
            if (success)
            {
                _session.Autosave(AutosaveReason.PlayerEdit);
            }

            Refresh();
            return BoardActionOutcome.Of(success, message);
        }
    }

    // ================================================================================================
    // NPCs
    // ================================================================================================

    /// <summary>One NPC the Grove hub can talk to.</summary>
    public sealed class NpcSummaryRow
    {
        public string NpcId;
        public string DisplayName;
    }

    /// <summary>One NPC request.</summary>
    public sealed class NpcRequestRow
    {
        public string RequestId;
        public string Text;
        public string ItemDisplay;
        public int Count;
        public int Held;
        public bool Fulfilled;
        public bool CanFulfill;
    }

    /// <summary>The selected NPC's side story: its current chapter (or "complete"), and whether it can be advanced now.</summary>
    public sealed class SideStoryRow
    {
        public string StoryId;
        public string DisplayName;
        public int ChaptersCompleted;
        public int ChapterCount;
        public bool Complete;

        /// <summary>The current chapter's line, or null (complete, or none started).</summary>
        public string CurrentText;

        public bool CanAdvance;
        public string ItemDisplay;
        public int ItemCount;
        public int ItemHeld;
    }

    /// <summary>What an NPC action reported, for a toast.</summary>
    public sealed class NpcActionOutcome
    {
        public bool Success;
        public string Message;

        internal static NpcActionOutcome Of(bool success, string message)
        {
            return new NpcActionOutcome { Success = success, Message = message };
        }
    }

    /// <summary>
    /// The Npc tab (docs/design/grove.md, D2/D4): a talk panel showing the resolved line (marks it
    /// seen), open requests with a fulfil button (enabled only when the item is held), and side story
    /// chapter progress. A pure view over <see cref="Npc.NpcRules"/> plus the session's actions,
    /// re-read on <see cref="Refresh"/>.
    /// </summary>
    public sealed class NpcPanelViewModel
    {
        private readonly GameSession _session;

        public NpcPanelViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Refresh();
        }

        public List<NpcSummaryRow> Npcs { get; } = new List<NpcSummaryRow>();

        public string SelectedNpcId { get; private set; }

        public string TalkLine { get; private set; }

        public List<NpcRequestRow> Requests { get; } = new List<NpcRequestRow>();

        public List<SideStoryRow> SideStories { get; } = new List<SideStoryRow>();

        public void SelectNpc(string npcId)
        {
            if (Npcs.Exists(n => n.NpcId == npcId))
            {
                SelectedNpcId = npcId;
                Refresh();
            }
        }

        public void Refresh()
        {
            PlayerSave save = _session.Save;
            DialogueBook book = _session.Content.Dialogue;
            save.EnsureInitialized();

            Npcs.Clear();
            foreach (NpcData npc in book.AllNpcs)
            {
                if (npc != null && !string.IsNullOrEmpty(npc.NpcId))
                {
                    Npcs.Add(new NpcSummaryRow { NpcId = npc.NpcId, DisplayName = npc.DisplayName ?? npc.NpcId });
                }
            }

            if (string.IsNullOrEmpty(SelectedNpcId) || !Npcs.Exists(n => n.NpcId == SelectedNpcId))
            {
                SelectedNpcId = Npcs.Count > 0 ? Npcs[0].NpcId : null;
            }

            HashSet<string> facts = NpcRules.BuildFacts(save, _session.Content.GroveLibrary, _session.Content.GardenLibrary, _session.Content.ExpeditionLibrary);

            Requests.Clear();
            SideStories.Clear();
            TalkLine = null;
            if (string.IsNullOrEmpty(SelectedNpcId))
            {
                return;
            }

            DialogueLineData line = NpcRules.ResolveAndMark(save, book, SelectedNpcId, facts);
            TalkLine = line?.Text;

            foreach (RequestData request in book.RequestsFor(SelectedNpcId))
            {
                bool fulfilled = NpcRules.IsRequestFulfilled(save, request);
                if (!fulfilled && !NpcRules.IsRequestAvailable(save, request, facts))
                {
                    continue;
                }

                Requests.Add(new NpcRequestRow
                {
                    RequestId = request.RequestId,
                    Text = request.Text,
                    ItemDisplay = _session.Content.GardenLibrary.Variety(request.ItemId)?.DisplayName ?? GardenViewModel.Humanize(request.ItemId),
                    Count = request.Count,
                    Held = save.Grove.Items.GetCount(request.ItemId),
                    Fulfilled = fulfilled,
                    CanFulfill = !fulfilled && NpcRules.CanFulfillRequest(save, request, facts)
                });
            }

            foreach (SideStoryData story in book.SideStoriesFor(SelectedNpcId))
            {
                bool complete = NpcRules.IsSideStoryComplete(save, story);
                SideStoryChapterData current = NpcRules.CurrentChapter(save, story);
                SideStoryState state = save.Npc.FindSideStory(story.StoryId);
                SideStoryRow row = new SideStoryRow
                {
                    StoryId = story.StoryId,
                    DisplayName = story.DisplayName ?? story.StoryId,
                    ChaptersCompleted = state?.ChaptersCompleted.Count ?? 0,
                    ChapterCount = story.Chapters?.Length ?? 0,
                    Complete = complete
                };

                if (current != null && NpcRules.IsChapterAvailable(save, story, facts))
                {
                    row.CurrentText = current.Text;
                    row.CanAdvance = NpcRules.CanFulfillChapter(save, story, facts);
                    if (!string.IsNullOrEmpty(current.RequestItemId))
                    {
                        row.ItemDisplay = _session.Content.GardenLibrary.Variety(current.RequestItemId)?.DisplayName ?? GardenViewModel.Humanize(current.RequestItemId);
                        row.ItemCount = current.RequestCount;
                        row.ItemHeld = save.Grove.Items.GetCount(current.RequestItemId);
                    }
                }

                SideStories.Add(row);
            }
        }

        public NpcActionOutcome FulfillRequest(string requestId)
        {
            DialogueBook book = _session.Content.Dialogue;
            RequestData request = book.Request(requestId);
            HashSet<string> facts = NpcRules.BuildFacts(_session.Save, _session.Content.GroveLibrary, _session.Content.GardenLibrary, _session.Content.ExpeditionLibrary);
            NpcActionResult result = NpcRules.FulfillRequest(_session.Save, request, facts, _session.Content.Economy?.Cosmetics);
            return Apply(result.Success, result.Success ? "Given. Thank you." : result.Error);
        }

        public NpcActionOutcome AdvanceSideStory(string storyId)
        {
            DialogueBook book = _session.Content.Dialogue;
            SideStoryData story = book.SideStory(storyId);
            HashSet<string> facts = NpcRules.BuildFacts(_session.Save, _session.Content.GroveLibrary, _session.Content.GardenLibrary, _session.Content.ExpeditionLibrary);
            NpcActionResult result = NpcRules.FulfillChapter(_session.Save, story, facts, _session.Content.Economy?.Cosmetics);
            return Apply(result.Success, result.Success ? "The story continues." : result.Error);
        }

        private NpcActionOutcome Apply(bool success, string message)
        {
            if (success)
            {
                _session.Autosave(AutosaveReason.PlayerEdit);
            }

            Refresh();
            return NpcActionOutcome.Of(success, message);
        }
    }

    // ================================================================================================
    // The hub
    // ================================================================================================

    /// <summary>
    /// The Grove hub (docs/design/grove.md, D4): which inner tab (Glade/Garden/Board/Npc) is showing.
    /// The four sub view-models (<see cref="Glade"/>, <see cref="Garden"/>, <see cref="Board"/>,
    /// <see cref="Npc"/>) are built together and refreshed independently.
    /// </summary>
    public sealed class GroveHubViewModel
    {
        public GroveHubViewModel(GameSession session)
        {
            Glade = new GladeViewModel(session);
            Garden = new GardenViewModel(session);
            Board = new BoardViewModel(session);
            Npc = new NpcPanelViewModel(session);
        }

        public GroveTab Tab { get; private set; } = GroveTab.Glade;

        public GladeViewModel Glade { get; }

        public GardenViewModel Garden { get; }

        public BoardViewModel Board { get; }

        public NpcPanelViewModel Npc { get; }

        public void Select(GroveTab tab)
        {
            Tab = tab;
        }

        /// <summary>Re-reads every sub-tab (a habitat/decor unlocked elsewhere, a gift rolled, an expedition returned).</summary>
        public void RefreshAll()
        {
            Glade.Refresh();
            Garden.Refresh();
            Board.Refresh();
            Npc.Refresh();
        }
    }
}
