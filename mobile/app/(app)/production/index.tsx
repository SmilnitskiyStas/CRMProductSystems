import {
  View,
  Text,
  FlatList,
  TouchableOpacity,
  ActivityIndicator,
  Modal,
  TextInput,
  Alert,
  ScrollView,
  KeyboardAvoidingView,
  Platform,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { useRouter } from 'expo-router';
import { useEffect, useState } from 'react';
import NetInfo, { useNetInfo } from '@react-native-community/netinfo';
import {
  useProductionOrders,
  useRecipes,
} from '@/features/production/hooks/useProduction';
import type { ProductionOrderListItem, ProductionStatus } from '@/features/production/types';
import { useAuthStore } from '@/features/auth/store';
import { useOperationalDraft } from '@/features/operational-drafts/useOperationalDraft';
import { enqueueOperationalMutation, syncOperationalMutations } from '@/features/offline-mutations/operationalQueue';

// ── Status config ──────────────────────────────────────────────────────────────

const STATUS_LABELS: Record<ProductionStatus, string> = {
  planned: 'Заплановано',
  in_progress: 'В роботі',
  done: 'Виконано',
  cancelled: 'Скасовано',
};

const STATUS_COLORS: Record<ProductionStatus, { bg: string; text: string }> = {
  planned: { bg: 'bg-blue-100', text: 'text-blue-700' },
  in_progress: { bg: 'bg-amber-100', text: 'text-amber-700' },
  done: { bg: 'bg-green-100', text: 'text-green-700' },
  cancelled: { bg: 'bg-gray-100', text: 'text-gray-500' },
};

type FilterStatus = 'all' | ProductionStatus;

const FILTER_OPTIONS: { key: FilterStatus; label: string }[] = [
  { key: 'all', label: 'Всі' },
  { key: 'planned', label: 'Заплановано' },
  { key: 'in_progress', label: 'В роботі' },
  { key: 'done', label: 'Виконано' },
  { key: 'cancelled', label: 'Скасовано' },
];

// ── Order card ─────────────────────────────────────────────────────────────────

function OrderCard({
  item,
  onPress,
}: {
  item: ProductionOrderListItem;
  onPress: () => void;
}) {
  const sc = STATUS_COLORS[item.status];
  return (
    <TouchableOpacity
      onPress={onPress}
      activeOpacity={0.7}
      className="bg-white mx-4 mb-3 rounded-2xl p-4"
    >
      <View className="flex-row items-start justify-between">
        <View className="flex-1 mr-3">
          <Text className="text-base font-semibold text-gray-900" numberOfLines={1}>
            {item.recipeName}
          </Text>
          <Text className="text-sm text-gray-500 mt-0.5">{item.locationName}</Text>
        </View>
        <View className={`px-2 py-0.5 rounded-full ${sc.bg}`}>
          <Text className={`text-xs font-semibold ${sc.text}`}>
            {STATUS_LABELS[item.status]}
          </Text>
        </View>
      </View>
      <View className="flex-row items-center mt-3 gap-4">
        <View className="flex-row items-center gap-1">
          <Ionicons name="cube-outline" size={13} color="#9ca3af" />
          <Text className="text-xs text-gray-400">Кількість: {item.plannedQty}</Text>
        </View>
        <View className="flex-row items-center gap-1">
          <Ionicons name="time-outline" size={13} color="#9ca3af" />
          <Text className="text-xs text-gray-400">
            {new Date(item.createdAt).toLocaleDateString('uk-UA', {
              day: '2-digit',
              month: 'short',
            })}
          </Text>
        </View>
        {item.completedAt && (
          <View className="flex-row items-center gap-1">
            <Ionicons name="checkmark-circle-outline" size={13} color="#16a34a" />
            <Text className="text-xs text-gray-400">
              {new Date(item.completedAt).toLocaleDateString('uk-UA', {
                day: '2-digit',
                month: 'short',
              })}
            </Text>
          </View>
        )}
      </View>
    </TouchableOpacity>
  );
}

// ── Create order modal ─────────────────────────────────────────────────────────

function CreateOrderModal({
  visible,
  onClose,
}: {
  visible: boolean;
  onClose: () => void;
}) {
  const user = useAuthStore((s) => s.user);
  const owner = user?.tenantId ? { tenantId: user.tenantId, userId: user.id } : null;
  const draft = useOperationalDraft(owner, 'production');
  const netInfo = useNetInfo();
  const { data: recipes, refetch: refetchRecipes } = useRecipes();
  const [isSubmitting, setIsSubmitting] = useState(false);

  const [recipeId, setRecipeId] = useState('');
  const [plannedQty, setPlannedQty] = useState('1');
  const [notes, setNotes] = useState('');
  const [recipePickerOpen, setRecipePickerOpen] = useState(false);

  const selectedRecipe = recipes?.find((r) => r.id === recipeId);
  const draftPayload = {
    kind: 'production' as const,
    locationId: user?.locationId ?? '',
    recipeId,
    plannedQty,
    notes,
  };

  useEffect(() => {
    if (draft.restored?.payload.kind !== 'production') return;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setRecipeId(draft.restored.payload.recipeId);
    setPlannedQty(draft.restored.payload.plannedQty);
    setNotes(draft.restored.payload.notes);
  }, [draft.restored]);

  useEffect(() => {
    if (!draft.hydrated || (!recipeId && plannedQty === '1' && !notes)) return;
    void draft.persist(draftPayload);
  }, [recipeId, plannedQty, notes, draft.hydrated]); // eslint-disable-line react-hooks/exhaustive-deps

  function reset() {
    setRecipeId('');
    setPlannedQty('1');
    setNotes('');
  }

  function discard() {
    Alert.alert('Видалити чернетку?', 'Усі введені дані цього ордера буде втрачено.', [
      { text: 'Скасувати', style: 'cancel' },
      { text: 'Видалити', style: 'destructive', onPress: () => void draft.clear().then(reset) },
    ]);
  }

  async function handleSubmit() {
    if (!recipeId) {
      Alert.alert('Оберіть рецепт');
      return;
    }
    const qty = parseFloat(plannedQty);
    if (!qty || qty <= 0) {
      Alert.alert('Введіть коректну кількість');
      return;
    }
    if (!user?.locationId) {
      Alert.alert('Помилка', 'Локацію не призначено для вашого профілю.');
      return;
    }
    if (!owner || isSubmitting) return;
    setIsSubmitting(true);
    try {
      const network = await NetInfo.fetch();
      if (network.isConnected && network.isInternetReachable !== false) {
        const refreshed = await refetchRecipes();
        if (refreshed.error) throw refreshed.error;
        if (!refreshed.data?.some((recipe) => recipe.id === recipeId && recipe.isActive)) {
          Alert.alert('Дані змінилися', 'Рецепт більше недоступний. Оновіть список і перевірте чернетку.');
          return;
        }
      }
      await enqueueOperationalMutation(owner, { kind: 'production.create', payload: {
        recipeId,
        locationId: user.locationId,
        plannedQty: qty,
        notes: notes.trim() || undefined,
      } });
      await draft.clear();
      reset();
      onClose();
      const result = network.isConnected && network.isInternetReachable !== false
        ? await syncOperationalMutations(owner) : { synced: 0, remaining: 1 };
      Alert.alert(result.synced > 0 ? 'Успішно' : 'Збережено офлайн', result.synced > 0
        ? 'Виробничий ордер створено.' : 'Ордер буде передано після появи інтернету.');
    } catch {
      Alert.alert('Помилка', 'Не вдалося надійно зберегти ордер. Дані форми залишено.');
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
      <KeyboardAvoidingView
        className="flex-1 justify-end"
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      >
        <View className="flex-1 bg-black/40" />
        <View className="bg-white rounded-t-3xl px-6 pt-6 pb-10">
          {netInfo.isConnected === false && (
            <Text className="text-sm text-red-700 bg-red-50 p-3 rounded-xl mb-3">Немає мережі. Чернетка зберігається, відправлення заблоковано.</Text>
          )}
          {draft.submission.status !== 'idle' && draft.submission.message && (
            <Text className="text-sm text-amber-700 bg-amber-50 p-3 rounded-xl mb-3">{draft.submission.message}</Text>
          )}
          <View className="flex-row items-center justify-between mb-5">
            <Text className="text-lg font-bold text-gray-900">Новий ордер</Text>
            <TouchableOpacity onPress={onClose}>
              <Ionicons name="close" size={24} color="#6b7280" />
            </TouchableOpacity>
            <TouchableOpacity onPress={discard}>
              <Text className="text-red-600 text-sm">Видалити</Text>
            </TouchableOpacity>
          </View>

          {/* Recipe selector */}
          <Text className="text-sm text-gray-600 mb-1.5">Рецепт *</Text>
          <TouchableOpacity
            onPress={() => setRecipePickerOpen(true)}
            className="border border-gray-200 rounded-xl px-4 py-3 flex-row items-center justify-between mb-4"
          >
            <Text className={selectedRecipe ? 'text-gray-900 text-sm font-medium' : 'text-gray-400 text-sm'}>
              {selectedRecipe ? selectedRecipe.name : 'Оберіть рецепт...'}
            </Text>
            <Ionicons name="chevron-down" size={16} color="#9ca3af" />
          </TouchableOpacity>

          {/* Planned quantity */}
          <Text className="text-sm text-gray-600 mb-1.5">Планова кількість *</Text>
          <TextInput
            className="border border-gray-200 rounded-xl px-4 py-3 text-base text-gray-900 mb-4"
            value={plannedQty}
            onChangeText={setPlannedQty}
            keyboardType="numeric"
            placeholder="1"
            placeholderTextColor="#9ca3af"
          />

          {/* Notes */}
          <Text className="text-sm text-gray-600 mb-1.5">Примітки</Text>
          <TextInput
            className="border border-gray-200 rounded-xl px-4 py-3 text-base text-gray-900 min-h-[72px] mb-5"
            value={notes}
            onChangeText={setNotes}
            placeholder="Необов'язково..."
            placeholderTextColor="#9ca3af"
            multiline
            textAlignVertical="top"
          />

          <TouchableOpacity
            onPress={() => void handleSubmit()}
            disabled={isSubmitting}
            className="bg-primary-600 rounded-xl py-4 items-center"
          >
            {isSubmitting ? (
              <ActivityIndicator color="white" />
            ) : (
              <Text className="text-white font-semibold text-base">Створити ордер</Text>
            )}
          </TouchableOpacity>
        </View>
      </KeyboardAvoidingView>

      {/* Recipe picker sub-modal */}
      <Modal
        visible={recipePickerOpen}
        transparent
        animationType="slide"
        onRequestClose={() => setRecipePickerOpen(false)}
      >
        <TouchableOpacity
          activeOpacity={1}
          onPress={() => setRecipePickerOpen(false)}
          className="flex-1 justify-end bg-black/50"
        >
          <View className="bg-white rounded-t-3xl px-6 pt-6 pb-10 max-h-[60%]">
            <Text className="text-lg font-bold text-gray-900 mb-4">Оберіть рецепт</Text>
            <ScrollView showsVerticalScrollIndicator={false}>
              {(recipes ?? []).filter((r) => r.isActive).map((r) => (
                <TouchableOpacity
                  key={r.id}
                  onPress={() => { setRecipeId(r.id); setRecipePickerOpen(false); }}
                  className="flex-row items-center justify-between py-3.5 border-b border-gray-50"
                >
                  <View className="flex-1 mr-3">
                    <Text className="text-sm font-medium text-gray-800">{r.name}</Text>
                    <Text className="text-xs text-gray-400 mt-0.5">
                      Вихід: {r.outputQty} {r.unit} — {r.outputItemName}
                    </Text>
                  </View>
                  {r.id === recipeId && (
                    <Ionicons name="checkmark" size={20} color="#16a34a" />
                  )}
                </TouchableOpacity>
              ))}
              {(recipes ?? []).filter((r) => r.isActive).length === 0 && (
                <Text className="text-gray-400 text-center py-8">Немає активних рецептів</Text>
              )}
            </ScrollView>
          </View>
        </TouchableOpacity>
      </Modal>
    </Modal>
  );
}

// ── Main screen ────────────────────────────────────────────────────────────────

export default function ProductionOrdersScreen() {
  const router = useRouter();
  const [activeFilter, setActiveFilter] = useState<FilterStatus>('all');
  const [createVisible, setCreateVisible] = useState(false);

  const queryParams = activeFilter === 'all' ? undefined : { status: activeFilter };
  const { data: orders, isLoading, refetch, isRefetching } = useProductionOrders(queryParams);

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      {/* Header */}
      <View className="flex-row items-center px-4 pt-3 pb-2 gap-3">
        <TouchableOpacity onPress={() => router.back()}>
          <Ionicons name="arrow-back" size={24} color="#111827" />
        </TouchableOpacity>
        <Text className="text-xl font-bold text-gray-900 flex-1">Виробництво</Text>
        <TouchableOpacity
          onPress={() => router.push('/(app)/production/recipes')}
          className="flex-row items-center gap-1 bg-gray-100 px-3 py-1.5 rounded-xl"
        >
          <Ionicons name="flask-outline" size={16} color="#374151" />
          <Text className="text-sm font-medium text-gray-700">Рецепти</Text>
        </TouchableOpacity>
      </View>

      {/* Status filter chips */}
      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={{ paddingHorizontal: 16, paddingVertical: 8, gap: 8 }}
      >
        {FILTER_OPTIONS.map((opt) => (
          <TouchableOpacity
            key={opt.key}
            onPress={() => setActiveFilter(opt.key)}
            className={`px-3 py-1.5 rounded-full ${
              activeFilter === opt.key
                ? 'bg-primary-600'
                : 'bg-white border border-gray-200'
            }`}
          >
            <Text
              className={`text-sm font-medium ${
                activeFilter === opt.key ? 'text-white' : 'text-gray-600'
              }`}
            >
              {opt.label}
            </Text>
          </TouchableOpacity>
        ))}
      </ScrollView>

      {/* Orders list */}
      {isLoading ? (
        <View className="flex-1 items-center justify-center">
          <ActivityIndicator size="large" color="#16a34a" />
        </View>
      ) : (
        <FlatList
          data={orders ?? []}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => (
            <OrderCard
              item={item}
              onPress={() => router.push(`/(app)/production/${item.id}`)}
            />
          )}
          contentContainerStyle={{ paddingTop: 4, paddingBottom: 100 }}
          refreshing={isRefetching}
          onRefresh={refetch}
          ListEmptyComponent={
            <View className="items-center justify-center pt-24">
              <Ionicons name="construct-outline" size={48} color="#d1d5db" />
              <Text className="text-gray-400 mt-3 text-base">Ордерів ще немає</Text>
            </View>
          }
        />
      )}

      {/* FAB */}
      <TouchableOpacity
        onPress={() => setCreateVisible(true)}
        className="absolute bottom-8 right-6 bg-primary-600 w-14 h-14 rounded-full items-center justify-center shadow-lg"
        style={{ elevation: 4 }}
      >
        <Ionicons name="add" size={28} color="white" />
      </TouchableOpacity>

      <CreateOrderModal
        visible={createVisible}
        onClose={() => setCreateVisible(false)}
      />
    </SafeAreaView>
  );
}
