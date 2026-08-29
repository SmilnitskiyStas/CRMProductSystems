import { useEffect, useRef, useState } from 'react';
import { Dimensions, Keyboard, Platform } from 'react-native';

/**
 * Android edge-to-edge/modal windows do not consistently resize for the IME. This inset is
 * deliberately Android-only; iOS remains handled by KeyboardAvoidingView.
 */
export function useAndroidKeyboardInset(): number {
  const [inset, setInset] = useState(0);
  const availableHeight = useRef(Dimensions.get('window').height);
  useEffect(() => {
    if (Platform.OS !== 'android') return;
    const show = Keyboard.addListener('keyboardDidShow', (event) => {
      // Avoid applying the inset twice when a rebuilt native app already uses adjustResize.
      const resizedBy = Math.max(0, availableHeight.current - Dimensions.get('window').height);
      setInset(Math.max(0, event.endCoordinates.height - resizedBy));
    });
    const hide = Keyboard.addListener('keyboardDidHide', () => {
      setInset(0);
      availableHeight.current = Dimensions.get('window').height;
    });
    return () => { show.remove(); hide.remove(); };
  }, []);
  return inset;
}
