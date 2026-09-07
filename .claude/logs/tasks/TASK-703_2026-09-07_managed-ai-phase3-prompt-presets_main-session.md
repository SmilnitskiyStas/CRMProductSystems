# TASK-703 — Керована AI-інтеграція, Фаза 3: пресети промптів по типах бізнесу

**Дата:** 2026-09-07
**Виконавець:** main session
**Статус:** review → (деплой через push→CI)
**Дизайн:** `.claude/docs/managed-ai-integration-plan.md` §Фази / рядок «Фаза 3»
**Пов'язане:** TASK-701 (Фаза 1), TASK-702 (Фаза 2), memory `shelfguard-managed-ai-phase1`

## Що зроблено

Зручний спосіб не набирати `extra_instructions` руками для кожного клієнта — стартові
пресети бізнес-контексту, по одному на тип бізнесу.

**Рішення власника (AskUserQuestion):**
- Пресети **фіксовані в коді** (не таблиця в БД, не provider-editable). Зміна тексту = деплой.
- Пресет **заповнює редаговане поле** `extra_instructions` — не зберігається посиланням.
  → нуль змін на бекенді: `AiPromptResolver` / схема `integration_configs` не чіпаються.

Реалізація **чисто frontend**:

| Файл | Зміна |
|---|---|
| `frontend/features/provider/aiPromptPresets.ts` | **новий.** `AI_PROMPT_PRESETS: Record<BusinessType, string>` — 9 стислих україномовних пресетів (retail, auto_service, restaurant, warehouse, production, distribution, pharmacy, floristry, supplier) |
| `frontend/features/provider/components/TenantDetailPanel.tsx` | секція «AI-агент», режим редагування: `<select>` «Пресет промпту» над полем «Додаткові інструкції». Вибір → `setAiExtra(AI_PROMPT_PRESETS[bt])`. Пресет типу бізнесу цього клієнта позначено «— рекомендовано». `tBiz` = `useTranslations("Dashboard.provider.businessTypes")` |
| `frontend/messages/uk.json` + `en.json` | +4 ключі `Dashboard.provider.tenantDetailPanel.aiPreset{Label,Placeholder,Recommended,Hint}` (однакова позиція в обох файлах) |

Пресети йдуть у системний промпт КОЖНОГО з 6 адвайзерів **після** guardrail ізоляції
(`AiGuardrail.Compose` → guardrail ∙ extra_instructions ∙ базовий промпт) — це бізнес-контекст,
не заміна правил ізоляції. Плюмбінг existing з Фази 1.

## Свідомо поза обсягом

- Немає кроку «пресет» у `CreateTenantWizard` (дизайн-док: «радше ні для v1»).
- Пресети лише україномовні (базові промпти адвайзерів теж) — не per-locale.
- Пресет заповнює тільки `extra_instructions`, не чіпає провайдера/модель.
- Вибір пресету перезаписує наявний текст у полі без confirm — провайдерський power-user
  екран, дію видно одразу, можна Cancel всього редагування.

## Перевірка

- `npx tsc --noEmit` — чисто (worktree, node_modules junction з основної теки).
- `npx next lint` — 0 warnings/errors.
- `npm run build` — exit 0, усі роути зібрані.
- **Браузер-верифікація:** пропущено локально (провайдерська картка потребує backend +
  provider-JWT + tenant; Фази 1–2 цього ж воркстріму верифікувались на проді). Перевірка
  на проді після деплою: provider login → картка клієнта → «AI-агент» → «Змінити» →
  дропдаун «Пресет промпту» → вибір типу → поле «Додаткові інструкції» заповнюється.

## Гілка / коміт

Worktree off `origin/main` (основна тека на гілці Codex-сесії), гілка `feat/managed-ai-phase3`,
1 коміт, rebase на `origin/main` перед push.
