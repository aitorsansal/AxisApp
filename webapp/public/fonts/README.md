# Axis icon font

`axis-lucide.woff` is a 10 KB subset of `AxisApp/Resources/Fonts/lucide.ttf`, using only the codepoints in `src/lib/icons.ts`. It keeps web icons identical to the Android client without an additional runtime dependency. The glyphs retain Lucide and Feather licensing in `LICENSE-lucide.txt`.

Regenerate with Python fontTools when adding a codepoint: populate a `fontTools.subset.Subsetter` with the icon mapping's Unicode values and save the existing Android font with `Options.flavor = "woff"`. Do not choose codepoints from a different font version.
