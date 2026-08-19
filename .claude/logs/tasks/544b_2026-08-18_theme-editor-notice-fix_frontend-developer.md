# TASK-544b — Fix Theme Editor's stale "changes are immediate" notice

**Agent:** frontend-developer
**Status:** done
**Date:** 2026-08-18

## Scope delivered

`ThemeEditorSection.tsx`'s inline notice (added TASK-537) claimed theme saves take effect
immediately for every consumer with no preview/publish step. TASK-544 made that false: theme is
now composed into the tenant's document at `POST /api/v1/mobile/config/publish` time, and
`GET /api/v1/mobile/config` reads `theme` from the last *published* version, not live from
`MobileTheme` anymore. Updated the notice to state the real behavior instead.

## Changes

- `frontend/features/consumer-app/components/ThemeEditorSection.tsx`:
  - Renamed the i18n key `liveEffectNotice` → `draftNotice`, matching the exact key name already
    used by the sibling `AppBuilderCanvas.tsx`/`NavigationBuilderSection.tsx` notices for the same
    draft/publish concept — keeps section-notice vocabulary consistent across the feature.
  - Updated the component's JSDoc ("LIVE-EFFECT CAVEAT" → "DRAFT/PUBLISH STATE") and the inline
    comment above the notice `<div>`, both of which described the same now-false immediate-effect
    behavior.
  - No changes to form fields, validation, live preview, or save mechanics.
- `frontend/messages/en.json` / `uk.json`: replaced the `themeEditor.liveEffectNotice` value with
  `themeEditor.draftNotice` (key renamed + reworded), one line each.

### Copy change

- **Old (EN):** "Changes take effect immediately after saving — and are shown to every customer
  in the app right away. There is no preview or publish step yet."
- **New (EN):** "Changes are saved to the pending theme immediately, but only reach the consumer
  app after the tenant's next publish. Publishing isn't available from this screen yet — it will
  be added in a future update."
- **Old (UK):** "Зміни набувають чинності одразу після збереження — і одразу відображаються в
  застосунку для всіх покупців. Попереднього перегляду чи публікації поки не існує."
- **New (UK):** "Зміни одразу зберігаються в чернетці теми, але потрапляють у застосунок покупців
  лише після наступної публікації. Публікація поки не доступна на цьому екрані — вона з'явиться в
  одному з наступних оновлень."

No "Publish" button or link to a publish screen was added — TASK-546 owns that.

## Verification

- `npx tsc --noEmit` — PASS, no output.
- `npx eslint features/consumer-app/components/ThemeEditorSection.tsx` — 0 errors, 1 pre-existing
  warning (`no-img-element` on the logo preview `<img>`, unrelated to this change, shared with
  `BannerForm.tsx`'s identical pattern).
- `node -e "JSON.parse(...)"` on both message files — valid JSON.
- `git diff` on `en.json`/`uk.json` confirmed only the single `themeEditor` notice line changed
  per file (the rest of each file's diff is pre-existing uncommitted TASK-531–543 work, not
  touched this session). `ThemeEditorSection.tsx` is untracked (same pre-existing state); edits
  were confined to the JSDoc, the notice comment, and the `t()` call per the task's scope.

## Next

None — TASK-544b is complete. TASK-546 (Version History UI + rollback) will eventually add a real
publish action; this task deliberately did not anticipate that UI.
