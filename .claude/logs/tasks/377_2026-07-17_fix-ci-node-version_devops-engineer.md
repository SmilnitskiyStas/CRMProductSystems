# TASK-377: CI fix — frontend-ci EUSAGE (npm ci lockfile sync), не Node/ESLint

**Agent:** devops-engineer
**Date:** 2026-07-17
**Status:** done

## Проблема

`frontend-ci` падав на останніх 3 пушах (runs #192-194, включно з повністю порожнім
коммітом #194) з generic "Process completed with exit code 1", без деталей у
GitHub UI. Гіпотеза користувача (Node 24 forced-runtime × ESLint 8 несумісність)
була розумною відправною точкою, але не підтвердилась.

## Реальна причина (з Docker-репродукції, не здогадка)

Відтворив точні CI-кроки (`npm ci` → `npx tsc --noEmit` → `npm run lint`) у
Linux-контейнерах (`node:20-slim`, `node:22-slim`, `node:24-slim`) з git-committed
деревом `frontend/` (через `git archive HEAD`, щоб виключити локальний
`node_modules`/`.next`). Реальна помилка виявилась на найпершому кроці, `npm ci`:

```
npm error code EUSAGE
npm error `npm ci` can only install packages when your package.json and
npm error package-lock.json or npm-shrinkwrap.json are in sync...
npm error Missing: @swc/helpers@0.5.23 from lock file
```

Корінь: `next` вимагає `@swc/helpers@0.5.5` (hoisted у lockfile), але
`next-intl` (доданий у 60234a15 для i18n) тягне вкладений `@swc/core`, чий
`peerDependency` хоче `@swc/helpers>=0.5.17` — конфлікт, якого lockfile не
покриває окремим nested-записом.

Ключове: **npm 10.x** (це саме той npm, який бандлиться і з Node 20.x, і з
Node 22.x — перевірено обидва) вважає це "not in sync" і хардфейлить `ci` за
~секунди, до того як tsc/lint взагалі стартують (звідси ~12с і відсутність
tsc/eslint діагностики в анотаціях). **npm 11.x** той самий конфлікт як
blocking не трактує — `npm ci` під ним проходить чисто без жодних змін
lockfile. Локальний Windows-репро користувача проходив, бо там глобально
стоїть npm 11.14.1 (новіший за те, що бандлить сам Node) — це npm-версія,
а не ОС чи Node-версія, розділяла "працює локально" / "падає в CI".

**Гіпотеза користувача (bump Node → 22.x) перевірена і спростована окремо:**
`node:22-slim` бандлить npm 10.9.8 — той самий клас npm 10.x — і падає
ідентично. Сам по собі Node-bump нічого не виправляв би.
`node:24-slim` бандлить npm 11.16.0 — тому 24.x випадково "працював би", але
опора на те, який саме npm випадково притягне конкретна Node-лінія — крихка.

ESLint 8.56.0/eslint-config-next 14.2.35 — не проблема: `npm run lint` дав
чистий `✔ No ESLint warnings or errors` одразу після того, як `npm ci`
відпрацював під npm≥11. Апгрейд ESLint не був потрібен, не чіпав.

## Що змінив

`.github/workflows/ci.yml`:
- `frontend-ci`, `worker-ci`, `mobile-ci`: `node-version` `'20.x'` → `'22.x'`
  (усуває "Node 20 deprecated" шум; Node 20 LTS вже EOL). Це не сам фікс, але
  безпечний side-cleanup, який просив користувач як стартову точку.
- `frontend-ci` **(сам фікс)**: новий крок `Ensure npm >= 11` —
  `run: npm install -g npm@11` — одразу після setup-node, перед `npm ci`.
  Floating `@11` (не exact patch), консистентно зі стилем решти файлу
  (`'8.x'`, `'22.x'`) — підхоплює патчі в межах перевіреної мажорної лінії.
- `worker-ci`/`mobile-ci`: без npm-піна — їхні lockfile не мають цього
  peer-dep конфлікту, пін там не потрібен (мінімальний скоуп, нічого зайвого).
- i18n-файли не чіпав (не було потреби — код чистий, підтверджено tsc+lint).

## Верифікація

Всі кроки прогнані в свіжих Linux-контейнерах під новою конфігурацією
(Node 22 + `npm install -g npm@11` для frontend; default npm 10.9.8 для
worker/mobile — не змінювався):

| Job | Крок | Результат |
|---|---|---|
| frontend-ci | `npm ci` | exit 0 |
| frontend-ci | `npx tsc --noEmit` | exit 0 |
| frontend-ci | `npm run lint` | exit 0, "No ESLint warnings or errors" |
| worker-ci | `npm ci` | exit 0 (88 packages, 0 vulnerabilities) |
| worker-ci | `npx tsc --noEmit` | exit 0 |
| mobile-ci | `npm ci` | exit 0 (881 packages) |
| mobile-ci | `npx tsc --noEmit` | exit 0 |

Жодних регресій у worker/mobile. Контейнери прибрані після перевірки.

## Побічна знахідка (не в скоупі, не чіпав)

`frontend/.env.local` — трекається в git (`git ls-files` підтверджує),
попри те що `.gitignore` його виключає (правило не діє на вже затрекані
файли). Вміст зараз безпечний (лише `NEXT_PUBLIC_API_URL=http://localhost:5000`
+ коментар), секретів немає. Поза скоупом TASK-377 — окремий чіп заведено.

## Файли
- `.github/workflows/ci.yml`

## Не в скоупі
- Push у main — користувач робить сам, щоб особисто прослідкувати наступний CI-прогін.
- i18n-код (app/[locale]/*, middleware.ts, i18n/*, messages/*) — не чіпав, не було причин.
- `frontend/.env.local` tracked-file issue — заведено окремо.
