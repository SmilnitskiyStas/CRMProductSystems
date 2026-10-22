# TASK-762 frontend — production inbox + orders ship

- Requests (production mode) = pure inbox: stage chip, removed summary tab / row selection / create-orders / request-level ship; detail shows accepted qty, hasRecipe warning + "sync-orders" retry, linked orders links.
- Store page: stage chip in list + detail.
- Orders: renamed "Виробничі замовлення" (sidebar/title/back); list "Магазини" column; detail requests table (status chip, shipment link), DONE block with per-row Ship + Ship-all (ship-requests, inline per-request results).
- Types/api/hooks: productionStage, hasRecipe, linkedOrders, requestCount/storeCount, ShipRequestResultDto, syncRequestOrders, shipOrderRequests.
- Removed ProductionRequestSummary.tsx (unused). i18n uk+en.
- Build: tsc --noEmit and eslint features/production clean.
