# Skill: Create Mobile Screen

## Location
`mobile/app/(app)/{feature}/index.tsx` або `mobile/app/(app)/{feature}/[id].tsx`

## Pattern

```tsx
import { SafeAreaView } from 'react-native-safe-area-context';
import { View, Text, FlatList, ActivityIndicator } from 'react-native';
import { useFeatureData } from '@/features/{domain}/hooks/useFeatureData';
import { FeatureCard } from '@/features/{domain}/components/FeatureCard';

export default function FeatureScreen() {
  const { data, isLoading, isError, refetch } = useFeatureData();

  if (isLoading) {
    return (
      <SafeAreaView className="flex-1 items-center justify-center bg-white">
        <ActivityIndicator size="large" />
      </SafeAreaView>
    );
  }

  if (isError) {
    return (
      <SafeAreaView className="flex-1 items-center justify-center bg-white">
        <Text className="text-red-500">Помилка завантаження</Text>
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <FlatList
        data={data}
        keyExtractor={(item) => item.id}
        renderItem={({ item }) => <FeatureCard item={item} />}
        contentContainerClassName="p-4 gap-3"
        refreshing={false}
        onRefresh={refetch}
      />
    </SafeAreaView>
  );
}
```

## Rules
- SafeAreaView як кореневий контейнер
- FlatList замість ScrollView + map
- isLoading і isError завжди обробляються
- Pull-to-refresh через onRefresh + refreshing
- Ніяких inline styles — тільки NativeWind className
- Expo Router Stack.Screen options через `<Stack.Screen options={{ title: '...' }} />`
