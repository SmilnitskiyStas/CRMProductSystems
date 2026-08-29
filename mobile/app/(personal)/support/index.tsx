import { useState } from 'react';
import {
  ActivityIndicator, Alert, FlatList, KeyboardAvoidingView, Modal, Platform,
  ScrollView, Text, TextInput, TouchableOpacity, View,
} from 'react-native';
import { SafeAreaView, useSafeAreaInsets } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useLoyaltyUiStore } from '@/features/loyalty/store';
import { useConsumerTickets, useCreateConsumerTicket } from '@/features/consumer-support/hooks';
import { useAndroidKeyboardInset } from '@/features/keyboard/useAndroidKeyboardInset';

const STATUS: Record<string, string> = {
  open: 'Відкрито', in_progress: 'Опрацьовується', resolved: 'Вирішено', closed: 'Закрито',
};

export default function ConsumerSupportScreen() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const keyboardInset = useAndroidKeyboardInset();
  const tenantId = useLoyaltyUiStore((state) => state.selectedTenantId);
  const tickets = useConsumerTickets(tenantId);
  const create = useCreateConsumerTicket();
  const [open, setOpen] = useState(false);
  const [subject, setSubject] = useState('');
  const [body, setBody] = useState('');

  function close() { if (!create.isPending) setOpen(false); }
  function submit() {
    if (!tenantId || !subject.trim() || !body.trim()) return Alert.alert('Заповніть тему та повідомлення');
    create.mutate({ tenantId, subject: subject.trim(), body: body.trim() }, {
      onSuccess: (ticket) => { setOpen(false); setSubject(''); setBody(''); router.push(`/(personal)/support/${ticket.id}`); },
      onError: () => Alert.alert('Помилка', 'Не вдалося створити звернення.'),
    });
  }

  return <SafeAreaView className="flex-1 bg-gray-50">
    <View className="flex-row items-center bg-white px-5 py-4"><TouchableOpacity onPress={() => router.back()}><Ionicons name="arrow-back" size={22} /></TouchableOpacity><Text className="ml-3 flex-1 text-xl font-bold">Звернення</Text><TouchableOpacity disabled={!tenantId} onPress={() => setOpen(true)} className="h-10 w-10 items-center justify-center rounded-full bg-green-600"><Ionicons name="add" color="white" size={24} /></TouchableOpacity></View>
    {!tenantId ? <View className="flex-1 items-center justify-center px-6"><Text className="text-center text-gray-500">Спочатку оберіть мережу в гаманці.</Text></View> : tickets.isLoading ? <ActivityIndicator className="mt-16" color="#16a34a" /> : <FlatList data={tickets.data?.items ?? []} keyExtractor={(item) => item.id} contentContainerClassName="gap-2 p-4" ListEmptyComponent={<Text className="mt-16 text-center text-gray-400">Звернень ще немає</Text>} renderItem={({ item }) => <TouchableOpacity onPress={() => router.push(`/(personal)/support/${item.id}`)} className="rounded-xl bg-white p-4"><View className="flex-row justify-between"><Text className="mr-2 flex-1 font-semibold">{item.subject}</Text><Text className="text-xs text-green-700">{STATUS[item.status] ?? item.status}</Text></View><Text className="mt-2 text-xs text-gray-400">{new Date(item.updatedAt).toLocaleString('uk-UA')}</Text></TouchableOpacity>} />}

    <Modal visible={open} transparent animationType="slide" onRequestClose={close} statusBarTranslucent>
      <KeyboardAvoidingView
        className="flex-1 justify-end bg-black/40"
        behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
        keyboardVerticalOffset={Platform.OS === 'ios' ? 0 : 12}
      >
        <TouchableOpacity activeOpacity={1} onPress={close} className="flex-1" />
        <View
          className="max-h-[88%] rounded-t-[32px] bg-white px-5 pt-4"
          style={{
            paddingBottom: Math.max(insets.bottom, 20),
            marginBottom: Platform.OS === 'android' ? keyboardInset : 0,
          }}
        >
          <View className="mb-4 h-1 w-10 self-center rounded-full bg-gray-300" />
          <View className="mb-4 flex-row items-center"><View className="flex-1"><Text className="text-2xl font-bold text-gray-900">Нове звернення</Text><Text className="mt-1 text-sm text-gray-500">Магазин відповість вам у цьому розділі</Text></View><TouchableOpacity onPress={close} className="h-10 w-10 items-center justify-center rounded-full bg-gray-100"><Ionicons name="close" size={20} color="#374151" /></TouchableOpacity></View>
          <ScrollView keyboardShouldPersistTaps="handled" keyboardDismissMode="interactive" showsVerticalScrollIndicator={false} contentContainerStyle={{ paddingBottom: 4 }}>
            <Text className="mb-1.5 ml-1 text-xs font-semibold text-gray-500">Тема</Text>
            <TextInput value={subject} onChangeText={setSubject} returnKeyType="next" placeholder="Коротко опишіть питання" className="rounded-2xl border border-gray-200 bg-gray-50 px-4 py-3.5 text-gray-900" />
            <Text className="mb-1.5 ml-1 mt-4 text-xs font-semibold text-gray-500">Повідомлення</Text>
            <TextInput value={body} onChangeText={setBody} placeholder="Розкажіть детальніше, чим можемо допомогти" multiline textAlignVertical="top" scrollEnabled className="min-h-32 max-h-52 rounded-2xl border border-gray-200 bg-gray-50 px-4 py-3.5 text-gray-900" />
            <TouchableOpacity onPress={submit} disabled={create.isPending || !subject.trim() || !body.trim()} className={`mt-5 items-center rounded-2xl py-4 ${subject.trim() && body.trim() ? 'bg-green-600' : 'bg-gray-200'}`}>
              {create.isPending ? <ActivityIndicator color="white" /> : <Text className={`font-bold ${subject.trim() && body.trim() ? 'text-white' : 'text-gray-400'}`}>Надіслати звернення</Text>}
            </TouchableOpacity>
          </ScrollView>
        </View>
      </KeyboardAvoidingView>
    </Modal>
  </SafeAreaView>;
}
