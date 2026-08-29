import { useCallback, useState, type ComponentProps, type ReactNode } from 'react';
import { ActivityIndicator, Alert, KeyboardAvoidingView, Modal, Platform, ScrollView, Text, TextInput, TouchableOpacity, View } from 'react-native';
import { SafeAreaView, useSafeAreaInsets } from 'react-native-safe-area-context';
import { useFocusEffect, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useAuthStore } from '@/features/auth/store';
import { terminateSession } from '@/features/auth/session';
import { useActiveTenantStore } from '@/features/tenant/store';
import { useRetailTheme } from '@/features/theme/RetailThemeProvider';
import { useChangeConsumerPhone, useConsumerProfile, useConsumerProfileHistory, useUpdateConsumerProfile } from '@/features/consumer-profile/hooks';
import { useLoyaltyTierProgress, useMemberships } from '@/features/loyalty/hooks/useLoyalty';
import { useLoyaltyUiStore } from '@/features/loyalty/store';
import { useAndroidKeyboardInset } from '@/features/keyboard/useAndroidKeyboardInset';

type IconName = ComponentProps<typeof Ionicons>['name'];
const errorText = (error: unknown) => (error as { response?: { data?: { error?: string } } })?.response?.data?.error ?? 'Перевірте дані та спробуйте ще раз.';

function MenuItem({ icon, iconBg, iconColor, title, subtitle, onPress, right }: { icon: IconName; iconBg: string; iconColor: string; title: string; subtitle?: string; onPress: () => void; right?: ReactNode }) {
  return <TouchableOpacity accessibilityRole="button" onPress={onPress} className="flex-row items-center px-4 py-3.5">
    <View className="h-11 w-11 items-center justify-center rounded-2xl" style={{ backgroundColor: iconBg }}><Ionicons name={icon} size={21} color={iconColor} /></View>
    <View className="ml-3 flex-1"><Text className="text-[15px] font-semibold text-gray-900">{title}</Text>{subtitle ? <Text className="mt-0.5 text-xs leading-4 text-gray-500">{subtitle}</Text> : null}</View>
    {right ?? <Ionicons name="chevron-forward" size={19} color="#9ca3af" />}
  </TouchableOpacity>;
}
function Section({ title, children }: { title: string; children: ReactNode }) { return <View className="mt-6"><Text className="mb-2 px-1 text-xs font-bold uppercase tracking-wider text-gray-400">{title}</Text><View className="overflow-hidden rounded-3xl border border-gray-100 bg-white">{children}</View></View>; }
function Divider() { return <View className="ml-[68px] h-px bg-gray-100" />; }

export default function PersonalAccountScreen() {
  const router = useRouter();
  const theme = useRetailTheme();
  const workspaceAccessToken = useAuthStore((s) => s.workspaceAccessToken);
  const staffUser = useAuthStore((s) => s.user);
  const consumerUser = useAuthStore((s) => s.consumerUser);
  const setConsumerUser = useAuthStore((s) => s.setConsumerUser);
  const resetActiveTenant = useActiveTenantStore((s) => s.reset);
  const selectedTenantId = useLoyaltyUiStore((s) => s.selectedTenantId);
  const memberships = useMemberships(Boolean(consumerUser));
  const tier = useLoyaltyTierProgress(selectedTenantId);
  const refetchTier = tier.refetch;
  const profile = useConsumerProfile(Boolean(consumerUser));
  const history = useConsumerProfileHistory(Boolean(consumerUser));
  const updateProfile = useUpdateConsumerProfile();
  const changePhone = useChangeConsumerPhone();
  const [isLoggingOut, setIsLoggingOut] = useState(false);
  const [editOpen, setEditOpen] = useState(false);
  const [phoneOpen, setPhoneOpen] = useState(false);
  const [fullName, setFullName] = useState(''); const [email, setEmail] = useState('');
  const [phone, setPhone] = useState(''); const [password, setPassword] = useState('');
  const displayName = profile.data?.fullName ?? staffUser?.fullName ?? consumerUser?.fullName ?? 'Користувач';
  const displayContact = profile.data?.email || profile.data?.phone || staffUser?.email || consumerUser?.phone || '';
  const totalBalance = (memberships.data ?? []).reduce((sum, item) => sum + item.balance, 0);
  const initials = displayName.trim().split(/\s+/).slice(0, 2).map((part) => part[0]).join('').toUpperCase() || '?';

  useFocusEffect(useCallback(() => {
    if (selectedTenantId) void refetchTier();
  }, [selectedTenantId, refetchTier]));

  function logout() { Alert.alert('Вийти з акаунта?', 'Локальні дані сесії буде очищено.', [{ text: 'Скасувати', style: 'cancel' }, { text: 'Вийти', style: 'destructive', onPress: async () => { setIsLoggingOut(true); await resetActiveTenant(); await terminateSession(); router.replace('/(auth)/select-role'); } }]); }
  function openEdit() { setFullName(profile.data?.fullName ?? displayName); setEmail(profile.data?.email ?? ''); setEditOpen(true); }

  return <SafeAreaView className="flex-1 bg-gray-50">
    <ScrollView showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 44 }}>
      <View className="overflow-hidden rounded-b-[36px] px-5 pb-7 pt-4" style={{ backgroundColor: theme.colors.primary }}>
        <View className="flex-row items-center justify-between"><View><Text className="text-xs font-bold uppercase tracking-[2px] text-white/70">Особистий простір</Text><Text className="mt-1 text-2xl font-bold text-white">Мій профіль</Text></View><TouchableOpacity onPress={openEdit} className="h-11 w-11 items-center justify-center rounded-2xl bg-white/15"><Ionicons name="create-outline" size={22} color="white" /></TouchableOpacity></View>
        <View className="mt-6 flex-row items-center"><View className="h-20 w-20 items-center justify-center rounded-[28px] border-2 border-white/40 bg-white/20"><Text className="text-2xl font-bold text-white">{initials}</Text></View><View className="ml-4 flex-1"><Text className="text-xl font-bold text-white" numberOfLines={2}>{displayName}</Text><Text className="mt-1 text-sm text-white/75" numberOfLines={1}>{displayContact}</Text><View className="mt-2 self-start rounded-full bg-white/15 px-3 py-1"><Text className="text-xs font-semibold text-white">{workspaceAccessToken ? 'Клієнт · Працівник' : 'Клієнт'}</Text></View></View></View>
      </View>

      {consumerUser ? <View className="mx-5 -mt-1 flex-row rounded-3xl border border-gray-100 bg-white px-2 py-4 shadow-sm"><Stat value={String(memberships.data?.length ?? 0)} label="Мої мережі" border /><Stat value={totalBalance.toFixed(2)} label="Бонусів" color="#15803d" border /><Stat value={tier.data?.currentTierName ?? (tier.data?.nextTierName ? 'Не присвоєно' : 'Не налаштовано')} label="Мій ранг" color="#b45309" onPress={() => router.push('/(personal)/tier-progress')} /></View> : null}

      <View className="px-5">
        {consumerUser ? <Section title="Особисті дані"><MenuItem icon="person-outline" iconBg="#ecfdf5" iconColor="#15803d" title="Ім’я та email" subtitle={profile.data?.email ?? 'Email не вказано'} onPress={openEdit} /><Divider /><MenuItem icon="phone-portrait-outline" iconBg="#eff6ff" iconColor="#2563eb" title="Номер телефону" subtitle={profile.data?.phone ?? consumerUser.phone} onPress={() => { setPhone(profile.data?.phone ?? consumerUser.phone); setPhoneOpen(true); }} /></Section> : null}

        <Section title="Покупки та взаємодія"><MenuItem icon="storefront-outline" iconBg="#f0fdf4" iconColor="#16a34a" title="Мої магазини" subtitle="Мережі, торгові точки та бонусні програми" onPress={() => router.push('/(personal)/retailers')} /><Divider /><MenuItem icon="chatbubbles-outline" iconBg="#faf5ff" iconColor="#9333ea" title="Звернення до магазину" subtitle="Поставити питання або переглянути відповідь" onPress={() => router.push('/(personal)/support')} /><Divider /><MenuItem icon="time-outline" iconBg="#fff7ed" iconColor="#ea580c" title="Історія покупок і бонусів" subtitle="Нарахування, списання та відгуки" onPress={() => router.push('/(personal)/history')} /></Section>

        {workspaceAccessToken ? <Section title="Для працівника"><MenuItem icon="briefcase-outline" iconBg="#eef2ff" iconColor="#4f46e5" title="Робочий простір" subtitle="Залишки, приймання та інші операції" onPress={() => router.replace('/(app)')} right={<View className="rounded-full bg-indigo-50 px-3 py-1.5"><Text className="text-xs font-bold text-indigo-700">Відкрити</Text></View>} /></Section> : null}

        {history.data?.items.length ? <Section title="Безпека профілю"><View className="p-4"><View className="flex-row items-center"><Ionicons name="shield-checkmark-outline" size={20} color="#16a34a" /><Text className="ml-2 font-semibold text-gray-900">Останні зміни</Text></View>{history.data.items.slice(0, 3).map((item, index) => <View key={`${item.changedAt}-${index}`} className="mt-3 flex-row items-center justify-between"><Text className="text-sm text-gray-600">{item.fieldName === 'full_name' ? 'Ім’я' : item.fieldName === 'phone' ? 'Телефон' : item.fieldName === 'email' ? 'Email' : item.fieldName}</Text><Text className="text-xs text-gray-400">{new Date(item.changedAt).toLocaleDateString('uk-UA')}</Text></View>)}</View></Section> : null}

        <TouchableOpacity onPress={logout} disabled={isLoggingOut} className="mt-7 flex-row items-center justify-center rounded-2xl border border-red-100 bg-red-50 py-4">{isLoggingOut ? <ActivityIndicator color="#dc2626" /> : <><Ionicons name="log-out-outline" size={20} color="#dc2626" /><Text className="ml-2 font-bold text-red-600">Вийти з акаунта</Text></>}</TouchableOpacity>
        <Text className="mt-4 text-center text-[11px] text-gray-400">ShelfGuard · персональні дані захищено</Text>
      </View>
    </ScrollView>

    <EditSheet visible={editOpen} title="Особисті дані" onClose={() => setEditOpen(false)}><Field label="Ім’я" icon="person-outline"><TextInput value={fullName} onChangeText={setFullName} placeholder="Ваше ім’я" className="flex-1 py-3 text-gray-900" /></Field><Field label="Електронна пошта" icon="mail-outline"><TextInput value={email} onChangeText={setEmail} autoCapitalize="none" keyboardType="email-address" placeholder="email@example.com" className="flex-1 py-3 text-gray-900" /></Field><TouchableOpacity disabled={updateProfile.isPending || !fullName.trim()} onPress={() => updateProfile.mutate({ fullName: fullName.trim(), email: email.trim() }, { onSuccess: (updated) => { if (consumerUser) setConsumerUser({ ...consumerUser, fullName: updated.fullName }); setEditOpen(false); }, onError: (error) => Alert.alert('Не вдалося зберегти', errorText(error)) })} className="mt-2 items-center rounded-2xl bg-green-600 py-4">{updateProfile.isPending ? <ActivityIndicator color="white" /> : <Text className="font-bold text-white">Зберегти зміни</Text>}</TouchableOpacity></EditSheet>

    <EditSheet visible={phoneOpen} title="Зміна телефону" description="Для безпеки підтвердьте зміну поточним паролем." onClose={() => { setPhoneOpen(false); setPassword(''); }}><Field label="Новий номер" icon="call-outline"><TextInput value={phone} onChangeText={setPhone} keyboardType="phone-pad" placeholder="+380…" className="flex-1 py-3 text-gray-900" /></Field><Field label="Поточний пароль" icon="lock-closed-outline"><TextInput value={password} onChangeText={setPassword} secureTextEntry placeholder="Введіть пароль" className="flex-1 py-3 text-gray-900" /></Field><TouchableOpacity disabled={changePhone.isPending || !phone.trim() || !password} onPress={() => changePhone.mutate({ newPhone: phone.trim(), currentPassword: password }, { onSuccess: (updated) => { if (consumerUser) setConsumerUser({ ...consumerUser, phone: updated.phone }); setPhoneOpen(false); setPassword(''); }, onError: (error) => Alert.alert('Не вдалося змінити номер', errorText(error)) })} className="mt-2 items-center rounded-2xl bg-green-600 py-4">{changePhone.isPending ? <ActivityIndicator color="white" /> : <Text className="font-bold text-white">Підтвердити зміну</Text>}</TouchableOpacity></EditSheet>
  </SafeAreaView>;
}

function Stat({ value, label, color = '#111827', border = false, onPress }: { value: string; label: string; color?: string; border?: boolean; onPress?: () => void }) {
  const content = <><Text className="max-w-full text-base font-bold" style={{ color }} numberOfLines={1}>{value}</Text><View className="mt-0.5 flex-row items-center"><Text className="text-[11px] text-gray-500">{label}</Text>{onPress ? <Ionicons name="chevron-forward" size={11} color="#9ca3af" /> : null}</View></>;
  return onPress
    ? <TouchableOpacity accessibilityRole="button" accessibilityLabel={`${label}: ${value}. Переглянути прогрес`} onPress={onPress} className={`flex-1 items-center ${border ? 'border-r border-gray-100' : ''}`}>{content}</TouchableOpacity>
    : <View className={`flex-1 items-center ${border ? 'border-r border-gray-100' : ''}`}>{content}</View>;
}
function EditSheet({ visible, title, description, onClose, children }: { visible: boolean; title: string; description?: string; onClose: () => void; children: ReactNode }) {
  const insets = useSafeAreaInsets();
  const keyboardInset = useAndroidKeyboardInset();

  return <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
    <KeyboardAvoidingView className="flex-1 justify-end bg-black/40" behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
      <TouchableOpacity activeOpacity={1} onPress={onClose} className="flex-1" />
      <View
        className="max-h-[88%] rounded-t-[32px] bg-white px-5 pt-4"
        style={{
          paddingBottom: Math.max(insets.bottom, 20),
          marginBottom: Platform.OS === 'android' ? keyboardInset : 0,
        }}
      >
        <View className="mb-4 h-1 w-10 self-center rounded-full bg-gray-300" />
        <View className="mb-5 flex-row items-start"><View className="flex-1"><Text className="text-2xl font-bold text-gray-900">{title}</Text>{description ? <Text className="mt-1 text-sm leading-5 text-gray-500">{description}</Text> : null}</View><TouchableOpacity onPress={onClose} className="h-10 w-10 items-center justify-center rounded-full bg-gray-100"><Ionicons name="close" size={20} color="#374151" /></TouchableOpacity></View>
        <ScrollView keyboardShouldPersistTaps="handled" keyboardDismissMode="interactive" showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 4 }}>
          <View className="gap-3">{children}</View>
        </ScrollView>
      </View>
    </KeyboardAvoidingView>
  </Modal>;
}
function Field({ label, icon, children }: { label: string; icon: IconName; children: ReactNode }) { return <View><Text className="mb-1.5 ml-1 text-xs font-semibold text-gray-500">{label}</Text><View className="flex-row items-center rounded-2xl border border-gray-200 bg-gray-50 px-3"><Ionicons name={icon} size={19} color="#6b7280" /><View className="ml-2 flex-1">{children}</View></View></View>; }
