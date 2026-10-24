import { View, Text, Image } from 'react-native';
import { apiClient, resolveApiAssetUrl } from '@/lib/api-client';
import { privateAssetSource } from '@/lib/private-assets';
import { useAuthStore } from '@/features/auth/store';
import type { ScanItemStatus, ShelfScan, ShelfScanItem } from '../types';

const STATUS_STYLE: Record<ScanItemStatus, { label: string; bg: string; text: string }> = {
  present: { label: 'Є', bg: 'bg-green-100', text: 'text-green-700' },
  low: { label: 'Мало', bg: 'bg-amber-100', text: 'text-amber-700' },
  missing: { label: 'Немає', bg: 'bg-red-100', text: 'text-red-700' },
};

const ORDER: Record<ScanItemStatus, number> = { missing: 0, low: 1, present: 2 };

export function formatScanDate(iso: string): string {
  return new Date(iso).toLocaleString('uk-UA', {
    day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit',
  });
}

const PRIORITY_TEXT = { low: 'text-gray-500', medium: 'text-amber-700', high: 'text-red-700' } as const;

function recommendationText(r: NonNullable<ShelfScanItem['recommendation']>): string | null {
  switch (r.action) {
    case 'restock_from_warehouse': return `Винести зі складу ≈ ${r.suggestedQty} шт`;
    case 'order_from_supplier': return `Немає на складі — замовити ≈ ${r.suggestedQty} шт`;
    case 'check_manually': return 'Розбіжність з обліком — перевірте вручну';
    default: return null;
  }
}

function ItemInsights({ item }: { item: ShelfScanItem }) {
  if (item.status === 'present') return null;
  const hasData = item.storeQty != null || item.warehouseQty != null || item.sold7d != null;
  const rec = item.recommendation ? recommendationText(item.recommendation) : null;
  if (!hasData && !rec) return null;
  return (
    <View className="mt-1.5 bg-gray-50 rounded-lg p-2 gap-0.5">
      {hasData ? (
        <Text className="text-xs text-gray-600">
          Магазин: {item.storeQty ?? 0} · Склад: {item.warehouseQty ?? 0} · Продано 7д/30д: {item.sold7d ?? 0}/{item.sold30d ?? 0}
          {item.daysOfCover != null ? ` · Запасу на ${Math.round(item.daysOfCover)} дн.` : ''}
        </Text>
      ) : null}
      {rec && item.recommendation ? (
        <Text className={`text-xs font-semibold ${PRIORITY_TEXT[item.recommendation.priority]}`}>{rec}</Text>
      ) : null}
    </View>
  );
}

export function ScanResultView({ scan, showImage = true }: { scan: ShelfScan; showImage?: boolean }) {
  const token = useAuthStore((state) => state.workspaceAccessToken);
  const image = privateAssetSource(resolveApiAssetUrl(scan.imageUrl), token, apiClient.defaults.baseURL);
  const items = [...scan.items].sort((a, b) => ORDER[a.status] - ORDER[b.status]);
  const failed = scan.status === 'failed' || !!scan.errorMessage;

  return (
    <View className="gap-3">
      {showImage && image ? (
        <Image source={image} className="w-full h-48 rounded-xl bg-gray-100" resizeMode="cover" />
      ) : null}

      <View className="flex-row gap-2">
        <View className="flex-1 bg-red-50 rounded-xl p-3 items-center">
          <Text className="text-2xl font-bold text-red-600">{scan.missingCount}</Text>
          <Text className="text-xs text-red-600">Немає</Text>
        </View>
        <View className="flex-1 bg-amber-50 rounded-xl p-3 items-center">
          <Text className="text-2xl font-bold text-amber-600">{scan.lowCount}</Text>
          <Text className="text-xs text-amber-600">Мало</Text>
        </View>
      </View>

      {failed ? (
        <Text className="text-sm text-red-600">{scan.errorMessage ?? 'Не вдалося обробити фото.'}</Text>
      ) : null}
      {scan.summary ? <Text className="text-sm text-gray-700">{scan.summary}</Text> : null}

      {items.map((item) => {
        const st = STATUS_STYLE[item.status];
        return (
          <View key={item.itemId} className="flex-row items-start gap-3 py-2 border-b border-gray-100">
            <View className={`px-2.5 py-1 rounded-full ${st.bg}`}>
              <Text className={`text-xs font-semibold ${st.text}`}>{st.label}</Text>
            </View>
            <View className="flex-1">
              <Text className="text-sm font-medium text-gray-900">{item.name}</Text>
              <Text className="text-xs text-gray-500 mt-0.5">
                ≈ {item.estimatedCount} шт · впевненість {Math.round(item.confidence * 100)}%
              </Text>
              {item.note ? <Text className="text-xs text-gray-500 mt-0.5">{item.note}</Text> : null}
              <ItemInsights item={item} />
            </View>
          </View>
        );
      })}

      <Text className="text-xs text-gray-400 text-center">
        Орієнтовно, потребує перевірки · {formatScanDate(scan.createdAt)}
      </Text>
    </View>
  );
}
