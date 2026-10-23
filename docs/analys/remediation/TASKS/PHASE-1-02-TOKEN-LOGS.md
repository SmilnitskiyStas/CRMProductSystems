# PHASE-1-02-TOKEN-LOGS

- **Source ID / severity:** SEC-003 / P1; CONFIRMED перед fix.
- **Root cause:** nginx $request/combined format містять SignalR access_token query; native error formatter теж додає original request при upstream failure.
- **Scope/final change:** gateway access format використовує method + $uri + protocol; edge має явний аналогічний safe format. Для /api/hubs/ raw error log вимкнено, sanitized access log зі status/latency лишається. Edge hub location зберігає proxy headers та WebSocket upgrade. Token transport і hub ownership authorization не змінені.
- **Evidence:** infra/gateway/nginx.conf:59, infra/gateway/nginx.conf:119; infra/nginx/shelfguard-ratelimit.conf:22; infra/nginx/shelfguard.conf:21,66,96.
- **Tests:** tests/Test-NginxPrivacy.ps1 — gateway/edge nginx -t та hub upstream failure з synthetic bearer marker. У логах є sanitized request/status і немає marker. PASS; test-results/phase-1-nginx.json. Read-only source mounts, no external network/ports, fake temporary TLS certificate; реальні keys/logs не читалися.
- **Compatibility/operational tradeoff:** в access logs більше немає query parameters; raw hub error text відсутній. Для діагностики використовувати status/latency/upstream role та redacted application event/correlation logs. Не повертати raw logging заради debug.
- **Migration:** schema/data не змінені. Edge log format із rate-limit include треба розгорнути разом із server config; nginx -t перед reload.
- **Rollback:** зберегти safe access format і raw hub error suppression; не повертати $request/$args/combined. Runtime edge/WAF/APM/log sinks поза source потребують окремого metadata review.
- **Owner approval:** продовження фази 1 по плану. Status: LOCAL FIX VERIFIED; NOT DEPLOYED; end-to-end ST-04 реального deployment OPEN.
