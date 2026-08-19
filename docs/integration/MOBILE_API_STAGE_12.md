# Mobile API follow-up — Secure preview and schema alignment

Stage 12 anticipates a staff-only endpoint:

`GET /api/v1/mobile/config/preview?tenantId={tenantId}`

Requirements:

- authenticate an authorized tenant staff/admin session;
- require a short-lived, scoped, single-purpose preview token;
- return only the requested tenant's current validated draft composed with its draft theme;
- never expose draft through the anonymous published endpoint;
- rate-limit and audit preview access;
- reject expired, reused, wrong-tenant, or wrong-user tokens.

Mobile sends the preview token in `X-Mobile-Preview-Token`, never in the URL. The final header or
authorization contract can be adjusted when the backend endpoint is implemented.

Before enabling real preview, mobile validation must migrate from temporary schema v0 to the
canonical `contracts/mobile-config.schema.json` schema v1. The current canonical contract also
allows free-form navigation icon strings while mobile intentionally requires icon identifiers;
those whitelists must be reconciled rather than weakened client-side.
