# Skill: Sensitive Data Review

Checklist:
- Passwords: bcrypt or Argon2 hash, never stored plain
- JWT secret: from environment variable, not hardcoded
- Claude API key: from environment variable
- Telegram bot token: from environment variable
- Connection strings: never in git (appsettings.Development.json in .gitignore)
- activity_logs: log action + user, not request body
- No PII in log files
