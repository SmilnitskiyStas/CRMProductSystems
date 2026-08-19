import { useEffect, useState } from 'react';
import { Image, Linking, Pressable, ScrollView, Text, View, type ImageStyle, type StyleProp } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useSelectedConsumerContext } from '@/features/consumer-content/hooks';
import { useSetPreferredStore } from '@/features/loyalty/hooks/useLoyalty';
import { useAuthStore } from '@/features/auth/store';
import type { BlockComponentProps } from '../types';
import { useRetailTheme } from '@/features/theme/RetailThemeProvider';
import type {
  BannerCarouselProps,
  HeroBannerProps,
  LoyaltyBalanceProps,
  LoyaltyCardProps,
  NewsListProps,
  ProductCollectionProps,
  ProductItem,
  PromotionCollectionProps,
  PromotionItem,
  QuickActionsProps,
  SectionHeaderProps,
  StoreListProps,
} from './types';

function money(value: number): string {
  return value.toLocaleString('uk-UA', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function SafeRemoteImage({ imageUrl, style }: { imageUrl?: string; style: StyleProp<ImageStyle> }) {
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [imageUrl]);
  if (!imageUrl || failed) return null;
  return <Image testID="remote-image" source={{ uri: imageUrl }} resizeMode="cover" style={style} onError={() => setFailed(true)} />;
}

function ImageOrPlaceholder({ imageUrl, height = 120 }: { imageUrl?: string; height?: number }) {
  const theme = useRetailTheme();
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [imageUrl]);
  if (!imageUrl || failed) return <View testID="image-placeholder" style={{ width: '100%', height, backgroundColor: theme.colors.border }} />;
  return <Image testID="remote-image" source={{ uri: imageUrl }} resizeMode="cover" style={{ width: '100%', height }} onError={() => setFailed(true)} />;
}

export function HeroBannerBlock({ block }: BlockComponentProps<HeroBannerProps>) {
  const theme = useRetailTheme();
  const { title, subtitle, imageUrl, eyebrow, ctaLabel, ctaLink, heightPx } = block.props;
  return (
    <View
      testID={`block-${block.id}`}
      style={{
        minHeight: heightPx ?? 190,
        marginBottom: theme.spacing.md,
        overflow: 'hidden',
        borderRadius: theme.radius.card,
        backgroundColor: theme.colors.primary,
      }}
    >
      <SafeRemoteImage imageUrl={imageUrl} style={{ position: 'absolute', inset: 0 }} />
      <View style={{ flex: 1, justifyContent: 'flex-end', padding: theme.spacing.lg, backgroundColor: imageUrl ? '#00000066' : 'transparent' }}>
        {eyebrow ? <Text style={{ color: theme.colors.onPrimary, fontSize: 12, fontWeight: '700' }}>{eyebrow}</Text> : null}
        <Text style={{ color: theme.colors.onPrimary, fontSize: 26, fontWeight: '800', marginTop: 4 }}>{title}</Text>
        {subtitle ? <Text style={{ color: theme.colors.onPrimary, fontSize: 14, marginTop: 6 }}>{subtitle}</Text> : null}
        {ctaLabel && ctaLink ? <Pressable onPress={() => void Linking.openURL(ctaLink)} style={{ alignSelf: 'flex-start', marginTop: theme.spacing.md, borderRadius: theme.radius.button, backgroundColor: theme.colors.surface, paddingHorizontal: theme.spacing.md, paddingVertical: theme.spacing.sm }}><Text style={{ color: theme.colors.primary, fontWeight: '700' }}>{ctaLabel}</Text></Pressable> : null}
      </View>
    </View>
  );
}

export function BannerCarouselBlock({ block }: BlockComponentProps<BannerCarouselProps>) {
  const theme = useRetailTheme();
  return (
    <ScrollView horizontal showsHorizontalScrollIndicator={false} style={{ marginBottom: theme.spacing.md }}>
      {block.props.items.map((item) => (
        <View key={item.id} testID={`banner-card-${item.id}`} style={{ width: block.props.cardWidthPx ?? 280, marginRight: theme.spacing.sm, overflow: 'hidden', borderRadius: theme.radius.card, backgroundColor: theme.colors.surface }}>
          <ImageOrPlaceholder imageUrl={item.imageUrl} height={130} />
          <View style={{ padding: theme.spacing.md }}>
            <Text style={{ color: theme.colors.textPrimary, fontSize: 17, fontWeight: '700' }}>{item.title}</Text>
            {item.subtitle ? <Text style={{ color: theme.colors.textSecondary, marginTop: 4 }}>{item.subtitle}</Text> : null}
          </View>
        </View>
      ))}
    </ScrollView>
  );
}

export function LoyaltyCardBlock({ block }: BlockComponentProps<LoyaltyCardProps>) {
  const theme = useRetailTheme();
  const { title = 'Картка покупця', balance, cardNumber, tier } = block.props;
  return (
    <View style={{ marginBottom: theme.spacing.md, borderRadius: theme.radius.card, padding: theme.spacing.lg, backgroundColor: theme.colors.primary }}>
      <Text style={{ color: theme.colors.onPrimary, fontSize: 13 }}>{title}</Text>
      <Text style={{ color: theme.colors.onPrimary, fontSize: 32, fontWeight: '800', marginTop: 8 }}>{money(balance)}</Text>
      <View style={{ flexDirection: 'row', justifyContent: 'space-between', marginTop: theme.spacing.lg }}>
        <Text style={{ color: theme.colors.onPrimary }}>{cardNumber ?? '•••• ••••'}</Text>
        {tier ? <Text style={{ color: theme.colors.onPrimary, fontWeight: '700' }}>{tier}</Text> : null}
      </View>
    </View>
  );
}

export function LoyaltyBalanceBlock({ block }: BlockComponentProps<LoyaltyBalanceProps>) {
  const theme = useRetailTheme();
  return (
    <View style={{ marginBottom: theme.spacing.md, borderRadius: theme.radius.card, padding: theme.spacing.md, backgroundColor: theme.colors.surface }}>
      <Text style={{ color: theme.colors.textSecondary }}>{block.props.label ?? 'Бонусний баланс'}</Text>
      <Text style={{ color: theme.colors.textPrimary, fontSize: 25, fontWeight: '800', marginTop: 4 }}>
        {money(block.props.balance)} {block.props.unit ?? 'бонусів'}
      </Text>
    </View>
  );
}

function PromotionCard({ item, width }: { item: PromotionItem; width: number | `${number}%` }) {
  const theme = useRetailTheme();
  return (
    <View testID={`promotion-card-${item.id}`} style={{ width, overflow: 'hidden', borderRadius: theme.radius.card, backgroundColor: theme.colors.surface }}>
      <ImageOrPlaceholder imageUrl={item.imageUrl} />
      <View style={{ padding: theme.spacing.md }}>
        {item.badge ? <Text style={{ color: theme.colors.primary, fontSize: 11, fontWeight: '800' }}>{item.badge}</Text> : null}
        <Text style={{ color: theme.colors.textPrimary, fontWeight: '700', marginTop: 3 }}>{item.title}</Text>
        {item.subtitle ? <Text style={{ color: theme.colors.textSecondary, fontSize: 12, marginTop: 4 }}>{item.subtitle}</Text> : null}
      </View>
    </View>
  );
}

export function PromotionCarouselBlock({ block }: BlockComponentProps<PromotionCollectionProps>) {
  const theme = useRetailTheme();
  return (
    <View style={{ marginBottom: theme.spacing.md }}>
      {block.props.title ? <Text style={{ color: theme.colors.textPrimary, fontSize: 20, fontWeight: '800', marginBottom: theme.spacing.sm }}>{block.props.title}</Text> : null}
    <ScrollView horizontal showsHorizontalScrollIndicator={false} style={{ marginBottom: theme.spacing.md }} contentContainerStyle={{ gap: theme.spacing.sm }}>
      {block.props.items.map((item) => <PromotionCard key={item.id} item={item} width={block.props.cardWidthPx ?? 210} />)}
    </ScrollView>
    </View>
  );
}

export function PromotionGridBlock({ block }: BlockComponentProps<PromotionCollectionProps>) {
  const theme = useRetailTheme();
  const width = block.props.columns === 3 ? '31%' : '48%';
  return (
    <View style={{ marginBottom: theme.spacing.md }}>
      {block.props.title ? <Text style={{ color: theme.colors.textPrimary, fontSize: 20, fontWeight: '800', marginBottom: theme.spacing.sm }}>{block.props.title}</Text> : null}
    <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm, marginBottom: theme.spacing.md }}>
      {block.props.items.map((item) => <PromotionCard key={item.id} item={item} width={width} />)}
    </View>
    </View>
  );
}

function ProductCard({ item, width }: { item: ProductItem; width: number | `${number}%` }) {
  const theme = useRetailTheme();
  return (
    <View testID={`product-card-${item.id}`} style={{ width, overflow: 'hidden', borderRadius: theme.radius.card, backgroundColor: theme.colors.surface }}>
      <ImageOrPlaceholder imageUrl={item.imageUrl} />
      <View style={{ padding: theme.spacing.md }}>
        <Text numberOfLines={2} style={{ color: theme.colors.textPrimary, fontWeight: '700' }}>{item.name}</Text>
        <Text style={{ color: theme.colors.primary, fontSize: 17, fontWeight: '800', marginTop: 7 }}>{money(item.price)} ₴</Text>
        {item.oldPrice !== undefined ? <Text style={{ color: theme.colors.textSecondary, fontSize: 12, textDecorationLine: 'line-through' }}>{money(item.oldPrice)} ₴</Text> : null}
        {item.unit ? <Text style={{ color: theme.colors.textSecondary, fontSize: 11, marginTop: 2 }}>{item.unit}</Text> : null}
      </View>
    </View>
  );
}

export function ProductCarouselBlock({ block }: BlockComponentProps<ProductCollectionProps>) {
  const theme = useRetailTheme();
  return (
    <View style={{ marginBottom: theme.spacing.md }}>
      {block.props.title ? <Text style={{ color: theme.colors.textPrimary, fontSize: 20, fontWeight: '800', marginBottom: theme.spacing.sm }}>{block.props.title}</Text> : null}
    <ScrollView horizontal showsHorizontalScrollIndicator={false} style={{ marginBottom: theme.spacing.md }} contentContainerStyle={{ gap: theme.spacing.sm }}>
      {block.props.items.map((item) => <ProductCard key={item.id} item={item} width={block.props.cardWidthPx ?? 170} />)}
    </ScrollView>
    </View>
  );
}

export function ProductGridBlock({ block }: BlockComponentProps<ProductCollectionProps>) {
  const theme = useRetailTheme();
  const width = block.props.columns === 3 ? '31%' : '48%';
  return (
    <View style={{ marginBottom: theme.spacing.md }}>
      {block.props.title ? <Text style={{ color: theme.colors.textPrimary, fontSize: 20, fontWeight: '800', marginBottom: theme.spacing.sm }}>{block.props.title}</Text> : null}
    <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm, marginBottom: theme.spacing.md }}>
      {block.props.items.map((item) => <ProductCard key={item.id} item={item} width={width} />)}
    </View>
    </View>
  );
}

export function SectionHeaderBlock({ block }: BlockComponentProps<SectionHeaderProps>) {
  const theme = useRetailTheme();
  return (
    <View style={{ flexDirection: 'row', alignItems: 'flex-end', marginBottom: theme.spacing.sm, marginTop: theme.spacing.sm }}>
      <View style={{ flex: 1 }}>
        <Text style={{ color: theme.colors.textPrimary, fontSize: 21, fontWeight: '800', textAlign: block.props.alignment ?? 'left' }}>{block.props.title}</Text>
        {block.props.subtitle ? <Text style={{ color: theme.colors.textSecondary, marginTop: 3, textAlign: block.props.alignment ?? 'left' }}>{block.props.subtitle}</Text> : null}
      </View>
      {block.props.actionLabel ? <Text style={{ color: theme.colors.primary, fontWeight: '700' }}>{block.props.actionLabel}</Text> : null}
    </View>
  );
}

export function QuickActionsBlock({ block }: BlockComponentProps<QuickActionsProps>) {
  const theme = useRetailTheme();
  const router = useRouter();
  const routes: Record<string, '/(personal)' | '/(personal)/promotions' | '/(personal)/catalog' | '/(personal)/wallet' | '/(personal)/coupons' | '/(personal)/retailers' | '/(personal)/news' | '/(personal)/account'> = {
    home: '/(personal)', promotions: '/(personal)/promotions', catalog: '/(personal)/catalog', loyalty: '/(personal)/wallet', coupons: '/(personal)/coupons', stores: '/(personal)/retailers', news: '/(personal)/news', profile: '/(personal)/account',
  };
  return (
    <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm, marginBottom: theme.spacing.md }}>
      {block.props.items.map((item) => (
        <Pressable key={item.id} onPress={() => routes[item.id] && router.push(routes[item.id])} style={{ width: '23%', alignItems: 'center' }}>
          <View style={{ width: 48, height: 48, alignItems: 'center', justifyContent: 'center', borderRadius: theme.radius.button, backgroundColor: theme.colors.border }}>
            <Text style={{ color: theme.colors.primary, fontWeight: '800' }}>{(item.icon ?? item.label).slice(0, 1).toUpperCase()}</Text>
          </View>
          <Text numberOfLines={2} style={{ color: theme.colors.textPrimary, fontSize: 12, textAlign: 'center', marginTop: 6 }}>{item.label}</Text>
        </Pressable>
      ))}
    </View>
  );
}

export function NewsListBlock({ block }: BlockComponentProps<NewsListProps>) {
  const theme = useRetailTheme();
  return (
    <View style={{ gap: theme.spacing.sm, marginBottom: theme.spacing.md }}>
      {block.props.items.map((item) => (
        <View key={item.id} style={{ flexDirection: 'row', overflow: 'hidden', borderRadius: theme.radius.card, backgroundColor: theme.colors.surface }}>
          <View style={{ width: 96 }}><ImageOrPlaceholder imageUrl={item.imageUrl} height={96} /></View>
          <View style={{ flex: 1, padding: theme.spacing.md }}>
            <Text style={{ color: theme.colors.textPrimary, fontWeight: '700' }}>{item.title}</Text>
            {item.summary ? <Text numberOfLines={2} style={{ color: theme.colors.textSecondary, fontSize: 12, marginTop: 4 }}>{item.summary}</Text> : null}
            {item.publishedAt ? <Text style={{ color: theme.colors.textSecondary, fontSize: 10, marginTop: 5 }}>{item.publishedAt}</Text> : null}
          </View>
        </View>
      ))}
    </View>
  );
}

export function StoreListBlock({ block }: BlockComponentProps<StoreListProps>) {
  const theme = useRetailTheme();
  const [expanded, setExpanded] = useState(false);
  const hasPersonalAccess = useAuthStore((state) => state.personalAccessToken !== null);
  const { membership } = useSelectedConsumerContext(hasPersonalAccess);
  const preferredStore = useSetPreferredStore();

  function selectStore(storeId: string) {
    if (!membership || preferredStore.isPending) return;
    void preferredStore.mutateAsync({ tenantId: membership.tenantId, storeId });
  }
  return (
    <View
      style={{
        marginBottom: theme.spacing.md,
        overflow: 'visible',
        zIndex: expanded ? 100 : 1,
        elevation: expanded ? 12 : 0,
      }}
    >
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={expanded ? 'Закрити список магазинів' : 'Відкрити список магазинів'}
        accessibilityState={{ expanded }}
        onPress={() => setExpanded((current) => !current)}
        style={{
          flexDirection: 'row',
          alignItems: 'center',
          padding: theme.spacing.md,
          borderRadius: theme.radius.card,
          backgroundColor: theme.colors.surface,
        }}
      >
        <View style={{ width: 42, height: 42, alignItems: 'center', justifyContent: 'center', borderRadius: theme.radius.button, backgroundColor: theme.colors.border }}>
          <Ionicons name="storefront-outline" size={21} color={theme.colors.primary} />
        </View>
        <View style={{ flex: 1, marginLeft: theme.spacing.sm }}>
          <Text style={{ color: theme.colors.textPrimary, fontSize: 17, fontWeight: '800' }}>{block.props.title || 'Магазини'}</Text>
          <Text style={{ color: theme.colors.textSecondary, fontSize: 12, marginTop: 2 }}>
            {block.props.items.length ? `${block.props.items.length} доступних` : 'Немає доступних магазинів'}
          </Text>
        </View>
        <Ionicons name={expanded ? 'chevron-up' : 'chevron-down'} size={21} color={theme.colors.textSecondary} />
      </Pressable>
      {expanded ? (
        <View
          style={{
            position: 'absolute',
            top: '100%',
            left: 0,
            right: 0,
            zIndex: 101,
            elevation: 14,
            gap: theme.spacing.sm,
            marginTop: theme.spacing.xs,
            padding: theme.spacing.md,
            borderRadius: theme.radius.card,
            backgroundColor: theme.colors.surface,
            shadowColor: '#000000',
            shadowOffset: { width: 0, height: 8 },
            shadowOpacity: 0.18,
            shadowRadius: 16,
          }}
        >
          {block.props.items.map((item) => (
            <Pressable
              key={item.id}
              accessibilityRole="button"
              accessibilityLabel={`Обрати магазин ${item.name}`}
              accessibilityState={{ selected: membership?.preferredStoreId === item.id, disabled: !membership }}
              disabled={!membership || preferredStore.isPending}
              onPress={() => selectStore(item.id)}
              style={{ borderRadius: theme.radius.button, padding: theme.spacing.md, backgroundColor: theme.colors.background, opacity: preferredStore.isPending ? 0.6 : 1 }}
            >
              <View style={{ flexDirection: 'row', alignItems: 'center' }}>
                <Ionicons name="location-outline" size={18} color={theme.colors.primary} />
                <Text style={{ flex: 1, marginLeft: theme.spacing.sm, color: theme.colors.textPrimary, fontWeight: '700' }}>{item.name}</Text>
                {item.openNow !== undefined ? <Text style={{ color: item.openNow ? theme.colors.primary : theme.colors.textSecondary, fontSize: 12 }}>{item.openNow ? 'Відчинено' : 'Зачинено'}</Text> : null}
                {membership?.preferredStoreId === item.id ? <Ionicons name="checkmark-circle" size={20} color={theme.colors.primary} /> : null}
              </View>
              {item.address ? <Text style={{ color: theme.colors.textSecondary, marginLeft: 18 + theme.spacing.sm, marginTop: 5 }}>{item.address}</Text> : null}
              {block.props.showDistance !== false && item.distanceKm !== undefined ? <Text style={{ color: theme.colors.textSecondary, fontSize: 12, marginLeft: 18 + theme.spacing.sm, marginTop: 4 }}>{item.distanceKm.toFixed(1)} км</Text> : null}
            </Pressable>
          ))}
          {!block.props.items.length ? <Text style={{ paddingVertical: theme.spacing.md, textAlign: 'center', color: theme.colors.textSecondary }}>Магазини цієї мережі поки недоступні.</Text> : null}
        </View>
      ) : null}
    </View>
  );
}
