import { useEffect, useState } from 'react';
import { ActivityIndicator, Alert, FlatList, Modal, Text, TextInput, TouchableOpacity, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { useAutoSelectMembership, useLoyaltyHistory, useMemberships } from '@/features/loyalty/hooks/useLoyalty';
import { useLoyaltyUiStore } from '@/features/loyalty/store';
import { MembershipSelector } from '@/features/loyalty/components/MembershipSelector';
import type { LoyaltyLedgerEntry } from '@/features/loyalty/types';
import { useCreatePurchaseReview, useMyReviews } from '@/features/purchase-reviews/hooks';

function formatMoney(n: number): string {
  return (n >= 0 ? '+' : '') + n.toFixed(2);
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString('uk-UA', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

const ENTRY_TYPE_LABELS: Record<string, string> = {
  accrual: 'Нарахування',
  redemption: 'Списання',
  manual_adjustment: 'Коригування',
  expiry: 'Згорання',
};

export default function LoyaltyHistoryScreen() {
  const { data: memberships, isLoading: membershipsLoading } = useMemberships();
  const selectedTenantId = useAutoSelectMembership(memberships);
  const setSelectedTenantId = useLoyaltyUiStore((s) => s.setSelectedTenantId);
  const [page, setPage] = useState(1);

  // A tenant switch should restart pagination, not keep whatever page the previous
  // tenant's history happened to be on.
  useEffect(() => {
    setPage(1);
  }, [selectedTenantId]);

  const { data, isLoading, isFetching } = useLoyaltyHistory(selectedTenantId, page);
  const reviews = useMyReviews(selectedTenantId);
  const createReview = useCreatePurchaseReview();
  const [reviewEntry, setReviewEntry] = useState<LoyaltyLedgerEntry | null>(null);
  const [rating, setRating] = useState(5);
  const [comment, setComment] = useState('');
  const reviewedIds = new Set((reviews.data?.items ?? []).map((item) => item.posTransactionId));

  function submitReview() {
    if (!selectedTenantId || !reviewEntry?.posTransactionId) return;
    createReview.mutate({ tenantId: selectedTenantId, posTransactionId: reviewEntry.posTransactionId, rating, comment: comment.trim() || undefined }, {
      onSuccess: () => { setReviewEntry(null); setComment(''); },
      onError: (error) => Alert.alert('Не вдалося залишити відгук', (error as {response?:{data?:{error?:string}}})?.response?.data?.error ?? 'Спробуйте ще раз.'),
    });
  }

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      <View className="flex-row items-center px-5 pt-4 pb-3 bg-white border-b border-gray-100">
        <Ionicons name="time-outline" size={24} color="#16a34a" />
        <Text className="text-xl font-bold text-gray-900 ml-3">Історія нарахувань</Text>
      </View>

      {membershipsLoading ? (
        <View className="flex-1 items-center justify-center">
          <ActivityIndicator size="large" color="#16a34a" />
        </View>
      ) : !memberships || memberships.length === 0 ? (
        <View className="flex-1 items-center justify-center px-6">
          <Ionicons name="time-outline" size={48} color="#d1d5db" />
          <Text className="text-gray-400 mt-3 text-center">
            Історія з&apos;явиться після приєднання до бонусної програми
          </Text>
        </View>
      ) : (
        <>
          <View className="pt-3">
            <MembershipSelector
              memberships={memberships}
              selectedTenantId={selectedTenantId}
              onSelect={setSelectedTenantId}
            />
          </View>

          {isLoading ? (
            <View className="flex-1 items-center justify-center">
              <ActivityIndicator size="large" color="#16a34a" />
            </View>
          ) : (
            <FlatList
              data={data?.items ?? []}
              keyExtractor={(item) => item.id}
              contentContainerStyle={{ padding: 16, paddingBottom: 40 }}
              ItemSeparatorComponent={() => <View className="h-2" />}
              ListEmptyComponent={
                <View className="items-center py-16">
                  <Text className="text-gray-400">Ще немає операцій</Text>
                </View>
              }
              ListFooterComponent={
                data && data.totalPages > 1 ? (
                  <View className="flex-row justify-center items-center gap-4 mt-4">
                    <TouchableOpacity disabled={page <= 1} onPress={() => setPage((p) => p - 1)}>
                      <Ionicons name="chevron-back-circle" size={32} color={page <= 1 ? '#d1d5db' : '#16a34a'} />
                    </TouchableOpacity>
                    <Text className="text-gray-500 text-sm">
                      {page} / {data.totalPages}
                    </Text>
                    <TouchableOpacity
                      disabled={page >= data.totalPages}
                      onPress={() => setPage((p) => p + 1)}
                    >
                      <Ionicons
                        name="chevron-forward-circle"
                        size={32}
                        color={page >= data.totalPages ? '#d1d5db' : '#16a34a'}
                      />
                    </TouchableOpacity>
                  </View>
                ) : null
              }
              renderItem={({ item }: { item: LoyaltyLedgerEntry }) => (
                <View className="bg-white rounded-xl p-4">
                <View className="flex-row items-center justify-between">
                  <View className="flex-1 mr-2">
                    <Text className="text-sm font-semibold text-gray-900">
                      {ENTRY_TYPE_LABELS[item.entryType] ?? item.entryType}
                    </Text>
                    <Text className="text-xs text-gray-400 mt-0.5">{formatDate(item.createdAt)}</Text>
                    {item.note ? <Text className="text-xs text-gray-400 mt-0.5">{item.note}</Text> : null}
                  </View>
                  <Text className={`text-base font-bold ${item.amount >= 0 ? 'text-green-600' : 'text-red-500'}`}>
                    {formatMoney(item.amount)} ₴
                  </Text>
                </View>
                {item.entryType === 'accrual' && item.posTransactionId && !reviewedIds.has(item.posTransactionId) ? <TouchableOpacity onPress={()=>{setReviewEntry(item);setRating(5);setComment('');}} className="mt-3 border-t border-gray-100 pt-3"><Text className="text-sm font-semibold text-green-700">Залишити відгук про покупку</Text></TouchableOpacity>:null}
                </View>
              )}
            />
          )}
          {isFetching && !isLoading && (
            <View className="absolute top-16 right-4">
              <ActivityIndicator size="small" color="#16a34a" />
            </View>
          )}
        </>
      )}
      <Modal visible={Boolean(reviewEntry)} transparent animationType="slide"><View className="flex-1 justify-end bg-black/40"><View className="bg-white rounded-t-3xl p-6"><Text className="text-xl font-bold">Відгук про покупку</Text><View className="flex-row justify-center gap-2 my-5">{[1,2,3,4,5].map(star=><TouchableOpacity key={star} onPress={()=>setRating(star)}><Ionicons name={star<=rating?'star':'star-outline'} size={34} color="#f59e0b"/></TouchableOpacity>)}</View><TextInput value={comment} onChangeText={setComment} multiline placeholder="Коментар (необов’язково)" className="border border-gray-200 rounded-xl p-4 min-h-24"/><TouchableOpacity onPress={submitReview} disabled={createReview.isPending} className="bg-green-600 rounded-xl py-4 items-center mt-4"><Text className="text-white font-semibold">Надіслати відгук</Text></TouchableOpacity><TouchableOpacity onPress={()=>setReviewEntry(null)} className="py-3 items-center"><Text>Скасувати</Text></TouchableOpacity></View></View></Modal>
    </SafeAreaView>
  );
}
