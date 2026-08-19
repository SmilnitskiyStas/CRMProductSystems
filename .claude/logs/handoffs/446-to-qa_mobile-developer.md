# TASK-446 → QA handoff

Run Android device visual/accessibility regression on the three reference screens only: staff
login, dashboard, and customers. Include keyboard/safe area, large font, TalkBack, touch targets,
loading/error/empty states, refresh, exact Back behavior, role/module variants, and css-interop
launch regression. Implementation evidence and file list are in
`.claude/logs/tasks/446_2026-08-01_mobile-design-system-foundation_mobile-developer.md`.

2026-08-01 attempt: current bundle launch is free of the former css-interop/navigation crash, but
device API routing stayed unavailable after a controlled Wi-Fi reconnect. Resume the remaining
screen smoke only after the phone can reach the configured API. Preserve the existing session.

2026-08-01 continuation: dashboard and Customers pass on device, including safe area,
accessibility labels/touch bounds, Customers empty/search/clear, Android Back, and absence of the
css-interop regression. Staff-login logout/login was not executed because Metro/ADB became
uncontrollable after launch; the retained manager session and app data were preserved. Large-font
is blocked by realme `WRITE_SETTINGS`, and TalkBack was not run. Resume only those three acceptance
gaps; do not repeat the passed dashboard/Customers cases.
