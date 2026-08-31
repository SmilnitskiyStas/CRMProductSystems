import {
  View,
  Text,
  TouchableOpacity,
  FlatList,
  ActivityIndicator,
  Alert,
  TextInput,
  Modal,
  ScrollView,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useState } from 'react';
import {
  useSupplierById,
  useSupplierItems,
  useCreateReview,
} from '@/features/marketplace/hooks/useMarketplace';
import type { SupplierItem, SupplierMetrics } from '@/features/marketplace/types';
import { useRegionLabel } from '@/features/geo/hooks';
import type { DeliveryCoverage } from '@/features/geo/types';
import { useAuthStore } from '@/features/auth/store';

// ── Star picker ────────────────────────────────────────────────────────────────

function StarPicker({ value, onChange }: { value: number; onChange: (v: number) => void }) {
  return (
    <View className="flex-row gap-2 justify-center py-2">
      {[1, 2, 3, 4, 5].map((i) => (
        <TouchableOpacity key={i} onPress={() => onChange(i)}>
          <Ionicons
            name={i <= value ? 'star' : 'star-outline'}
            size={32}
            color={i <= value ? '#f59e0b' : '#d1d5db'}
          />
        </TouchableOpacity>
      ))}
    </View>
  );
}

function StarDisplay({ value }: { value: number | null }) {
  if (value === null) return <Text className="text-sm text-gray-400">—</Text>;
  return (
    <View className="flex-row items-center gap-1">
      {[1, 2, 3, 4, 5].map((i) => (
        <Ionicons
          key={i}
          name={i <= Math.round(value) ? 'star' : 'star-outline'}
          size={13}
          color={i <= Math.round(value) ? '#f59e0b' : '#d1d5db'}
        />
      ))}
      <Text className="text-sm font-semibold text-gray-700 ml-1">{value.toFixed(1)}</Text>
    </View>
  );
}

// ── Metric tile ────────────────────────────────────────────────────────────────

function MetricTile({
  label,
  value,
  unit,
  fallback = '—',
  sublabel,
}: {
  label: string;
  value: number | null;
  unit?: string;
  /** Shown in place of the value when `value` is null (default "—"). */
  fallback?: string;
  /** Faint one-liner under the value, e.g. sample-size context. */
  sublabel?: string;
}) {
  return (
    <View className="flex-1 bg-gray-50 rounded-xl p-3 items-center">
      <Text className="text-xs text-gray-500 text-center mb-1">{label}</Text>
      <Text className="text-base font-bold text-gray-800 text-center">
        {value !== null ? `${value}${unit ?? ''}` : fallback}
      </Text>
      {sublabel ? (
        <Text className="text-[10px] text-gray-400 text-center mt-0.5">{sublabel}</Text>
      ) : null}
    </View>
  );
}

// ── Delivery coverage (read-only; supplier editing is web-only) ─────────────────

function DeliveryCoverageBlock({ coverage }: { coverage: DeliveryCoverage }) {
  const regionLabel = useRegionLabel();
  const served = coverage.served ?? [];
  const notServed = coverage.notServed ?? [];
  const note = coverage.note?.trim() ? coverage.note : null;

  if (served.length === 0 && notServed.length === 0 && !note) return null;

  return (
    <View className="mt-3 pt-3 border-t border-gray-100">
      <Text className="text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">
        Регіони доставки
      </Text>

      {served.length > 0 ? (
        <View className="gap-1 mb-1">
          {served.map((entry) => (
            <View key={entry.regionCode} className="flex-row flex-wrap items-baseline">
              <Text className="text-sm text-gray-700 font-medium">
                {regionLabel(entry.regionCode)}
              </Text>
              <Text className="text-xs text-gray-400 ml-1.5">
                {entry.terms?.trim() ? entry.terms : 'за домовленістю'}
              </Text>
            </View>
          ))}
        </View>
      ) : null}

      {notServed.length > 0 ? (
        <Text className="text-xs text-gray-400 mb-1">
          Не доставляє: {notServed.map((code) => regionLabel(code)).join(', ')}
        </Text>
      ) : null}

      {note ? <Text className="text-xs text-gray-500 italic">{note}</Text> : null}
    </View>
  );
}

// ── Delivery per-region breakdown (collapsible) ────────────────────────────────

function DeliveryByRegionList({ stats }: { stats: SupplierMetrics['deliveryByRegion'] }) {
  const regionLabel = useRegionLabel();
  const rows = [...(stats ?? [])].sort((a, b) => a.avgDeliveryDays - b.avgDeliveryDays);
  if (rows.length === 0) {
    return (
      <Text className="text-xs text-gray-400 mt-2">Ще недостатньо даних по регіонах</Text>
    );
  }
  return (
    <View className="gap-1 mt-2">
      {rows.map((r) => (
        <View key={r.regionCode} className="flex-row items-baseline">
          <Text className="text-xs text-gray-600 flex-1" numberOfLines={1}>
            {regionLabel(r.regionCode)}
          </Text>
          <Text className="text-xs font-medium text-gray-700 ml-2">{r.avgDeliveryDays} дн.</Text>
          <Text className="text-[10px] text-gray-300 ml-2">n={r.sampleSize}</Text>
        </View>
      ))}
    </View>
  );
}

// ── Item row ───────────────────────────────────────────────────────────────────

function ItemRow({ item, isLast }: { item: SupplierItem; isLast: boolean }) {
  const name = item.itemName ?? item.customName ?? '—';
  return (
    <View className={`px-4 py-3 flex-row items-center ${isLast ? '' : 'border-b border-gray-50'}`}>
      <View className="flex-1">
        <Text className="text-sm font-medium text-gray-800">{name}</Text>
        {item.minQty ? (
          <Text className="text-xs text-gray-400 mt-0.5">
            Мін. {item.minQty} {item.unit ?? 'шт'}
          </Text>
        ) : null}
      </View>
      <View className="items-end ml-3">
        {item.price !== null ? (
          <Text className="text-sm font-semibold text-gray-800">
            ₴{item.price.toFixed(2)}
          </Text>
        ) : null}
        <View className={`mt-0.5 px-2 py-0.5 rounded-full ${item.isAvailable ? 'bg-green-100' : 'bg-gray-100'}`}>
          <Text className={`text-xs font-medium ${item.isAvailable ? 'text-green-700' : 'text-gray-500'}`}>
            {item.isAvailable ? 'Є в наявності' : 'Відсутній'}
          </Text>
        </View>
      </View>
    </View>
  );
}

// ── Review modal ───────────────────────────────────────────────────────────────

function ReviewModal({
  supplierId,
  visible,
  onClose,
}: {
  supplierId: string;
  visible: boolean;
  onClose: () => void;
}) {
  const [rating, setRating] = useState(5);
  const [comment, setComment] = useState('');
  const { mutateAsync, isPending } = useCreateReview(supplierId);

  async function handleSubmit() {
    try {
      await mutateAsync({ rating, comment: comment.trim() || null });
      setComment('');
      setRating(5);
      onClose();
      Alert.alert('Дякуємо!', 'Ваш відгук збережено.');
    } catch (err: unknown) {
      const status = (err as { response?: { status?: number } })?.response?.status;
      if (status === 409) {
        Alert.alert('Вже залишили відгук', 'Ви вже залишили відгук для цього постачальника.');
      } else {
        Alert.alert('Помилка', 'Не вдалося зберегти відгук. Спробуйте пізніше.');
      }
    }
  }

  return (
    <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
      <View className="flex-1 justify-end bg-black/40">
        <View className="bg-white rounded-t-3xl px-6 pt-6 pb-10">
          <View className="flex-row items-center justify-between mb-4">
            <Text className="text-lg font-bold text-gray-900">Залишити відгук</Text>
            <TouchableOpacity onPress={onClose}>
              <Ionicons name="close" size={24} color="#6b7280" />
            </TouchableOpacity>
          </View>

          <Text className="text-sm text-gray-600 mb-2 text-center">Ваша оцінка</Text>
          <StarPicker value={rating} onChange={setRating} />

          <Text className="text-sm text-gray-600 mt-4 mb-2">Коментар (необов'язково)</Text>
          <TextInput
            className="border border-gray-200 rounded-xl px-4 py-3 text-base text-gray-800 min-h-[80px]"
            placeholder="Ваш досвід роботи з постачальником..."
            placeholderTextColor="#9ca3af"
            value={comment}
            onChangeText={setComment}
            multiline
            textAlignVertical="top"
          />

          <TouchableOpacity
            onPress={() => void handleSubmit()}
            disabled={isPending}
            className="bg-primary-600 rounded-xl py-4 items-center mt-5"
          >
            {isPending ? (
              <ActivityIndicator color="white" />
            ) : (
              <Text className="text-white font-semibold text-base">Надіслати відгук</Text>
            )}
          </TouchableOpacity>
        </View>
      </View>
    </Modal>
  );
}

// ── Main screen ────────────────────────────────────────────────────────────────

type Tab = 'catalog' | 'reviews';

export default function SupplierDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const [activeTab, setActiveTab] = useState<Tab>('catalog');
  const [reviewModalVisible, setReviewModalVisible] = useState(false);
  const [regionBreakdownOpen, setRegionBreakdownOpen] = useState(false);

  const { data: profile, isLoading: profileLoading } = useSupplierById(id);
  const { data: items, isLoading: itemsLoading } = useSupplierItems(id);

  if (profileLoading) {
    return (
      <SafeAreaView className="flex-1 bg-gray-50 items-center justify-center">
        <ActivityIndicator size="large" color="#16a34a" />
      </SafeAreaView>
    );
  }

  if (!profile) {
    return (
      <SafeAreaView className="flex-1 bg-gray-50 items-center justify-center">
        <Text className="text-gray-500">Постачальника не знайдено</Text>
      </SafeAreaView>
    );
  }

  const m = profile.metrics;

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      {/* Back header */}
      <View className="flex-row items-center px-4 pt-3 pb-2 gap-3">
        <TouchableOpacity onPress={() => router.back()}>
          <Ionicons name="arrow-back" size={24} color="#111827" />
        </TouchableOpacity>
        <Text className="text-lg font-bold text-gray-900 flex-1" numberOfLines={1}>
          {profile.supplierName}
        </Text>
        <View className={`px-2 py-0.5 rounded-full ${profile.plan === 'premium' ? 'bg-amber-100' : 'bg-gray-100'}`}>
          <Text className={`text-xs font-semibold ${profile.plan === 'premium' ? 'text-amber-700' : 'text-gray-500'}`}>
            {profile.plan === 'premium' ? 'Premium' : 'Free'}
          </Text>
        </View>
      </View>

      <ScrollView showsVerticalScrollIndicator={false}>
        {/* Info card */}
        <View className="mx-4 mt-1 bg-white rounded-2xl p-4">
          {profile.region ? (
            <View className="flex-row items-center gap-1 mb-2">
              <Ionicons name="location-outline" size={14} color="#9ca3af" />
              <Text className="text-sm text-gray-600">{profile.region}</Text>
            </View>
          ) : null}

          {profile.categories && profile.categories.length > 0 ? (
            <View className="flex-row flex-wrap gap-1 mb-2">
              {profile.categories.map((cat) => (
                <View key={cat} className="bg-blue-50 px-2 py-0.5 rounded-full">
                  <Text className="text-xs text-blue-700">{cat}</Text>
                </View>
              ))}
            </View>
          ) : null}

          {profile.workingHours ? (
            <View className="flex-row items-center gap-1 mb-1">
              <Ionicons name="time-outline" size={13} color="#9ca3af" />
              <Text className="text-xs text-gray-500">{profile.workingHours}</Text>
            </View>
          ) : null}

          {profile.paymentTerms ? (
            <View className="flex-row items-center gap-1">
              <Ionicons name="card-outline" size={13} color="#9ca3af" />
              <Text className="text-xs text-gray-500">{profile.paymentTerms}</Text>
            </View>
          ) : null}

          {profile.deliveryCoverage ? (
            <DeliveryCoverageBlock coverage={profile.deliveryCoverage} />
          ) : null}
        </View>

        {/* Metrics */}
        {m ? (
          <View className="mx-4 mt-3">
            <Text className="text-xs font-semibold text-gray-400 uppercase tracking-wider mb-2">
              Показники
            </Text>
            <View className="bg-white rounded-2xl p-4">
              <View className="flex-row items-center mb-3">
                <Text className="text-sm text-gray-600 mr-2">Рейтинг:</Text>
                <StarDisplay value={m.rating} />
              </View>

              <View className="flex-row gap-2">
                <MetricTile
                  label="Доставка"
                  value={m.avgDeliveryDays}
                  unit=" дн."
                  sublabel={
                    m.deliverySampleSize != null
                      ? `на основі ${m.deliverySampleSize} замовлень`
                      : undefined
                  }
                />
                <MetricTile
                  label="Час відповіді"
                  value={m.responseTimeHours}
                  unit=" год."
                  fallback="недостатньо даних"
                  sublabel={
                    m.responseTimeHours !== null && m.responseSampleSize != null
                      ? `на основі ${m.responseSampleSize} звернень`
                      : undefined
                  }
                />
              </View>

              <View className="flex-row gap-2 mt-2">
                <MetricTile
                  label="Точність"
                  value={m.orderAccuracy !== null ? Math.round(m.orderAccuracy * 100) : null}
                  unit="%"
                />
                <MetricTile
                  label="Якість"
                  value={m.qualityScore !== null ? Math.round(m.qualityScore * 100) : null}
                  unit="%"
                />
              </View>

              {(m.deliveryByRegion && m.deliveryByRegion.length > 0) ||
              m.deliverySampleSize != null ? (
                <View className="mt-2">
                  <TouchableOpacity
                    onPress={() => setRegionBreakdownOpen((v) => !v)}
                    className="flex-row items-center gap-1 py-1 self-start"
                  >
                    <Text className="text-xs font-medium text-primary-600">
                      {regionBreakdownOpen ? 'Приховати регіони' : 'Детальніше по регіонах'}
                    </Text>
                    <Ionicons
                      name={regionBreakdownOpen ? 'chevron-up' : 'chevron-down'}
                      size={12}
                      color="#16a34a"
                    />
                  </TouchableOpacity>
                  {regionBreakdownOpen ? (
                    <DeliveryByRegionList stats={m.deliveryByRegion} />
                  ) : null}
                </View>
              ) : null}
            </View>
          </View>
        ) : null}

        {/* Tabs */}
        <View className="mx-4 mt-4">
          <View className="flex-row bg-gray-100 rounded-xl p-1 mb-3">
            {(['catalog', 'reviews'] as Tab[]).map((tab) => (
              <TouchableOpacity
                key={tab}
                onPress={() => setActiveTab(tab)}
                className={`flex-1 py-2 rounded-lg items-center ${
                  activeTab === tab ? 'bg-white' : ''
                }`}
              >
                <Text className={`text-sm font-semibold ${activeTab === tab ? 'text-gray-900' : 'text-gray-500'}`}>
                  {tab === 'catalog' ? 'Каталог' : 'Відгуки'}
                </Text>
              </TouchableOpacity>
            ))}
          </View>

          {/* Catalog tab */}
          {activeTab === 'catalog' && (
            <View className="bg-white rounded-2xl overflow-hidden mb-4">
              {itemsLoading ? (
                <View className="py-8 items-center">
                  <ActivityIndicator color="#16a34a" />
                </View>
              ) : !items || items.length === 0 ? (
                <View className="py-10 items-center">
                  <Ionicons name="cube-outline" size={36} color="#d1d5db" />
                  <Text className="text-gray-400 mt-2 text-sm">Каталог порожній</Text>
                </View>
              ) : (
                items.map((item, idx) => (
                  <ItemRow key={item.id} item={item} isLast={idx === items.length - 1} />
                ))
              )}
            </View>
          )}

          {/* Reviews tab */}
          {activeTab === 'reviews' && (
            <View className="mb-4">
              {user ? (
                <TouchableOpacity
                  onPress={() => setReviewModalVisible(true)}
                  className="bg-primary-600 rounded-xl py-3.5 items-center mb-3"
                >
                  <Text className="text-white font-semibold">Залишити відгук</Text>
                </TouchableOpacity>
              ) : (
                <View className="bg-gray-100 rounded-xl py-3.5 items-center mb-3">
                  <Text className="text-gray-500 text-sm">Увійдіть, щоб залишити відгук</Text>
                </View>
              )}
              <View className="bg-white rounded-2xl py-10 items-center">
                <Ionicons name="chatbubble-outline" size={36} color="#d1d5db" />
                <Text className="text-gray-400 mt-2 text-sm">Відгуків поки немає</Text>
              </View>
            </View>
          )}
        </View>
      </ScrollView>

      <ReviewModal
        supplierId={id}
        visible={reviewModalVisible}
        onClose={() => setReviewModalVisible(false)}
      />
    </SafeAreaView>
  );
}
