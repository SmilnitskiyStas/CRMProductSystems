# TASK-720 — Landing: POS/ПРРО copy sweep + roadmap progression

- A: прибрано POS-касу / ПРРО / Checkbox / Z-звіти / «каса з телефона» з `Landing.*` (uk+en): meta, hero, features, loyalty, pricing, FAQ (чесно: ще ні, планується), /retail, trust strip (→ B2B-маркетплейс), /features (група «Продажі»), roadmap (прибрано з done; planned «Власна касова система» + підтримка ПРРО), /how-it-works.
- B: `/roadmap` — сегментований progress bar (`RoadmapProgress`) + stage track (`RoadmapTimeline`: горизонтальний desktop / вертикальний mobile, rail заповнена до «В роботі»). `MiniRoadmapSection` на головній використовує той самий `RoadmapProgress`. `RoadmapColumns.tsx` видалено. Нові ключі: `Landing.pages.roadmap.progress.*`; видалено `miniRoadmap.done/doneTitle`. CSS: `.rm-pulse`/`.rm-shimmer` (reduced-motion guarded).
- Build: tsc, lint, next build — OK. Не комітилось.
