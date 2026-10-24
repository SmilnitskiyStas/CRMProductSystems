# PHASE-1-11 — public browser API origin

Date: 24.10.2026. Status: IN PROGRESS.

Production login failed with ERR_CONNECTION_REFUSED because a locally prepared Next.js build embedded NEXT_PUBLIC_API_URL=http://localhost:5000 from .env.local. Previous page-health and exact-bundle checks did not exercise browser API routing.

Rebuild with the explicit public HTTPS API origin. Add a production-build guard that rejects missing, loopback, insecure or credential-bearing API origins; remove the Dockerfile loopback default. Preserve the existing local staging flow through an explicit build-only opt-in, never enabled by default in production. Add guard tests to frontend CI and a browser-client login-origin regression test.

Deploy only the corrected frontend; current API, worker, sessions and MFA policy remain unchanged. Verification must inspect the actual public login chunks, check CORS with credentials and perform one negative login using a synthetic nonexistent identity. No real user credentials are needed.

Validation so far: 20 native build-config checks PASS (including actual Next production-config wiring) and 55 frontend API/private-assets/MFA checks PASS. Corrected production build uses an explicit HTTPS API origin and disables the local staging opt-in.
