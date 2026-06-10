# Skill: Setup Navigation (Expo Router)

## Auth Guard — (app)/_layout.tsx

```tsx
import { Tabs, Redirect } from 'expo-router';
import { useAuthStore } from '@/features/auth/store';
import { Ionicons } from '@expo/vector-icons';

export default function AppLayout() {
  const token = useAuthStore((s) => s.accessToken);

  if (!token) return <Redirect href="/(auth)/login" />;

  return (
    <Tabs
      screenOptions={{
        tabBarActiveTintColor: '#16a34a',
        tabBarInactiveTintColor: '#9ca3af',
        headerShown: false,
      }}
    >
      <Tabs.Screen
        name="index"
        options={{
          title: 'Дашборд',
          tabBarIcon: ({ color }) => <Ionicons name="home-outline" size={22} color={color} />,
        }}
      />
      <Tabs.Screen
        name="stock/index"
        options={{
          title: 'Склад',
          tabBarIcon: ({ color }) => <Ionicons name="layers-outline" size={22} color={color} />,
        }}
      />
      <Tabs.Screen
        name="scan"
        options={{
          title: 'Скан',
          tabBarIcon: ({ color }) => <Ionicons name="scan-outline" size={26} color={color} />,
        }}
      />
      <Tabs.Screen
        name="receipt/index"
        options={{
          title: 'Прийомка',
          tabBarIcon: ({ color }) => <Ionicons name="receipt-outline" size={22} color={color} />,
        }}
      />
      <Tabs.Screen
        name="profile/index"
        options={{
          title: 'Профіль',
          tabBarIcon: ({ color }) => <Ionicons name="person-outline" size={22} color={color} />,
        }}
      />
    </Tabs>
  );
}
```

## Auth Layout — (auth)/_layout.tsx

```tsx
import { Stack } from 'expo-router';

export default function AuthLayout() {
  return (
    <Stack screenOptions={{ headerShown: false }}>
      <Stack.Screen name="login" />
    </Stack>
  );
}
```

## Deep Link Config (app.json)
```json
{
  "expo": {
    "scheme": "shelfguard",
    "plugins": ["expo-router"]
  }
}
```

## Rules
- Auth guard в `(app)/_layout.tsx` — Redirect на login якщо немає токена
- File-based routing — назва файлу = URL сегмент
- `[id].tsx` для динамічних сегментів, `useLocalSearchParams()` для читання
- `headerShown: false` на layout, визначати header на конкретному екрані
- Tabs навігатор тільки в `(app)` — в auth Stack навігатор
