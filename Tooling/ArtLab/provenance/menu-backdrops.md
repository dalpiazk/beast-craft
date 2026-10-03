# Provenance: per-region menu backdrops

**Status:** producer-approved picks recorded below. AI-assisted; disclose where a store requires it
(see `docs/art/art-brief.md`).

**Purpose:** UI direction D ("painted world + storybook page"), the menu backdrop painting behind
each region's screen in the Journal/Settings UI kit (issue #52). Twelve backdrops, one per campaign
region (`r00`-`r11`), each unlocked the first time a player reaches that region. Scripts:
`Tooling/ArtLab/scripts/menu_backdrops/`. This commit records the prompts, seeds and grade parameters
and ships the generation/grading tooling; the graded PNGs themselves, and the UI code that displays
them, ship with the Journal/Settings UI kit commit on this same branch.

Tools: local only (Intel Arc, XPU, bf16, via the Beast Craft ArtLab venv at
`%LOCALAPPDATA%\BeastCraftArtLab`); no cloud service, nothing uploaded, no network calls
(`HF_HUB_OFFLINE=1`, set by `Tooling/ArtLab/scripts/common.py`). Model and licence: see
`Tooling/ArtLab/README.md` (same base checkpoint as the beasts/enemies and as direction D,
`cagliostrolab/animagine-xl-4.0`).

## 1. Pipeline and settings (every region)

- **Pipeline:** `StableDiffusionXLPipeline` (txt2img only), base `cagliostrolab/animagine-xl-4.0`, VAE
  `madebyollin/sdxl-vae-fp16-fix`, DPMSolverMultistep + Karras sigmas. No InstantStyle IP-Adapter, no
  ControlNet, no img2img init in the final, shipped prompts -- plain prompt-only SDXL, same as
  direction D's own backdrop (section 2 below). An IP-Adapter image-conditioning path was tried and
  rejected partway through; see section 5, "what didn't work."
- **Device:** XPU (Intel Arc), dtype bf16.
- **Size:** 832x1216 (portrait SDXL-native). **Steps:** 32. **CFG:** 6.0.
- **Seeds:** two seed families, reused across every region in each family for a controlled comparison
  (same noise, different prompt per region): **401-404** ("batch 1" -- r00, r01, r03) and **501-504**
  ("batch 2" -- r02, r04-r11). A seed is only retried, at `seed + 5000*attempt`, if the decode comes
  back black/NaN (`common.is_black()`); this never triggered for any of the 12 regions' final seeds.
- **Prompt-safe vocabulary only**, per `docs/art/art-brief.md` / `Pipeline/README.md`: no studio,
  franchise, character or living-artist names anywhere in any prompt, filename or this note.
- **Colour grade:** every region's chosen raw render is then run through `grade.py` (Pillow + numpy,
  no models) with the shared default parameters in section 4, unifying all 12 (plus direction D's own
  backdrop) under one "painted journal" treatment. See section 5 for why a post-process grade was
  used instead of getting one prompt/IP-Adapter setup to hold both region identity and D's rendering
  style at once.

## 2. Direction D's own backdrop (for reference -- the shared style target)

Direction D's Settings-screen backdrop (`settings_D_backdrop.png`) is the visual reference the other
12 regions' style and the colour grade are both aiming at. Not one of the 12 region backdrops itself.

- **Prompt:** `gouache background painting, soft wet-blended washes, details painted last, towering
  white cumulus clouds, deep blue summer sky, warm afternoon sun, lush layered meadow greens fading to
  blue-grey haze, distant hills, gentle breeze in the grass, wildflowers, hand-painted animation
  background art, no characters, no text, painterly, masterpiece, absurdres`
- **Negative:** `text, watermark, logo, signature, characters, people, person, figure, animal, photo,
  photorealistic, 3d render, glossy, plastic, vector art, sticker, low quality, blurry, lowres,
  oversaturated, harsh gradient`
- **Seeds tried:** 301-306 (six seeds, all usable, no rejects for quality). **Chosen: seed 302** --
  calm, relatively flat sky and a long, quiet, gently rolling meadow band in the middle of the frame,
  lining up with where the Settings tab bar and card sit; the cloud mass confined to the top-left,
  detail at the very bottom. Seed 301 had a small stray red mark bottom-right reading like a painted
  artist's seal/stamp and was excluded on sight. 303/304/306 were gorgeous but put their cumulus mass
  where the tab bar/card land; 305 was a close second but busier directly behind the card than 302.

## 3. The 12 region backdrops: prompts, seeds, token counts

Two prompt families, carried over unchanged from how they were actually generated (full history in
section 5):

- **Batch 1 (r00, r01, r03):** full-sentence prompts written directly from each region's brief, one
  shared negative. These three came through on-brief and artefact-free on the very first generation
  pass and were kept as-is per the producer -- never rewritten into the shorter style-anchor structure
  below. Shared negative: `text, watermark, logo, signature, characters, people, person, figure,
  animal, photo, photorealistic, 3d render, glossy, plastic, vector art, sticker, low quality, blurry,
  lowres, oversaturated, harsh gradient`.
- **Batch 2 (r02, r04-r11):** the final state after 14 rounds of fixes (section 5). Each prompt is the
  region's own short, subject-first, noun-heavy clause, followed by the shared closing style suffix
  (`STYLE_ANCHOR`):
  > `soft gouache painting, wet-blended watercolor, hand-painted animation background, wide vista`

  ...and each negative is the shared base (`NEG_BASE`) plus per-region additions:
  > `NEG_BASE` = `text, watermark, logo, signature, stamp, red seal, characters, character, people,
  > person, human, figure, silhouette of a person, animal, creature, heavy black ink, sumi-e, ink
  > wash, high contrast, dark, low quality, blurry, oversaturated, harsh gradient`
  > `NEG_BUILDINGS` (added for every batch-2 region except r11) = `, buildings, architecture`
  > `NEG_FLOATING` (r02, r04) = `, floating island, floating rock`
  > `NEG_RUSTWOOD` (r05) = `, autumn forest, maple leaves, monochrome`

Token counts below were measured directly against both SDXL tokenizers (`tokenizer`, `tokenizer_2` --
identical counts on both) via this commit's own `tokcheck.py`, run against the ArtLab venv's cached
model on 2026-10-02; "OVER" means CLIP silently truncates the prompt's tail past 77 tokens (no error).

| Region | Chosen seed | Other seeds tried | Positive tokens | Negative tokens |
| --- | --- | --- | --- | --- |
| r00 Hearthglen | **401** | 402, 403, 404 | 98 **OVER** | 53 ok |
| r01 Verdant Hollow | **402** | 401, 403, 404 | 93 **OVER** | 53 ok |
| r02 Emberreach | **502** | 501, 503, 504 | 44 ok | 76 ok |
| r03 Tidefall | **404** | 401, 402, 403 | 91 **OVER** | 53 ok |
| r04 Stormcrag | **503** | 501, 502, 504 | 45 ok | 74 ok |
| r05 Rustwood | **504** | 501, 502, 503 | 57 ok | 76 ok |
| r06 Frostmere | **504** | 501, 502, 503 | 45 ok | 68 ok |
| r07 Thunderspire | **502** | 501, 503, 504 | 50 ok | 76 ok |
| r08 Deepwild | **501** | 502, 503, 504 | 48 ok | 68 ok |
| r09 Cinder Throne | **503** | 501, 502, 504 | 51 ok | 68 ok |
| r10 Worldcrown | **502** | 501, 503, 504 | 49 ok | 68 ok |
| r11 Duskmeridian | **504** | 501, 502, 503 | 47 ok | 64 ok |

**On r00/r01/r03's "OVER" token counts:** these three were generated and picked before the truncation
bug was discovered (section 5, round 2) and were never rewritten, per the producer's instruction to
keep their batch-1 picks as-is. CLIP drops only the prompt's trailing tail -- for these three that's
the last few quality tags (`painterly, masterpiece, absurdres`), not the biome-defining nouns earlier
in the sentence -- and all three read on-brief and artefact-free regardless, which is why this was
judged acceptable rather than a reason to re-prompt and regenerate. If either prompt is ever edited,
re-run `tokcheck.py` and budget for 77 tokens same as the batch-2 family.

**Full exact prompt text per region** (the batch-2 subject clause plus `STYLE_ANCHOR`, and the
batch-2 negative plus its per-region additions on top of `NEG_BASE`, are spelled out in full in
`Tooling/ArtLab/scripts/menu_backdrops/gen_backdrops.py`'s `REGIONS` dict -- not duplicated a second
time here to avoid drift between two copies):

| Region | Positive subject clause (batch 2) / full prompt (batch 1) |
| --- | --- |
| r00 Hearthglen | `gouache background painting, soft wet-blended washes, details painted last, a sheltered green vale with warm hedgerows and clover meadows, an old stone mill with a water wheel in the middle distance, a mossy apple orchard at the edge, soft golden late-afternoon light, gentle rolling hills, calm pale butter-gold sky in the upper middle, hand-painted animation background art, warm and cozy, no characters, no text, painterly, masterpiece, absurdres` |
| r01 Verdant Hollow | `gouache background painting, soft wet-blended washes, details painted last, sun-dappled woodland canopy with deep green moss and ferns, ancient moss-covered standing stones in a forest clearing, dappled sunbeams breaking through leaves, soft green-gold haze, fireflies glowing faintly in the shade, calm open clearing in the upper middle, hand-painted animation background art, no characters, no text, painterly, masterpiece, absurdres` |
| r02 Emberreach | `volcanic ash plain, dark basalt ridges, glowing orange cracks, smouldering embers, warm smoky dusk sky` + style suffix |
| r03 Tidefall | `gouache background painting, soft wet-blended washes, details painted last, a sea cave with a waterfall of tide pouring through weathered stone arches, drowned ruins and old pillars half submerged in turquoise water, cool blue-green light, frosted icy pools in the shadows, a calm pale open sky framed by the cave mouth in the upper middle, hand-painted animation background art, no characters, no text, painterly, masterpiece, absurdres` |
| r04 Stormcrag | `jagged grey crags, dark storm clouds, rain curtains, distant lightning, wind-bent grass, moody light breaking through` + style suffix |
| r05 Rustwood | `forest of iron-bark trees, rust-red and copper foliage, metallic sheen on bark, rusted iron ruins in the undergrowth, ore-streaked ground, soft overcast light` + style suffix |
| r06 Frostmere | `vast frozen lake to the horizon, cracked glowing blue-white ice, snowfields, dark pine treeline, pale winter sky` + style suffix |
| r07 Thunderspire | `storm plateau of tall glassy crystal spires, lightning striking the spire tips, dark purple-blue storm sky, rain-slicked rock, empty landscape` + style suffix |
| r08 Deepwild | `ancient overgrown deep forest, colossal moss-grown trunks like towers, dense glowing undergrowth, deep green teal haze, soft light shafts` + style suffix |
| r09 Cinder Throne | `charred volcanic citadel of black glass and rock, rivers of glowing lava, molten throne-like peak, ember-lit ridges, smoky red-orange glow` + style suffix |
| r10 Worldcrown | `mountain summit crowning the world, waterfall and river of fire pouring side by side from one peak, swirling violet-gold haze, layered clouds` + style suffix |
| r11 Duskmeridian | `endless twilight sky realm, no ground, radiant gold dawn and deep violet dusk sharing one horizon, floating ancient stone arches, soft luminous haze` + style suffix |

("+ style suffix" = `, soft gouache painting, wet-blended watercolor, hand-painted animation
background, wide vista`, i.e. `STYLE_ANCHOR` appended with a leading comma, exactly as generated.)

Per-region negative additions on top of `NEG_BASE`: r02 `NEG_BUILDINGS + NEG_FLOATING + ", eruption"`;
r04 `NEG_BUILDINGS + NEG_FLOATING`; r05 `NEG_BUILDINGS + NEG_RUSTWOOD`; r06/r08/r09/r10
`NEG_BUILDINGS` only; r07 `NEG_BUILDINGS + ", bird, monster, robot, mech"`; r11 none (no
`NEG_BUILDINGS` -- r11's brief explicitly wants floating stone arches).

## 4. `grade.py` parameters (final, as shipped)

Pure Pillow + numpy, zero models/network/GPU. Six stages in order: lift blacks/compress highlights,
reduce saturation (uniform, plus a saturation-gated extra pull on three "fantasy poster" hue bands),
split-tone toward the palette, a soft top haze, paper grain + watercolour-wash texture, a soft
vignette. See `grade.py`'s own docstring and comments for the full mechanism of each stage.

| Parameter | Value | Notes |
| --- | --- | --- |
| `black_floor` | computed from `INK_PLUM` luma (~0.176) | not hardcoded; tracks the palette |
| `knee` | 0.78 | highlight rolloff starts at 78% brightness |
| `highlight_scale` | 0.72 | headroom above the knee compressed by this factor |
| `sat_reduce` | 0.27 | uniform saturation reduction (producer's 25-40% range, gentle end) |
| `extra_sat_pull` | 0.45 | extra pull on extreme hues, gated (see below) |
| extreme-hue gate | s=0.55 to s=0.85 | smoothstep; below 0.55 = no extra pull, above 0.85 = full |
| extreme hue bands | 28 deg (orange/ember), 130 deg (green/forest), 195 deg (cyan/lightning) | widths 16/20/18 deg (gaussian) |
| `grade_strength` | 0.17 | split-tone blend strength (shadow->COOL_SHADOW, highlight->APRICOT, mid->PAPER) |
| `haze_strength` | 0.08 | top-of-frame atmospheric haze |
| `haze_height` | 0.32 | fraction of image height the haze fades out over |
| `texture_opacity` | 0.32 | paper grain + wash pooling strength |
| `vignette_strength` | 0.14 | very soft radial darkening from 55% radius out |
| `seed` | per-filename hash (SHA1 mod 10000) unless `--seed` given | deterministic, non-repeating grain per image |

**One tuning pass** (not a repeated loop, per the producer's "one tuning pass max" instruction) fixed
two problems found in the first test: r02's ember/lava glow lost almost all its warmth (the extra
saturation pull was hitting ordinary, moderately-saturated warm colour, not just true neon extremes --
fixed by adding the saturation gate above and raising `extra_sat_pull` since it could now afford to be
stronger once gated), and direction D's own backdrop came out visibly paler/flatter than the original
(a bad sign, since D should barely move under a grade aimed at *its* look -- fixed by lowering
`sat_reduce`, `haze_strength` and `texture_opacity` from a first-pass set that was a bit strong in
aggregate even though no single value looked extreme alone).

## 5. What didn't work (and the fixes)

Twelve regions took fourteen rounds to land on the final prompts above. The short version, in order:

1. **Style-anchor-first truncation at 77 tokens (round 2).** The first version of the batch-2 prompts
   put the region's subject first and a long style-anchor phrase at the very end. CLIP's tokenizers
   silently truncate anything past 77 tokens (diffusers only logs a warning, never errors), and every
   one of these prompts ran 81-113 tokens -- so the *entire* style anchor landed in the truncated tail
   and never reached the model. The images this produced were visually indistinguishable from an
   un-anchored prompt; the bug was caught by reviewing the generation log's truncation warnings, not
   by a visual defect. **Fix:** restructured prompts style-first and trimmed every prompt (positive
   and negative) to fit under 77 tokens on both tokenizers, verified with a token-count script
   (this directory's `tokcheck.py` is that script, generalized).
2. **"Pastel" style anchor over-correcting into a flat, faceted surface study (round 4).** Once
   truncation was fixed, r02's test came out a pale, faceted, stained-glass canyon wall -- no sky, no
   depth, not a landscape. The anchor's `"airy pastel light"` was washing everything toward a flat,
   low-chroma, faceted look, and nothing in it told the model this should read as a wide outdoor vista
   rather than a close-up surface. **Fix:** dropped "pastel"; added explicit composition terms (`wide
   landscape vista, sky visible, depth`).
3. **Style anchor crowding out region identity (round 5).** With the anchor still positioned first
   (carried over from round 2's truncation fix) and grown to ~25-30 tokens of atmospheric/technique
   language, SDXL's text encoder weighted it so heavily that region identity nearly disappeared: r02
   rendered as generic dusk hills with no volcanic read at all, r04 as bright sunny cloudscapes with
   no storm. **Fix:** restructured every prompt **subject-first**, with a single short style suffix
   (`STYLE_ANCHOR`, now just one line) moved to the end -- the structure shipped in section 3.
4. **InstantStyle IP-Adapter conditioning leaking content, not just style (round 6, rejected).** Once
   subject-first prompts nailed region identity, the whole batch swung into a dark, saturated "fantasy
   poster" register, and one seed (r04) rendered a human figure. The attempted fix was image-
   conditioned style transfer toward direction D's own backdrop, blended 0.6/0.4 with Verdant Hollow's
   kept pick for "cohesion." Result: the secondary reference leaked actual **content** (a waterfall,
   green grass) into unrelated regions, not just influencing the rendering style the way InstantStyle's
   style-block-only injection is supposed to restrict it to. A narrower `d`-only preset (no secondary
   reference) was tried next, but the producer then chose a different direction entirely (item 5 below)
   before that narrower preset was ever visually verified. This whole code path is **not** reproduced
   in `gen_backdrops.py` (see that file's and its README's "what's deliberately not in this script").
   A related crash was also hit and fixed along the way: `pipe.enable_attention_slicing()` replaces
   every UNet attention processor, including the IP-Adapter-aware ones, with generic sliced processors
   that can't handle the IP-Adapter's `(image_embeds, scale)` tuple input -- fixed by skipping
   attention slicing whenever IP-Adapter conditioning is active, matching the repo's working
   beast/enemy pipelines (which also never enable slicing with `ipa=True`).
5. **Resolution: a deterministic post-process colour grade instead of one prompt/IP-Adapter setup
   doing both jobs (round 13).** Rounds 4-6 kept trading one requirement for the other (strong
   identity vs. direction D's soft rendering). The producer's decision: keep the subject-first,
   prompt-only images (no IP-Adapter, strongest and most unambiguous region identity of any round) and
   unify them afterward with a CPU-only colour grade. That grade is `grade.py` (section 4).
6. **Creatures slipping through (round 14).** A full review of the kept prompt-only images found every
   r07 Thunderspire seed contained a creature (birds, a monster-like fantasy-creature figure, a mech), plus a
   stray robot in r05 and a human figure in r04 that the round-6 negative additions should have caught
   but didn't reliably. Diagnosis for r07 specifically: `"forest of glassy crystal spires drinking
   lightning"` personified the spires (plus "forest of," a noun more associated with living things)
   into something creature-like. **Fix:** reworded r07's subject to an explicit, unmistakable empty
   landscape (`"storm plateau ... empty landscape"`, dropping "forest of" and "drinking"); added
   `"creature"` to the shared negative base for every region (`"animal"` alone doesn't cover
   robot/monster/mech-type figures).
7. **Ink-wash, stamp and signature artefacts (batch 1 -> batch 2 regen).** The very first full batch
   (seeds 401-404 across all 12 regions, before any of the rounds above) found several regions had
   drifted hard off-brief or picked up unwanted marks the negative prompt didn't catch: r05 Rustwood
   rendered as a misty Japanese-ink-wash autumn forest with zero metal/iron content despite "iron-
   trunked trees" being in the prompt; r04 Stormcrag and r02 Emberreach rendered as literal floating
   islands with buildings; r06 Frostmere had 3 of 4 seeds rejected for a red stamp/seal mark or a
   legible cursive signature. The generic `watermark, logo, signature` negative terms did not reliably
   suppress stamps/seals. **Fix (feeding into round 2 onward):** added explicit `stamp, red seal`
   terms to the shared negative base; added `NEG_RUSTWOOD` (`autumn forest, maple leaves, monochrome`)
   and `NEG_BUILDINGS`/`NEG_FLOATING` per region; re-prompted and regenerated r02, r04, r05, r06, r07
   (all flagged off-brief or artefact-hit) plus generated r08-r11 for the first time as "batch 2," new
   seed family 501-504. r00, r01, r03 came through this first batch clean and on-brief and were never
   touched again.

**Process note, unrelated to prompt quality:** generation for batch 2 was interrupted by the session
harness's own background-shell-pressure-reaper three separate times across the rounds above (a
session-level safeguard, not a script crash -- confirmed each time by zero black/NaN or partial/corrupt
files, since an image is only written after it fully decodes). The working pattern that avoided this
was running `gen_backdrops.py` in the foreground, a few seeds at a time, rather than as a long
background batch job -- see this directory's `README.md` for the operational guidance that came out of
that (foreground, small batches, ~15 GB free memory headroom).

## 6. Files

| File | What |
| --- | --- |
| `Tooling/ArtLab/scripts/menu_backdrops/gen_backdrops.py` | txt2img generator, all 12 regions, both prompt families |
| `Tooling/ArtLab/scripts/menu_backdrops/grade.py` | the post-process colour grade |
| `Tooling/ArtLab/scripts/menu_backdrops/tokcheck.py` | 77-token budget check for every region's prompts |
| `Tooling/ArtLab/scripts/menu_backdrops/README.md` | how to regenerate and grade a region's backdrop |

The graded images themselves are **not** part of this commit -- they ship with the Journal/Settings UI
kit commit on this same branch, per the producer's picks in section 3, as
`content/art/ui/backdrops/r00.jpg` ... `r11.jpg` (ArtKey `ui/backdrop/<regionId>`, registered in
`Tooling/PixelArt/illustrated.json`'s Painted list). Each graded 832x1216 PNG is centre-cropped to the
game's 9:16 portrait canvas, resized to 1080x1920 and saved as a quality-88 JPEG (`*.jpg` is already
Git LFS-tracked): 218-374 KB per file, well under the ~700 KB/file budget, with no visible banding or
block artefacts behind the UI kit's dim overlay at that quality. PNG was not used for the shipped
asset: these are continuous-tone painted/grained images (not the flat-fill UI kit pieces), so lossless
PNG at 1080x1920 ran 1-1.5 MB per file against JPEG's 220-370 KB for no visible difference once dimmed
under the parchment UI.
