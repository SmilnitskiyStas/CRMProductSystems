import { useState } from 'react';
import {
  View,
  Text,
  Modal,
  TouchableOpacity,
  TextInput,
  ScrollView,
  ActivityIndicator,
  Alert,
} from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useCreateCustomer } from '../hooks/useAutoService';
import type { AsCustomer } from '../types';

// ─── Customer detail bottom sheet ────────────────────────────────────────────

interface CustomerSheetProps {
  customer: AsCustomer | null;
  visible: boolean;
  onClose: () => void;
}

export function CustomerSheet({ customer, visible, onClose }: CustomerSheetProps) {
  if (!customer) return null;

  return (
    <Modal visible={visible} transparent animationType="slide">
      <TouchableOpacity
        activeOpacity={1}
        onPress={onClose}
        className="flex-1 justify-end bg-black/50"
      >
        <TouchableOpacity
          activeOpacity={1}
          onPress={() => {
            // prevent closing when tapping inside the sheet
          }}
        >
          <View className="bg-white rounded-t-3xl px-5 pt-4 pb-8">
            {/* Handle */}
            <View className="w-10 h-1 bg-gray-200 rounded-full self-center mb-4" />

            {/* Header */}
            <View className="flex-row items-start justify-between mb-4">
              <View className="flex-1 mr-3">
                <Text className="text-xl font-bold text-gray-900">{customer.name}</Text>
                {customer.phone && (
                  <View className="flex-row items-center gap-1.5 mt-1">
                    <Ionicons name="call-outline" size={14} color="#9ca3af" />
                    <Text className="text-sm text-gray-500">{customer.phone}</Text>
                  </View>
                )}
                {customer.email && (
                  <View className="flex-row items-center gap-1.5 mt-0.5">
                    <Ionicons name="mail-outline" size={14} color="#9ca3af" />
                    <Text className="text-sm text-gray-500">{customer.email}</Text>
                  </View>
                )}
              </View>
              <TouchableOpacity
                onPress={onClose}
                className="w-8 h-8 items-center justify-center rounded-full bg-gray-100"
              >
                <Ionicons name="close" size={18} color="#374151" />
              </TouchableOpacity>
            </View>

            {/* Notes */}
            {customer.notes && (
              <View className="bg-gray-50 rounded-xl p-3 mb-4">
                <Text className="text-xs font-semibold text-gray-500 mb-1">Нотатки</Text>
                <Text className="text-sm text-gray-700">{customer.notes}</Text>
              </View>
            )}

            {/* Vehicle count */}
            <View className="flex-row items-center gap-2 bg-blue-50 rounded-xl px-4 py-3">
              <Ionicons name="car-outline" size={18} color="#3b82f6" />
              <Text className="text-sm font-medium text-blue-700">
                {customer.vehicleCount ?? 0} автомобілів
              </Text>
            </View>
          </View>
        </TouchableOpacity>
      </TouchableOpacity>
    </Modal>
  );
}

// ─── Create customer modal ────────────────────────────────────────────────────

interface CreateCustomerModalProps {
  visible: boolean;
  onClose: () => void;
}

export function CreateCustomerModal({ visible, onClose }: CreateCustomerModalProps) {
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [email, setEmail] = useState('');
  const createCustomer = useCreateCustomer();

  function reset() {
    setName('');
    setPhone('');
    setEmail('');
  }

  function handleClose() {
    reset();
    onClose();
  }

  function handleSubmit() {
    if (!name.trim()) {
      Alert.alert('Введіть ім\'я клієнта');
      return;
    }
    createCustomer.mutate(
      { name: name.trim(), phone: phone.trim() || undefined, email: email.trim() || undefined },
      {
        onSuccess: () => {
          handleClose();
        },
        onError: () => {
          Alert.alert('Помилка', 'Не вдалося створити клієнта');
        },
      }
    );
  }

  return (
    <Modal visible={visible} animationType="slide" presentationStyle="pageSheet">
      <View className="flex-1 bg-white">
        {/* Header */}
        <View className="flex-row items-center justify-between px-4 pt-5 pb-3 border-b border-gray-100">
          <Text className="text-lg font-bold text-gray-900">Новий клієнт</Text>
          <TouchableOpacity
            onPress={handleClose}
            className="w-8 h-8 items-center justify-center rounded-full bg-gray-100"
          >
            <Ionicons name="close" size={18} color="#374151" />
          </TouchableOpacity>
        </View>

        <ScrollView className="flex-1 p-4" keyboardShouldPersistTaps="handled">
          {/* Name */}
          <View className="mb-4">
            <Text className="text-sm font-semibold text-gray-700 mb-1.5">
              Ім'я <Text className="text-red-500">*</Text>
            </Text>
            <TextInput
              className="border border-gray-200 rounded-xl px-4 py-3 text-sm text-gray-900 bg-gray-50"
              value={name}
              onChangeText={setName}
              placeholder="Іван Петренко"
              placeholderTextColor="#9ca3af"
            />
          </View>

          {/* Phone */}
          <View className="mb-4">
            <Text className="text-sm font-semibold text-gray-700 mb-1.5">Телефон</Text>
            <TextInput
              className="border border-gray-200 rounded-xl px-4 py-3 text-sm text-gray-900 bg-gray-50"
              value={phone}
              onChangeText={setPhone}
              placeholder="+380501234567"
              placeholderTextColor="#9ca3af"
              keyboardType="phone-pad"
            />
          </View>

          {/* Email */}
          <View className="mb-4">
            <Text className="text-sm font-semibold text-gray-700 mb-1.5">Email</Text>
            <TextInput
              className="border border-gray-200 rounded-xl px-4 py-3 text-sm text-gray-900 bg-gray-50"
              value={email}
              onChangeText={setEmail}
              placeholder="ivan@example.com"
              placeholderTextColor="#9ca3af"
              keyboardType="email-address"
              autoCapitalize="none"
            />
          </View>
        </ScrollView>

        {/* Submit */}
        <View className="px-4 py-4 border-t border-gray-100">
          <TouchableOpacity
            onPress={handleSubmit}
            disabled={createCustomer.isPending}
            className="bg-primary-600 py-3.5 rounded-xl items-center"
          >
            {createCustomer.isPending ? (
              <ActivityIndicator color="white" />
            ) : (
              <Text className="text-white font-bold text-base">Створити клієнта</Text>
            )}
          </TouchableOpacity>
        </View>
      </View>
    </Modal>
  );
}
