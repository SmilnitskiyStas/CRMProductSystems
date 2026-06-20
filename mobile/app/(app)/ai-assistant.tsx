import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  FlatList,
  KeyboardAvoidingView,
  Platform,
  ActivityIndicator,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { useRouter } from 'expo-router';
import { useRef, useState } from 'react';
import { useAiAssistant } from '@/features/ai-assistant/hooks/useAiAssistant';
import type { AiAssistantContextSummary, ChatMessage } from '@/features/ai-assistant/types';

// ── Context badges ─────────────────────────────────────────────────────────────

const CONTEXT_ITEMS: {
  key: keyof AiAssistantContextSummary;
  label: (n: number) => string;
  color: string;
}[] = [
  { key: 'criticalStockBatchesCount', label: (n) => `${n} критичних партій`, color: '#ef4444' },
  { key: 'pendingOrdersCount',        label: (n) => `${n} замовлень`,         color: '#f59e0b' },
  { key: 'salesDaysCount',            label: (n) => `${n} рядків продажів`,   color: '#3b82f6' },
  { key: 'activeSuppliersCount',      label: (n) => `${n} постачальників`,    color: '#10b981' },
];

function ContextBadges({ context }: { context: AiAssistantContextSummary }) {
  const visible = CONTEXT_ITEMS.filter((ci) => context[ci.key] > 0);
  if (visible.length === 0) return null;
  return (
    <View className="flex-row flex-wrap gap-1 mt-1.5 ml-1">
      {visible.map((ci) => (
        <View
          key={ci.key}
          className="rounded-full px-2 py-0.5"
          style={{ backgroundColor: `${ci.color}20`, borderWidth: 1, borderColor: `${ci.color}40` }}
        >
          <Text style={{ color: ci.color, fontSize: 10 }}>{ci.label(context[ci.key])}</Text>
        </View>
      ))}
    </View>
  );
}

// ── Typing indicator ───────────────────────────────────────────────────────────

function TypingIndicator() {
  return (
    <View className="flex-row items-center gap-2 px-4 py-2">
      <View
        className="rounded-2xl px-4 py-3 flex-row items-center gap-2"
        style={{ backgroundColor: '#1f2937', maxWidth: '75%' }}
      >
        <ActivityIndicator size="small" color="#6b7280" />
        <Text style={{ color: '#9ca3af', fontSize: 13 }}>AI аналізує дані…</Text>
      </View>
    </View>
  );
}

// ── Message bubble ─────────────────────────────────────────────────────────────

function MessageBubble({ msg }: { msg: ChatMessage }) {
  const isUser = msg.role === 'user';
  return (
    <View
      className={`px-4 py-2 ${isUser ? 'items-end' : 'items-start'}`}
    >
      <View
        className="rounded-2xl px-4 py-3"
        style={{
          backgroundColor: isUser ? '#1d4ed8' : '#1f2937',
          maxWidth: '80%',
          borderRadius: isUser ? 18 : 18,
          borderBottomRightRadius: isUser ? 4 : 18,
          borderBottomLeftRadius: isUser ? 18 : 4,
        }}
      >
        <Text style={{ color: '#e8edf5', fontSize: 14, lineHeight: 21 }}>
          {msg.text}
        </Text>
      </View>
      {!isUser && msg.context && <ContextBadges context={msg.context} />}
    </View>
  );
}

// ── Suggested questions ────────────────────────────────────────────────────────

const SUGGESTIONS = [
  'Які товари закінчуються?',
  'Що продається найкраще цього тижня?',
  'Скільки замовлень очікують підтвердження?',
  'Який стан залишків по критичних позиціях?',
];

// ── Main screen ────────────────────────────────────────────────────────────────

export default function AiAssistantScreen() {
  const router = useRouter();
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState('');
  const listRef = useRef<FlatList>(null);
  const { mutate: ask, isPending } = useAiAssistant();

  function sendMessage(text: string) {
    const trimmed = text.trim();
    if (!trimmed || isPending) return;

    const userMsg: ChatMessage = {
      id: `u-${Date.now()}`,
      role: 'user',
      text: trimmed,
    };

    setMessages((prev) => [...prev, userMsg]);
    setInput('');

    ask(
      { message: trimmed },
      {
        onSuccess: (data) => {
          const aiMsg: ChatMessage = {
            id: `a-${Date.now()}`,
            role: 'assistant',
            text: data.reply,
            context: data.context,
          };
          setMessages((prev) => [...prev, aiMsg]);
          setTimeout(() => {
            listRef.current?.scrollToEnd({ animated: true });
          }, 100);
        },
        onError: (err: unknown) => {
          const status = (err as { response?: { status?: number } })?.response?.status;
          let errText = 'Помилка зв\'язку. Спробуйте ще раз.';
          if (status === 503) errText = 'AI сервіс тимчасово недоступний. Перевірте налаштування Claude API.';
          if (status === 403) errText = 'Модуль inventory не активовано для цього тенанта.';
          const errMsg: ChatMessage = {
            id: `e-${Date.now()}`,
            role: 'assistant',
            text: errText,
          };
          setMessages((prev) => [...prev, errMsg]);
        },
      },
    );

    setTimeout(() => {
      listRef.current?.scrollToEnd({ animated: true });
    }, 50);
  }

  const allItems: (ChatMessage | { id: string; type: 'typing' })[] = isPending
    ? [...messages, { id: 'typing', type: 'typing' as const }]
    : messages;

  return (
    <SafeAreaView className="flex-1" style={{ backgroundColor: '#0d1117' }}>
      <KeyboardAvoidingView
        className="flex-1"
        behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
        keyboardVerticalOffset={Platform.OS === 'ios' ? 0 : 20}
      >
        {/* Header */}
        <View
          className="flex-row items-center px-4 py-3 gap-3"
          style={{ borderBottomWidth: 1, borderBottomColor: '#1f2937' }}
        >
          <TouchableOpacity onPress={() => router.back()}>
            <Ionicons name="arrow-back" size={24} color="#e8edf5" />
          </TouchableOpacity>
          <View className="w-9 h-9 rounded-full items-center justify-center" style={{ backgroundColor: '#1d4ed820' }}>
            <Ionicons name="sparkles-outline" size={19} color="#60a5fa" />
          </View>
          <View className="flex-1">
            <Text style={{ color: '#e8edf5', fontSize: 15, fontWeight: '600' }}>
              AI Бізнес-Асистент
            </Text>
            <Text style={{ color: '#4b5563', fontSize: 11 }}>
              Залишки · Замовлення · Продажі · Постачальники
            </Text>
          </View>
        </View>

        {/* Messages */}
        <FlatList
          ref={listRef}
          data={allItems}
          keyExtractor={(item) => item.id}
          contentContainerStyle={{ paddingVertical: 12, flexGrow: 1 }}
          onContentSizeChange={() => {
            if (messages.length > 0) {
              listRef.current?.scrollToEnd({ animated: false });
            }
          }}
          renderItem={({ item }) => {
            if ('type' in item && item.type === 'typing') {
              return <TypingIndicator />;
            }
            return <MessageBubble msg={item as ChatMessage} />;
          }}
          ListEmptyComponent={
            <View className="flex-1 items-center justify-center px-6 py-10">
              <View
                className="w-16 h-16 rounded-full items-center justify-center mb-4"
                style={{ backgroundColor: '#1f2937' }}
              >
                <Ionicons name="sparkles-outline" size={30} color="#60a5fa" />
              </View>
              <Text
                style={{ color: '#9ca3af', fontSize: 14, textAlign: 'center', marginBottom: 24 }}
              >
                Запитай про стан бізнесу. Наприклад:
              </Text>
              <View className="gap-2 w-full">
                {SUGGESTIONS.map((s) => (
                  <TouchableOpacity
                    key={s}
                    onPress={() => sendMessage(s)}
                    className="rounded-xl px-4 py-3"
                    style={{ backgroundColor: '#1f2937' }}
                  >
                    <Text style={{ color: '#9ca3af', fontSize: 13 }}>{s}</Text>
                  </TouchableOpacity>
                ))}
              </View>
            </View>
          }
        />

        {/* Input area */}
        <View
          className="flex-row items-end gap-2 px-4 py-3"
          style={{ borderTopWidth: 1, borderTopColor: '#1f2937' }}
        >
          <TextInput
            className="flex-1 rounded-xl px-4 py-3 text-sm"
            style={{
              backgroundColor: '#0d1117',
              borderWidth: 1,
              borderColor: '#374151',
              color: '#e8edf5',
              minHeight: 44,
              maxHeight: 120,
              lineHeight: 20,
            }}
            placeholder="Запитати AI..."
            placeholderTextColor="#4b5563"
            value={input}
            onChangeText={setInput}
            multiline
            textAlignVertical="center"
            editable={!isPending}
            returnKeyType="send"
            onSubmitEditing={() => sendMessage(input)}
            blurOnSubmit={false}
          />
          <TouchableOpacity
            onPress={() => sendMessage(input)}
            disabled={!input.trim() || isPending}
            className="w-11 h-11 rounded-xl items-center justify-center"
            style={{
              backgroundColor: input.trim() && !isPending ? '#1d4ed8' : '#1f2937',
            }}
          >
            <Ionicons
              name="send"
              size={18}
              color={input.trim() && !isPending ? '#ffffff' : '#4b5563'}
            />
          </TouchableOpacity>
        </View>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}
