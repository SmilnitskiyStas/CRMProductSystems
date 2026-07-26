import { Stack } from 'expo-router';

export default function AuthLayout() {
  return (
    <Stack screenOptions={{ headerShown: false }}>
      <Stack.Screen name="select-role" />
      <Stack.Screen name="login" />
      <Stack.Screen name="consumer-login" />
      <Stack.Screen name="consumer-register" />
    </Stack>
  );
}
