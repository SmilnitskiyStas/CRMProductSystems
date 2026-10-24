# UI-01 — full-width employee settings

Date: 24.10.2026. Status: DEPLOYED AND VERIFIED.

Owner requested that employee settings use the whole available page area and group related controls. Removed the 780px page cap. Personal information, account security (password plus MFA), and notifications now form clearly labeled groups with quick section links. Wide content uses two columns; narrow content uses a single column. Very wide content can show password and MFA side by side. Container queries follow available content width when the sidebar changes.

Preserved #profile, #password, #security and #telegram targets. Removed the duplicate avatar/name block inside the profile form because the full-width page header already provides it. Contact fields adapt to narrow cards. Auth/API behavior, permissions, password/MFA actions and Telegram linking logic remain unchanged.

Visual review uses the actual React page/forms with a synthetic cached profile and all API network actions disabled. At available widths 390, 1280 and 1920px, document scroll width equals client width. Wide profile/security cards use both sides of the page, narrow cards follow profile → security → notifications order, and heading IDs are unique. No real user settings were read or submitted. Local screenshot is kept outside versioned source.

Production build (105 routes), type checks and lint PASS. GitHub CI run 485 passed every validation job. Frontend-only deployment retained the verified API/worker and MFA policy.


Deployment — 24.10.2026, approximately 13:15 Europe/Kyiv: frontend revision `a66f5e94a0bb8cf8a63c72d31159dfd03f67453d` deployed after Linux canary validation. Public settings JS/CSS hashes and login API-origin chunks match the checked build. Web service is healthy; public pages and credentialed CORS/negative login checks pass. API and worker images remain unchanged on the previous verified application release. No database migrations or user-setting writes were performed. Temporary synthetic preview and canary services were stopped/removed. PR #26 remains open and unmerged.

24.10.2026 follow-up: owner requested removal of the three quick-navigation controls. Removed the shortcut row and its unused CSS/label, retaining the full-width groups, password/MFA anchors and a normal 24px gap below the profile header.
