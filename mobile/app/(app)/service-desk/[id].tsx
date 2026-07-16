import { useState } from 'react';
import {
  View,
  Text,
  ScrollView,
  ActivityIndicator,
  TouchableOpacity,
  Alert,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useAuthStore } from '@/features/auth/store';
import { useTicket, useUpdateTicket } from '@/features/service-desk/hooks/useServiceDesk';
import { AddCommentModal } from '@/features/service-desk/components/AddCommentModal';
import type { TicketComment } from '@/features/service-desk/types';
import { AT_LEAST_STORE_MANAGER_OR_PROVIDER, hasRole } from '@/lib/roles';

const PRIORITY_STYLES: Record<string, string> = {
  low: 'bg-gray-100 text-gray-600',
  medium: 'bg-blue-100 text-blue-700',
  high: 'bg-orange-100 text-orange-700',
  critical: 'bg-red-100 text-red-700',
};

const PRIORITY_LABELS: Record<string, string> = {
  low: 'Низький',
  medium: 'Середній',
  high: 'Високий',
  critical: 'Критичний',
};

const STATUS_STYLES: Record<string, string> = {
  open: 'bg-blue-100 text-blue-700',
  in_progress: 'bg-yellow-100 text-yellow-700',
  waiting: 'bg-purple-100 text-purple-700',
  resolved: 'bg-green-100 text-green-700',
  closed: 'bg-gray-100 text-gray-600',
};

const STATUS_LABELS: Record<string, string> = {
  open: 'Відкрито',
  in_progress: 'В роботі',
  waiting: 'Очікування',
  resolved: 'Вирішено',
  closed: 'Закрито',
};

const CATEGORY_LABELS: Record<string, string> = {
  general: 'Загальне',
  technical: 'Технічне',
  billing: 'Оплата',
  feature_request: 'Пропозиція',
  bug: 'Помилка',
};

const STATUS_OPTIONS = [
  { value: 'open', label: 'Відкрито' },
  { value: 'in_progress', label: 'В роботі' },
  { value: 'waiting', label: 'Очікування' },
  { value: 'resolved', label: 'Вирішено' },
  { value: 'closed', label: 'Закрито' },
];

function CommentItem({ comment, isManager }: { comment: TicketComment; isManager: boolean }) {
  const showInternal = comment.isInternal && isManager;
  const formattedDate = new Date(comment.createdAt).toLocaleDateString('uk-UA', {
    day: '2-digit',
    month: '2-digit',
    year: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });

  return (
    <View
      className={`bg-white rounded-xl p-3 ${
        showInternal ? 'border-l-4 border-l-orange-400 border border-orange-100' : 'border border-gray-100'
      }`}
    >
      <View className="flex-row items-center justify-between mb-1.5">
        <Text className="text-sm font-semibold text-gray-900">{comment.authorName}</Text>
        <View className="flex-row items-center gap-2">
          {showInternal && (
            <View className="bg-orange-100 px-2 py-0.5 rounded-full">
              <Text className="text-xs font-medium text-orange-700">Внутрішній</Text>
            </View>
          )}
          <Text className="text-xs text-gray-400">{formattedDate}</Text>
        </View>
      </View>
      <Text className="text-sm text-gray-700 leading-5">{comment.body}</Text>
    </View>
  );
}

export default function TicketDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const isManager = hasRole(user?.role, AT_LEAST_STORE_MANAGER_OR_PROVIDER);

  const { data, isLoading, isError } = useTicket(id);
  const updateTicket = useUpdateTicket();

  const [showAddComment, setShowAddComment] = useState(false);

  function handleChangeStatus() {
    Alert.alert(
      'Змінити статус',
      undefined,
      [
        ...STATUS_OPTIONS.map((opt) => ({
          text: opt.label,
          onPress: () => {
            updateTicket.mutate(
              { id, payload: { status: opt.value } },
              {
                onError: () => Alert.alert('Помилка', 'Не вдалося змінити статус'),
              }
            );
          },
        })),
        { text: 'Скасувати', style: 'cancel' as const },
      ]
    );
  }

  if (isLoading) {
    return (
      <SafeAreaView className="flex-1 bg-white items-center justify-center">
        <ActivityIndicator size="large" color="#16a34a" />
      </SafeAreaView>
    );
  }

  if (isError || !data) {
    return (
      <SafeAreaView className="flex-1 bg-white items-center justify-center px-4">
        <Text className="text-red-500 text-center">Не вдалося завантажити тікет</Text>
        <TouchableOpacity onPress={() => router.back()} className="mt-4">
          <Text className="text-primary-600 font-medium">Назад</Text>
        </TouchableOpacity>
      </SafeAreaView>
    );
  }

  const priorityStyle = PRIORITY_STYLES[data.priority] ?? 'bg-gray-100 text-gray-600';
  const statusStyle = STATUS_STYLES[data.status] ?? 'bg-gray-100 text-gray-600';
  const priorityLabel = PRIORITY_LABELS[data.priority] ?? data.priority;
  const statusLabel = STATUS_LABELS[data.status] ?? data.status;
  const categoryLabel = CATEGORY_LABELS[data.category] ?? data.category;

  // Filter out internal comments for non-managers
  const visibleComments = data.comments.filter(
    (c) => !c.isInternal || isManager
  );

  return (
    <SafeAreaView className="flex-1 bg-gray-50">
      {/* Header */}
      <View className="bg-white px-4 pt-4 pb-3 border-b border-gray-100">
        <View className="flex-row items-center gap-3">
          <TouchableOpacity
            onPress={() => router.back()}
            className="w-9 h-9 items-center justify-center rounded-full bg-gray-100"
          >
            <Ionicons name="arrow-back" size={20} color="#374151" />
          </TouchableOpacity>
          <Text className="flex-1 text-base font-bold text-gray-900" numberOfLines={1}>
            #{data.number} — {data.title}
          </Text>
        </View>
      </View>

      <ScrollView contentContainerClassName="p-4 gap-4">
        {/* Status + Priority badges */}
        <View className="flex-row gap-2">
          <View className={`px-3 py-1 rounded-full ${statusStyle}`}>
            <Text className="text-sm font-semibold">{statusLabel}</Text>
          </View>
          <View className={`px-3 py-1 rounded-full ${priorityStyle}`}>
            <Text className="text-sm font-semibold">{priorityLabel}</Text>
          </View>
        </View>

        {/* Details section */}
        <View className="bg-white rounded-xl p-4 gap-3">
          <Text className="text-xs font-semibold text-gray-400 uppercase tracking-wide">
            Деталі
          </Text>

          <View className="flex-row gap-2">
            <Text className="text-sm text-gray-500 w-28">Категорія</Text>
            <Text className="text-sm font-medium text-gray-900 flex-1">{categoryLabel}</Text>
          </View>

          {data.locationName && (
            <View className="flex-row gap-2">
              <Text className="text-sm text-gray-500 w-28">Локація</Text>
              <Text className="text-sm font-medium text-gray-900 flex-1">{data.locationName}</Text>
            </View>
          )}

          <View className="flex-row gap-2">
            <Text className="text-sm text-gray-500 w-28">Створив</Text>
            <Text className="text-sm font-medium text-gray-900 flex-1">{data.createdByName}</Text>
          </View>

          <View className="flex-row gap-2">
            <Text className="text-sm text-gray-500 w-28">Дата створення</Text>
            <Text className="text-sm font-medium text-gray-900 flex-1">
              {new Date(data.createdAt).toLocaleDateString('uk-UA', {
                day: '2-digit',
                month: '2-digit',
                year: 'numeric',
                hour: '2-digit',
                minute: '2-digit',
              })}
            </Text>
          </View>

          {data.assignedToName && (
            <View className="flex-row gap-2">
              <Text className="text-sm text-gray-500 w-28">Відповідальний</Text>
              <Text className="text-sm font-medium text-gray-900 flex-1">{data.assignedToName}</Text>
            </View>
          )}

          {data.resolvedAt && (
            <View className="flex-row gap-2">
              <Text className="text-sm text-gray-500 w-28">Вирішено</Text>
              <Text className="text-sm font-medium text-gray-900 flex-1">
                {new Date(data.resolvedAt).toLocaleDateString('uk-UA', {
                  day: '2-digit',
                  month: '2-digit',
                  year: 'numeric',
                  hour: '2-digit',
                  minute: '2-digit',
                })}
              </Text>
            </View>
          )}
        </View>

        {/* Description */}
        <View className="bg-white rounded-xl p-4">
          <Text className="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-2">
            Опис
          </Text>
          <Text className="text-sm text-gray-700 leading-5">{data.description}</Text>
        </View>

        {/* Comments section */}
        <View>
          <View className="flex-row items-center justify-between mb-2 px-1">
            <Text className="text-xs font-semibold text-gray-400 uppercase tracking-wide">
              Коментарі ({visibleComments.length})
            </Text>
            <TouchableOpacity
              onPress={() => setShowAddComment(true)}
              className="flex-row items-center gap-1 bg-primary-50 px-3 py-1.5 rounded-lg"
            >
              <Ionicons name="add" size={14} color="#16a34a" />
              <Text className="text-xs font-semibold text-primary-700">Коментар</Text>
            </TouchableOpacity>
          </View>

          {visibleComments.length === 0 ? (
            <View className="bg-white rounded-xl p-6 items-center">
              <Ionicons name="chatbubble-outline" size={32} color="#d1d5db" />
              <Text className="text-gray-400 text-sm mt-2">Коментарів ще немає</Text>
            </View>
          ) : (
            <View className="gap-2">
              {visibleComments.map((comment) => (
                <CommentItem key={comment.id} comment={comment} isManager={isManager} />
              ))}
            </View>
          )}
        </View>

        {/* Manager actions */}
        {isManager && (
          <TouchableOpacity
            onPress={handleChangeStatus}
            disabled={updateTicket.isPending}
            className="bg-white border border-gray-200 rounded-xl py-3.5 items-center flex-row justify-center gap-2"
          >
            {updateTicket.isPending ? (
              <ActivityIndicator size="small" color="#374151" />
            ) : (
              <>
                <Ionicons name="swap-horizontal-outline" size={18} color="#374151" />
                <Text className="text-gray-700 font-semibold">Змінити статус</Text>
              </>
            )}
          </TouchableOpacity>
        )}

        <View className="h-4" />
      </ScrollView>

      <AddCommentModal
        visible={showAddComment}
        ticketId={id}
        isManager={isManager}
        onClose={() => setShowAddComment(false)}
      />
    </SafeAreaView>
  );
}
