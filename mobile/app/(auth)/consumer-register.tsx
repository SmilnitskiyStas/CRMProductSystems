import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  KeyboardAvoidingView,
  Platform,
  ActivityIndicator,
  ScrollView,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useForm, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useRouter } from 'expo-router';
import { useConsumerRegister } from '@/features/auth/hooks/useConsumerAuth';
import {
  consumerRegisterSchema,
  type ConsumerRegisterFormData,
} from '@/features/auth/validation';

export default function ConsumerRegisterScreen() {
  const router = useRouter();
  const { mutate, isPending, error } = useConsumerRegister();

  const { control, handleSubmit, formState: { errors } } = useForm<ConsumerRegisterFormData>({
    resolver: zodResolver(consumerRegisterSchema),
    defaultValues: { fullName: '', phone: '', email: '', password: '' },
  });

  const axiosErr = error as { response?: { status?: number; data?: { error?: string } } } | null;
  const serverStatus = axiosErr?.response?.status;
  const serverMessage = axiosErr?.response?.data?.error;

  return (
    <SafeAreaView className="flex-1 bg-white">
      <KeyboardAvoidingView behavior={Platform.OS === 'ios' ? 'padding' : 'height'} className="flex-1">
        <ScrollView contentContainerStyle={{ flexGrow: 1, justifyContent: 'center' }} keyboardShouldPersistTaps="handled" className="px-6">
          <View className="mb-8">
            <Text className="text-3xl font-bold text-gray-900">Реєстрація</Text>
            <Text className="text-base text-gray-500 mt-2">Створіть свій акаунт ShelfGuard</Text>
          </View>

          <View className="gap-4">
            <View>
              <Text className="text-sm font-medium text-gray-700 mb-1.5">Ім&apos;я та прізвище</Text>
              <Controller
                control={control}
                name="fullName"
                render={({ field: { onChange, onBlur, value } }) => (
                  <TextInput
                    className="border border-gray-300 rounded-xl px-4 py-3 text-base text-gray-900 bg-gray-50"
                    placeholder="Іван Іваненко"
                    autoComplete="name"
                    value={value}
                    onBlur={onBlur}
                    onChangeText={onChange}
                  />
                )}
              />
              {errors.fullName && <Text className="text-red-500 text-xs mt-1">{errors.fullName.message}</Text>}
            </View>

            <View>
              <Text className="text-sm font-medium text-gray-700 mb-1.5">Телефон</Text>
              <Controller
                control={control}
                name="phone"
                render={({ field: { onChange, onBlur, value } }) => (
                  <TextInput
                    className="border border-gray-300 rounded-xl px-4 py-3 text-base text-gray-900 bg-gray-50"
                    placeholder="+380XXXXXXXXX"
                    keyboardType="phone-pad"
                    autoComplete="tel"
                    value={value}
                    onBlur={onBlur}
                    onChangeText={onChange}
                  />
                )}
              />
              {errors.phone && <Text className="text-red-500 text-xs mt-1">{errors.phone.message}</Text>}
            </View>

            <View>
              <Text className="text-sm font-medium text-gray-700 mb-1.5">Email (необов&apos;язково)</Text>
              <Controller
                control={control}
                name="email"
                render={({ field: { onChange, onBlur, value } }) => (
                  <TextInput
                    className="border border-gray-300 rounded-xl px-4 py-3 text-base text-gray-900 bg-gray-50"
                    placeholder="you@example.com"
                    keyboardType="email-address"
                    autoCapitalize="none"
                    autoComplete="email"
                    value={value}
                    onBlur={onBlur}
                    onChangeText={onChange}
                  />
                )}
              />
              {errors.email && <Text className="text-red-500 text-xs mt-1">{errors.email.message}</Text>}
            </View>

            <View>
              <Text className="text-sm font-medium text-gray-700 mb-1.5">Пароль</Text>
              <Controller
                control={control}
                name="password"
                render={({ field: { onChange, onBlur, value } }) => (
                  <TextInput
                    className="border border-gray-300 rounded-xl px-4 py-3 text-base text-gray-900 bg-gray-50"
                    placeholder="••••••••••••"
                    secureTextEntry
                    autoComplete="password-new"
                    value={value}
                    onBlur={onBlur}
                    onChangeText={onChange}
                  />
                )}
              />
              <Text className="text-gray-400 text-xs mt-1">Щонайменше 12 символів, літера і цифра</Text>
              {errors.password && <Text className="text-red-500 text-xs mt-1">{errors.password.message}</Text>}
            </View>

            {error && (
              <Text className="text-red-500 text-sm text-center">
                {serverStatus === 409
                  ? 'Такий номер телефону вже зареєстровано.'
                  : serverMessage ?? 'Не вдалося зареєструватися. Спробуйте ще раз.'}
              </Text>
            )}

            <TouchableOpacity
              onPress={handleSubmit((data) => mutate({ ...data, email: data.email || undefined }))}
              disabled={isPending}
              className="bg-primary-600 rounded-xl py-4 items-center mt-2"
            >
              {isPending ? (
                <ActivityIndicator color="white" />
              ) : (
                <Text className="text-white font-semibold text-base">Зареєструватися</Text>
              )}
            </TouchableOpacity>

            <TouchableOpacity onPress={() => router.back()} className="items-center mt-2">
              <Text className="text-primary-600 text-sm font-medium">Вже є акаунт? Увійти</Text>
            </TouchableOpacity>
          </View>
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}
