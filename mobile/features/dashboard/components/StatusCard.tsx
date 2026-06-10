import { View, Text, TouchableOpacity } from 'react-native';

interface StatusCardProps {
  label: string;
  count: number;
  colorClass: string;
  bgClass: string;
  onPress?: () => void;
}

export function StatusCard({ label, count, colorClass, bgClass, onPress }: StatusCardProps) {
  return (
    <TouchableOpacity
      onPress={onPress}
      className={`flex-1 rounded-2xl p-4 ${bgClass} items-center justify-center min-h-[90px]`}
    >
      <Text className={`text-3xl font-bold ${colorClass}`}>{count}</Text>
      <Text className={`text-sm mt-1 ${colorClass} opacity-80`}>{label}</Text>
    </TouchableOpacity>
  );
}
