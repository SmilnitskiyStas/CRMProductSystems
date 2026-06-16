import { View, Text, FlatList, ActivityIndicator, TouchableOpacity } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useTransfers } from '@/features/transfers/hooks/useTransfers';
import { TransferCard } from '@/features/transfers/components/TransferCard';
import { useAuthStore } from '@/features/auth/store';

export default function TransfersScreen() {
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const { data, isLoading, isError, refetch } = useTransfers(user?.locationId ?? undefined);

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <View className="px-4 pt-4 pb-2 flex-row items-center justify-between">
        <View className="flex-row items-center gap-3">
          <TouchableOpacity
            onPress={() => router.back()}
            className="w-9 h-9 items-center justify-center rounded-full bg-gray-100"
          >
            <Ionicons name="arrow-back" size={20} color="#374151" />
          </TouchableOpacity>
          <Text className="text-2xl font-bold text-gray-900">Переміщення</Text>
        </View>
        <TouchableOpacity
          onPress={() => router.push('/(app)/transfers/create')}
          className="w-10 h-10 bg-primary-600 rounded-full items-center justify-center shadow-sm"
        >
          <Ionicons name="add" size={24} color="white" />
        </TouchableOpacity>
      </View>

      {isLoading ? (
        <View className="flex-1 items-center justify-center">
          <ActivityIndicator size="large" color="#16a34a" />
        </View>
      ) : isError ? (
        <View className="flex-1 items-center justify-center px-4">
          <Text className="text-red-500 text-center">Помилка завантаження</Text>
          <TouchableOpacity onPress={() => { void refetch(); }} className="mt-4">
            <Text className="text-primary-600 font-medium">Спробувати знову</Text>
          </TouchableOpacity>
        </View>
      ) : (
        <FlatList
          data={data}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => (
            <TransferCard
              item={item}
              onPress={() => router.push(`/(app)/transfers/${item.id}`)}
            />
          )}
          ItemSeparatorComponent={() => <View className="h-2" />}
          contentContainerClassName="px-4 pb-6 pt-2"
          refreshing={false}
          onRefresh={() => { void refetch(); }}
          ListEmptyComponent={
            <View className="items-center justify-center py-20">
              <Ionicons name="swap-horizontal-outline" size={48} color="#d1d5db" />
              <Text className="text-gray-400 text-base mt-3">Переміщень немає</Text>
              <TouchableOpacity
                onPress={() => router.push('/(app)/transfers/create')}
                className="mt-4 bg-primary-600 px-5 py-2.5 rounded-xl"
              >
                <Text className="text-white font-semibold">Створити переміщення</Text>
              </TouchableOpacity>
            </View>
          }
        />
      )}
    </SafeAreaView>
  );
}
