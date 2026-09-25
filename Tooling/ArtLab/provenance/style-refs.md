# Provenance: the style references (`refs/`)

InstantStyle conditions every generation on a weighted mean of three of **our own** round-2 outputs (no third-party
artwork is used as a reference): `d_406` 0.5, `a_104` 0.3, `b_208` 0.2. They were used for style and colour only.
A fourth, `e_452`, was dropped in round 4: its design reads Moltres-like, so it was never a shape or canny source.

All three: Animagine XL 4.0 + fp16-fix VAE, 896x1152, DPM++ 2M Karras, 20 steps, CFG 6,
`torch.Generator("cpu").manual_seed(seed)`, round 2 (2026-09-24).

| Ref | Method | Seed | Prompt template |
| --- | --- | --- | --- |
| `d_406` | txt2img, no ControlNet (mode d) | 406 | v3 (see `phoenix.md`) |
| `a_104` | txt2img, no ControlNet (mode a) | 104 | v2 |
| `b_208` | txt2img + canny ControlNet 0.40 (end 0.6) on a scripted gesture sketch (mode b) | 208 | v2 |

**Template v2:**
- Prompt: `no humans, chibi, cute phoenix, original creature, proud fire bird, smug, big head, amber eyes, flame
  crest, curved neck, raised wings, flame-tipped feathers, long flowing flame tail, crimson and gold, cream chest,
  full body, three-quarter view, thick dark outlines, cel shading, simple background, masterpiece, high score,
  absurdres`
- Negative: `pokemon, moltres, ho-oh, talonflame, torchic, digimon, fanart, dragon, quadruped, egg-shaped body,
  lowres, bad anatomy, extra wings, extra legs, text, watermark, worst quality, low quality, low score, bad score,
  blurry, realistic, 3d, human, multiple views`

The producer approved these as house-style references in round 2 (`d_406` as the Phoenix's design winner).
