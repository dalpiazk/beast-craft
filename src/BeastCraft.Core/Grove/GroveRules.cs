using System;
using System.Collections.Generic;
using BeastCraft.Common;
using BeastCraft.Economy;
using BeastCraft.Progression;
using BeastCraft.Save;

namespace BeastCraft.Grove
{
    /// <summary>
    /// The Grove's rules (habitats, decor, affinity and gifts): deterministic, non-throwing,
    /// refuse-and-no-op on bad input (the <c>CosmeticRules</c>/<c>IdleRewardCalculator</c> idiom). No
    /// combat power anywhere: nothing here touches stats, damage or campaign difficulty.
    /// <para>
    /// <strong>Cooldowns and gifts</strong> are all read through <see cref="OfflineClock"/>, the same
    /// anti-tamper reconciliation idle rewards use: a clock moved back or forward is silently clamped
    /// to the provable elapsed time. A beast's first Feed/Play is never on cooldown (no anchor yet);
    /// its first <see cref="RefreshGifts"/> only starts that beast's gift clock (mirrors
    /// <c>IdleRewardCalculator</c>'s first claim), so gifts begin accruing from the visit that starts
    /// them, never retroactively.
    /// </para>
    /// </summary>
    public static class GroveRules
    {
        /// <summary>The most gifts a beast can hold uncollected at once.</summary>
        public const int MaxPendingGifts = 3;

        private const int GiftSeedStream = 0x47494654;

        private const double MsPerHour = 3600000.0;

        // ---- Unlocks ----

        /// <summary>
        /// Grants every habitat and decor piece whose condition already holds (a <c>"start"</c>/
        /// <c>"starter"</c> one, or a <c>"shrine"</c> one whose <c>UnlockId</c> is in
        /// <c>DiscoveryProgress.GroveUnlockIds</c>) and is not already owned. Idempotent — call it
        /// freely (session start, entering the Grove). Returns how many were newly granted.
        /// </summary>
        public static int RefreshUnlocks(PlayerSave save, GroveLibrary library)
        {
            if (save == null || library == null)
            {
                return 0;
            }

            save.EnsureInitialized();
            int granted = 0;
            foreach (HabitatData habitat in library.Data.Habitats ?? new HabitatData[0])
            {
                if (habitat != null && IsSourceMet(save, habitat.UnlockSource, habitat.UnlockId, "start") &&
                    AddOnce(save.Grove.HabitatsUnlocked, habitat.HabitatId))
                {
                    granted++;
                }
            }

            foreach (DecorData decor in library.Data.Decor ?? new DecorData[0])
            {
                if (decor != null && IsSourceMet(save, decor.Source, decor.UnlockId, "starter") &&
                    AddOnce(save.Grove.UnlockedDecorIds, decor.DecorId))
                {
                    granted++;
                }
            }

            return granted;
        }

        /// <summary>Whether habitat <paramref name="habitat"/> is unlocked (already granted, or its condition holds).</summary>
        public static bool IsHabitatUnlocked(PlayerSave save, HabitatData habitat)
        {
            if (save?.Grove?.HabitatsUnlocked == null || habitat == null)
            {
                return false;
            }

            return save.Grove.HabitatsUnlocked.Contains(habitat.HabitatId) || IsSourceMet(save, habitat.UnlockSource, habitat.UnlockId, "start");
        }

        /// <summary>Whether decor <paramref name="decorId"/> is owned (whether placed or not).</summary>
        public static bool IsDecorUnlocked(PlayerSave save, string decorId)
        {
            return save?.Grove?.UnlockedDecorIds != null && !string.IsNullOrEmpty(decorId) && save.Grove.UnlockedDecorIds.Contains(decorId);
        }

        private static bool IsSourceMet(PlayerSave save, string source, string unlockId, string immediateSource)
        {
            if (source == immediateSource)
            {
                return true;
            }

            return source == "shrine" && !string.IsNullOrEmpty(unlockId) && save.Discovery?.GroveUnlockIds != null && save.Discovery.GroveUnlockIds.Contains(unlockId);
        }

        // ---- Decor placement ----

        /// <summary>Places <paramref name="decorId"/> in <paramref name="habitatId"/>: refuses when not unlocked, out of scope, already placed, or the habitat's slots are full.</summary>
        public static GroveActionResult PlaceDecor(PlayerSave save, GroveLibrary library, string habitatId, string decorId, float x, float y, int rotation)
        {
            if (save == null || library == null)
            {
                return GroveActionResult.Refused("No save or Grove content.");
            }

            HabitatData habitat = library.Habitat(habitatId);
            if (habitat == null || !IsHabitatUnlocked(save, habitat))
            {
                return GroveActionResult.Refused("That habitat is not unlocked.");
            }

            DecorData decor = library.Decor(decorId);
            if (decor == null || !IsDecorUnlocked(save, decorId))
            {
                return GroveActionResult.Refused("That decor is not unlocked.");
            }

            if (!string.IsNullOrEmpty(decor.HabitatScope) && decor.HabitatScope != habitatId)
            {
                return GroveActionResult.Refused("That decor cannot be placed in this habitat.");
            }

            if (save.Grove.IsPlaced(decorId))
            {
                return GroveActionResult.Refused("That decor is already placed.");
            }

            if (save.Grove.PlacedCountIn(habitatId) >= habitat.SlotCount)
            {
                return GroveActionResult.Refused("This habitat's decor slots are full.");
            }

            save.Grove.PlacedDecor.Add(new PlacedDecorEntry { HabitatId = habitatId, DecorId = decorId, X = x, Y = y, Rotation = rotation });
            return GroveActionResult.Succeeded(0, 0);
        }

        /// <summary>Removes decor <paramref name="decorId"/> from wherever it is placed (it stays owned). Returns whether anything was removed.</summary>
        public static bool RemoveDecor(PlayerSave save, string decorId)
        {
            return save?.Grove?.PlacedDecor != null && save.Grove.PlacedDecor.RemoveAll(entry => entry != null && entry.DecorId == decorId) > 0;
        }

        // ---- Affinity ----

        /// <summary>Feeds beast <paramref name="beastId"/>: grants <c>FeedXp</c> once per <c>DailyCooldownHours</c>, applying any tier-up reward.</summary>
        public static GroveActionResult Feed(PlayerSave save, GroveLibrary library, string beastId, DateTime nowUtc, TimeSpan nowMonotonic, CosmeticLibrary cosmetics = null)
        {
            return DoAction(save, library, beastId, true, nowUtc, nowMonotonic, cosmetics);
        }

        /// <summary>Plays with beast <paramref name="beastId"/>: grants <c>PlayXp</c> once per <c>DailyCooldownHours</c>, applying any tier-up reward.</summary>
        public static GroveActionResult Play(PlayerSave save, GroveLibrary library, string beastId, DateTime nowUtc, TimeSpan nowMonotonic, CosmeticLibrary cosmetics = null)
        {
            return DoAction(save, library, beastId, false, nowUtc, nowMonotonic, cosmetics);
        }

        private static GroveActionResult DoAction(PlayerSave save, GroveLibrary library, string beastId, bool isFeed, DateTime nowUtc, TimeSpan nowMonotonic,
                                                   CosmeticLibrary cosmetics)
        {
            if (save == null || library == null)
            {
                return GroveActionResult.Refused("No save or Grove content.");
            }

            OwnedBeast beast = save.FindBeast(beastId);
            if (beast == null)
            {
                return GroveActionResult.Refused("Unknown beast.");
            }

            save.EnsureInitialized();
            BeastAffinityState state = save.Grove.AffinityOf(beastId);
            long nowTicks = OfflineClock.UtcTicks(nowUtc);
            long nowMonoMs = OfflineClock.MonotonicMs(nowMonotonic);
            long lastUtc = isFeed ? state.LastFeedUtcTicks : state.LastPlayUtcTicks;
            long lastMono = isFeed ? state.LastFeedMonotonicMs : state.LastPlayMonotonicMs;
            long cooldownMs = (long)(Math.Max(1, library.Data.DailyCooldownHours) * MsPerHour);
            if (lastUtc > 0 && OfflineClock.ElapsedMs(lastUtc, lastMono, nowTicks, nowMonoMs, out bool _) < cooldownMs)
            {
                return GroveActionResult.Refused(isFeed ? "This beast has already been fed today." : "This beast has already played today.");
            }

            if (isFeed)
            {
                state.LastFeedUtcTicks = nowTicks;
                state.LastFeedMonotonicMs = nowMonoMs;
            }
            else
            {
                state.LastPlayUtcTicks = nowTicks;
                state.LastPlayMonotonicMs = nowMonoMs;
            }

            int xp = isFeed ? library.Data.FeedXp : library.Data.PlayXp;
            state.Xp += xp;

            GroveActionResult result = GroveActionResult.Succeeded(xp, state.Tier);
            int nextTier = state.Tier + 1;
            while (nextTier <= library.MaxTier)
            {
                AffinityTierData row = library.Tier(beast.Progress.SpeciesId, nextTier);
                if (row == null || state.Xp < row.XpThreshold)
                {
                    break;
                }

                state.Tier = nextTier;
                result.TierUp = true;
                result.Tier = nextTier;
                ApplyTierReward(save, row, result, cosmetics);
                nextTier++;
            }

            return result;
        }

        private static void ApplyTierReward(PlayerSave save, AffinityTierData row, GroveActionResult result, CosmeticLibrary cosmetics)
        {
            result.RewardKind = row.RewardKind;
            result.RewardId = row.RewardId;
            switch (row.RewardKind)
            {
                case "lore":
                    AddOnce(save.Grove.LoreIds, row.RewardId);
                    break;
                case "decor":
                    AddOnce(save.Grove.UnlockedDecorIds, row.RewardId);
                    break;
                case "look":
                    if (cosmetics != null)
                    {
                        CosmeticRules.UnlockOrRefund(save, cosmetics, row.RewardId, out int _);
                    }

                    break;
            }
        }

        // ---- Gifts ----

        /// <summary>
        /// Rolls forward beast <paramref name="beastId"/>'s gift clock: how many gift intervals
        /// (<c>GroveLibrary.GiftHoursForTier</c> of its current tier) have elapsed since the clock was
        /// last checked, each rolled with pity (<see cref="RollGift"/>) into
        /// <see cref="BeastAffinityState.PendingGifts"/>, up to <see cref="MaxPendingGifts"/>. The
        /// clock advances only by the whole intervals actually turned into a gift, so time beyond the
        /// cap is never lost — it is simply waiting for a free slot (collect one to free it). The
        /// beast's very first call only starts its clock (mirrors <c>IdleRewardCalculator</c>'s first
        /// claim). Returns how many gifts were newly rolled.
        /// </summary>
        public static int RefreshGifts(PlayerSave save, GroveLibrary library, string beastId, DateTime nowUtc, TimeSpan nowMonotonic)
        {
            if (save == null || library == null)
            {
                return 0;
            }

            OwnedBeast beast = save.FindBeast(beastId);
            if (beast == null)
            {
                return 0;
            }

            save.EnsureInitialized();
            BeastAffinityState state = save.Grove.AffinityOf(beastId);
            long nowTicks = OfflineClock.UtcTicks(nowUtc);
            long nowMonoMs = OfflineClock.MonotonicMs(nowMonotonic);

            if (state.LastGiftUtcTicks <= 0)
            {
                state.LastGiftUtcTicks = nowTicks;
                state.LastGiftMonotonicMs = nowMonoMs;
                return 0;
            }

            int room = MaxPendingGifts - state.PendingGifts.Count;
            if (room <= 0)
            {
                return 0;
            }

            GiftTableData table = library.GiftTableFor(beast.Progress.SpeciesId);
            if (table == null || table.Entries == null || table.Entries.Length == 0)
            {
                return 0;
            }

            long intervalMs = (long)(library.GiftHoursForTier(Math.Max(1, state.Tier)) * MsPerHour);
            long elapsedMs = OfflineClock.ElapsedMs(state.LastGiftUtcTicks, state.LastGiftMonotonicMs, nowTicks, nowMonoMs, out bool _);
            int available = (int)(elapsedMs / Math.Max(1, intervalMs));
            int toGenerate = Math.Min(available, room);
            if (toGenerate <= 0)
            {
                return 0;
            }

            for (int i = 0; i < toGenerate; i++)
            {
                state.PendingGifts.Add(RollGift(table, state));
                state.GiftIndex++;
            }

            long consumedMs = toGenerate * intervalMs;
            state.LastGiftUtcTicks += consumedMs * TimeSpan.TicksPerMillisecond;
            if (state.LastGiftMonotonicMs >= 0 && nowMonoMs >= 0)
            {
                state.LastGiftMonotonicMs += consumedMs;
            }

            return toGenerate;
        }

        /// <summary>Collects the oldest pending gift of <paramref name="beastId"/>, applying its reward, or null (none pending).</summary>
        public static GiftInstance CollectGift(PlayerSave save, string beastId, CosmeticLibrary cosmetics = null)
        {
            BeastAffinityState state = save?.Grove?.FindAffinity(beastId);
            if (state == null || state.PendingGifts == null || state.PendingGifts.Count == 0)
            {
                return null;
            }

            GiftInstance gift = state.PendingGifts[0];
            state.PendingGifts.RemoveAt(0);
            ApplyGiftReward(save, gift, cosmetics);
            return gift;
        }

        /// <summary>Collects every pending gift of <paramref name="beastId"/>, optionally appending each to <paramref name="collected"/>. Returns how many were collected.</summary>
        public static int CollectAllGifts(PlayerSave save, string beastId, List<GiftInstance> collected = null, CosmeticLibrary cosmetics = null)
        {
            int count = 0;
            GiftInstance gift;
            while ((gift = CollectGift(save, beastId, cosmetics)) != null)
            {
                collected?.Add(gift);
                count++;
            }

            return count;
        }

        private static void ApplyGiftReward(PlayerSave save, GiftInstance gift, CosmeticLibrary cosmetics)
        {
            switch (gift.ItemKind)
            {
                case "decor":
                    AddOnce(save.Grove.UnlockedDecorIds, gift.ItemId);
                    break;
                case "lore":
                    AddOnce(save.Grove.LoreIds, gift.ItemId);
                    break;
                case "cosmetic":
                    if (cosmetics != null)
                    {
                        CosmeticRules.UnlockOrRefund(save, cosmetics, gift.ItemId, out int _);
                    }

                    break;
            }
        }

        /// <summary>
        /// One gift roll: weighted among <paramref name="table"/>'s entries, forced non-common when
        /// <see cref="BeastAffinityState.GiftPityMisses"/> has reached <c>PityAt - 1</c> consecutive
        /// common results. Deterministic: <c>LootRoller.DeriveSeed(state.GiftSeed, state.GiftIndex)</c>
        /// (the beast's gift seed is assigned on its first roll, derived from its id).
        /// </summary>
        internal static GiftInstance RollGift(GiftTableData table, BeastAffinityState state)
        {
            if (state.GiftSeed == 0)
            {
                state.GiftSeed = SeedFromBeastId(state.BeastId);
            }

            int rollSeed = LootRoller.DeriveSeed(state.GiftSeed, state.GiftIndex);
            Random rng = new Random(rollSeed);
            bool forcedNonCommon = state.GiftPityMisses >= table.PityAt - 1;
            GiftEntryData chosen = WeightedPick(rng, table.Entries, forcedNonCommon);
            state.GiftPityMisses = chosen.IsCommon ? state.GiftPityMisses + 1 : 0;
            return new GiftInstance { ItemKind = chosen.ItemKind, ItemId = chosen.ItemId };
        }

        private static GiftEntryData WeightedPick(Random rng, GiftEntryData[] entries, bool forceNonCommon)
        {
            List<GiftEntryData> pool = new List<GiftEntryData>();
            if (forceNonCommon)
            {
                foreach (GiftEntryData entry in entries)
                {
                    if (entry != null && !entry.IsCommon)
                    {
                        pool.Add(entry);
                    }
                }
            }

            if (pool.Count == 0)
            {
                foreach (GiftEntryData entry in entries)
                {
                    if (entry != null)
                    {
                        pool.Add(entry);
                    }
                }
            }

            int total = 0;
            foreach (GiftEntryData entry in pool)
            {
                total += Math.Max(0, entry.Weight);
            }

            int roll = total <= 0 ? 0 : rng.Next(total);
            int cursor = 0;
            foreach (GiftEntryData entry in pool)
            {
                cursor += Math.Max(0, entry.Weight);
                if (roll < cursor)
                {
                    return entry;
                }
            }

            return pool[pool.Count - 1];
        }

        private static int SeedFromBeastId(string beastId)
        {
            int hash = 17;
            foreach (char c in beastId ?? string.Empty)
            {
                hash = unchecked((hash * 31) + c);
            }

            int seed = LootRoller.DeriveSeed(hash, GiftSeedStream);
            return seed == 0 ? 1 : seed;
        }

        private static bool AddOnce(List<string> list, string id)
        {
            if (list == null || string.IsNullOrEmpty(id) || list.Contains(id))
            {
                return false;
            }

            list.Add(id);
            return true;
        }

        // ---- Colour forms (D3) ----

        /// <summary>
        /// Spends <paramref name="library"/>'s colour form <paramref name="colourFormId"/>'s
        /// <see cref="ColourFormData.ItemId"/>/<see cref="ColourFormData.ItemCount"/> from
        /// <see cref="GroveProgress.Items"/> to unlock its cosmetic option account-wide
        /// (<c>Economy.CosmeticRules.UnlockOrRefund</c>) — reusing the existing per-species cosmetic
        /// shape rather than a new save field (docs/design/grove.md, "Colour evolutions"): the
        /// cleanest model for "owned per species, switchable per beast", since
        /// <c>Economy.CosmeticRules.TrySetOption</c> already does exactly that. Refused for an
        /// unknown form or not enough of its item; consuming it when it is already owned still
        /// spends the item but grants <see cref="CosmeticLibrary.DuplicateLookTokens"/> look tokens
        /// instead of wasting it (the same "don't waste a found reward" stance as every other Grove
        /// unlock). Applying/switching an owned form on a specific beast is
        /// <c>Economy.CosmeticRules.TrySetOption</c> directly — no separate method here.
        /// </summary>
        public static ColourFormResult TryUnlockColourForm(PlayerSave save, GroveLibrary library, CosmeticLibrary cosmetics, string colourFormId)
        {
            if (save == null || library == null || cosmetics == null)
            {
                return ColourFormResult.Refused("No save or content.");
            }

            ColourFormData form = library.ColourForm(colourFormId);
            if (form == null)
            {
                return ColourFormResult.Refused("Unknown colour form.");
            }

            save.EnsureInitialized();
            if (!save.Grove.Items.TryConsume(form.ItemId, form.ItemCount))
            {
                return ColourFormResult.Refused("Not enough " + form.ItemId + ".");
            }

            string key = CosmeticCollection.Key(form.CosmeticCategoryId, form.CosmeticOptionId);
            bool newlyUnlocked = CosmeticRules.UnlockOrRefund(save, cosmetics, key, out int tokensGranted);
            return ColourFormResult.Succeeded(newlyUnlocked, tokensGranted);
        }
    }

    /// <summary>What <see cref="GroveRules.TryUnlockColourForm"/> did.</summary>
    public sealed class ColourFormResult
    {
        public bool Success { get; private set; } = true;

        public string Error { get; private set; }

        /// <summary>Whether the form was newly unlocked (false when it was already owned — see <see cref="TokensGranted"/>).</summary>
        public bool NewlyUnlocked { get; private set; }

        /// <summary>Look tokens granted instead, when the form was already owned; 0 otherwise.</summary>
        public int TokensGranted { get; private set; }

        internal static ColourFormResult Succeeded(bool newlyUnlocked, int tokensGranted)
        {
            return new ColourFormResult { NewlyUnlocked = newlyUnlocked, TokensGranted = tokensGranted };
        }

        internal static ColourFormResult Refused(string error)
        {
            return new ColourFormResult { Success = false, Error = error };
        }
    }

    /// <summary>What a Grove action (<see cref="GroveRules.Feed"/>, <see cref="GroveRules.Play"/>, <see cref="GroveRules.PlaceDecor"/>) did.</summary>
    public sealed class GroveActionResult
    {
        public bool Success { get; private set; } = true;

        public string Error { get; private set; }

        /// <summary>Affinity XP gained (0 for a non-affinity action, e.g. <see cref="GroveRules.PlaceDecor"/>).</summary>
        public int XpGained { get; private set; }

        /// <summary>The beast's tier after the action.</summary>
        public int Tier { get; internal set; }

        /// <summary>Whether the action crossed into a new tier.</summary>
        public bool TierUp { get; internal set; }

        /// <summary>The highest tier reached's reward kind ("lore", "decor", "look", "idle_anim" or "none"), or null when no tier-up.</summary>
        public string RewardKind { get; internal set; }

        /// <summary>The reward's id, or "" ( <c>idle_anim</c>/<c>none</c>), or null when no tier-up.</summary>
        public string RewardId { get; internal set; }

        internal static GroveActionResult Succeeded(int xpGained, int tier)
        {
            return new GroveActionResult { XpGained = xpGained, Tier = tier };
        }

        internal static GroveActionResult Refused(string error)
        {
            return new GroveActionResult { Success = false, Error = error };
        }
    }
}
