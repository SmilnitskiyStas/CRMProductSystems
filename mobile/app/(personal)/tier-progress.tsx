import { useCallback } from 'react';
import { ActivityIndicator, Image, ScrollView, Text, TouchableOpacity, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { useFocusEffect, useRouter } from 'expo-router';
import { MembershipSelector } from '@/features/loyalty/components/MembershipSelector';
import { useAutoSelectMembership, useLoyaltyTierLadder, useLoyaltyTierProgress, useMemberships } from '@/features/loyalty/hooks/useLoyalty';
import type { LoyaltyTierDefinition, LoyaltyTierProgressMetrics, LoyaltyTierRequirements } from '@/features/loyalty/types';
import { resolveApiAssetUrl } from '@/lib/api-client';
import { selectMembershipForTenant } from '@/features/loyalty/selection';
import { useLoyaltyUiStore } from '@/features/loyalty/store';

export default function TierProgressScreen() {
  const router = useRouter();
  const memberships = useMemberships();
  const selectedTenantId = useLoyaltyUiStore((state) => state.selectedTenantId);
  const setSelectedTenantId = useLoyaltyUiStore((state) => state.setSelectedTenantId);
  useAutoSelectMembership(memberships.data);
  const selectedMembership = selectMembershipForTenant(memberships.data, selectedTenantId);
  const tier = useLoyaltyTierProgress(selectedMembership?.tenantId ?? null);
  const ladder = useLoyaltyTierLadder(selectedMembership?.tenantId ?? null);
  const refetchTier = tier.refetch;

  useFocusEffect(useCallback(() => {
    if (selectedMembership?.tenantId) void refetchTier();
  }, [refetchTier, selectedMembership?.tenantId]));

  const score = tier.data?.compositeScore ?? 0;
  const remaining = tier.data?.scoreToNextTier ?? 0;
  const targetScore = score + remaining;
  const progressPercent = tier.data?.nextTierName
    ? targetScore <= 0 ? 100 : Math.min(100, Math.max(0, score / targetScore * 100))
    : 100;

  return <SafeAreaView className="flex-1 bg-gray-50">
    <View className="flex-row items-center border-b border-gray-100 bg-white px-4 py-3">
      <TouchableOpacity accessibilityRole="button" accessibilityLabel="Назад" onPress={() => router.back()} className="h-11 w-11 items-center justify-center rounded-2xl bg-gray-100"><Ionicons name="arrow-back" size={21} color="#1f2937" /></TouchableOpacity>
      <View className="ml-3"><Text className="text-xl font-bold text-gray-900">Прогрес рангу</Text><Text className="text-xs text-gray-500">Ваш шлях у програмі лояльності</Text></View>
    </View>

    <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 40 }}>
      {(memberships.data?.length ?? 0) > 1 ? <View className="mt-4"><Text className="mb-2 px-5 text-sm font-semibold text-gray-700">Мережа магазинів</Text><MembershipSelector memberships={memberships.data ?? []} selectedTenantId={selectedTenantId} onSelect={setSelectedTenantId} /></View> : null}

      {memberships.isLoading || tier.isLoading ? <ActivityIndicator size="large" color="#d97706" className="mt-20" />
        : !selectedMembership ? <EmptyState icon="storefront-outline" title="Немає вибраної мережі" description="Приєднайтеся до програми лояльності магазину." />
          : tier.isError || !tier.data ? <View className="mx-5 mt-12 items-center rounded-3xl bg-white p-7"><Ionicons name="cloud-offline-outline" size={42} color="#d97706" /><Text className="mt-3 text-lg font-bold text-gray-900">Не вдалося отримати прогрес</Text><TouchableOpacity onPress={() => void tier.refetch()} className="mt-4 rounded-2xl bg-amber-500 px-5 py-3"><Text className="font-bold text-white">Спробувати ще раз</Text></TouchableOpacity></View>
            : <View className="px-5 pt-5">
              <View className="overflow-hidden rounded-[28px] p-6" style={{ backgroundColor: '#d97706' }}>
                <Text className="text-sm font-semibold text-white/80">{selectedMembership.tenantName}</Text>
                <View className="mt-4 flex-row items-end justify-between"><View><Text className="text-xs font-bold uppercase tracking-wider text-white/70">Поточний ранг</Text><Text className="mt-1 text-3xl font-bold text-white">{tier.data.currentTierName ?? 'Ще не присвоєно'}</Text></View><Ionicons name="trophy" size={46} color="rgba(255,255,255,0.75)" /></View>
              </View>

              <View className="mt-4 rounded-3xl border border-gray-100 bg-white p-5">
                {tier.data.nextTierName ? <>
                  <View className="flex-row items-center justify-between"><Text className="font-bold text-gray-900">Наступний ранг</Text><Text className="font-bold text-amber-700">{tier.data.nextTierName}</Text></View>
                  <View className="mt-5 h-3 overflow-hidden rounded-full bg-amber-100"><View className="h-full rounded-full bg-amber-500" style={{ width: `${progressPercent}%` as `${number}%` }} /></View>
                  <View className="mt-2 flex-row justify-between"><Text className="text-xs text-gray-500">{score.toFixed(2)} бала</Text><Text className="text-xs font-semibold text-gray-700">{progressPercent.toFixed(0)}%</Text><Text className="text-xs text-gray-500">{targetScore.toFixed(2)} бала</Text></View>
                  <View className="mt-5 rounded-2xl bg-amber-50 p-4"><Text className="text-center text-sm text-amber-900">{remaining > 0 ? <>До рангу «{tier.data.nextTierName}» залишилося <Text className="font-bold">{remaining.toFixed(2)} бала</Text></> : <>Умови виконано. Ранг буде присвоєно після перерахунку.</>}</Text></View>
                  {tier.data.metrics && tier.data.nextTierRequirements ? <RequirementsProgress metrics={tier.data.metrics} requirements={tier.data.nextTierRequirements} /> : null}
                </> : <View className="items-center py-3"><Ionicons name="ribbon" size={42} color="#16a34a" /><Text className="mt-3 text-lg font-bold text-gray-900">Найвищий ранг досягнуто</Text><Text className="mt-1 text-center text-sm text-gray-500">Наступного рівня в цій програмі поки немає.</Text></View>}
              </View>

              <View className="mt-4 flex-row gap-3"><Benefit icon="sparkles-outline" label="Кешбек" value={`${tier.data.accrualMultiplier.toFixed(2)}%`} color="#15803d" bg="#f0fdf4" /><Benefit icon="pricetag-outline" label="Персональна знижка" value={`${tier.data.discountPercent.toFixed(2)}%`} color="#1d4ed8" bg="#eff6ff" /></View>

              <View className="mt-4 rounded-3xl border border-gray-100 bg-white p-5">
                <Text className="text-lg font-bold text-gray-900">Усі ранги</Text>
                <Text className="mt-1 text-sm text-gray-500">Рівні програми у порядку зростання</Text>
                <View className="mt-4 gap-3">
                  {(ladder.data?.length ? ladder.data : fallbackTiers(tier.data)).map((item) => <TierRow key={item.id} item={item} currentTierId={tier.data.currentTierId} nextTierId={tier.data.nextTierId} />)}
                </View>
                {ladder.isError ? <Text className="mt-4 text-xs leading-5 text-amber-700">Поки показано доступні дані про поточний і наступний ранги. Повний перелік з’явиться після оновлення consumer API.</Text> : null}
              </View>
              <Text className="mt-4 px-2 text-center text-xs leading-5 text-gray-400">Усі вибрані магазином умови повинні бути виконані. Прогрес перераховується щоночі.</Text>
            </View>}
    </ScrollView>
  </SafeAreaView>;
}

function Benefit({ icon, label, value, color, bg }: { icon: keyof typeof Ionicons.glyphMap; label: string; value: string; color: string; bg: string }) { return <View className="flex-1 rounded-3xl p-4" style={{ backgroundColor: bg }}><Ionicons name={icon} size={22} color={color} /><Text className="mt-3 text-xs text-gray-500">{label}</Text><Text className="mt-1 text-xl font-bold" style={{ color }}>{value}</Text></View>; }
function EmptyState({ icon, title, description }: { icon: keyof typeof Ionicons.glyphMap; title: string; description: string }) { return <View className="mx-5 mt-12 items-center rounded-3xl bg-white p-7"><Ionicons name={icon} size={42} color="#9ca3af" /><Text className="mt-3 text-lg font-bold text-gray-900">{title}</Text><Text className="mt-1 text-center text-sm text-gray-500">{description}</Text></View>; }

function fallbackTiers(progress: { currentTierId: string | null; currentTierName: string | null; nextTierId: string | null; nextTierName: string | null; compositeScore: number; scoreToNextTier: number | null; accrualMultiplier: number; discountPercent: number }): LoyaltyTierDefinition[] {
  const items: LoyaltyTierDefinition[] = [];
  if (progress.currentTierId && progress.currentTierName) items.push({ id: progress.currentTierId, name: progress.currentTierName, sortOrder: 0, minCompositeScore: progress.compositeScore, accrualMultiplier: progress.accrualMultiplier, discountPercent: progress.discountPercent });
  if (progress.nextTierId && progress.nextTierName) items.push({ id: progress.nextTierId, name: progress.nextTierName, sortOrder: 1, minCompositeScore: progress.compositeScore + (progress.scoreToNextTier ?? 0), accrualMultiplier: 0, discountPercent: 0 });
  return items;
}

function TierRow({ item, currentTierId, nextTierId }: { item: LoyaltyTierDefinition; currentTierId: string | null; nextTierId: string | null }) {
  const current = item.id === currentTierId;
  const next = item.id === nextTierId;
  const image = resolveApiAssetUrl(item.imageUrl);
  return <View className="flex-row items-center rounded-2xl border p-3" style={{ borderColor: current ? '#f59e0b' : '#f3f4f6', backgroundColor: current ? '#fffbeb' : '#ffffff' }}>{image ? <Image source={{ uri: image }} className="h-12 w-12 rounded-2xl" /> : <View className="h-12 w-12 items-center justify-center rounded-2xl" style={{ backgroundColor: current ? '#f59e0b' : '#f3f4f6' }}><Ionicons name={current ? 'trophy' : 'ribbon-outline'} size={20} color={current ? '#ffffff' : '#6b7280'} /></View>}<View className="ml-3 flex-1"><View className="flex-row items-center"><Text className="font-bold text-gray-900">{item.name}</Text>{current ? <Text className="ml-2 rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-bold text-amber-800">Ваш ранг</Text> : next ? <Text className="ml-2 rounded-full bg-blue-50 px-2 py-0.5 text-[10px] font-bold text-blue-700">Наступний</Text> : null}</View>{item.description ? <Text className="mt-1 text-xs text-gray-500">{item.description}</Text> : null}<Text className="mt-1 text-xs text-gray-500">кешбек {item.accrualMultiplier.toFixed(2)}% · знижка {item.discountPercent.toFixed(2)}%</Text></View></View>;
}

function RequirementsProgress({ metrics, requirements }: { metrics: LoyaltyTierProgressMetrics; requirements: LoyaltyTierRequirements }) {
  const rows: Array<[string, number | boolean, number | boolean]> = [];
  if (requirements.requireCompletedProfile) rows.push(['Профіль заповнений', metrics.profileCompleted, true]);
  if (requirements.minMembershipDays !== null) rows.push(['Днів у програмі', metrics.membershipDays, requirements.minMembershipDays]);
  if (requirements.minEarnedBonuses !== null) rows.push(['Накопичено бонусів', metrics.earnedBonuses, requirements.minEarnedBonuses]);
  if (requirements.minCashSpend !== null) rows.push(['Оплачено грошима', metrics.cashSpend, requirements.minCashSpend]);
  if (requirements.minBonusSpend !== null) rows.push(['Оплачено бонусами', metrics.bonusSpend, requirements.minBonusSpend]);
  if (requirements.minPurchaseCount !== null) rows.push(['Кількість покупок', metrics.purchaseCount, requirements.minPurchaseCount]);
  if (requirements.minReviewCount !== null) rows.push(['Кількість відгуків', metrics.reviewCount, requirements.minReviewCount]);
  if (!rows.length) return null;
  return <View className="mt-5 border-t border-gray-100 pt-4"><Text className="mb-3 font-bold text-gray-900">Умови переходу</Text>{rows.map(([name, actual, target]) => { const done = typeof target === 'boolean' ? actual === target : Number(actual) >= target; return <View key={name} className="mb-2 flex-row items-center"><Ionicons name={done ? 'checkmark-circle' : 'ellipse-outline'} size={19} color={done ? '#16a34a' : '#9ca3af'} /><Text className="ml-2 flex-1 text-sm text-gray-700">{name}</Text><Text className={done ? 'text-sm font-semibold text-green-700' : 'text-sm text-gray-500'}>{typeof target === 'boolean' ? (done ? 'Так' : 'Ні') : `${Number(actual).toFixed(2)} / ${target}`}</Text></View>; })}</View>;
}
