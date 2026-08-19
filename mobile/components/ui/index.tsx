import type { ComponentProps, ReactNode } from 'react';
import { useState } from 'react';
import {
  ActivityIndicator,
  KeyboardAvoidingView,
  Modal as NativeModal,
  Platform,
  Pressable,
  ScrollView,
  Text,
  TextInput,
  View,
  type TextInputProps,
  type ViewProps,
} from 'react-native';
import { SafeAreaView, type Edge } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { colors } from './tokens';

export * from './tokens';

const buttonClasses = {
  primary: 'bg-primary-600', secondary: 'bg-white border border-gray-300', danger: 'bg-red-600', ghost: 'bg-transparent',
} as const;
const buttonTextClasses = {
  primary: 'text-white', secondary: 'text-gray-900', danger: 'text-white', ghost: 'text-primary-700',
} as const;

export interface ScreenProps extends ViewProps {
  children: ReactNode;
  keyboard?: boolean;
  scroll?: boolean;
  edges?: Edge[];
  contentClassName?: string;
}
export function Screen({ children, keyboard = false, scroll = false, edges, className = '', contentClassName = '', ...props }: ScreenProps) {
  const content = scroll ? <ScrollView keyboardShouldPersistTaps="handled" contentContainerClassName={contentClassName}>{children}</ScrollView> : <View className={`flex-1 ${contentClassName}`}>{children}</View>;
  return <SafeAreaView {...props} edges={edges} className={`flex-1 bg-gray-50 ${className}`}>{keyboard ? <KeyboardAvoidingView className="flex-1" behavior={Platform.OS === 'ios' ? 'padding' : 'height'}>{content}</KeyboardAvoidingView> : content}</SafeAreaView>;
}

export interface HeaderProps { title: string; subtitle?: string; onBack?: () => void; action?: ReactNode; }
export function Header({ title, subtitle, onBack, action }: HeaderProps) {
  return <View className="min-h-[64px] px-4 py-3 flex-row items-center gap-3">
    {onBack ? <IconButton icon="arrow-back" label="Назад" onPress={onBack} /> : null}
    <View className="flex-1"><Text className="text-2xl font-bold text-gray-900" accessibilityRole="header">{title}</Text>{subtitle ? <Text className="text-sm text-gray-500 mt-0.5">{subtitle}</Text> : null}</View>
    {action}
  </View>;
}

export interface ButtonProps { label: string; onPress: () => void; variant?: keyof typeof buttonClasses; disabled?: boolean; loading?: boolean; accessibilityLabel?: string; icon?: ComponentProps<typeof Ionicons>['name']; }
export function Button({ label, onPress, variant = 'primary', disabled = false, loading = false, accessibilityLabel, icon }: ButtonProps) {
  const unavailable = disabled || loading;
  return <Pressable accessibilityRole="button" accessibilityLabel={accessibilityLabel ?? label} accessibilityState={{ disabled: unavailable, busy: loading }} disabled={unavailable} onPress={onPress} className={`min-h-[48px] px-4 rounded-xl flex-row gap-2 items-center justify-center ${buttonClasses[variant]} ${unavailable ? 'opacity-50' : ''}`}>
    {loading ? <ActivityIndicator color={variant === 'secondary' || variant === 'ghost' ? colors.brand[700] : colors.neutral[0]} /> : icon ? <Ionicons name={icon} size={20} color={variant === 'secondary' || variant === 'ghost' ? colors.brand[700] : colors.neutral[0]} /> : null}
    {!loading ? <Text className={`text-base font-semibold ${buttonTextClasses[variant]}`}>{label}</Text> : null}
  </Pressable>;
}

export interface IconButtonProps { icon: ComponentProps<typeof Ionicons>['name']; label: string; onPress: () => void; disabled?: boolean; color?: string; }
export function IconButton({ icon, label, onPress, disabled = false, color = colors.neutral[700] }: IconButtonProps) {
  return <Pressable accessibilityRole="button" accessibilityLabel={label} accessibilityState={{ disabled }} disabled={disabled} onPress={onPress} hitSlop={4} className={`w-11 h-11 rounded-full items-center justify-center bg-gray-100 ${disabled ? 'opacity-50' : ''}`}><Ionicons name={icon} size={21} color={color} /></Pressable>;
}

export function Card({ children, className = '', ...props }: ViewProps) { return <View {...props} className={`bg-white border border-gray-100 rounded-2xl p-4 ${className}`}>{children}</View>; }

export interface ListRowProps { title: string; subtitle?: string; leading?: ReactNode; trailing?: ReactNode; onPress?: () => void; accessibilityLabel?: string; }
export function ListRow({ title, subtitle, leading, trailing, onPress, accessibilityLabel }: ListRowProps) {
  const content = <>{leading}<View className="flex-1"><Text className="text-base font-semibold text-gray-900" numberOfLines={1}>{title}</Text>{subtitle ? <Text className="text-sm text-gray-500 mt-0.5" numberOfLines={2}>{subtitle}</Text> : null}</View>{trailing}{onPress ? <Ionicons name="chevron-forward" size={18} color={colors.neutral[400]} /> : null}</>;
  return onPress ? <Pressable accessibilityRole="button" accessibilityLabel={accessibilityLabel ?? title} onPress={onPress} className="min-h-[64px] bg-white rounded-xl px-4 py-3 flex-row items-center gap-3">{content}</Pressable> : <View className="min-h-[64px] bg-white rounded-xl px-4 py-3 flex-row items-center gap-3">{content}</View>;
}

export interface TextFieldProps extends TextInputProps { label: string; error?: string; trailing?: ReactNode; }
export function TextField({ label, error, trailing, className = '', ...props }: TextFieldProps) {
  return <View><Text className="text-sm font-medium text-gray-700 mb-1.5">{label}</Text><View className="relative"><TextInput {...props} accessibilityLabel={props.accessibilityLabel ?? label} accessibilityState={{ disabled: props.editable === false }} className={`min-h-[48px] border rounded-xl px-4 py-3 text-base text-gray-900 bg-white ${trailing ? 'pr-14' : ''} ${error ? 'border-red-500' : 'border-gray-300'} ${className}`} />{trailing ? <View className="absolute right-1 top-0">{trailing}</View> : null}</View><Text className={`text-xs mt-1 ${error ? 'text-red-600' : 'text-transparent'}`} accessibilityLiveRegion="polite">{error ?? ' '}</Text></View>;
}

export interface SelectOption { label: string; value: string; }
export interface SelectFieldProps { label: string; value?: string; options: SelectOption[]; onChange: (value: string) => void; error?: string; disabled?: boolean; placeholder?: string; }
export function SelectField({ label, value, options, onChange, error, disabled, placeholder = 'Оберіть значення' }: SelectFieldProps) {
  const [open, setOpen] = useState(false); const selected = options.find((item) => item.value === value);
  return <View><Text className="text-sm font-medium text-gray-700 mb-1.5">{label}</Text><Pressable accessibilityRole="button" accessibilityLabel={label} accessibilityState={{ disabled, expanded: open }} disabled={disabled} onPress={() => setOpen((current) => !current)} className={`min-h-[48px] border rounded-xl px-4 flex-row items-center ${error ? 'border-red-500' : 'border-gray-300'} ${disabled ? 'opacity-50' : ''}`}><Text className={`flex-1 text-base ${selected ? 'text-gray-900' : 'text-gray-400'}`}>{selected?.label ?? placeholder}</Text><Ionicons name="chevron-down" size={18} color={colors.neutral[500]} /></Pressable>{open ? <Card className="mt-1 p-1">{options.map((option) => <Pressable key={option.value} accessibilityRole="button" onPress={() => { onChange(option.value); setOpen(false); }} className="min-h-[44px] px-3 justify-center"><Text className="text-base text-gray-900">{option.label}</Text></Pressable>)}</Card> : null}{error ? <Text className="text-xs text-red-600 mt-1">{error}</Text> : null}</View>;
}

const badgeClasses = { neutral: 'bg-gray-100 text-gray-700', success: 'bg-green-100 text-green-800', warning: 'bg-amber-100 text-amber-800', danger: 'bg-red-100 text-red-800', info: 'bg-blue-100 text-blue-800' } as const;
export function StatusBadge({ label, tone = 'neutral' }: { label: string; tone?: keyof typeof badgeClasses }) { return <View className={`self-start rounded-full px-2.5 py-1 ${badgeClasses[tone].split(' ')[0]}`}><Text className={`text-xs font-semibold ${badgeClasses[tone].split(' ')[1]}`}>{label}</Text></View>; }

interface StateProps { title: string; message?: string; actionLabel?: string; onAction?: () => void; icon?: ComponentProps<typeof Ionicons>['name']; }
export function EmptyState({ title, message, actionLabel, onAction, icon = 'file-tray-outline' }: StateProps) { return <View className="items-center justify-center px-6 py-16"><Ionicons name={icon} size={48} color={colors.neutral[400]} /><Text className="text-lg font-semibold text-gray-800 text-center mt-3">{title}</Text>{message ? <Text className="text-sm text-gray-500 text-center mt-1">{message}</Text> : null}{actionLabel && onAction ? <View className="mt-4"><Button label={actionLabel} onPress={onAction} /></View> : null}</View>; }
export function ErrorState({ title = 'Не вдалося завантажити дані', message, actionLabel = 'Спробувати знову', onAction, icon = 'alert-circle-outline' }: Partial<StateProps>) { return <EmptyState title={title} message={message} actionLabel={onAction ? actionLabel : undefined} onAction={onAction} icon={icon} />; }
export function Skeleton({ className = 'h-4 w-full' }: { className?: string }) { return <View accessibilityLabel="Завантаження" className={`rounded-lg bg-gray-200 ${className}`} />; }

export interface ModalProps { visible: boolean; title: string; children: ReactNode; onClose: () => void; sheet?: boolean; }
export function Modal({ visible, title, children, onClose, sheet = false }: ModalProps) { return <NativeModal visible={visible} transparent animationType={sheet ? 'slide' : 'fade'} onRequestClose={onClose}><View className={`flex-1 bg-black/40 px-4 ${sheet ? 'justify-end px-0' : 'justify-center'}`}><View className={`bg-white p-5 ${sheet ? 'rounded-t-3xl pb-8' : 'rounded-2xl'}`}><View className="flex-row items-center mb-4"><Text accessibilityRole="header" className="text-xl font-bold text-gray-900 flex-1">{title}</Text><IconButton icon="close" label="Закрити" onPress={onClose} /></View>{children}</View></View></NativeModal>; }
export function Sheet(props: Omit<ModalProps, 'sheet'>) { return <Modal {...props} sheet />; }

export interface ConfirmDialogProps { visible: boolean; title: string; message: string; confirmLabel?: string; cancelLabel?: string; destructive?: boolean; loading?: boolean; onConfirm: () => void; onCancel: () => void; }
export function ConfirmDialog({ visible, title, message, confirmLabel = 'Підтвердити', cancelLabel = 'Скасувати', destructive, loading, onConfirm, onCancel }: ConfirmDialogProps) { return <Modal visible={visible} title={title} onClose={onCancel}><Text className="text-base text-gray-600 mb-5">{message}</Text><View className="gap-2"><Button label={confirmLabel} onPress={onConfirm} loading={loading} variant={destructive ? 'danger' : 'primary'} /><Button label={cancelLabel} onPress={onCancel} disabled={loading} variant="secondary" /></View></Modal>; }

export function OfflineBanner({ visible, message = 'Немає з’єднання. Зміни буде збережено локально.' }: { visible: boolean; message?: string }) { if (!visible) return null; return <View accessibilityRole="alert" className="bg-amber-100 border-b border-amber-200 px-4 py-2 flex-row items-center gap-2"><Ionicons name="cloud-offline-outline" size={18} color={colors.status.warning} /><Text className="text-sm text-amber-900 flex-1">{message}</Text></View>; }
