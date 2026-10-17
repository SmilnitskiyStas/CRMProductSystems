# TASK-723 — Resilience stage 2: failure isolation for external dependencies

Status: review (not deployed)

## Що зроблено
- `Microsoft.Extensions.Http.Resilience` 8.7.0 (Polly v8) в Infrastructure.
- Open-Meteo (retry ×2 GET, breaker, timeout 10s) і Nominatim (retry ×1, breaker, 8s): `ExternalHttpResilience` + `UnavailableTranslatingHandler` → `ExternalServiceUnavailableException`.
- Checkbox ПРРО: breaker + timeout 25s, **без retry** (POST receipts/sell не ідемпотентний — дубль фіскалізації); open circuit → `FiscalProviderException(transient, transport.circuit_open)` → чек іде в offline-чергу/retry-job (ADR-011).
- AI (Claude + OpenAI/Azure): `AiResilience` — breaker на endpoint (не на ключ тенанта), 4xx (ключ/кредити) не рахуються; OpenAI ще 1 retry на 5xx/мережу; Anthropic без retry (SDK вже ретраїть). Статус non-2xx передається через `Exception.Data`, тип лишився `InvalidOperationException` (контракт існуючих тестів).
- `GlobalExceptionHandler`: `ExternalServiceUnavailableException` → 503 `{error, code:"service_unavailable", service}` + `Retry-After`, замість 500.
- Telegram (worker): `fetch` без таймауту → `AbortSignal.timeout(10s)` + `CircuitBreaker` (5 послідовних transient → open 30s; 4xx не рахуються). У backend Telegram HTTP-викликів немає — це лише worker.

## Перевірка
- `dotnet test` 2474/2474 (+7 нових у `ExternalResilienceTests`), worker `tsc --noEmit` OK, breaker прогнано через tsx (open / half-open / 4xx).
- НЕ перевірено: жива робота з реальними Checkbox/Claude/Open-Meteo, поведінка під навантаженням, фронтенд при 503.

## Відкрито
- Frontend: окремо обробити `code: "service_unavailable"` (геокодер у формі локації тощо) — зараз показується загальна помилка.
- Vchasno, Resend (email) не охоплені.
- Пороги breaker (5 / 50% / 30s) — стартові, підкрутити за метриками.
