# Verdant Hollow art slots (painted presentation)

The exact files the art lane paints for the Hollow presentation pass. Every slot below already exists as a
generated **placeholder** (tagged `BeastCraft-Placeholder` in its PNG text chunk, written by
`Tooling/PixelArt/painted_placeholders.py`), is listed in the art manifest, and is drawn by the game today, so a
painting drops in as a file: overwrite the placeholder at the same path, run
`Tooling/PixelArt/.venv/Scripts/python Tooling/PixelArt/build.py`, commit the PNG (Git LFS) and the regenerated
manifest. No code or data change is needed; the placeholder script never overwrites an untagged (painted) file.
The one exception is the skill icons (section 3): their stand-ins are the generated 24 x 24 pixel icons, so a
painted icon is a new file at its slot's path, which `build.py` picks up by name.

**For every slot:** PNG, 8-bit sRGB RGBA, **straight (unpremultiplied) alpha** (the game premultiplies on load; a
slot shipped premultiplied instead says `"Premultiplied": true` on its `illustrated.json` Painted entry), drawn with
**linear filtering and mipmaps**, so paint at the size given and keep edges soft rather than pixel-hard. Sizes are
the final sizes (placeholders are half size where noted: the manifest sizes art by `WorldWidth`, backdrops by
fractions, so the final resolution drops in without data changes). Pivot = the point the game places; all
slots are pivoted at their **centre** unless a row says otherwise. VFX and UI frames are painted **in their own
colours** (the data does not tint them).

Tick a box when the painted file has landed.

## 1. Battle backdrops (18)

The Verdant Hollow (r01) keeps six paintings, each recomposed three times, once per arena size: the sunlit
meadows sun0, sun1, sun3, the ruins ruin0, ruin2 and the dusk glade dusk2. Portrait **1440 x 2560** (9:16), opaque.
The board (the arena's hex tiles) sits inside the painting at the **board rect** below; everything around it is
decorative margin that runs under the HUD to the screen edges (the top ~14% and bottom ~26% sit under the turn
order and the skill strip, behind a dark scrim), with a 4% bleed past the canvas on every side for the screen shake.
Paint the ground inside the board rect as walkable, fairly even terrain (the game draws a faint hex grid, the
deployment zones and the units over it: enemy rows at the top, the player's at the bottom); keep strong detail and
landmarks in the margins. The camera zooms up to about 2x into the board, so the board area should hold up at
that zoom.

**Obstacles.** Each painting's obstacles are battle data (`content/data/Encounters/battle-layouts.json`): they
block movement and standing (never line of sight). Paint one obstacle on **exactly** each listed hex (Q, R axial;
R < 0 the enemy side), sitting inside its hex and reading clearly as impassable, and **no other** prop that looks
like one anywhere on the board: rocks on the sunlit meadows, fallen stone rubble or broken pillar stumps on the ruins,
mossy boulders or a fallen bough on the dusk glade (a bough spans only its own listed hexes). Review overlays with
the exact hexes: `python Tooling/ArtLab/scripts/layout_overlay.py <painting.png> --layout <ArtKey>` (standard
library plus Pillow), or `--all [--images <folder of <id>_preview.png>]`; the placeholders already show them.

| Done | File | Arena | Board rect in px (x, y, w x h) | Board rect (fractions) | Obstacles (Q, R) | Motif |
| --- | --- | --- | --- | --- | --- | --- |
| [ ] | `content/art/backdrops/r01/sun0/small.png` | Small (5 x 7) | 199, 649, 1041 x 1171 | 0.13849, 0.25333, 0.72302 x 0.45754 | (-2, 0) (2, 0) | rocks |
| [ ] | `content/art/backdrops/r01/sun0/medium.png` | Medium (8 x 11) | 165, 582, 1110 x 1248 | 0.11469, 0.22751, 0.77062 x 0.48766 | (-2, -1) (4, -1) (-2, 1) (2, 1) | rocks |
| [ ] | `content/art/backdrops/r01/sun0/large.png` | Large (11 x 15) | 147, 548, 1146 x 1289 | 0.10216, 0.21392, 0.79567 x 0.50351 | (-3, -2) (5, -2) (-4, 2) (2, 2) (2, -3) (-2, 3) | rocks |
| [ ] | `content/art/backdrops/r01/sun1/small.png` | Small (5 x 7) | 199, 649, 1041 x 1171 | 0.13849, 0.25333, 0.72302 x 0.45754 | (0, -1) (1, 1) | rocks |
| [ ] | `content/art/backdrops/r01/sun1/medium.png` | Medium (8 x 11) | 165, 582, 1110 x 1248 | 0.11469, 0.22751, 0.77062 x 0.48766 | (-4, 0) (3, 0) (1, -2) (-2, 2) | rocks |
| [ ] | `content/art/backdrops/r01/sun1/large.png` | Large (11 x 15) | 147, 548, 1146 x 1289 | 0.10216, 0.21392, 0.79567 x 0.50351 | (-3, -1) (3, 1) (0, -3) (0, 3) (-1, 0) | rocks |
| [ ] | `content/art/backdrops/r01/sun3/small.png` | Small (5 x 7) | 199, 649, 1041 x 1171 | 0.13849, 0.25333, 0.72302 x 0.45754 | (-1, -1) (2, 1) | rocks |
| [ ] | `content/art/backdrops/r01/sun3/medium.png` | Medium (8 x 11) | 165, 582, 1110 x 1248 | 0.11469, 0.22751, 0.77062 x 0.48766 | (-1, -2) (2, -1) (-2, 1) (1, 2) (0, 0) | rocks |
| [ ] | `content/art/backdrops/r01/sun3/large.png` | Large (11 x 15) | 147, 548, 1146 x 1289 | 0.10216, 0.21392, 0.79567 x 0.50351 | (-2, -2) (4, -2) (-4, 2) (2, 2) (0, -1) (0, 1) | rocks |
| [ ] | `content/art/backdrops/r01/ruin0/small.png` | Small (5 x 7) | 199, 649, 1041 x 1171 | 0.13849, 0.25333, 0.72302 x 0.45754 | (-1, 0) (1, 0) | fallen stone rubble or broken pillar stumps |
| [ ] | `content/art/backdrops/r01/ruin0/medium.png` | Medium (8 x 11) | 165, 582, 1110 x 1248 | 0.11469, 0.22751, 0.77062 x 0.48766 | (-2, -1) (3, -1) (-3, 1) (2, 1) (0, 0) | fallen stone rubble or broken pillar stumps |
| [ ] | `content/art/backdrops/r01/ruin0/large.png` | Large (11 x 15) | 147, 548, 1146 x 1289 | 0.10216, 0.21392, 0.79567 x 0.50351 | (-2, -2) (3, -2) (-4, 2) (1, 2) (-4, 0) (4, 0) | fallen stone rubble or broken pillar stumps |
| [ ] | `content/art/backdrops/r01/ruin2/small.png` | Small (5 x 7) | 199, 649, 1041 x 1171 | 0.13849, 0.25333, 0.72302 x 0.45754 | (1, -1) (-1, 1) | fallen stone rubble or broken pillar stumps |
| [ ] | `content/art/backdrops/r01/ruin2/medium.png` | Medium (8 x 11) | 165, 582, 1110 x 1248 | 0.11469, 0.22751, 0.77062 x 0.48766 | (-2, -2) (0, -1) (1, 1) (2, 2) | fallen stone rubble or broken pillar stumps |
| [ ] | `content/art/backdrops/r01/ruin2/large.png` | Large (11 x 15) | 147, 548, 1146 x 1289 | 0.10216, 0.21392, 0.79567 x 0.50351 | (-2, -3) (-1, -2) (0, -1) (0, 1) (2, 2) (3, 3) | fallen stone rubble or broken pillar stumps |
| [ ] | `content/art/backdrops/r01/dusk2/small.png` | Small (5 x 7) | 199, 649, 1041 x 1171 | 0.13849, 0.25333, 0.72302 x 0.45754 | (0, 0) | mossy boulders or a fallen bough |
| [ ] | `content/art/backdrops/r01/dusk2/medium.png` | Medium (8 x 11) | 165, 582, 1110 x 1248 | 0.11469, 0.22751, 0.77062 x 0.48766 | (-3, 0) (2, 0) | mossy boulders or a fallen bough |
| [ ] | `content/art/backdrops/r01/dusk2/large.png` | Large (11 x 15) | 147, 548, 1146 x 1289 | 0.10216, 0.21392, 0.79567 x 0.50351 | (-3, -1) (3, 1) (-3, 2) (2, -2) | mossy boulders or a fallen bough |

Hex geometry inside the board rect: pointy-top hexes, 32 x 36 board px each, columns 32 px apart, rows 27 px apart,
odd rows shifted half a column right; the board rect is the box of every tile (the arena's `Width + 1/2` columns
wide); a hex's centre is at board px (32 Q + 16 R, 27 R) from tile (0, 0)'s. ArtKeys `backdrop/r01/<id>/<arena>`
(category `backdrop`, loaded on first use). The placeholders are 3/8 size.

## 2. Skill icon frame and rarity rings (6)

Every skill icon in the HUD (skill strip, skill detail card, the "now acting" badge on the turn order) is drawn in
three layers centred in its box, back to front: the **rarity ring** (1.00 of the box), the **icon** (0.76), the round
**frame** (0.90) over the icon's edge. All six files share one square **256 x 256** canvas, centre pivot, transparent
outside the circle. Placeholders are at final size. Guide for the circles (fractions of the canvas radius, as the
placeholders are drawn): frame band 0.82-0.985 (its inner edge covers the icon's rim), ring band 0.86-0.975 with a
soft outer glow (it shows around the frame).

| Done | File | ArtKey | What |
| --- | --- | --- | --- |
| [ ] | `content/art/ui/skill_icon/frame.png` | `ui/skill_icon/frame` | the round frame every icon sits in |
| [ ] | `content/art/ui/skill_icon/ring_common.png` | `ui/skill_icon/ring/common` | rarity ring: beast skills (default) |
| [ ] | `content/art/ui/skill_icon/ring_rare.png` | `ui/skill_icon/ring/rare` | rarity ring: avatar actives and passives (default) |
| [ ] | `content/art/ui/skill_icon/ring_epic.png` | `ui/skill_icon/ring/epic` | rarity ring: a skill given this rarity by an override |
| [ ] | `content/art/ui/skill_icon/ring_legendary.png` | `ui/skill_icon/ring/legendary` | rarity ring: a skill given this rarity by an override |
| [ ] | `content/art/ui/skill_icon/ring_gloam.png` | `ui/skill_icon/ring/gloam` | rarity ring: enemy skills (default) |

## 3. Skill icons (91)

Round painted icons, **256 x 256**, centre pivot, the motif filling the canvas's inscribed circle right to its edge
(the frame covers the outer ~3% of the radius; transparent outside the circle). Painted in the skill's element colours
(enemy skills: the Gloam's lilac/violet, since they take their unit's element in battle). **Drop-in by name:**
`build.py` finds `content/art/icons/skills/<id>.png` (enemy: `content/art/icons/skills/enemy/<enemy>_<skill>.png`),
lists it under the skill's ArtKey and retires the 24 x 24 pixel placeholder; it refuses a file no skill claims.

### Beast skills (60)

| Done | File | ArtKey | Skill | Element / kind |
| --- | --- | --- | --- | --- |
| [ ] | `content/art/icons/skills/boulder_slam.png` | `skill/boulder_slam` | Boulder Slam | Earth |
| [ ] | `content/art/icons/skills/stone_challenge.png` | `skill/stone_challenge` | Stone Challenge | Earth |
| [ ] | `content/art/icons/skills/granite_bulwark.png` | `skill/granite_bulwark` | Granite Bulwark | Earth |
| [ ] | `content/art/icons/skills/tectonic_shove.png` | `skill/tectonic_shove` | Tectonic Shove | Earth |
| [ ] | `content/art/icons/skills/quake.png` | `skill/quake` | Quake | Earth |
| [ ] | `content/art/icons/skills/stoneskin.png` | `skill/stoneskin` | Stoneskin | Earth |
| [ ] | `content/art/icons/skills/serpent_bite.png` | `skill/serpent_bite` | Serpent Bite | Water |
| [ ] | `content/art/icons/skills/undertow.png` | `skill/undertow` | Undertow | Water |
| [ ] | `content/art/icons/skills/deep_shell.png` | `skill/deep_shell` | Deep Shell | Water |
| [ ] | `content/art/icons/skills/tidal_wave.png` | `skill/tidal_wave` | Tidal Wave | Water |
| [ ] | `content/art/icons/skills/maelstrom.png` | `skill/maelstrom` | Maelstrom | Water |
| [ ] | `content/art/icons/skills/tidal_renewal.png` | `skill/tidal_renewal` | Tidal Renewal | Water |
| [ ] | `content/art/icons/skills/thorn_lash.png` | `skill/thorn_lash` | Thorn Lash | Nature |
| [ ] | `content/art/icons/skills/verdant_mend.png` | `skill/verdant_mend` | Verdant Mend | Nature |
| [ ] | `content/art/icons/skills/bark_ward.png` | `skill/bark_ward` | Bark Ward | Nature |
| [ ] | `content/art/icons/skills/entangling_roots.png` | `skill/entangling_roots` | Entangling Roots | Nature |
| [ ] | `content/art/icons/skills/spore_cloud.png` | `skill/spore_cloud` | Spore Cloud | Nature |
| [ ] | `content/art/icons/skills/lifebloom.png` | `skill/lifebloom` | Lifebloom | Nature |
| [ ] | `content/art/icons/skills/sunder.png` | `skill/sunder` | Sunder | Metal |
| [ ] | `content/art/icons/skills/iron_crush.png` | `skill/iron_crush` | Iron Crush | Metal |
| [ ] | `content/art/icons/skills/iron_fortress.png` | `skill/iron_fortress` | Iron Fortress | Metal |
| [ ] | `content/art/icons/skills/spiked_carapace.png` | `skill/spiked_carapace` | Spiked Carapace | Metal |
| [ ] | `content/art/icons/skills/shrapnel_burst.png` | `skill/shrapnel_burst` | Shrapnel Burst | Metal |
| [ ] | `content/art/icons/skills/juggernaut_charge.png` | `skill/juggernaut_charge` | Juggernaut Charge | Metal |
| [ ] | `content/art/icons/skills/rime_bolt.png` | `skill/rime_bolt` | Rime Bolt | Ice |
| [ ] | `content/art/icons/skills/deep_freeze.png` | `skill/deep_freeze` | Deep Freeze | Ice |
| [ ] | `content/art/icons/skills/frost_breath.png` | `skill/frost_breath` | Frost Breath | Ice |
| [ ] | `content/art/icons/skills/blizzard.png` | `skill/blizzard` | Blizzard | Ice |
| [ ] | `content/art/icons/skills/ice_armor.png` | `skill/ice_armor` | Ice Armor | Ice |
| [ ] | `content/art/icons/skills/absolute_zero.png` | `skill/absolute_zero` | Absolute Zero | Ice |
| [ ] | `content/art/icons/skills/thunder_talons.png` | `skill/thunder_talons` | Thunder Talons | Lightning |
| [ ] | `content/art/icons/skills/chain_lightning.png` | `skill/chain_lightning` | Chain Lightning | Lightning |
| [ ] | `content/art/icons/skills/static_charge.png` | `skill/static_charge` | Static Charge | Lightning |
| [ ] | `content/art/icons/skills/storm_dive.png` | `skill/storm_dive` | Storm Dive | Lightning |
| [ ] | `content/art/icons/skills/thunderclap.png` | `skill/thunderclap` | Thunderclap | Lightning |
| [ ] | `content/art/icons/skills/plasma_barrage.png` | `skill/plasma_barrage` | Plasma Barrage | Lightning |
| [ ] | `content/art/icons/skills/gale_talon.png` | `skill/gale_talon` | Gale Talon | Air |
| [ ] | `content/art/icons/skills/wind_lance.png` | `skill/wind_lance` | Wind Lance | Air |
| [ ] | `content/art/icons/skills/gust.png` | `skill/gust` | Gust | Air |
| [ ] | `content/art/icons/skills/tailwind.png` | `skill/tailwind` | Tailwind | Air |
| [ ] | `content/art/icons/skills/updraft.png` | `skill/updraft` | Updraft | Air |
| [ ] | `content/art/icons/skills/sky_rend.png` | `skill/sky_rend` | Sky Rend | Air |
| [ ] | `content/art/icons/skills/ember_shot.png` | `skill/ember_shot` | Ember Shot | Fire |
| [ ] | `content/art/icons/skills/flame_wave.png` | `skill/flame_wave` | Flame Wave | Fire |
| [ ] | `content/art/icons/skills/rebirth_flame.png` | `skill/rebirth_flame` | Rebirth Flame | Fire |
| [ ] | `content/art/icons/skills/blaze_bolt.png` | `skill/blaze_bolt` | Blaze Bolt | Fire |
| [ ] | `content/art/icons/skills/firestorm.png` | `skill/firestorm` | Firestorm | Fire |
| [ ] | `content/art/icons/skills/sunfire_nova.png` | `skill/sunfire_nova` | Sunfire Nova | Fire |
| [ ] | `content/art/icons/skills/sacred_spring.png` | `skill/sacred_spring` | Sacred Spring | Light |
| [ ] | `content/art/icons/skills/blessing.png` | `skill/blessing` | Blessing | Light |
| [ ] | `content/art/icons/skills/radiant_bolt.png` | `skill/radiant_bolt` | Radiant Bolt | Light |
| [ ] | `content/art/icons/skills/judgment.png` | `skill/judgment` | Judgment | Light |
| [ ] | `content/art/icons/skills/purifying_ward.png` | `skill/purifying_ward` | Purifying Ward | Light |
| [ ] | `content/art/icons/skills/halo.png` | `skill/halo` | Halo | Light |
| [ ] | `content/art/icons/skills/venom_spit.png` | `skill/venom_spit` | Venom Spit | Dark |
| [ ] | `content/art/icons/skills/coup_de_grace.png` | `skill/coup_de_grace` | Shadow Pounce | Dark |
| [ ] | `content/art/icons/skills/petrifying_gaze.png` | `skill/petrifying_gaze` | Petrifying Gaze | Dark |
| [ ] | `content/art/icons/skills/eclipse_fang.png` | `skill/eclipse_fang` | Eclipse Fang | Dark |
| [ ] | `content/art/icons/skills/predator_focus.png` | `skill/predator_focus` | Predator Focus | Dark |
| [ ] | `content/art/icons/skills/miasma.png` | `skill/miasma` | Miasma | Dark |

### Avatar actives (6)

| Done | File | ArtKey | Skill | Element / kind |
| --- | --- | --- | --- | --- |
| [ ] | `content/art/icons/skills/rallying_cry.png` | `skill/rallying_cry` | Rallying Cry | avatar active |
| [ ] | `content/art/icons/skills/mending_light.png` | `skill/mending_light` | Mending Light | avatar active |
| [ ] | `content/art/icons/skills/aegis.png` | `skill/aegis` | Aegis | avatar active |
| [ ] | `content/art/icons/skills/hex_of_frailty.png` | `skill/hex_of_frailty` | Hex of Frailty | avatar active |
| [ ] | `content/art/icons/skills/battle_focus.png` | `skill/battle_focus` | Battle Focus | avatar active |
| [ ] | `content/art/icons/skills/slowing_field.png` | `skill/slowing_field` | Slowing Field | avatar active |

### Avatar passives (10)

| Done | File | ArtKey | Skill | Element / kind |
| --- | --- | --- | --- | --- |
| [ ] | `content/art/icons/skills/keen_eye.png` | `skill/keen_eye` | Keen Eye | avatar passive |
| [ ] | `content/art/icons/skills/iron_will.png` | `skill/iron_will` | Iron Will | avatar passive |
| [ ] | `content/art/icons/skills/opening_ward.png` | `skill/opening_ward` | Opening Ward | avatar passive |
| [ ] | `content/art/icons/skills/battle_hymn.png` | `skill/battle_hymn` | Battle Hymn | avatar passive |
| [ ] | `content/art/icons/skills/withering_curse.png` | `skill/withering_curse` | Withering Curse | avatar passive |
| [ ] | `content/art/icons/skills/bloodlust.png` | `skill/bloodlust` | Rising Fervor | avatar passive |
| [ ] | `content/art/icons/skills/vengeance.png` | `skill/vengeance` | Kindred Resolve | avatar passive |
| [ ] | `content/art/icons/skills/storm_call.png` | `skill/storm_call` | Storm Call | avatar passive |
| [ ] | `content/art/icons/skills/verdant_pulse.png` | `skill/verdant_pulse` | Verdant Pulse | avatar passive |
| [ ] | `content/art/icons/skills/last_stand.png` | `skill/last_stand` | Last Stand | avatar passive |

### Enemy skills (15)

| Done | File | ArtKey | Skill | Enemy |
| --- | --- | --- | --- | --- |
| [ ] | `content/art/icons/skills/enemy/giant_crush.png` | `skill/enemy/giant/crush` | Crushing Fist | giant |
| [ ] | `content/art/icons/skills/enemy/giant_gaze.png` | `skill/enemy/giant/gaze` | Baleful Glare | giant |
| [ ] | `content/art/icons/skills/enemy/giant_quake.png` | `skill/enemy/giant/quake` | Ground Stomp | giant |
| [ ] | `content/art/icons/skills/enemy/giant_roar.png` | `skill/enemy/giant/roar` | Dread Roar | giant |
| [ ] | `content/art/icons/skills/enemy/champion_cleave.png` | `skill/enemy/champion/cleave` | Great Cleave | champion |
| [ ] | `content/art/icons/skills/enemy/champion_hex.png` | `skill/enemy/champion/hex` | Gloam Hex | champion |
| [ ] | `content/art/icons/skills/enemy/champion_shockwave.png` | `skill/enemy/champion/shockwave` | Shockwave | champion |
| [ ] | `content/art/icons/skills/enemy/brute_smash.png` | `skill/enemy/brute/smash` | Smash | brute |
| [ ] | `content/art/icons/skills/enemy/stalker_shadow_claw.png` | `skill/enemy/stalker/shadow_claw` | Ambush Claw | stalker |
| [ ] | `content/art/icons/skills/enemy/archer_arrow.png` | `skill/enemy/archer/arrow` | Arrow Shot | archer |
| [ ] | `content/art/icons/skills/enemy/caster_bolt.png` | `skill/enemy/caster/bolt` | Spite Bolt | caster |
| [ ] | `content/art/icons/skills/enemy/shaman_staff.png` | `skill/enemy/shaman/staff` | Staff Rap | shaman |
| [ ] | `content/art/icons/skills/enemy/shaman_storm.png` | `skill/enemy/shaman/storm` | Squall | shaman |
| [ ] | `content/art/icons/skills/enemy/swarmling_bite.png` | `skill/enemy/swarmling/bite` | Bite | swarmling |
| [ ] | `content/art/icons/skills/enemy/stingling_sting.png` | `skill/enemy/stingling/sting` | Sting | stingling |

## 4. VFX hero frames (59)

Single painted frames (not flipbooks): the game animates each procedurally (scale, rotation, fade and tint curves
in `content/data/Vfx/vfx-library.json`), blends the bright ones additively (paint them on black-free transparency:
glow lives in the alpha and colour, premultiplied on load) and the decals with alpha. Centre pivot. **World
width** is how many hexes the full canvas spans at scale 1 (rings and decals are stretched to the hit area, so
theirs is nominal); keep the motif inside the canvas with a soft falloff to fully transparent edges.

### Per element (10 elements x 5)

| Kind | Size | World width | What |
| --- | --- | --- | --- |
| burst | 512 x 512 | 1.5 | the impact burst: a radial splash/star, hot centre, additive; grows and fades over ~0.36 s |
| ring | 512 x 512 | 2.0 | the shockwave ring: a thin bright band near the edge, empty centre, additive; grows to the hit area |
| decal | 512 x 512 | 2.0 | the ground mark left on the target's tile: scorch/frost/moss..., alpha-blended, seen from above |
| glyph | 256 x 256 | 0.45 | one rune/sigil of the element, additive; three circle the caster as a charge-up |
| ember | 128 x 128 | 0.2 | one particle (spark, droplet, pebble, leaf, flake...), additive; many fly out and shrink |

| Done | File | Manifest name | Element |
| --- | --- | --- | --- |
| [ ] | `content/art/vfx/fire/burst.png` | `fx_painted_fire_burst` | Fire |
| [ ] | `content/art/vfx/fire/ring.png` | `fx_painted_fire_ring` | Fire |
| [ ] | `content/art/vfx/fire/decal.png` | `fx_painted_fire_decal` | Fire |
| [ ] | `content/art/vfx/fire/glyph.png` | `fx_painted_fire_glyph` | Fire |
| [ ] | `content/art/vfx/fire/ember.png` | `fx_painted_fire_ember` | Fire |
| [ ] | `content/art/vfx/water/burst.png` | `fx_painted_water_burst` | Water |
| [ ] | `content/art/vfx/water/ring.png` | `fx_painted_water_ring` | Water |
| [ ] | `content/art/vfx/water/decal.png` | `fx_painted_water_decal` | Water |
| [ ] | `content/art/vfx/water/glyph.png` | `fx_painted_water_glyph` | Water |
| [ ] | `content/art/vfx/water/ember.png` | `fx_painted_water_ember` | Water |
| [ ] | `content/art/vfx/earth/burst.png` | `fx_painted_earth_burst` | Earth |
| [ ] | `content/art/vfx/earth/ring.png` | `fx_painted_earth_ring` | Earth |
| [ ] | `content/art/vfx/earth/decal.png` | `fx_painted_earth_decal` | Earth |
| [ ] | `content/art/vfx/earth/glyph.png` | `fx_painted_earth_glyph` | Earth |
| [ ] | `content/art/vfx/earth/ember.png` | `fx_painted_earth_ember` | Earth |
| [ ] | `content/art/vfx/air/burst.png` | `fx_painted_air_burst` | Air |
| [ ] | `content/art/vfx/air/ring.png` | `fx_painted_air_ring` | Air |
| [ ] | `content/art/vfx/air/decal.png` | `fx_painted_air_decal` | Air |
| [ ] | `content/art/vfx/air/glyph.png` | `fx_painted_air_glyph` | Air |
| [ ] | `content/art/vfx/air/ember.png` | `fx_painted_air_ember` | Air |
| [ ] | `content/art/vfx/lightning/burst.png` | `fx_painted_lightning_burst` | Lightning |
| [ ] | `content/art/vfx/lightning/ring.png` | `fx_painted_lightning_ring` | Lightning |
| [ ] | `content/art/vfx/lightning/decal.png` | `fx_painted_lightning_decal` | Lightning |
| [ ] | `content/art/vfx/lightning/glyph.png` | `fx_painted_lightning_glyph` | Lightning |
| [ ] | `content/art/vfx/lightning/ember.png` | `fx_painted_lightning_ember` | Lightning |
| [ ] | `content/art/vfx/ice/burst.png` | `fx_painted_ice_burst` | Ice |
| [ ] | `content/art/vfx/ice/ring.png` | `fx_painted_ice_ring` | Ice |
| [ ] | `content/art/vfx/ice/decal.png` | `fx_painted_ice_decal` | Ice |
| [ ] | `content/art/vfx/ice/glyph.png` | `fx_painted_ice_glyph` | Ice |
| [ ] | `content/art/vfx/ice/ember.png` | `fx_painted_ice_ember` | Ice |
| [ ] | `content/art/vfx/nature/burst.png` | `fx_painted_nature_burst` | Nature |
| [ ] | `content/art/vfx/nature/ring.png` | `fx_painted_nature_ring` | Nature |
| [ ] | `content/art/vfx/nature/decal.png` | `fx_painted_nature_decal` | Nature |
| [ ] | `content/art/vfx/nature/glyph.png` | `fx_painted_nature_glyph` | Nature |
| [ ] | `content/art/vfx/nature/ember.png` | `fx_painted_nature_ember` | Nature |
| [ ] | `content/art/vfx/metal/burst.png` | `fx_painted_metal_burst` | Metal |
| [ ] | `content/art/vfx/metal/ring.png` | `fx_painted_metal_ring` | Metal |
| [ ] | `content/art/vfx/metal/decal.png` | `fx_painted_metal_decal` | Metal |
| [ ] | `content/art/vfx/metal/glyph.png` | `fx_painted_metal_glyph` | Metal |
| [ ] | `content/art/vfx/metal/ember.png` | `fx_painted_metal_ember` | Metal |
| [ ] | `content/art/vfx/light/burst.png` | `fx_painted_light_burst` | Light |
| [ ] | `content/art/vfx/light/ring.png` | `fx_painted_light_ring` | Light |
| [ ] | `content/art/vfx/light/decal.png` | `fx_painted_light_decal` | Light |
| [ ] | `content/art/vfx/light/glyph.png` | `fx_painted_light_glyph` | Light |
| [ ] | `content/art/vfx/light/ember.png` | `fx_painted_light_ember` | Light |
| [ ] | `content/art/vfx/dark/burst.png` | `fx_painted_dark_burst` | Dark |
| [ ] | `content/art/vfx/dark/ring.png` | `fx_painted_dark_ring` | Dark |
| [ ] | `content/art/vfx/dark/decal.png` | `fx_painted_dark_decal` | Dark |
| [ ] | `content/art/vfx/dark/glyph.png` | `fx_painted_dark_glyph` | Dark |
| [ ] | `content/art/vfx/dark/ember.png` | `fx_painted_dark_ember` | Dark |

### Per status (9)

**512 x 512**, world width 1.0, additive: played on the unit when the status lands (a pop in and fade) and, for a
lasting status, as its looping aura while it lasts (a soft pulse at ~0.7 opacity; the small status icon over the
HP bar stays). Read at a glance at unit size; a symbol plus a soft glow.

| Done | File | Manifest name | Status | Where |
| --- | --- | --- | --- | --- |
| [ ] | `content/art/vfx/status/stun.png` | `fx_painted_status_stun` | Stun | over the unit (aura Depth Over), slowly turning |
| [ ] | `content/art/vfx/status/shield.png` | `fx_painted_status_shield` | Shield | at the feet |
| [ ] | `content/art/vfx/status/burn.png` | `fx_painted_status_burn` | Burn (damage over time, fire) | at the feet |
| [ ] | `content/art/vfx/status/poison.png` | `fx_painted_status_poison` | Poison (damage over time, other) | at the feet |
| [ ] | `content/art/vfx/status/taunt.png` | `fx_painted_status_taunt` | Taunt | at the feet |
| [ ] | `content/art/vfx/status/cleanse.png` | `fx_painted_status_cleanse` | Cleanse | on apply only |
| [ ] | `content/art/vfx/status/heal.png` | `fx_painted_status_heal` | Heal | on apply only |
| [ ] | `content/art/vfx/status/buff.png` | `fx_painted_status_buff` | Stat buff | over the unit, faint |
| [ ] | `content/art/vfx/status/debuff.png` | `fx_painted_status_debuff` | Stat debuff | over the unit, faint |

## Checks after dropping art in

1. `Tooling/PixelArt/.venv/Scripts/python Tooling/PixelArt/build.py` (the manifest picks up the new sizes).
2. `dotnet test Tooling/EditModeTests -c Release` (the validators hold every slot: backdrops undistorted and
   covering the canvas, frames one linear frame, keys in the manifest).
3. Desktop screenshots, e.g. `dotnet run --project src/BeastCraft.Desktop -c Release -- --screenshot shot.png --turns 1
   --skill boulder_slam --at 650` (an element default with the painted frames), `--arena Small|Large` for the other
   backdrops, `--select-skill 1` for a framed icon on the detail card.

Design notes: `docs/design/presentation-and-vfx.md` ("Painted backdrops", "Framed icons", "Painted frames").
