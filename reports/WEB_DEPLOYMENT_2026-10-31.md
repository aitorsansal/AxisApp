# Web deployment ? 2026-10-31

The complete Together web update from source commit b2367da7991ab2ce435314f570b275d69d4ffb77 was deployed with the repository's production command:

`npm.cmd run deploy --prefix webapp`

- Destination: https://app.axisapp.aitorsansal.com/
- Cloudflare Worker: axisapp-webapp
- Cloudflare owner: aitorsansal
- Worker version: f83438ae-2851-48c9-813e-4d6d57f4febb
- Worker URL: https://axisapp-webapp.aitorsansal.workers.dev
- Production application bundle: /assets/App-D6jt9sQw.js
- Production application stylesheet: /assets/App-CLnhb7v5.css
- JavaScript entry: /assets/index-Dq1w5NgA.js

The command built the production configuration, uploaded four changed assets, published the Worker and passed its smoke checks for /, /profile and the expected JavaScript entry on the custom domain.

A read-only check in the existing signed-in production browser confirmed Group Detail rendered its new group context, directional balance summary and activity panel using the expected application stylesheet. No record was created or changed during deployment verification.

The deployed changes cover Group Detail, ordinary/recurring expense and repayment forms, Event Detail/forms, Group Events, Members, recurring lists and member profiles. The source commit also includes the pending Firebase dependency and pnpm workspace changes, as requested. Deployment followed the npm workflow; the pnpm workspace placeholder build settings were preserved.

The earlier automated fixture/domain checks remain documented in GROUP_DETAIL_DESIGN_REVIEW.md, FORMS_DESIGN_REVIEW.md and REMAINING_PAGES_DESIGN_REVIEW.md. Deployment smoke checks and the signed-in render check do not establish a new live financial-save, push-delivery or multi-client acceptance pass.

No Android release or backend migration was part of this web deployment.
