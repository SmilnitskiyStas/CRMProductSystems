# PHASE-1-11 — public browser API origin

Date: 24.10.2026. Status: DEPLOYED AND VERIFIED.

Production login failed with ERR_CONNECTION_REFUSED because a locally prepared Next.js build embedded NEXT_PUBLIC_API_URL=http://localhost:5000 from .env.local. Previous page-health and exact-bundle checks did not exercise browser API routing.

Rebuild with the explicit public HTTPS API origin. Add a production-build guard that rejects missing, loopback, insecure or credential-bearing API origins; remove the Dockerfile loopback default. Preserve the existing local staging flow through an explicit build-only opt-in, never enabled by default in production. Add guard tests to frontend CI and a browser-client login-origin regression test.

Deploy only the corrected frontend; current API, worker, sessions and MFA policy remain unchanged. Verification must inspect the actual public login chunks, check CORS with credentials and perform one negative login using a synthetic nonexistent identity. No real user credentials are needed.

Validation so far: 20 native build-config checks PASS (including actual Next production-config wiring) and 55 frontend API/private-assets/MFA checks PASS. Corrected production build uses an explicit HTTPS API origin and disables the local staging opt-in.

## Deployment and acceptance

Frontend application revision `190164276cf054e9a266cd3d70f8b266ae68d205` is deployed. API and worker remain on the verified `85d3191b` application revision; only the web service was recreated. Previous web image is retained. No database, credential, session or MFA policy changes were made for this fix.

Corrected Next production build passed (105 routes; existing next-intl environment notices remain). Every generated browser JS file was checked for the bad localhost:5000 literal before packaging. Linux canary and public HTTPS acceptance verified hashes and the configured origin of the API chunks actually referenced by /login. The public login page loads the corrected client, including `static/chunks/1420-204963a5c124edef.js`, which uses `https://api.agrusystems.pp.ua`.

Credentialed CORS preflight passed. One negative login against the public HTTPS API with a randomly generated nonexistent synthetic identity returned structured HTTP 401 with the correct CORS headers. No real account credentials or successful customer login were exercised. No email, fiscalization or AI operation was invoked.

Twenty build/config tests and 55 web regression tests passed. CI run 480 passed the new guard, browser-origin and type checks; its lint step revealed that Next lint also loads production configuration. CI now explicitly supplies the public API origin (workflow-only commit `b5d06626`); local lint passed with that configuration, and the follow-up CI is running.

The first canary acceptance script overreached into an unrelated encoded dynamic-route asset URL that returned HTML. Diagnostic output confirmed exact bytes and HTTPS origin for the shared login client; acceptance was corrected to check the API chunks actually referenced by the login page. The original payload/helper was preserved and the revised acceptance helper was separately hash-verified. No production cutover occurred until this login acceptance passed.

Browsers with the old application already open must reload the login page to load the newly hashed chunks. PR #26 remains open and unmerged.
