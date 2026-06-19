import { useState } from 'react';
import {
  View,
  Text,
  ScrollView,
  ActivityIndicator,
  TouchableOpacity,
  Alert,
  Linking,
  Modal,
  TextInput,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useAuthStore } from '@/features/auth/store';
import {
  useCustomer,
  useUpdateCustomer,
  useDeleteCustomer,
} from '@/features/customers/hooks/useCustomers';
import type { UpdateCustomerPayload } from '@/features/customers/types';

const MANAGER_ROLES = ['StoreManager', 'NetworkManager', 'Admin'];

const PAYMENT_TYPE_LABELS: Record<string, string> = {
  cash: 'Готівка',
  card: 'Картка',
  online: 'Онлайн',
};

const TRANSACTION_STATUS_COLORS: Record<string, string> = {
  completed: 'text-green-700 bg-green-100',
  pending: 'text-yellow-700 bg-yellow-100',
  cancelled: 'text-red-700 bg-red-100',
};

const TRANSACTION_STATUS_LABELS: Record<string, string> = {
  completed: 'Завершено',
  pending: 'В очікуванні',
  cancelled: 'Скасовано',
};

export default function CustomerDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const router = useRouter();
  const user = useAuthStore((s) => s.user);
  const isManager = user ? MANAGER_ROLES.includes(user.role) : false;

  const { data, isLoading, isError } = useCustomer(id);
  const updateCustomer = useUpdateCustomer();
  const deleteCustomer = useDeleteCustomer();

  // Edit modal state
  const [showEdit, setShowEdit] = useState(false);
  const [editName, setEditName] = useState('');
  const [editPhone, setEditPhone] = useState('');
  const [editEmail, setEditEmail] = useState('');
  const [editNotes, setEditNotes] = useState('');

  function openEdit() {
    if (!data) return;
    setEditName(data.name);
    setEditPhone(data.phone ?? '');
    setEditEmail(data.email ?? '');
    setEditNotes(data.notes ?? '');
    setShowEdit(true);
  }

  function handleUpdate() {
    const trimmedName = editName.trim();
    if (!trimmedName) {
      Alert.alert('Помилка', "Ім'я клієнта обов'язкове");
      return;
    }
    const payload: UpdateCustomerPayload = {
      name: trimmedName,
      phone: editPhone.trim() || undefined,
      email: editEmail.trim() || undefined,
      notes: editNotes.trim() || undefined,
    };
    updateCustomer.mutate(
      { id, payload },
      {
        onSuccess: () => setShowEdit(false),
        onError: () => Alert.alert('Помилка', 'Не вдалося оновити клієнта'),
      }
    );
  }

  function handleDelete() {
    Alert.alert(
      'Видалити клієнта?',
      `Клієнт "${data?.name}" буде видалений назавжди.`,
      [
        { text: 'Скасувати', style: 'cancel' },
        {
          text: 'Видалити',
          style: 'destructive',
          onPress: () =>
            deleteCustomer.mutate(id, {
              onSuccess: () => router.back(),
              onError: () => Alert.alert('Помилка', 'Не вдалося видалити клієнта'),
            }),
        },
      ]
    );
  }

  function handleCallPhone() {
    if (data?.phone) {
      void Linking.openURL(`tel:${data.phone}`);
    }
  }

  function handleSendEmail() {
    if (data?.email) {
      void Linking.openURL(`mailto:${data.email}`);
    }
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
        <Text className="text-red-500 text-center">Не вдалося завантажити клієнта</Text>
        <TouchableOpacity onPress={() => router.back()} className="mt-4">
          <Text className="text-primary-600 font-medium">Назад</Text>
        </TouchableOpacity>
      </SafeAreaView>
    );
  }

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
          <Text className="flex-1 text-lg font-bold text-gray-900" numberOfLines={1}>
            {data.name}
          </Text>
          {isManager && (
            <TouchableOpacity
              onPress={openEdit}
              className="w-9 h-9 items-center justify-center rounded-full bg-gray-100"
            >
              <Ionicons name="pencil-outline" size={18} color="#374151" />
            </TouchableOpacity>
          )}
        </View>
      </View>

      <ScrollView contentContainerClassName="p-4 gap-4">
        {/* Contact info */}
        <View className="bg-white rounded-xl p-4 gap-3">
          <Text className="text-xs font-semibold text-gray-400 uppercase tracking-wide">
            Контакти
          </Text>
          {data.phone ? (
            <TouchableOpacity
              onPress={handleCallPhone}
              className="flex-row items-center gap-3"
            >
              <View className="w-8 h-8 bg-green-100 rounded-full items-center justify-center">
                <Ionicons name="call-outline" size={16} color="#16a34a" />
              </View>
              <Text className="text-sm text-primary-600 font-medium">{data.phone}</Text>
            </TouchableOpacity>
          ) : (
            <View className="flex-row items-center gap-3">
              <View className="w-8 h-8 bg-gray-100 rounded-full items-center justify-center">
                <Ionicons name="call-outline" size={16} color="#9ca3af" />
              </View>
              <Text className="text-sm text-gray-400">Телефон не вказано</Text>
            </View>
          )}
          {data.email ? (
            <TouchableOpacity
              onPress={handleSendEmail}
              className="flex-row items-center gap-3"
            >
              <View className="w-8 h-8 bg-blue-100 rounded-full items-center justify-center">
                <Ionicons name="mail-outline" size={16} color="#2563eb" />
              </View>
              <Text className="text-sm text-primary-600 font-medium">{data.email}</Text>
            </TouchableOpacity>
          ) : (
            <View className="flex-row items-center gap-3">
              <View className="w-8 h-8 bg-gray-100 rounded-full items-center justify-center">
                <Ionicons name="mail-outline" size={16} color="#9ca3af" />
              </View>
              <Text className="text-sm text-gray-400">Email не вказано</Text>
            </View>
          )}
        </View>

        {/* Statistics */}
        <View className="bg-white rounded-xl p-4">
          <Text className="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-3">
            Статистика
          </Text>
          <View className="flex-row gap-3">
            <View className="flex-1 bg-gray-50 rounded-xl p-3 items-center">
              <Text className="text-xl font-bold text-gray-900">{data.totalOrders}</Text>
              <Text className="text-xs text-gray-500 mt-0.5">Замовлень</Text>
            </View>
            <View className="flex-1 bg-gray-50 rounded-xl p-3 items-center">
              <Text className="text-xl font-bold text-gray-900">
                {data.totalSpent.toFixed(0)} ₴
              </Text>
              <Text className="text-xs text-gray-500 mt-0.5">Витрачено</Text>
            </View>
            <View className="flex-1 bg-gray-50 rounded-xl p-3 items-center">
              <Text className="text-base font-bold text-gray-900">
                {new Date(data.createdAt).toLocaleDateString('uk-UA', {
                  day: '2-digit',
                  month: '2-digit',
                  year: '2-digit',
                })}
              </Text>
              <Text className="text-xs text-gray-500 mt-0.5">Реєстрація</Text>
            </View>
          </View>
        </View>

        {/* Tags */}
        {data.tags.length > 0 && (
          <View className="bg-white rounded-xl p-4">
            <Text className="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-3">
              Теги
            </Text>
            <View className="flex-row flex-wrap gap-2">
              {data.tags.map((tag) => (
                <View key={tag} className="px-3 py-1 bg-primary-50 rounded-full">
                  <Text className="text-xs font-medium text-primary-700">{tag}</Text>
                </View>
              ))}
            </View>
          </View>
        )}

        {/* Notes */}
        {data.notes && (
          <View className="bg-white rounded-xl p-4">
            <Text className="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-2">
              Примітки
            </Text>
            <Text className="text-sm text-gray-700 leading-5">{data.notes}</Text>
          </View>
        )}

        {/* Recent transactions */}
        {data.recentTransactions.length > 0 && (
          <View>
            <Text className="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-2 px-1">
              Останні транзакції
            </Text>
            <View className="bg-white rounded-xl overflow-hidden">
              {data.recentTransactions.slice(0, 5).map((tx, idx) => {
                const statusStyle =
                  TRANSACTION_STATUS_COLORS[tx.status] ?? 'text-gray-600 bg-gray-100';
                const statusLabel =
                  TRANSACTION_STATUS_LABELS[tx.status] ?? tx.status;
                const paymentLabel =
                  PAYMENT_TYPE_LABELS[tx.paymentType] ?? tx.paymentType;
                return (
                  <View
                    key={tx.id}
                    className={`px-4 py-3 ${idx > 0 ? 'border-t border-gray-50' : ''}`}
                  >
                    <View className="flex-row items-center justify-between">
                      <View className="flex-1 mr-3">
                        <Text className="text-sm font-semibold text-gray-900">
                          {tx.totalAmount.toFixed(2)} ₴
                        </Text>
                        <Text className="text-xs text-gray-400 mt-0.5">
                          {paymentLabel} ·{' '}
                          {new Date(tx.createdAt).toLocaleDateString('uk-UA')}
                        </Text>
                      </View>
                      <View className={`px-2 py-0.5 rounded-full ${statusStyle}`}>
                        <Text className="text-xs font-medium">{statusLabel}</Text>
                      </View>
                    </View>
                  </View>
                );
              })}
            </View>
          </View>
        )}

        {/* Delete button */}
        {isManager && (
          <TouchableOpacity
            onPress={handleDelete}
            disabled={deleteCustomer.isPending}
            className="bg-red-50 border border-red-200 rounded-xl py-3.5 items-center mt-2"
          >
            {deleteCustomer.isPending ? (
              <ActivityIndicator size="small" color="#ef4444" />
            ) : (
              <Text className="text-red-600 font-semibold">Видалити клієнта</Text>
            )}
          </TouchableOpacity>
        )}

        <View className="h-4" />
      </ScrollView>

      {/* Edit Modal */}
      <Modal visible={showEdit} animationType="slide" presentationStyle="pageSheet">
        <View className="flex-1 bg-white">
          <View className="flex-row items-center justify-between px-4 pt-5 pb-3 border-b border-gray-100">
            <Text className="text-lg font-bold text-gray-900">Редагувати клієнта</Text>
            <TouchableOpacity
              onPress={() => setShowEdit(false)}
              className="w-8 h-8 items-center justify-center rounded-full bg-gray-100"
            >
              <Ionicons name="close" size={18} color="#374151" />
            </TouchableOpacity>
          </View>

          <ScrollView className="flex-1 p-4" keyboardShouldPersistTaps="handled">
            <View className="mb-4">
              <Text className="text-sm font-semibold text-gray-700 mb-1.5">
                Ім'я <Text className="text-red-500">*</Text>
              </Text>
              <TextInput
                className="border border-gray-200 rounded-xl px-3 py-2.5 text-sm text-gray-900 bg-gray-50"
                value={editName}
                onChangeText={setEditName}
                autoCapitalize="words"
              />
            </View>
            <View className="mb-4">
              <Text className="text-sm font-semibold text-gray-700 mb-1.5">Телефон</Text>
              <TextInput
                className="border border-gray-200 rounded-xl px-3 py-2.5 text-sm text-gray-900 bg-gray-50"
                value={editPhone}
                onChangeText={setEditPhone}
                keyboardType="phone-pad"
                placeholder="+380..."
                placeholderTextColor="#9ca3af"
              />
            </View>
            <View className="mb-4">
              <Text className="text-sm font-semibold text-gray-700 mb-1.5">Email</Text>
              <TextInput
                className="border border-gray-200 rounded-xl px-3 py-2.5 text-sm text-gray-900 bg-gray-50"
                value={editEmail}
                onChangeText={setEditEmail}
                keyboardType="email-address"
                autoCapitalize="none"
                placeholder="email@example.com"
                placeholderTextColor="#9ca3af"
              />
            </View>
            <View className="mb-4">
              <Text className="text-sm font-semibold text-gray-700 mb-1.5">Примітки</Text>
              <TextInput
                className="border border-gray-200 rounded-xl px-3 py-2.5 text-sm text-gray-900 bg-gray-50"
                value={editNotes}
                onChangeText={setEditNotes}
                multiline
                numberOfLines={3}
                textAlignVertical="top"
                placeholder="Додаткова інформація..."
                placeholderTextColor="#9ca3af"
              />
            </View>
          </ScrollView>

          <View className="flex-row gap-3 px-4 py-4 border-t border-gray-100">
            <TouchableOpacity
              onPress={() => setShowEdit(false)}
              disabled={updateCustomer.isPending}
              className="flex-1 py-3 rounded-xl border border-gray-300 items-center"
            >
              <Text className="text-gray-600 font-semibold">Скасувати</Text>
            </TouchableOpacity>
            <TouchableOpacity
              onPress={handleUpdate}
              disabled={updateCustomer.isPending}
              className="flex-1 py-3 rounded-xl bg-primary-600 items-center"
            >
              {updateCustomer.isPending ? (
                <ActivityIndicator size="small" color="white" />
              ) : (
                <Text className="text-white font-semibold">Зберегти</Text>
              )}
            </TouchableOpacity>
          </View>
        </View>
      </Modal>
    </SafeAreaView>
  );
}
