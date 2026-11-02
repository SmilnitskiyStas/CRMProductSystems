# ERP operational tool

W3-A: `dry-run` однієї tenant/location scope з явним `SHELFGUARD_ERP_READ_CONNECTION`. W3-B: `materialize-control` для контрольного початкового ledger явно вибраних партій з окремим `SHELFGUARD_ERP_WRITE_CONNECTION`. Типового підключення немає; програма не запускає API та не застосовує міграції.

```powershell
dotnet build backend/ShelfGuard.Erp.Tool/ShelfGuard.Erp.Tool.csproj -c ErmW3Scaffold
dotnet run --project backend/ShelfGuard.Erp.Tool/ShelfGuard.Erp.Tool.csproj --no-build -c ErmW3Scaffold -- --help
```

Запуск аналізу: `dry-run --tenant <uuid> --location <uuid> [--max-rows 20000] [--output <новий.json>]`. Читання відбувається в `REPEATABLE READ, READ ONLY`; частковий результат або перезапис наявного файла не допускаються.

Повний [контракт і runbook W3-A](C:/Users/stass/source/CRMProductSystems/docs/ERM/AgruSystems_W3A_DryRun_Guide_v1.md).

Запис виконується тільки в maintenance-вікні після фактичного staging-приймання, з явними run/fingerprint/selection/reason/acceptance. Він блокує таблиці, перевіряє джерела та атомарно створює documents/ledger/projections/reconciliations і historical/suppressed outbox. Повтор того самого run повертає квитанцію без дублікатів. Робочий writer не перемикається.

Параметри, обмеження, retry невизначеного commit: [runbook W3-B](C:/Users/stass/source/CRMProductSystems/docs/ERM/AgruSystems_W3B_ControlOpening_Guide_v1.md).

На staging з legacy restrictive RLS потрібен `--operator <assigned_user_uuid>` для обох команд. Він перевіряється як активний tenant user із призначенням на локацію. Відсутність оператора спричиняє відмову, а не порожній звіт. Поточний writer request protocol v2 включає actor у hash; target identity protocol лишається v1.
