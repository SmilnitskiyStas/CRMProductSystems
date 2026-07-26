import { Stack } from 'expo-router';

export default function PosLayout() {
  return (
    <Stack
      screenOptions={{
        headerShown: false,
        animation: 'slide_from_right',
      }}
    >
      <Stack.Screen name="index" />
      <Stack.Screen name="scanner" />
      <Stack.Screen name="loyalty" />
      <Stack.Screen name="payment" />
      <Stack.Screen name="receipt" />
    </Stack>
  );
}
