# Mobile Redesign — Implementation Plan

Source: `AxisApp Mobile Design.zip` (`design_handoff_mobile_redesign/README.md` +
`AxisApp Screens (Rounded).dc.html`). High-fidelity static HTML reference — colors,
radii, spacing, copy are final values, not to be reinterpreted. This plan sequences
the port into MAUI XAML using the existing MVVM/Shell architecture.

**Current state check (2026-08-26): none of this is implemented yet.** The
uncommitted working-tree diff at the time this plan was written only contains prep
work — `Styles.xaml`/`Tokens.xaml` magic numbers converted to `{StaticResource}`
tokens, a new `AvatarSize{XS,S,M,L}` scale, a `PageHeaderBar` control extracted from
three pages, a `lucide.ttf` icon font registered but wired into exactly one icon,
and the unrelated Splash-screen feature. `Colors.xaml` is untouched, `App.xaml.cs`
still hardcodes `AppTheme.Dark`, no Manrope font exists, radius values are still the
old 8/12 scale. See the earlier findings summary in conversation for the full
before/after audit.

## Ordering principle

Tokens and infra first (nothing else can look right until these exist), then
restyle existing screens outward from the shared chrome (headers, cards, buttons)
before per-screen detail, then the two genuinely new screens last since they have
no existing page to anchor against.

---

## Phase 0 — Fonts & icons

- [ ] Download Manrope `.ttf` weights 500/600/700/800 from Google Fonts, add under
      `AxisApp/Resources/Fonts/`.
- [ ] Register in `MauiProgram.cs`'s `.ConfigureFonts(...)` alongside the existing
      `fonts.AddFont("lucide.ttf", "Lucide")` call — one `AddFont` per weight, aliased
      (e.g. `"Manrope-Bold"`, `"Manrope-ExtraBold"`) since MAUI fonts are registered
      per-file, not per-family-with-weight-variants.
- [ ] Decide the weight→role mapping from the README (headings/amounts → 800,
      labels/section headers → 700, body → 600) and encode it as new
      `FontFamily` tokens in `Tokens.xaml` (e.g. `FontDisplay`, `FontLabel`,
      `FontBody`) rather than hardcoding font names in every XAML file.
- [ ] Finish wiring the icon font: `AppConstants.Icons` already declares
      `ArrowLeft`/`MoreVertical`/`Plus`/`LogOut`/`User`/`Users`/`Ticket`/`Car`/`Plane`
      but only `ShoppingCart`/`ArrowRight` are actually used (`GroupDetailPage`'s
      category icon). Replace every hardcoded Unicode glyph (`&#8249;`, `&#8942;`,
      `+`, `&#10003;`) across `PageHeaderBar`, `GroupsPage`'s FAB,
      `GroupDetailPage`'s FAB, `AddExpensePage`'s split checkbox, the account menu's
      Log out row.
- [ ] Add any missing category icons the README's icon list implies but
      `AppConstants.Icons` doesn't yet have (Utilities, Entertainment — check against
      the actual category set in `ICategoriesRepository`/seed data).

## Phase 1 — Design tokens

### `Tokens.xaml`
- [ ] `RadiusControl` 8 → **10**, `ShapeControl` to match.
- [ ] `RadiusCard` 12 → **16**, `ShapeCard` to match.
- [ ] Add `RadiusLarge` / `ShapeLarge` = **22** (empty-state icon tile, dialogs) —
      doesn't exist today.
- [ ] `RadiusPill`/`ShapePill` stay 999 (unchanged).
- [ ] Add `ShadowSm`/`ShadowMd`/`ShadowLg` as `Shadow` resources, each needing a
      light and dark variant per the README's table — MAUI has no built-in
      light/dark-conditional `StaticResource` swap, so this depends on Phase 2's
      theming mechanism being in place first (see note in Phase 2).
- [ ] Avatars: already circular (`AvatarCircle` style uses `ShapePill`) — no change
      needed, confirmed pre-existing.

### `Colors.xaml` → split into base + preset layers
This is the biggest structural change and needs a decision before coding starts —
see **Open questions** below. Shape (regardless of the answer):
- [ ] New `Colors.Base.xaml`: background/surface/text/border roles, light + dark,
      per the README's "Base colors" table (`#F6F5F2`/`#16181C` etc.).
- [ ] New `Colors.Positive.xaml` / semantic tint colors (`#4F9E76`/`#C4694B` +
      tints, light + dark) — replaces ad-hoc use of `Success`/`Danger` for balance
      coloring specifically (keep `Success`/`Danger`/`Warning`/`Info` as-is for
      their current non-balance uses, e.g. form validation).
- [ ] 8 new `Colors.{Preset}.xaml` files (Indigo/Green/Red/Purple/Pink/Yellow/
      Orange/DarkBlue), each defining `Accent`/`AccentHover`/`AccentPressed`/
      `AccentTint` for both light and dark, per the README's table. Indigo is
      default.
- [ ] Every existing `{StaticResource Primary}`/`BtnPrimary*`/`InputBorderFocused`/
      etc. reference across `Styles.xaml` needs auditing — these currently point at
      hardcoded blue (`#3D6BFF`); they should become `{DynamicResource Accent}` (or
      equivalent) so a preset swap actually repaints the app. `{StaticResource}` on
      an accent-derived key will NOT respond to preset changes at runtime — this is
      the one place `{DynamicResource}` is required project-wide, contrary to the
      normal `StaticResource` convention.
- [ ] `TextOnAccent` needs the Yellow-preset override called out in the README
      (deliberately darkened accent so white text stays legible) — verify contrast
      isn't an issue on any other preset while doing this pass.

## Phase 2 — Theming infrastructure (light/dark + accent presets)

- [ ] New `Services/ThemeService.cs`: owns swapping which `Colors.{Preset}.{Light,
      Dark}.xaml` dictionaries are in `Application.Current.Resources
      .MergedDictionaries`, persisted via `Preferences` under a new
      `AppConstants.Preferences.ColorPresetKey` (and a separate key for light/dark/
      follow-system if that's user-choosable — see open question below).
- [ ] Remove `Application.Current!.UserAppTheme = AppTheme.Dark;` from
      `App.xaml.cs` — replace with `ThemeService` applying the persisted (or
      system-default) theme at startup, before `SplashPage` or `AppShell` renders.
- [ ] Register `ThemeService` in `MauiProgram.cs`, inject wherever the preset
      switcher UI lives (see Phase 8/9 — no dedicated Settings/Profile screen
      exists yet to host a preset picker; decide where it goes, see open
      questions).
- [ ] Verify `AppShell.xaml`'s own chrome (nav bar, if any becomes visible) and any
      platform-specific chrome (Android status bar color, Windows title bar) don't
      hardcode dark-only colors elsewhere — grep `Platforms/` for color literals.

## Phase 3 — Shared control restyle

Once tokens exist, most of this is mechanical — swap old radius/color references
for new ones. Order matters because later screens reuse these styles.

- [ ] `PageHeaderBar`: swap Unicode glyphs for Lucide `ArrowLeft`/`MoreVertical`,
      confirm it still clears Windows' native caption buttons after the type-ramp
      changes.
- [ ] `SurfaceFrame`/`ElevatedCard`/`ListItemCard`/`BalanceTileBase`: already
      reference `ShapeCard` (will pick up the 12→16 change automatically once
      Phase 1 lands) — no separate work, just re-verify visually.
- [ ] `BtnPrimaryStyle`/`BtnSecondaryStyle`/`BtnOutlinePrimary`/`InputBorderStyle`/
      `ChipBorderStyle`: same — already token-driven, confirm `Accent`
      `{DynamicResource}` swap (Phase 1) flows through correctly.
- [ ] `FabButtonStyle`: confirm final shape matches "56px circular, ShadowLg" —
      current style uses `RadiusPill` + `HorizontalOptions="Center"` but no
      explicit `WidthRequest`/`HeightRequest`, so it may render as a wide pill
      instead of a circle depending on `Text`/`Padding`. Needs an explicit
      56×56 circle, not just corner radius.
- [ ] Segmented control: no dedicated "2-option segmented pill" style exists today
      — `GroupDetailPage`'s Pairwise/Simplified toggle is currently a bare
      `Switch`, not a segmented control. Build a reusable
      `SegmentedControl`-shaped style (base `SegmentPillContainer` +
      `SegmentButtonBase` already exist and are used in `AddExpensePage`'s
      Equally/Manually control — reuse that pattern rather than inventing a new
      one).

## Phase 4 — Restyle existing screens (visual only, no new logic)

Each of these is "make it match the reference frame" — no ViewModel/command
changes, per the README's "Interactions & behavior" section.

- [ ] **Splash** (`SplashPage.xaml`): centered "Axis" wordmark (800/40px), 44×4px
      accent rounded bar, "One shared ledger." subhead (muted/13px/600), custom
      24px circular spinner (3px border, accent top segment, rotating) — replace
      the current plain `ActivityIndicator`. A rotating border segment needs either
      a `RotationAnimation` on a partially-stroked `Ellipse`/`Border`, or an
      embedded Lottie-style asset if that's simpler — decide during implementation.
- [ ] **Login** (`LoginPage.xaml`): currently a generic centered form with no
      wordmark-top-left layout, no "Log in"/subhead copy, no OR divider, no ghost
      "Forgot password?" link — this page needs the most rework of the "restyle
      only" screens since it's furthest from the reference today. `ForgotPassword`
      isn't wired to anything yet (check `LoginViewModel` — if no command exists,
      this is either a dead link or needs a command added, which would exceed
      "restyle only" scope — flag before implementing).
- [ ] **Groups list** (`GroupsPage.xaml`): header/card layout is structurally
      close already — mainly token/color swap. Two real gaps: (1) balance pill tag
      ("you're owed"/"you owe") described in the README isn't in the current
      template, only a colored amount — add the pill; (2) empty state is a single
      muted text line today, not the spec'd icon-tile + heading + stacked buttons
      layout — needs a real empty-state view branch (`IsEmpty` binding already
      exists, just swap what it shows).
- [ ] **Group detail** (`GroupDetailPage.xaml`): balances section currently a
      `Switch` + flat list for both modes — needs the segmented control
      (Phase 3) plus mode-specific layouts: Pairwise rows (avatar/name/caption/
      amount/Settle button — close to current shape already) vs. Simplified mode's
      "Net position" summary card (kicker + 28px/800 amount + explainer) which
      doesn't exist in any form today — new markup, bound to existing
      `MemberBalanceItem`/`IsPairwiseMode` data, no new ViewModel state needed per
      the README.
- [ ] **Add/edit expense** (`AddExpensePage.xaml`): header/category
      pills/paid-by row/split checklist are structurally already close (checkbox
      squircle-vs-sharp is the main visual gap) — mostly token/radius/color pass.
- [ ] **Join group** (`JoinGroupPage.xaml`): structurally close — QR placeholder,
      monospace code input, phantom/claimed match rows all exist; mainly
      token/color/radius pass plus swapping the `Link` button treatment to match.
- [ ] **New group** (`NewGroupPage.xaml`): already a dedicated page (contradicts
      the design README's assumption it's still `DisplayPromptAsync` — that's
      stale, ignore that part of the brief). Straightforward token/color pass.
- [ ] **Account menu** (`GroupsPage.xaml`'s `IsAccountMenuOpen` overlay): already
      matches the shape described (email + divider + disabled Profile + Log out in
      danger color) — verify card radius/shadow pick up Phase 1 changes, no
      structural work expected.

## Phase 5 — New screens

- [ ] **Settle up**: `GroupDetailViewModel.Settle(MemberBalanceItem)` currently
      executes directly with no confirmation UI at all. Decide: dedicated page
      (matches the README's screen 6 — two avatars + arrow, amount, explainer,
      Confirm/Cancel) vs. a confirm dialog. README's own phrasing ("decide whether
      to route there or keep it as a confirm dialog") leaves this open — **needs a
      decision before starting**, see open questions. If a page: new
      `Pages/SettleUpPage.xaml` + route constant + navigation from `Settle`,
      passing the `MemberBalanceItem`'s amount/counterparty via query params
      (same pattern `AddExpensePage` uses for `?expenseId=`).
- [ ] **Preset/theme picker UI**: not in the README's screen list at all (it
      covers the 9 numbered screens only) — the accent-preset and light/dark
      mechanism has no described home screen. Needs a decision: new minimal
      Settings/Profile page (the account menu already has a disabled "Profile"
      placeholder row — this could be its first real use), or an inline picker
      somewhere in the account menu overlay itself.

## Open questions (resolve before / during implementation, not deferred silently)

1. **Settle-up: page or dialog?** Affects whether Phase 5 adds a new route or just
   a `DisplayAlert`-style confirmation. Recommend the dedicated page — the README's
   own mockup for it is detailed enough (two avatars, arrow, amount, two buttons)
   that a generic alert would visibly undershoot it, and it's consistent with
   `AddExpensePage` already being a real page rather than a dialog.
2. **Where does the preset/light-dark picker live?** No screen in the handoff
   covers this. Recommend building a minimal real Profile page now that the
   account menu already has a disabled placeholder row pointing at one — smaller
   scope than inventing a new overlay pattern.
3. **Light/dark: user toggle, follow-system, or both?** README says "light/dark
   now needs to be a user choice (or follow system)" — doesn't commit to one.
   Recommend: default to follow-system (`AppInfo.RequestedTheme` via MAUI's theme
   change event), with an explicit override stored in `Preferences` once the user
   picks one from the Profile page — same optional-override pattern most apps use.
4. **`LoginPage`'s "Forgot password?" link** — verify whether `LoginViewModel` has
   any backing command/flow. If not, this is either scoped out of "restyle only"
   or needs a stub decision (disabled link vs. a real password-reset flow, which
   is a separate feature).
5. **16 resource-dictionary combinations** (8 presets × light/dark) — confirm the
   `Colors.{Preset}.{Light,Dark}.xaml` split (16 files) is preferred over a
   single-file-per-preset with `AppThemeBinding` inline (fewer files, but MAUI's
   `AppThemeBinding` only works in a handful of property contexts, not arbitrary
   `StaticResource` chains) — the README's own wording ("you likely want two
   dictionaries per preset... rather than 16 hand-written full files" — note this
   is actually describing splitting into 16 *smaller* files, base + preset-only,
   not 16 full files) implies the split-dictionary approach; confirm before
   generating 16 files by hand.

## Explicitly out of scope (per README)

- iOS-specific work (Universal Links, iOS-only styling) — iOS isn't in the active
  `TargetFrameworks` yet.
- Any new backend/schema work — this is a pure front-end restyle, no
  `supabase/schema.sql` changes expected.
- Editing a settle-up `Payment` (only `Expense` editing exists today) — unrelated
  to this redesign, not mentioned in the handoff.
