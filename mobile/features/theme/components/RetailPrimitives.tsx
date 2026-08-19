import type { PropsWithChildren, ReactNode } from 'react';
import {
  ActivityIndicator,
  Pressable,
  Text,
  View,
  type PressableProps,
  type ViewProps,
} from 'react-native';
import { useRetailTheme } from '../RetailThemeProvider';

export function RetailScreen({ children, style, ...props }: PropsWithChildren<ViewProps>) {
  const theme = useRetailTheme();
  return (
    <View {...props} style={[{ backgroundColor: theme.colors.background }, style]}>
      {children}
    </View>
  );
}

export function RetailCard({ children, style, ...props }: PropsWithChildren<ViewProps>) {
  const theme = useRetailTheme();
  return (
    <View
      {...props}
      style={[
        { backgroundColor: theme.colors.surface, borderRadius: theme.radius.card },
        style,
      ]}
    >
      {children}
    </View>
  );
}

export function RetailPressableCard({
  children,
  style,
  ...props
}: PropsWithChildren<PressableProps>) {
  const theme = useRetailTheme();
  return (
    <Pressable
      {...props}
      style={(state) => [
        { backgroundColor: theme.colors.surface, borderRadius: theme.radius.card },
        typeof style === 'function' ? style(state) : style,
      ]}
    >
      {children}
    </Pressable>
  );
}

interface RetailPrimaryButtonProps extends Omit<PressableProps, 'children'> {
  children: ReactNode;
  pending?: boolean;
}

export function RetailPrimaryButton({
  children,
  pending = false,
  disabled,
  style,
  ...props
}: RetailPrimaryButtonProps) {
  const theme = useRetailTheme();
  const inactive = disabled || pending;
  return (
    <Pressable
      {...props}
      disabled={inactive}
      style={(state) => [
        {
          alignItems: 'center',
          justifyContent: 'center',
          backgroundColor: theme.colors.primary,
          borderRadius: theme.radius.button,
          opacity: inactive || state.pressed ? 0.65 : 1,
        },
        typeof style === 'function' ? style(state) : style,
      ]}
    >
      {pending ? (
        <ActivityIndicator color={theme.colors.onPrimary} />
      ) : typeof children === 'string' ? (
        <Text style={{ color: theme.colors.onPrimary, fontWeight: '700' }}>{children}</Text>
      ) : (
        children
      )}
    </Pressable>
  );
}
