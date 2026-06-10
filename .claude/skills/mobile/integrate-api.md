# Skill: Integrate API (Mobile)

## API Client — mobile/lib/api.ts

```ts
import * as SecureStore from 'expo-secure-store';

const BASE_URL = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5000';

async function apiFetch<T>(path: string, options?: RequestInit): Promise<T> {
  const token = await SecureStore.getItemAsync('access_token');

  const res = await fetch(`${BASE_URL}${path}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...options?.headers,
    },
  });

  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

export const api = { fetch: apiFetch };
```

## Feature API — mobile/features/{domain}/api/{domain}.ts

```ts
import { api } from '@/lib/api';
import type { StockBatch, StockFilters } from '../types';

export async function getStock(filters: StockFilters): Promise<StockBatch[]> {
  const params = new URLSearchParams();
  if (filters.storeId) params.set('store_id', filters.storeId);
  if (filters.status)  params.set('status', filters.status);
  return api.fetch(`/api/stock?${params}`);
}
```

## React Query Hook — mobile/features/{domain}/hooks/useStock.ts

```ts
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getStock } from '../api/stock';
import type { StockFilters } from '../types';

export function useStock(filters: StockFilters) {
  return useQuery({
    queryKey: ['stock', filters],
    queryFn:  () => getStock(filters),
    staleTime: 30_000,
  });
}
```

## QueryClient Setup — mobile/lib/query-client.ts

```ts
import { QueryClient } from '@tanstack/react-query';

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 2,
      staleTime: 30_000,
    },
  },
});
```

## App Entry — wrap в QueryClientProvider

```tsx
// mobile/app/_layout.tsx
import { QueryClientProvider } from '@tanstack/react-query';
import { queryClient } from '@/lib/query-client';

export default function RootLayout() {
  return (
    <QueryClientProvider client={queryClient}>
      <Stack />
    </QueryClientProvider>
  );
}
```

## Rules
- expo-secure-store для token storage — НЕ AsyncStorage
- EXPO_PUBLIC_* env vars для публічних конфігів (доступні в клієнті)
- React Query — єдине джерело server state (не useState + useEffect + fetch)
- staleTime: 30_000 мінімум для мобільних запитів (економія трафіку)
- Retry: 2 для нестабільного мобільного з'єднання
