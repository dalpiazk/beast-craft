# Third-party notices

Beast Craft ships the following third-party components. Each keeps its own licence; the full
licence texts are at the links (and, for the font, beside the file).

## Fonts

### Fredoka (UI typeface)

- File: `content/fonts/Fredoka-SemiBold.ttf` (the SemiBold, weight 600, static instance of
  Fredoka as served by Google Fonts), copied by the hosts to `Content/fonts/` (desktop, beside the
  executable; Android, APK assets).
- Copyright 2016 The Fredoka Project Authors (https://github.com/hafontia/Fredoka-One).
- Licence: **SIL Open Font License, Version 1.1** — full text in `content/fonts/OFL.txt`, which
  ships with the font. No Reserved Font Name is declared. The font may be bundled, embedded and
  redistributed with the game; it may not be sold on its own, and any modified version must stay
  under the OFL.
- Source: https://fonts.google.com/specimen/Fredoka

## Libraries (NuGet)

| Package | Used for | Licence |
| --- | --- | --- |
| MonoGame.Framework.DesktopGL / MonoGame.Framework.Android 3.8.5.1 | the game framework (hosts) | Ms-PL (https://github.com/MonoGame/MonoGame/blob/develop/LICENSE.txt) |
| FontStashSharp.MonoGame 1.6.1, FontStashSharp.Base, FontStashSharp.Rasterizers.StbTrueTypeSharp 1.2.9 | runtime TTF rasterising for the UI text (`TtfText`) | Zlib (https://github.com/FontStashSharp/FontStashSharp/blob/main/LICENSE) |
| StbTrueTypeSharp 1.26.13, StbImageSharp 2.30.16 | FontStashSharp's rasteriser and image loader | MIT or Unlicense, used under MIT (https://github.com/StbSharp) |
| Cyotek.Drawing.BitmapFont 2.0.4 | FontStashSharp dependency (bitmap-font reader, unused by the game) | MIT (https://github.com/cyotek/Cyotek.Drawing.BitmapFont) |

The .NET runtime and base libraries (including System.Text.Json) are MIT-licensed by the .NET
Foundation. Build-time and test-only packages (the test SDK, NUnit) are not shipped.

## Tools used to make game art (not shipped)

The starter trio's beast art (`content/art/beasts/`, with masters in `content/art/source/`) was made
with the local AI-assisted pipeline in `Tooling/ArtLab/`, with the producer as art director. These
models were used as **tools**: none of their weights or code ships with the game or is in this
repository. How each asset was made is recorded in `Tooling/ArtLab/provenance/`.

| Model (Hugging Face) | Used for | Licence |
| --- | --- | --- |
| cagliostrolab/animagine-xl-4.0 | the base SDXL image model (generation, img2img, inpainting) | CreativeML Open RAIL++-M, dated July 26, 2023 (the SDXL licence, adopted by the model card "without any modifications or additional restrictions"; https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0/blob/main/LICENSE.md) |
| madebyollin/sdxl-vae-fp16-fix | the SDXL VAE, half-precision safe | MIT |
| xinsir/controlnet-canny-sdxl-1.0 | edge-guided generation (the design lock) | Apache-2.0 |
| xinsir/controlnet-tile-sdxl-1.0 | the tiled detail pass | Apache-2.0 |
| h94/IP-Adapter (`ip-adapter_sdxl_vit-h` + its OpenCLIP ViT-H/14 image encoder) | style conditioning (InstantStyle) from our own style references | Apache-2.0 |
| facebook/sam2.1-hiera-small | character and rig-part masks | Apache-2.0 |

**On the CreativeML Open RAIL++-M licence.** It places use-based restrictions on the *model* and its
derivatives (Section III, paragraph 5, and Attachment A: for example no unlawful use, no exploiting or
harming minors, no defamation or harassment), and conditions on redistributing the model, which we do
not do. It makes no ownership claim on what the model produces. Section III, paragraph 6, "The Output
You Generate", reads in full:

> Except as set forth herein, Licensor claims no rights in the Output You generate using the Model. You
> are accountable for the Output you generate and its subsequent uses. No use of the output can
> contravene any provision as stated in the License.

So the licensor claims no rights in the images we generated; we are accountable for them, and they
must not be used in a way the licence's use restrictions forbid. This is not legal advice: AI-generated images may have limited copyright protection in some
jurisdictions, and store disclosure rules apply (see `docs/art/art-brief.md`, decision 10).
