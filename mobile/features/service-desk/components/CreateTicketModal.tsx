import { useState } from 'react';
import {
  View,
  Text,
  Modal,
  TextInput,
  TouchableOpacity,
  ScrollView,
  ActivityIndicator,
  Alert,
} from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useCreateTicket } from '../hooks/useServiceDesk';

const CATEGORIES = [
  { value: 'general', label: 'Загальне' },
  { value: 'technical', label: 'Технічне' },
  { value: 'billing', label: 'Оплата' },
  { value: 'feature_request', label: 'Пропозиція' },
  { value: 'bug', label: 'Помилка' },
];

const PRIORITIES = [
  { value: 'low', label: 'Низький', style: 'bg-gray-100 border-gray-300', activeStyle: 'bg-gray-500 border-gray-500', textStyle: 'text-gray-600', activeTextStyle: 'text-white' },
  { value: 'medium', label: 'Середній', style: 'bg-blue-50 border-blue-200', activeStyle: 'bg-blue-600 border-blue-600', textStyle: 'text-blue-600', activeTextStyle: 'text-white' },
  { value: 'high', label: 'Високий', style: 'bg-orange-50 border-orange-200', activeStyle: 'bg-orange-500 border-orange-500', textStyle: 'text-orange-600', activeTextStyle: 'text-white' },
  { value: 'critical', label: 'Критичний', style: 'bg-red-50 border-red-200', activeStyle: 'bg-red-600 border-red-600', textStyle: 'text-red-600', activeTextStyle: 'text-white' },
];

interface Props {
  visible: boolean;
  onClose: () => void;
}

export function CreateTicketModal({ visible, onClose }: Props) {
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [category, setCategory] = useState('general');
  const [priority, setPriority] = useState('medium');

  const createTicket = useCreateTicket();

  function handleSubmit() {
    const trimmedTitle = title.trim();
    const trimmedDescription = description.trim();

    if (!trimmedTitle) {
      Alert.alert('Помилка', 'Введіть заголовок тікету');
      return;
    }
    if (!trimmedDescription) {
      Alert.alert('Помилка', 'Введіть опис тікету');
      return;
    }

    createTicket.mutate(
      {
        title: trimmedTitle,
        description: trimmedDescription,
        category,
        priority,
      },
      {
        onSuccess: () => {
          setTitle('');
          setDescription('');
          setCategory('general');
          setPriority('medium');
          onClose();
        },
        onError: () => Alert.alert('Помилка', 'Не вдалося створити тікет'),
      }
    );
  }

  function handleClose() {
    setTitle('');
    setDescription('');
    setCategory('general');
    setPriority('medium');
    onClose();
  }

  return (
    <Modal visible={visible} animationType="slide" presentationStyle="pageSheet">
      <View className="flex-1 bg-white">
        {/* Header */}
        <View className="flex-row items-center justify-between px-4 pt-5 pb-3 border-b border-gray-100">
          <Text className="text-lg font-bold text-gray-900">Новий тікет</Text>
          <TouchableOpacity
            onPress={handleClose}
            className="w-8 h-8 items-center justify-center rounded-full bg-gray-100"
          >
            <Ionicons name="close" size={18} color="#374151" />
          </TouchableOpacity>
        </View>

        <ScrollView className="flex-1 p-4" keyboardShouldPersistTaps="handled">
          {/* Title */}
          <View className="mb-4">
            <Text className="text-sm font-semibold text-gray-700 mb-1.5">
              Заголовок <Text className="text-red-500">*</Text>
            </Text>
            <TextInput
              className="border border-gray-200 rounded-xl px-3 py-2.5 text-sm text-gray-900 bg-gray-50"
              placeholder="Коротко опишіть проблему..."
              placeholderTextColor="#9ca3af"
              value={title}
              onChangeText={setTitle}
              returnKeyType="next"
            />
          </View>

          {/* Description */}
          <View className="mb-4">
            <Text className="text-sm font-semibold text-gray-700 mb-1.5">
              Опис <Text className="text-red-500">*</Text>
            </Text>
            <TextInput
              className="border border-gray-200 rounded-xl px-3 py-2.5 text-sm text-gray-900 bg-gray-50"
              placeholder="Детальний опис проблеми..."
              placeholderTextColor="#9ca3af"
              value={description}
              onChangeText={setDescription}
              multiline
              numberOfLines={4}
              textAlignVertical="top"
              style={{ minHeight: 90 }}
            />
          </View>

          {/* Category */}
          <View className="mb-4">
            <Text className="text-sm font-semibold text-gray-700 mb-1.5">Категорія</Text>
            <View className="gap-1">
              {CATEGORIES.map((cat) => {
                const isSelected = category === cat.value;
                return (
                  <TouchableOpacity
                    key={cat.value}
                    onPress={() => setCategory(cat.value)}
                    className={`flex-row items-center px-3 py-2.5 rounded-xl border ${
                      isSelected
                        ? 'bg-primary-50 border-primary-400'
                        : 'bg-gray-50 border-gray-200'
                    }`}
                  >
                    <View
                      className={`w-4 h-4 rounded-full border-2 mr-3 items-center justify-center ${
                        isSelected ? 'border-primary-600' : 'border-gray-300'
                      }`}
                    >
                      {isSelected && (
                        <View className="w-2 h-2 rounded-full bg-primary-600" />
                      )}
                    </View>
                    <Text
                      className={`text-sm font-medium ${
                        isSelected ? 'text-primary-700' : 'text-gray-600'
                      }`}
                    >
                      {cat.label}
                    </Text>
                  </TouchableOpacity>
                );
              })}
            </View>
          </View>

          {/* Priority */}
          <View className="mb-4">
            <Text className="text-sm font-semibold text-gray-700 mb-1.5">Пріоритет</Text>
            <View className="flex-row gap-2 flex-wrap">
              {PRIORITIES.map((p) => {
                const isSelected = priority === p.value;
                return (
                  <TouchableOpacity
                    key={p.value}
                    onPress={() => setPriority(p.value)}
                    className={`px-3 py-2 rounded-xl border ${
                      isSelected ? p.activeStyle : p.style
                    }`}
                  >
                    <Text
                      className={`text-xs font-semibold ${
                        isSelected ? p.activeTextStyle : p.textStyle
                      }`}
                    >
                      {p.label}
                    </Text>
                  </TouchableOpacity>
                );
              })}
            </View>
          </View>
        </ScrollView>

        {/* Buttons */}
        <View className="flex-row gap-3 px-4 py-4 border-t border-gray-100">
          <TouchableOpacity
            onPress={handleClose}
            disabled={createTicket.isPending}
            className="flex-1 py-3 rounded-xl border border-gray-300 items-center"
          >
            <Text className="text-gray-600 font-semibold">Скасувати</Text>
          </TouchableOpacity>
          <TouchableOpacity
            onPress={handleSubmit}
            disabled={createTicket.isPending}
            className="flex-1 py-3 rounded-xl bg-primary-600 items-center"
          >
            {createTicket.isPending ? (
              <ActivityIndicator size="small" color="white" />
            ) : (
              <Text className="text-white font-semibold">Подати</Text>
            )}
          </TouchableOpacity>
        </View>
      </View>
    </Modal>
  );
}
