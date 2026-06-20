# TASK-263 — Mobile: AI Assistant chat screen

**Agent:** mobile-developer
**Date:** 2026-06-20
**Status:** done

## Summary

Реалізовано мобільний AI Business Assistant для ShelfGuard.
Backend (TASK-250, POST /api/ai/assistant) вже готовий.
Frontend web (AiAssistantWidget.tsx) вже готовий.
Залишалась тільки мобільна частина.

## Нові файли

### Feature layer
- `mobile/features/ai-assistant/types.ts` — AiAssistantRequest, AiAssistantResponse, AiAssistantContextSummary, ChatMessage
- `mobile/features/ai-assistant/api.ts` — sendAssistantMessage (POST /api/ai/assistant)
- `mobile/features/ai-assistant/hooks/useAiAssistant.ts` — useMutation hook

### Screen
- `mobile/app/(app)/ai-assistant.tsx` — повноекранний dark-themed chat:
  - Header: arrow-back + sparkles іконка + заголовок + підзаголовок
  - FlatList повідомлень (user: right/blue, AI: left/dark-gray)
  - Context badges під AI-відповідями (критичні партії / замовлення / продажі / постачальники)
  - TypingIndicator (ActivityIndicator + текст) поки API відповідає
  - Empty state з 4 підказками-кнопками (тап → одразу надсилає питання)
  - TextInput + кнопка Send (неактивна поки немає тексту або loading)
  - KeyboardAvoidingView для iOS/Android
  - Error handling: 503 → Claude API недоступний, 403 → модуль не активний

## Оновлені файли
- `mobile/app/(app)/_layout.tsx` — ai-assistant hidden route
- `mobile/app/(app)/index.tsx` — AI Асистент темна картка перед AI-orders banner
- `mobile/app/(app)/more/index.tsx` — "AI Асистент" (sparkles-outline, #60a5fa) в MODULES

## UX рішення
- Dark theme (#0d1117 bg) — відповідає web-виджету, контрастує з рештою light-themed екранів
- Suggested questions на empty state → одразу надсилаються без ручного введення
- scrollToEnd() після кожного нового повідомлення
- Пікапа помилок по HTTP status кодах (503, 403)
- message.id = `u-${Date.now()}` / `a-${Date.now()}` для унікальних FlatList keys

## Acceptance criteria
- [x] npx tsc --noEmit — 0 помилок
- [x] SafeAreaView на кореневому екрані
- [x] FlatList для chat messages
- [x] React Query (useMutation) — не useState + fetch
- [x] Hidden route зареєстровано в _layout.tsx
- [x] AI картка на Dashboard
- [x] AI в more/index.tsx MODULES
- [x] Error handling (503 + 403)
- [x] Context badges під AI-відповідями
