# Axis identity assets

The approved mark is three stacked coins with transparent gaps between them: muted blue `#5B72D6` at the bottom, muted amber `#CBA257` in the middle, and mint `#38D998` on top. Navy `#0B1220` is the tile/background colour. The production SVG uses flat fills; the gaps are real cut-outs, so the monochrome silhouettes keep them.

## Source and generation

`build-master.mjs` is the geometry source: it builds the coins with paper.js boolean operations and writes `axis-mark.svg`, the vector master. All copies, raster sizes, and the Android notification drawable are generated from its three paths by `generate-assets.mjs`.

The generator needs Node.js, Sharp and (for `build-master.mjs`) paper.js as development tools, not a browser dependency. With Sharp and paper available in `webapp/node_modules`:

```powershell
node branding/build-master.mjs
node branding/generate-assets.mjs
node branding/verify-assets.mjs
```

For a one-off regeneration without changing the app's dependency files, install them from `webapp/` with `npm install --no-save --package-lock=false paper sharp`. Alternatively set `AXIS_SHARP_PATH` or `AXIS_PAPER_PATH` to an existing module path. Run the generator from any working directory. After a Debug Android build, `node branding/verify-assets.mjs --android-build` also checks the actual generated adaptive foreground against the 66dp safe circle. If changing `ForegroundScale`, use a rebuild to refresh MAUI's generated icon cache.

`verify-assets.mjs` checks path parity, colours, PNG/ICO dimensions, transparent white notification pixels, maskable safe areas, and application wiring. Open `preview.html` to review the colour and monochrome variants at their actual small sizes.

## Asset roles

| Use | Generated asset / integration |
| --- | --- |
| Android launcher, round/themed icon; Windows icon | `AxisApp/Resources/AppIcon/appicon.svg` and `appiconfg.svg`, configured by `MauiIcon`. MAUI generates platform sizes and uses the foreground alpha for Android's monochrome themed icon. |
| Android system splash | `AxisApp/Resources/Splash/splash.svg` |
| Native login, loading and home wordmarks | `AxisApp/Resources/Images/axis_mark.svg`, displayed as `axis_mark.png` after MAUI processing |
| Native Android notification / status bar | `Platforms/Android/Resources/drawable/ic_stat_axis.xml`: white shapes only, transparent background, 24dp viewport presentation. Used by incoming push notifications, action confirmation notifications, and Firebase's default icon metadata. Never substitute the launcher tile here. |
| Browser favicon | `webapp/public/favicon.svg`, `favicon-32.png`, and a 16/32/48px `favicon.ico` |
| Installed PWA | `icon-192.png`, `icon-512.png`, `icon-512-maskable.png`; manifest references versioned URLs and navy theme/background |
| Apple home-screen bookmark | `webapp/public/apple-touch-icon.png`, 180px |
| Web push illustration | Full-colour `icon-192.png` |
| Android browser/PWA notification badge | `notification-badge.png`: 96px white-on-transparent glyph |
| Web login, sidebar and home wordmarks | `webapp/public/axis-mark.svg` through `BrandMark` |
| Static auth/legal page and email graphics | `webapp/public/email-assets/`, referenced by the static pages and `supabase/email-templates/` |
| Store artwork | `exports/google-play-512.png`, ready for a separate Google Play listing update |
| Reusable transparent exports | `exports/axis-mark-512.png`, `exports/axis-notification-96.png` |

## Delivery

Web assets, including the hosted link/email assets in `webapp/public/email-assets/`, ship with the normal `webapp` build/deployment. Android launcher and notification resources require an updated installed app; changing hosted files does not update an existing native APK. Store artwork is provided here; this task does not publish a Play Store release or alter store metadata.

References: [Android adaptive icons](https://developer.android.com/develop/ui/compose/system/icon_design_adaptive), [.NET MAUI app icons](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/images/app-icons?view=net-maui-10.0), and [Firebase default notification icons](https://firebase.google.com/docs/cloud-messaging/android/receive-messages).
