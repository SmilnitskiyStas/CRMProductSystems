import { useEffect, useState } from 'react';
import { Image, ScrollView, Text, View, type ImageStyle, type StyleProp } from 'react-native';
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
  const { title, subtitle, imageUrl, eyebrow } = block.props;
  return (
    <View
      testID={`block-${block.id}`}
      style={{
        minHeight: 190,
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
      </View>
    </View>
  );
}

export function BannerCarouselBlock({ block }: BlockComponentProps<BannerCarouselProps>) {
  const theme = useRetailTheme();
  return (
    <ScrollView horizontal showsHorizontalScrollIndicator={false} style={{ marginBottom: theme.spacing.md }}>
      {block.props.items.map((item) => (
        <View key={item.id} style={{ width: 280, marginRight: theme.spacing.sm, overflow: 'hidden', borderRadius: theme.radius.card, backgroundColor: theme.colors.surface }}>
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
    <View style={{ width, overflow: 'hidden', borderRadius: theme.radius.card, backgroundColor: theme.colors.surface }}>
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
    <ScrollView horizontal showsHorizontalScrollIndicator={false} style={{ marginBottom: theme.spacing.md }} contentContainerStyle={{ gap: theme.spacing.sm }}>
      {block.props.items.map((item) => <PromotionCard key={item.id} item={item} width={210} />)}
    </ScrollView>
  );
}

export function PromotionGridBlock({ block }: BlockComponentProps<PromotionCollectionProps>) {
  const theme = useRetailTheme();
  const width = block.props.columns === 3 ? '31%' : '48%';
  return (
    <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm, marginBottom: theme.spacing.md }}>
      {block.props.items.map((item) => <PromotionCard key={item.id} item={item} width={width} />)}
    </View>
  );
}

function ProductCard({ item, width }: { item: ProductItem; width: number | `${number}%` }) {
  const theme = useRetailTheme();
  return (
    <View style={{ width, overflow: 'hidden', borderRadius: theme.radius.card, backgroundColor: theme.colors.surface }}>
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
    <ScrollView horizontal showsHorizontalScrollIndicator={false} style={{ marginBottom: theme.spacing.md }} contentContainerStyle={{ gap: theme.spacing.sm }}>
      {block.props.items.map((item) => <ProductCard key={item.id} item={item} width={170} />)}
    </ScrollView>
  );
}

export function ProductGridBlock({ block }: BlockComponentProps<ProductCollectionProps>) {
  const theme = useRetailTheme();
  const width = block.props.columns === 3 ? '31%' : '48%';
  return (
    <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm, marginBottom: theme.spacing.md }}>
      {block.props.items.map((item) => <ProductCard key={item.id} item={item} width={width} />)}
    </View>
  );
}

export function SectionHeaderBlock({ block }: BlockComponentProps<SectionHeaderProps>) {
  const theme = useRetailTheme();
  return (
    <View style={{ flexDirection: 'row', alignItems: 'flex-end', marginBottom: theme.spacing.sm, marginTop: theme.spacing.sm }}>
      <View style={{ flex: 1 }}>
        <Text style={{ color: theme.colors.textPrimary, fontSize: 21, fontWeight: '800' }}>{block.props.title}</Text>
        {block.props.subtitle ? <Text style={{ color: theme.colors.textSecondary, marginTop: 3 }}>{block.props.subtitle}</Text> : null}
      </View>
      {block.props.actionLabel ? <Text style={{ color: theme.colors.primary, fontWeight: '700' }}>{block.props.actionLabel}</Text> : null}
    </View>
  );
}

export function QuickActionsBlock({ block }: BlockComponentProps<QuickActionsProps>) {
  const theme = useRetailTheme();
  return (
    <View style={{ flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.sm, marginBottom: theme.spacing.md }}>
      {block.props.items.map((item) => (
        <View key={item.id} style={{ width: '23%', alignItems: 'center' }}>
          <View style={{ width: 48, height: 48, alignItems: 'center', justifyContent: 'center', borderRadius: theme.radius.button, backgroundColor: theme.colors.border }}>
            <Text style={{ color: theme.colors.primary, fontWeight: '800' }}>{(item.icon ?? item.label).slice(0, 1).toUpperCase()}</Text>
          </View>
          <Text numberOfLines={2} style={{ color: theme.colors.textPrimary, fontSize: 12, textAlign: 'center', marginTop: 6 }}>{item.label}</Text>
        </View>
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
  return (
    <View style={{ gap: theme.spacing.sm, marginBottom: theme.spacing.md }}>
      {block.props.items.map((item) => (
        <View key={item.id} style={{ borderRadius: theme.radius.card, padding: theme.spacing.md, backgroundColor: theme.colors.surface }}>
          <View style={{ flexDirection: 'row', alignItems: 'center' }}>
            <Text style={{ flex: 1, color: theme.colors.textPrimary, fontWeight: '700' }}>{item.name}</Text>
            {item.openNow !== undefined ? <Text style={{ color: item.openNow ? theme.colors.primary : theme.colors.textSecondary, fontSize: 12 }}>{item.openNow ? 'Відчинено' : 'Зачинено'}</Text> : null}
          </View>
          {item.address ? <Text style={{ color: theme.colors.textSecondary, marginTop: 5 }}>{item.address}</Text> : null}
          {item.distanceKm !== undefined ? <Text style={{ color: theme.colors.textSecondary, fontSize: 12, marginTop: 4 }}>{item.distanceKm.toFixed(1)} км</Text> : null}
        </View>
      ))}
    </View>
  );
}
