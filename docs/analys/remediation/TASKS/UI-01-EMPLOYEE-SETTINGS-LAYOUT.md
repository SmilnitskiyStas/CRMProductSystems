# UI-01 — full-width employee settings

Date: 24.10.2026. Status: IN PROGRESS.

Owner requested that employee settings use the whole available page area and group related controls. Removed the 780px page cap. Personal information, account security (password plus MFA), and notifications now form clearly labeled groups with quick section links. Wide content uses two columns; narrow content uses a single column. Very wide content can show password and MFA side by side. Container queries follow available content width when the sidebar changes.

Preserved #profile, #password, #security and #telegram targets. Removed the duplicate avatar/name block inside the profile form because the full-width page header already provides it. Contact fields adapt to narrow cards. Auth/API behavior, permissions, password/MFA actions and Telegram linking logic remain unchanged.

Visual review uses the actual React page/forms with a synthetic cached profile and all API network actions disabled. At available widths 390, 1280 and 1920px, document scroll width equals client width. Wide profile/security cards use both sides of the page, narrow cards follow profile → security → notifications order, and heading IDs are unique. No real user settings were read or submitted. Local screenshot is kept outside versioned source.

Production build/type/lint validation is in progress. Frontend-only deployment will retain the current verified API/worker and MFA policy.
