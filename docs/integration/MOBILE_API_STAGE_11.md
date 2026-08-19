# Mobile API follow-up — Retailer invite resolve

For production retailer onboarding QR codes, add a public, rate-limited resolve contract such as:

`POST /api/consumer/retailer-invites/resolve`

Request: an opaque, signed, expiring invite token. Response: consumer-safe tenant ID, retailer
name, logo, active stores, invite expiry, and whether joining is currently allowed.

The join itself should remain the existing authenticated, idempotent
`POST /api/consumer/loyalty/{tenantId}/join` operation and must require explicit confirmation in
mobile. QR content must never contain theme/config JSON or be treated as an arbitrary URL.

Until that endpoint exists, Stage 11 accepts only a versioned UUID payload and verifies it against
the existing consumer-safe network catalogue.
