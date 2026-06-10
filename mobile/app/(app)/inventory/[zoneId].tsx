import { useState } from 'react';
import { View, Text, FlatList, TouchableOpacity } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Stack, useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useStock } from '@/features/stock/hooks/useStock';
import { StockBatchCard } from '@/features/stock/components/StockBatchCard';

export default function InventoryZoneScreen() {
  const { zoneId } = useLocalSearchParams<{ zoneId: string }>();
  const router = useRouter();
  const { data: items, isLoading, refetch } = useStock({ zone_id: zoneId });

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <Stack.Screen
        options={{
          headerShown: true,
          title: 'Інвентаризація зони',
          headerBackTitle: '',
          headerRight: () => (
            <TouchableOpacity onPress={() => router.push('/(app)/scan')}>
              <Ionicons name="scan-outline" size={22} color="#16a34a" />
            </TouchableOpacity>
          ),
        }}
      />

      <View className="px-4 py-3 bg-white border-b border-gray-100">
        <Text className="text-sm text-gray-500">
          Відскануйте товари в зоні для звірки залишків
        </Text>
      </View>

      <FlatList
        data={items}
        keyExtractor={(item) => item.id}
        renderItem={({ item }) => (
          <StockBatchCard
            item={item}
            onPress={() => router.push(`/(app)/stock/${item.id}`)}
          />
        )}
        ItemSeparatorComponent={() => <View className="h-2" />}
        contentContainerClassName="px-4 py-4"
        refreshing={isLoading}
        onRefresh={refetch}
        ListEmptyComponent={
          <View className="items-center justify-center py-20">
            <Text className="text-gray-400">В зоні немає партій</Text>
          </View>
        }
      />
    </SafeAreaView>
  );
}
