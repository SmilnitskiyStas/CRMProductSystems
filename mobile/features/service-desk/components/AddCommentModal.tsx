import { useState } from 'react';
import {
  View,
  Text,
  Modal,
  TextInput,
  TouchableOpacity,
  ActivityIndicator,
  Alert,
  Switch,
} from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useAddComment } from '../hooks/useServiceDesk';

interface Props {
  visible: boolean;
  ticketId: string;
  isManager: boolean;
  onClose: () => void;
}

export function AddCommentModal({ visible, ticketId, isManager, onClose }: Props) {
  const [body, setBody] = useState('');
  const [isInternal, setIsInternal] = useState(false);

  const addComment = useAddComment(ticketId);

  function handleSubmit() {
    const trimmedBody = body.trim();
    if (!trimmedBody) {
      Alert.alert('Помилка', 'Введіть текст коментаря');
      return;
    }

    addComment.mutate(
      { body: trimmedBody, isInternal: isManager ? isInternal : false },
      {
        onSuccess: () => {
          setBody('');
          setIsInternal(false);
          onClose();
        },
        onError: () => Alert.alert('Помилка', 'Не вдалося додати коментар'),
      }
    );
  }

  function handleClose() {
    setBody('');
    setIsInternal(false);
    onClose();
  }

  return (
    <Modal visible={visible} animationType="slide" presentationStyle="pageSheet">
      <View className="flex-1 bg-white">
        {/* Header */}
        <View className="flex-row items-center justify-between px-4 pt-5 pb-3 border-b border-gray-100">
          <Text className="text-lg font-bold text-gray-900">Додати коментар</Text>
          <TouchableOpacity
            onPress={handleClose}
            className="w-8 h-8 items-center justify-center rounded-full bg-gray-100"
          >
            <Ionicons name="close" size={18} color="#374151" />
          </TouchableOpacity>
        </View>

        <View className="flex-1 p-4">
          {/* Comment body */}
          <View className="mb-4">
            <Text className="text-sm font-semibold text-gray-700 mb-1.5">
              Коментар <Text className="text-red-500">*</Text>
            </Text>
            <TextInput
              className="border border-gray-200 rounded-xl px-3 py-2.5 text-sm text-gray-900 bg-gray-50"
              placeholder="Введіть коментар..."
              placeholderTextColor="#9ca3af"
              value={body}
              onChangeText={setBody}
              multiline
              numberOfLines={5}
              textAlignVertical="top"
              style={{ minHeight: 110 }}
              autoFocus
            />
          </View>

          {/* Internal toggle — managers only */}
          {isManager && (
            <View className="flex-row items-center justify-between bg-orange-50 border border-orange-200 rounded-xl px-4 py-3">
              <View className="flex-1 mr-3">
                <Text className="text-sm font-semibold text-orange-800">Внутрішній</Text>
                <Text className="text-xs text-orange-600 mt-0.5">
                  Видно тільки менеджерам
                </Text>
              </View>
              <Switch
                value={isInternal}
                onValueChange={setIsInternal}
                trackColor={{ false: '#e5e7eb', true: '#f97316' }}
                thumbColor="white"
              />
            </View>
          )}
        </View>

        {/* Buttons */}
        <View className="flex-row gap-3 px-4 py-4 border-t border-gray-100">
          <TouchableOpacity
            onPress={handleClose}
            disabled={addComment.isPending}
            className="flex-1 py-3 rounded-xl border border-gray-300 items-center"
          >
            <Text className="text-gray-600 font-semibold">Скасувати</Text>
          </TouchableOpacity>
          <TouchableOpacity
            onPress={handleSubmit}
            disabled={addComment.isPending}
            className="flex-1 py-3 rounded-xl bg-primary-600 items-center"
          >
            {addComment.isPending ? (
              <ActivityIndicator size="small" color="white" />
            ) : (
              <Text className="text-white font-semibold">Надіслати</Text>
            )}
          </TouchableOpacity>
        </View>
      </View>
    </Modal>
  );
}
