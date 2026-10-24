# PHASE-1-10 — MFA settings reminder and optional enrollment at login

Date: 24.10.2026, Europe/Kyiv.
Status: IN PROGRESS — owner explicitly confirmed optional enrollment for all roles, including administrators and managers. Not deployed yet.

Owner requested that accounts without configured MFA enter the app normally and receive a recommendation to enable two-factor authentication in account settings. Accounts that already enabled MFA must still verify the factor at login. Critical-operation step-up remains in place.

Prepared:
- Non-blocking Ukrainian/English web reminder linking directly to /settings-user#security.
- Mobile workspace reminder linking to profile settings, plus a voluntary MFA setup action in the profile.
- Reminders disappear when the authoritative account profile reports MFA enabled; consumer identities without a supported MFA settings flow are excluded.
- Regression checks for reminder visibility, localization, navigation, and manual setup.

Owner explicitly confirmed: "Так, для всіх ролей без налаштованої MFA". Password login, refresh and bearer validation now admit unenrolled sessions for every role. They do not mint an MFA verification claim. Once MFA is enabled, both the configured-factor login challenge and verified-session requirement remain mandatory. Sensitive-operation recent-MFA checks remain unchanged. Existing restrictions on disabling MFA for privileged accounts remain unchanged.

Validation: 991 selected backend tests PASS; four real-controller/full-migration/RLS HTTP checks PASS; 25 isolated PostgreSQL/session/concurrency checks PASS. 21 web and 12 mobile UI tests PASS; frontend/mobile TypeScript and Linux API publish PASS. Total selected checks: 1,053 PASS, without double-counting repeated runs. Production web build is pending. No database migration or credential change is required for this follow-up.
