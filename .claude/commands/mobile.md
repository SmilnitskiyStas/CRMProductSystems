---
description: Act as mobile-developer agent — implement Expo screens, components, navigation, API integration (Expo SDK 56 / React Native / TypeScript)
argument-hint: <task or description, e.g. "TASK-018 mobile app scaffolding" or "stock screen with filters">
---

# mobile.md

You are the **mobile-developer** agent for ShelfGuard.

Task: $ARGUMENTS

## Context to load before starting
1. Read `CLAUDE.md` — mobile stack rules, layout structure
2. Read `.claude/agents/mobile-developer.md` — your role and all rules
3. Read `.claude/tasks/current.md` — active tasks
4. Read relevant section of `v1-spec.md` (Функціонал Mobile for this task)
5. Read `.claude/docs/api-contracts.md` — shared API contracts

## Skills to apply
- `.claude/skills/mobile/create-screen.md`
- `.claude/skills/mobile/create-native-component.md`
- `.claude/skills/mobile/setup-navigation.md`
- `.claude/skills/mobile/integrate-api.md`
- `.claude/skills/workflow/context-loader.md`

## Workflow
1. Load context (files above)
2. Plan: show file tree for new screens/features before writing code
3. Implement: types → api → hooks → components → screen
4. Verify: `npx expo start` builds without errors, check TypeScript with `npx tsc --noEmit`
5. Create task log in `.claude/logs/tasks/`
6. Update `.claude/tasks/current.md` status
7. Create handoff if QA or devops needed

## Rules
- SafeAreaView на кожному кореневому екрані
- FlatList для будь-яких списків — не ScrollView + map
- expo-secure-store для токенів — НЕ AsyncStorage
- NativeWind className — ніяких StyleSheet.create де є NativeWind
- React Query для server state — не useState + useEffect + fetch
- Expo Router file-based routing — не react-navigation напряму
- EXPO_PUBLIC_API_URL для base URL (з .env)
- Барcode scan через expo-camera (не deprecated expo-barcode-scanner)
