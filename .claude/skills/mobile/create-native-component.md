# Skill: Create Native Component

## Location
`mobile/features/{domain}/components/{ComponentName}.tsx`

## Pattern

```tsx
import { View, Text, TouchableOpacity } from 'react-native';
import type { StockBatch } from '../types';

interface Props {
  item: StockBatch;
  onPress?: (id: string) => void;
}

export function StockCard({ item, onPress }: Props) {
  const statusColor = {
    safe:     'bg-green-100 text-green-700',
    warning:  'bg-yellow-100 text-yellow-700',
    critical: 'bg-red-100 text-red-700',
    expired:  'bg-gray-200 text-gray-500',
  }[item.status] ?? 'bg-gray-100 text-gray-600';

  return (
    <TouchableOpacity
      onPress={() => onPress?.(item.id)}
      className="bg-white rounded-xl p-4 shadow-sm border border-gray-100"
      activeOpacity={0.7}
    >
      <View className="flex-row items-center justify-between">
        <Text className="font-semibold text-gray-900 flex-1" numberOfLines={1}>
          {item.productName}
        </Text>
        <View className={`px-2 py-0.5 rounded-full ml-2 ${statusColor}`}>
          <Text className="text-xs font-medium">{item.status}</Text>
        </View>
      </View>
      <Text className="text-gray-500 text-sm mt-1">
        {item.quantity} {item.unit} · Партія {item.batchNumber}
      </Text>
    </TouchableOpacity>
  );
}
```

## Rules
- TouchableOpacity / Pressable для tappable елементів (не View з onPress)
- numberOfLines для тексту що може переповнитись
- activeOpacity={0.7} на TouchableOpacity
- Ніякого StyleSheet.create — тільки NativeWind className
- Props interface завжди явний (не React.FC<Props>)
- Компоненти не роблять API запити — отримують дані через props
