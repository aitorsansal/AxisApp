# React + TypeScript + Vite

## Axis deployment checks

Use `npm run deploy --prefix webapp` from the repository root. It builds,
deploys with Wrangler and checks the published home route, `/profile` SPA
fallback and expected JavaScript entry. `npm run smoke --prefix webapp`
repeats those HTTP checks against the current local build. Set
`AXIS_SMOKE_BASE_URL` to check a preview host instead. These checks establish
that the expected shell/assets are served; browser startup and authenticated
workflows still need UI checks. A failed smoke check reports failure after
deployment; it does not automatically roll back the release.

The Vite build rejects missing, malformed or example Supabase settings before
writing assets. Supply `VITE_SUPABASE_URL` and
`VITE_SUPABASE_PUBLISHABLE_KEY` through `webapp/.env.local` locally or the
hosting provider's **build environment**. Runtime Worker variables do not
populate Vite's client bundle. Never use a Supabase secret key in the client.
Firebase configuration remains a separate notification setup concern.

Startup validates the same settings before importing the app. Failed startup
shows an English/Spanish retry screen without exposing configuration values.
The HTML retains a loading/reload message if JavaScript cannot load at all.

Run the isolated missing-config browser fixture with
`npx vite --config tests/startup.vite.config.ts` from `webapp/`, then visit
`http://127.0.0.1:5175/`. This configuration intentionally clears the client
settings; never use it for a release.

This template provides a minimal setup to get React working in Vite with HMR and some Oxlint rules.

Currently, two official plugins are available:

- [@vitejs/plugin-react](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react) uses [Oxc](https://oxc.rs)
- [@vitejs/plugin-react-swc](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react-swc) uses [SWC](https://swc.rs/)

## React Compiler

The React Compiler is not enabled on this template because of its impact on dev & build performances. To add it, see [this documentation](https://react.dev/learn/react-compiler/installation).

## Expanding the Oxlint configuration

If you are developing a production application, we recommend enabling type-aware lint rules by installing `oxlint-tsgolint` and editing `.oxlintrc.json`:

```json
{
  "$schema": "./node_modules/oxlint/configuration_schema.json",
  "plugins": ["react", "typescript", "oxc"],
  "options": {
    "typeAware": true
  },
  "rules": {
    "react/rules-of-hooks": "error",
    "react/only-export-components": ["warn", { "allowConstantExport": true }]
  }
}
```

See the [Oxlint rules documentation](https://oxc.rs/docs/guide/usage/linter/rules) for the full list of rules and categories.
