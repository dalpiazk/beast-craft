# Avatar, Inventory and Shop/Trader: the out-of-combat screens

**Status: BUILT.** The three screens `screens.md`'s "Known gaps" still called out: the full Beastbinder
screen (skills, gear, wardrobe), Inventory (materials, consumables, spare gear), and the Trader (shop
map nodes and the camp's travelling trader). No new game mechanic and no balance change: every screen
is presentation over Core rules that already existed (`AvatarProgression`, `SkillBook`, `GearRules`,
`CosmeticRules`, `ShopService`) — the one genuine gap was wiring, not rules (below). The tuned and
campaign-pacing reports are unaffected.

## 0. What already existed (the survey this PR started from)

Avatar progression, skills and gear were already fully built in Core with no UI over them:
`AvatarProgress`/`AvatarProgression` (level, XP, the same curve shape as a beast's, no level cap),
`AvatarSkillBook` (`Actives`/`Passives`, each a `SkillBook`: `Known`, `Equipped[3]`, `Learn`, `Equip`,
`Unequip`, `SwapSlots` — the identical API a beast's skill book uses), `AvatarGearSO`/`AvatarGearSlot`
(`Weapon`/`Armor`/`Trinket`) and `GearRules.EquipAvatarGear`/`UnequipAvatarGear` (the same
`GearEquipResult` shape as a beast's, minus `EquippedElsewhere`: avatar gear has one owner by
construction). `CampaignAvatar.Profile(content)` is the stat fixture battle already uses (HP 1, every
combat stat 100 at max level, borrowed growth curve) — the Avatar screen reuses it rather than
inventing a second stat source, so its numbers are exactly what a battle would use.
Cosmetics/wardrobe rules were also complete and already avatar-aware: every `CosmeticRules` method
takes `beastId: null` to mean the avatar (`AppearanceOf`, `Worn`, `TrySetOption`, `TrySetColor`), and
`CosmeticCategory.IsAvatar`/`CosmeticLibrary.AvatarScope` already separate the avatar's categories
(hair, outfit, headwear, cape, hair/skin/eye colour) from a species' — but **nothing called any of it
with a null beast id**: the avatar wardrobe was unused plumbing. Likewise the economy: `ShopService`/
`IShopService` (`TryBuy`, `TrySellGear`, `GetStock`) and `CampaignRules.Trade`/`ShopContextFor` were
fully built and tested (`Tooling/EditModeTests/Tests/ShopTests.cs`,
`Tooling/BalanceSim/CampaignEconomyModel.cs`), but **`GameContent` never built a `ShopService`** — a
Shop map node's tap fell straight to `MapTapKind.ComingSoon`, and the camp's travelling trader
(economy-and-shop.md, "The Trader") was never opened from any screen. Inventories
(`MaterialInventory`, `GearInventory`, `PlayerSave.Consumables`, `Grove.GroveItemInventory`,
`PlayerSave.LookTokens`) held real data with no screen reading them beyond the beast detail screen's
own worn-gear listing and the look-token shop.

**The one infrastructure gap this PR closes (flagged, not a game mechanic):** `GameContent` now loads
`content/data/Economy/shop-tables.json` and builds a `ShopService` (`GameContent.Shop`), the same
`Read`/`Prefix(errors, ...)`/build pattern every other library already follows (mirrors how
`EconomyContent` itself is assembled). Nothing about the shop's rules, prices or stock rolls changed —
this is the load-time wiring `ShopService` always needed and never had.

## 1. Avatar screen

Replaces the minimal identity card (`HomeScreen.DrawAvatar`) with `AvatarScreen`, pushed from the
Avatar tab exactly the way Grove pushes `GroveScreen` (`HomeScreen.SelectTab`) rather than drawing
inline. Four inner tabs (`AvatarTab`), driven by `AvatarHubViewModel` over four child view-models
(`src/BeastCraft.Presentation/Screens/AvatarViewModels.cs`), the same shape as `GroveHubViewModel`:

- **Overview**: level and XP bar (`AvatarProgression.XpToNextLevel`), the titled display name
  (`AchievementsViewModel.TitledName`, reused rather than re-derived — the same "Beastbinder" text the
  Results screen already shows), the current stat block — base (`CampaignAvatar.Profile(content)
  .GetStatsAtLevel(level)`) and total with gear (`StatCalculator.ComputeStats` over
  `StatCalculator.CollectModifiers` of the worn `AvatarGearSO`s, the identical pipeline
  `BattleAvatar.Create` uses in battle, so the screen is WYSIWYG with what actually fights) — and the
  way to Achievements/titles and the Look-token shop (the same two buttons the old identity card had,
  now on this tab). Theorycrafter lens: base vs. total per stat, not just a total.
- **Skills**: the avatar's three equipped actives and three equipped passives
  (`AvatarSkillBook.Actives`/`Passives`), each slot a card built with `SkillCard.Of` (actives — the
  exact card the beast detail screen and the skill strip already use, since avatar actives and beast
  skills share one `SkillSO` id space) or a small passive card built the same way from
  `PassiveSkillSO` (trigger, target, one power line per effect via `SkillCard.PowerLine`, reused
  rather than re-implemented). *Change* opens a `ChoiceModal` of the avatar's other known
  actives/passives for that slot (`SkillBook.Equip`/`SwapSlots`, the same rule the beast detail screen
  calls) — the last skill in a book cannot be emptied. **No "learn" action here**: avatar actives and
  passives are learned only from the Trader (`AvatarSkillUnlocks`, economy-and-shop.md) exactly as
  today; this tab is the existing loadout rule (equip/swap among what is already known), not a new
  acquisition path.
- **Gear**: the three avatar slots (Weapon/Armor/Trinket), each showing what is worn (name, bonuses
  via `DerivedStats.ShortName`, the same bonus-line format `BeastDetailViewModel.View` already builds
  for beast gear) and the player's other owned avatar gear for that slot, with Equip/Take off calling
  `GearRules.EquipAvatarGear`/`UnequipAvatarGear` against `_session.Content.Battle` (the
  `ISaveGearCatalog`) — the identical call shape `BeastDetailViewModel.EquipGear` already uses for a
  beast, generalized to the avatar's own three slots.
- **Wardrobe**: every avatar cosmetic category (`library.Categories` where `IsAvatar`) — discrete
  categories (hair, outfit, headwear, cape) as owned/locked chips with Wear (`CosmeticRules
  .TrySetOption(save, null, categoryId, optionId, library)`, the same call `BeastDetailViewModel
  .WearColourForm` already makes with a beast id, here with `null` for the avatar — the established
  meaning throughout `CosmeticRules`); colour categories (hair/skin/eye colour) as a small fixed swatch
  row (six curated hex presets per category, held in the view-model, not new content) calling
  `CosmeticRules.TrySetColor(save, null, categoryId, color, library)` — colours are always free.
  **No colour-picker widget exists in the toolkit** (`Widgets.cs` has no such control), so a full RGB
  picker is out of scope; the swatch row is the minimal UI the existing free-colour rule needs and is
  flagged below for a producer call on whether a real picker is wanted later. Locked looks read the
  same "Owned/locked" language the look-token shop and colour-forms UI already use, with a link to the
  Look-token shop for `TokenPurchasable` ones. Collector lens: wardrobe as its own tab, mirroring the
  Compendium's "owned vs. locked" framing.

## 2. Inventory screen

`InventoryScreen`, pushed from the Inventory tab the same way. Three inner tabs
(`InventoryTab`/`InventoryHubViewModel`, `src/BeastCraft.Presentation/Screens/InventoryViewModels.cs`):

- **Gear**: every owned gear instance (beast and avatar), filter (All / Beast / Avatar / by slot) and
  sort (Rarity, Level, Name — mirroring the Roster tab's own Sort chip cycle) chips; each row shows
  name, slot, rarity, bonuses, and "Worn by `<beast name>`" / "Worn (avatar)" / "Unworn". **Equip**
  opens a `BeastPickerModal`: for beast gear, one option per owned beast (`GearRules.EquipBeastGear`,
  the rule already validates level/slot and reports why not); for avatar gear, it equips directly
  (`GearRules.EquipAvatarGear`, one target, no picker needed). **Sell** (unworn gear only —
  `ShopService.TrySellGear` refuses worn gear itself, `Equipped`) uses the newly-wired
  `GameSession.Content.Shop`, gold from `ShopSaleResult.Gold`, at the existing 25% sell-back rule —
  reachable from anywhere, not only at a Trader, since `TrySellGear` needs no `ShopContext`. Materials
  and consumables have **no sell action**: no Core rule exists for it (economy-and-shop.md never
  proposed one), matching the instruction to add a UI action only where a rule already exists.
- **Materials**: held skill materials (`MaterialInventory`), Grove items (`Grove.GroveItemInventory`)
  and consumables (`PlayerSave.Consumables`, with each `ConsumableSO`'s effect line via the same
  `EconomyContent.Consumables` lookup the encounter screen's consumable picker uses) as three simple
  counted lists — read-only, since materials are spent through skill training (the beast detail
  screen), Grove items through the Wildgarden/Board/Npc screens, and consumables are chosen at the
  Encounter screen, not "used" from Inventory: no Core rule exists for using any of them from here,
  so none is added. Casual Idler lens: one glance at what is held, nothing to fiddle with.
- **Looks**: the look-token balance (`PlayerSave.LookTokens`) with a button to the existing
  Look-token shop screen, and a per-category owned/total count across every category (avatar and
  every species) — a small Collector "collection" summary in the same spirit as the Compendium's
  completion bar, reading `CosmeticCollection.Has`/`CosmeticLibrary.Categories` only (no new state).

**"New" markers.** Flagged, not built: a true "new since you last looked" marker needs a persisted
"seen" set (there is no such save field or convention anywhere in the codebase today — confirmed by
survey), which is new save state the brief's "no new mechanics unless a screen truly needs a missing
Core rule, then keep it minimal and flag it" clause is exactly about. Cheaply derivable from existing
data alone would only be "unworn", which already has its own clear label on the Gear tab (a piece kept
unworn on purpose is not "new") — so rather than mislabel it, this PR ships without a "new" badge and
leaves it as a producer decision (a `HashSet<string>` of seen gear instance ids, mirroring
`CosmeticCollection.Unlocked`'s shape, would be the minimal schema addition if wanted).

## 3. Shop/Trader screen

`ShopScreen` (`src/BeastCraft.Presentation/Screens/ShopViewModel.cs`,
`src/BeastCraft.Game/Screens/ShopScreens.cs`), reached by tapping a Shop map node
(`MapTapKind.Shop`, new — `MapViewModel.Tap` no longer answers a Shop node with `ComingSoon`) and from
the Camp modal's new "Trade" button (`CampModal`, the camp's travelling trader,
economy-and-shop.md: "every camp has a travelling trader too"). Two inner tabs:

- **Stock**: every category the Trader currently offers (`ShopService.GetStock`, which freezes/reads
  the node's stock the first time it is opened, unchanged), grouped by category header, each listing
  showing name, price, remaining (or "Sold out"), and a disabled reason (not enough gold, sold out,
  already known/unlocked, can't carry more, too low level) read from `ShopService.CanBuy(save,
  context, listingIndex)` — a genuine non-mutating dry run over the same `Check` helper `TryBuy`
  itself calls (added this round: one source of truth, so the disabled reason can never drift from
  what a real purchase attempt would say), never a second mutating call. A `BeastSkill` (tome)
  listing's row skips this (its eligibility is per beast, not generic — only stock/gold show) and
  **Buy** opens a `BeastPickerModal` of owned beasts first (`ShopService.TryBuy` itself reports
  `IneligibleTarget` for a beast that cannot learn it, rather than the screen pre-filtering, matching
  how gear equip errors are surfaced elsewhere); every other category calls
  `ShopService.TryBuy(save, context, listingIndex, targetBeastId)` directly. Results toast a short
  message per `ShopOutcome`.
- **Sell**: unworn owned gear, reusing the same rows/rule as the Inventory Gear tab's Sell action
  (`ShopService.TrySellGear`) — kept as its own tab here since a Trader visit is the moment a player
  is naturally clearing out spares, matching the existing UI pattern of a screen offering the same Core
  action from more than one entry point (look-token spending is reachable from both the Avatar tab and
  a beast's Gear & bonds tab today).

**Opening a Shop node** calls `CampaignRules.Trade(save, regions, nodeId, session.Content.Shop)` once
on `Enter()` (rolls/freezes stock, marks the node visited/cleared, reveals fog around it — unchanged
Core behaviour), then every Buy/Sell reads/writes through `CampaignRules.ShopContextFor(run, node)`,
recomputed each time (it is derived, not random, so nothing needs to be cached). The camp trader opens
the same screen over the camp's own node (`ShopContextFor(run, campNode)`) **without** calling `Trade`
(`Trade` refuses a non-Shop node type by design; the camp trader was already meant to skip it per
economy-and-shop.md, "The Trader") — `ShopService.Open`/`GetStock` alone roll/freeze its stock.

## 4. Persona lens summary

- **Theorycrafter**: Avatar Overview's base-vs-total stat block, Skills tab's full `SkillCard`
  targeting/power text (identical to a beast's), Inventory Gear's bonus lines and disabled-reason text.
- **Collector**: Avatar Wardrobe (owned/locked chips, colour swatches), Inventory Looks (per-category
  owned/total, look-token balance), gear rarity shown throughout.
- **Casual Idler**: Inventory Materials tab is a single glance, no actions to manage; Shop's Buy/Sell
  are one tap with a clear disabled reason and a toast result; nothing here is timed or requires a
  return visit.

## 5. Debug screens

`--screen avatar` (Overview tab), `--screen inventory` (Gear tab), `--screen shop` (walks the current
expedition — camping/trading through the way, as `--screen soothe` already does — until a Shop node is
reachable, then opens it), mirroring `BeastCraftGame.GoTo`'s existing `grove-*` dispatch pattern.

## 6. Decisions for the producer to review

- The Wardrobe tab's six-swatch colour presets are UI-only curation over the existing free
  `TrySetColor` rule, not authored content; a producer may prefer a full colour picker later (needs a
  new toolkit widget) or a curated per-category palette in content instead of a fixed six.
- No "new" marker for Inventory (above) — flagged as a minimal save addition if wanted, not built.
- The camp trader's "Trade" button opens the same `ShopScreen`; a producer may prefer the camp modal to
  show a compact inline preview instead of a full screen hop.
