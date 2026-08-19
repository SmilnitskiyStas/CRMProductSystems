import { ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useMobileConfig } from '@/features/mobile-config/MobileConfigProvider';
import { PageRenderer } from './PageRenderer';
import { useRetailTheme } from '@/features/theme/RetailThemeProvider';

export function ConfiguredRetailPage({ pageKey, title }: { pageKey: string; title: string }) {
  const { config } = useMobileConfig();
  const theme = useRetailTheme();
  const hasBlocks = Boolean(config.pages[pageKey]?.blocks.length);

  return (
    <SafeAreaView style={{ flex: 1, backgroundColor: theme.colors.background }} edges={['top', 'left', 'right']}>
      <ScrollView contentContainerStyle={{ padding: 16, paddingBottom: 40 }}>
        <Text className="text-3xl font-bold" style={{ color: theme.colors.textPrimary }}>{title}</Text>
        <View className="mt-4">
          <PageRenderer pageKey={pageKey} />
        </View>
        {!hasBlocks ? (
          <View className="mt-8 px-6 py-10" style={{ borderRadius: theme.radius.card, backgroundColor: theme.colors.surface }}>
            <Text className="text-center text-lg font-bold" style={{ color: theme.colors.textPrimary }}>Сторінка налаштовується</Text>
            <Text className="mt-2 text-center text-sm leading-6" style={{ color: theme.colors.textSecondary }}>
              Контент з’явиться після публікації конфігурації магазину.
            </Text>
          </View>
        ) : null}
      </ScrollView>
    </SafeAreaView>
  );
}
