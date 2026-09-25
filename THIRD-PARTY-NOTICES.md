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
