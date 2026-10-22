# TASK-751 — Планограма frontend (підкладка плану, типи зон, інсайти полиці, збагачений скан)

Контракт backend: TASK-750. Лише `frontend/`.

- Підкладка: `FloorPlanLayout.background`, `lib/floorPlanBackground.ts` (PDF 1-ша сторінка -> PNG через `pdfjs-dist@4.10.38`, dynamic import), upload `POST /api/locations/{id}/floor-plan/background`, кнопка «Завантажити план» + прозорість + прибрати (SidePanel), Canvas малює фон під зонами, висота канви під аспект зображення.
- PDF worker лежить у `public/pdfjs/pdf.worker.min.mjs` (копія з pdfjs-dist; бандл .mjs через SWC-мініфікатор падає). Оновлювати при апгрейді pdfjs-dist.
- Типи зон: `lib/zoneTypes.ts` (shelf, fridge, checkout, entrance_exit, department, storage, other + legacy), палітра в SidePanel, ZoneDialog `initialType`; checkout/entrance_exit — маркери без FEFO-статусу. Режим «Перегляд»: клік по зоні -> shelves.
- Інсайти: `ShelfItemInsightDto`, `shelvesApi.getShelfInsights`, `useShelfInsights`, `ShelfInsightPanel` (клік по секції з товарами; порожня секція відкриває пікер як раніше).
- `ShelfScanPanel`: нові поля ScanItemDto + рекомендація, summary зверху.
- i18n: uk/en (`shelfInsight`, `floorPlan`, `floorPlanPage`, `zoneTypes`, `shelvesPage`).
- Build: `tsc --noEmit`, `npm run lint`, `next build` — OK. У браузері не перевірено (backend ендпоінтів ще нема).
