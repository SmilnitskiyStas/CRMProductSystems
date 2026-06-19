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
import { useCreateCustomer } from '../hooks/useCustomers';

interface Props {
  visible: boolean;
  onClose: () => void;
}

export function CreateCustomerModal({ visible, onClose }: Props) {
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [email, setEmail] = useState('');
  const [notes, setNotes] = useState('');

  const createCustomer = useCreateCustomer();

  function reset() {
    setName('');
    setPhone('');
    setEmail('');
    setNotes('');
  }

  function handleClose() {
    reset();
    onClose();
  }

  function handleSubmit() {
    const trimmedName = name.trim();
    if (!trimmedName) {
      Alert.alert('Помилка', "Ім'я клієнта обов'язкове");
      return;
    }

    createCustomer.mutate(
      {
        name: trimmedName,
        phone: phone.trim() || undefined,
        email: email.trim() || undefined,
        notes: notes.trim() || undefined,
      },
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
              className="border border-gray-200 rounded-xl px-3 py-2.5 text-sm text-gray-900 bg-gray-50"
              placeholder="Введіть ім'я клієнта"
              placeholderTextColor="#9ca3af"
              value={name}
              onChangeText={setName}
              autoCapitalize="words"
            />
          </View>

          {/* Phone */}
          <View className="mb-4">
            <Text className="text-sm font-semibold text-gray-700 mb-1.5">Телефон</Text>
            <TextInput
              className="border border-gray-200 rounded-xl px-3 py-2.5 text-sm text-gray-900 bg-gray-50"
              placeholder="+380..."
              placeholderTextColor="#9ca3af"
              value={phone}
              onChangeText={setPhone}
              keyboardType="phone-pad"
            />
          </View>

          {/* Email */}
          <View className="mb-4">
            <Text className="text-sm font-semibold text-gray-700 mb-1.5">Email</Text>
            <TextInput
              className="border border-gray-200 rounded-xl px-3 py-2.5 text-sm text-gray-900 bg-gray-50"
              placeholder="email@example.com"
              placeholderTextColor="#9ca3af"
              value={email}
              onChangeText={setEmail}
              keyboardType="email-address"
              autoCapitalize="none"
            />
          </View>

          {/* Notes */}
          <View className="mb-4">
            <Text className="text-sm font-semibold text-gray-700 mb-1.5">Примітки</Text>
            <TextInput
              className="border border-gray-200 rounded-xl px-3 py-2.5 text-sm text-gray-900 bg-gray-50"
              placeholder="Додаткова інформація..."
              placeholderTextColor="#9ca3af"
              value={notes}
              onChangeText={setNotes}
              multiline
              numberOfLines={3}
              textAlignVertical="top"
            />
          </View>
        </ScrollView>

        {/* Footer buttons */}
        <View className="flex-row gap-3 px-4 py-4 border-t border-gray-100">
          <TouchableOpacity
            onPress={handleClose}
            disabled={createCustomer.isPending}
            className="flex-1 py-3 rounded-xl border border-gray-300 items-center"
          >
            <Text className="text-gray-600 font-semibold">Скасувати</Text>
          </TouchableOpacity>
          <TouchableOpacity
            onPress={handleSubmit}
            disabled={createCustomer.isPending}
            className="flex-1 py-3 rounded-xl bg-primary-600 items-center"
          >
            {createCustomer.isPending ? (
              <ActivityIndicator size="small" color="white" />
            ) : (
              <Text className="text-white font-semibold">Зберегти</Text>
            )}
          </TouchableOpacity>
        </View>
      </View>
    </Modal>
  );
}
