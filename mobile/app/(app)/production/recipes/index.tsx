import { FlatList, Text, TouchableOpacity, View, ActivityIndicator } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { useRouter } from 'expo-router';
import { useRecipes } from '@/features/production/hooks/useProduction';
import type { RecipeListItem } from '@/features/production/types';
import { OfflineReadStatus } from '@/features/offline-read-cache/OfflineReadStatus';
import { useOfflineReadUx } from '@/features/offline-read-cache/ux';
import { useAuthStore } from '@/features/auth/store';

function RecipeCard({ item }: { item: RecipeListItem }) {
  return (
    <View className="bg-white mx-4 mb-3 rounded-2xl p-4">
      <View className="flex-row items-start justify-between">
        <View className="flex-1 mr-3">
          <Text className="text-base font-semibold text-gray-900">{item.name}</Text>
          <Text className="text-sm text-gray-500 mt-0.5">
            Вихід: {item.outputQty} {item.unit} — {item.outputItemName}
          </Text>
        </View>
        {!item.isActive && (
          <View className="bg-gray-100 px-2 py-0.5 rounded-full">
            <Text className="text-xs text-gray-500">Неактивний</Text>
          </View>
        )}
      </View>
      <View className="flex-row items-center mt-3 gap-4">
        <View className="flex-row items-center gap-1">
          <Ionicons name="list-outline" size={14} color="#9ca3af" />
          <Text className="text-xs text-gray-400">
            {item.ingredientCount} інгредієнт{item.ingredientCount === 1 ? '' : 'ів'}
          </Text>
        </View>
      </View>
    </View>
  );
}

export default function RecipesScreen() {
  const offline = useAuthStore((state) => state.hydrationStatus === 'offline_read_ready');
  const router = useRouter();
  const { data: recipes, isLoading, isError, refetch, isRefetching } = useRecipes(false, !offline);
  const offlineState = useOfflineReadUx(['production-recipes', false], {
    hasData: recipes !== undefined,
    isFetching: isRefetching,
    isError,
  });

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      {/* Header */}
      <View className="flex-row items-center px-4 pt-3 pb-3 gap-3">
        <TouchableOpacity onPress={() => router.back()}>
          <Ionicons name="arrow-back" size={24} color="#111827" />
        </TouchableOpacity>
        <Text className="text-xl font-bold text-gray-900 flex-1">Рецепти</Text>
      </View>

      <OfflineReadStatus state={offlineState} onRetry={() => { void refetch(); }} />

      {isLoading ? (
        <View className="flex-1 items-center justify-center">
          <ActivityIndicator size="large" color="#16a34a" />
        </View>
      ) : isError && recipes === undefined ? (
        <View className="flex-1 items-center justify-center px-4">
          <Text className="text-red-500 text-center">Не вдалося завантажити рецепти</Text>
          <TouchableOpacity accessibilityRole="button" onPress={() => { void refetch(); }} className="min-h-[44px] mt-3 justify-center">
            <Text className="text-primary-600 font-semibold">Спробувати знову</Text>
          </TouchableOpacity>
        </View>
      ) : (
        <FlatList
          data={recipes ?? []}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => <RecipeCard item={item} />}
          contentContainerStyle={{ paddingTop: 8, paddingBottom: 32 }}
          refreshing={isRefetching}
          onRefresh={refetch}
          ListEmptyComponent={
            <View className="flex-1 items-center justify-center pt-24">
              <Ionicons name="flask-outline" size={48} color="#d1d5db" />
              <Text className="text-gray-400 mt-3 text-base">Рецептів ще немає</Text>
            </View>
          }
        />
      )}
    </SafeAreaView>
  );
}
