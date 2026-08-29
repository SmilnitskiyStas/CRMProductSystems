import { useState } from 'react';
import { ActivityIndicator, FlatList, KeyboardAvoidingView, Platform, Text, TextInput, TouchableOpacity, View } from 'react-native';
import { SafeAreaView, useSafeAreaInsets } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useAddConsumerMessage, useConsumerTicket } from '@/features/consumer-support/hooks';
import { useAndroidKeyboardInset } from '@/features/keyboard/useAndroidKeyboardInset';
import { useConsumerSupportRealtime } from '@/features/consumer-support/realtime';

const STATUS: Record<string, string> = { open: 'Відкрито', in_progress: 'Опрацьовується', resolved: 'Вирішено', closed: 'Закрито' };

export default function TicketScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const keyboardInset = useAndroidKeyboardInset();
  const ticket = useConsumerTicket(id);
  const add = useAddConsumerMessage(id);
  useConsumerSupportRealtime(id);
  const [body, setBody] = useState('');
  if (ticket.isLoading) return <SafeAreaView className="flex-1 items-center justify-center"><ActivityIndicator color="#16a34a" /></SafeAreaView>;
  if (!ticket.data) return <SafeAreaView className="flex-1 items-center justify-center"><Text>Звернення не знайдено</Text></SafeAreaView>;
  const closed = ['resolved', 'closed'].includes(ticket.data.status);

  return <SafeAreaView className="flex-1 bg-gray-50" edges={['top', 'left', 'right']}>
    <KeyboardAvoidingView
      className="flex-1"
      behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      keyboardVerticalOffset={0}
      style={{ marginBottom: Platform.OS === 'android' ? keyboardInset : 0 }}
    >
      <View className="flex-row items-center border-b border-gray-100 bg-white p-4"><TouchableOpacity onPress={() => router.back()} className="h-10 w-10 items-center justify-center rounded-full bg-gray-100"><Ionicons name="arrow-back" size={21} /></TouchableOpacity><View className="ml-3 flex-1"><Text className="font-bold text-gray-900" numberOfLines={1}>{ticket.data.subject}</Text><Text className="mt-0.5 text-xs text-green-700">{STATUS[ticket.data.status] ?? ticket.data.status}</Text></View></View>
      <FlatList
        inverted
        data={[...(ticket.data.messages ?? [])].reverse()}
        keyExtractor={(message) => message.id}
        keyboardDismissMode="interactive"
        keyboardShouldPersistTaps="handled"
        contentContainerClassName="gap-2 p-4"
        renderItem={({ item }) => <View className={`max-w-[85%] rounded-2xl p-3 ${item.senderConsumerAccountId ? 'self-end bg-green-100' : 'self-start bg-white'}`}><Text className="leading-5 text-gray-900">{item.body}</Text><Text className="mt-1 text-[10px] text-gray-400">{new Date(item.createdAt).toLocaleString('uk-UA')}</Text></View>}
      />
      {!closed ? <View className="flex-row items-end border-t border-gray-100 bg-white px-3 pt-3" style={{ paddingBottom: Math.max(insets.bottom, 12) }}><TextInput value={body} onChangeText={setBody} placeholder="Напишіть повідомлення…" multiline textAlignVertical="center" className="max-h-28 min-h-12 flex-1 rounded-2xl border border-gray-200 bg-gray-50 px-4 py-3 text-gray-900" /><TouchableOpacity disabled={!body.trim() || add.isPending} onPress={() => add.mutate(body.trim(), { onSuccess: () => setBody('') })} className={`ml-2 h-12 w-12 items-center justify-center rounded-2xl ${body.trim() ? 'bg-green-600' : 'bg-gray-200'}`}>{add.isPending ? <ActivityIndicator size="small" color="white" /> : <Ionicons name="send" color={body.trim() ? 'white' : '#9ca3af'} size={20} />}</TouchableOpacity></View> : <View className="border-t border-gray-100 bg-white p-4" style={{ paddingBottom: Math.max(insets.bottom, 16) }}><Text className="text-center text-sm text-gray-500">Звернення закрито</Text></View>}
    </KeyboardAvoidingView>
  </SafeAreaView>;
}
