import { ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useMobileConfig } from '@/features/mobile-config/MobileConfigProvider';
import { PageRenderer } from './PageRenderer';

export function ConfiguredRetailPage({ pageKey, title }: { pageKey: string; title: string }) {
  const { config } = useMobileConfig();
  const hasBlocks = Boolean(config.pages[pageKey]?.blocks.length);

  return (
    <SafeAreaView className="flex-1 bg-gray-50" edges={['top', 'left', 'right']}>
      <ScrollView contentContainerStyle={{ padding: 16, paddingBottom: 40 }}>
        <Text className="text-3xl font-bold text-gray-900">{title}</Text>
        <View className="mt-4">
          <PageRenderer pageKey={pageKey} />
        </View>
        {!hasBlocks ? (
          <View className="mt-8 rounded-3xl bg-white px-6 py-10">
            <Text className="text-center text-lg font-bold text-gray-900">Сторінка налаштовується</Text>
            <Text className="mt-2 text-center text-sm leading-6 text-gray-500">
              Контент з’явиться після публікації конфігурації магазину.
            </Text>
          </View>
        ) : null}
      </ScrollView>
    </SafeAreaView>
  );
}
