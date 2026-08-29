import { useEffect, useRef } from 'react';
import { Animated, Easing, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';

export function MobileAppLoadingScreen() {
  const progress = useRef(new Animated.Value(0)).current;
  useEffect(() => {
    const animation = Animated.loop(Animated.sequence([
      Animated.timing(progress, { toValue: 1, duration: 900, easing: Easing.inOut(Easing.ease), useNativeDriver: false }),
      Animated.timing(progress, { toValue: 0.18, duration: 500, easing: Easing.inOut(Easing.ease), useNativeDriver: false }),
    ]));
    animation.start();
    return () => animation.stop();
  }, [progress]);
  const width = progress.interpolate({ inputRange: [0, 1], outputRange: ['12%', '100%'] });
  return <View className="flex-1 items-center justify-center bg-white px-10">
    <View className="h-20 w-20 items-center justify-center rounded-3xl bg-green-50"><Ionicons name="storefront" size={38} color="#16a34a" /></View>
    <Text className="mt-6 text-xl font-bold text-gray-900">Завантажуємо застосунок</Text>
    <Text className="mt-2 text-center text-sm text-gray-500">Отримуємо дизайн і налаштування вашого магазину</Text>
    <View className="mt-7 h-2 w-full overflow-hidden rounded-full bg-gray-100"><Animated.View className="h-2 rounded-full bg-green-600" style={{ width }} /></View>
  </View>;
}
