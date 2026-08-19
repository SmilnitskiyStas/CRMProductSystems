import { useMemo } from 'react';
import { View } from 'react-native';
import JsBarcode from 'jsbarcode';

interface Props {
  value: string;
  width?: number;
  height?: number;
}

interface BarcodeEncoding {
  data: string;
}

interface BarcodeOutput {
  encodings?: BarcodeEncoding[];
}

/**
 * Fabric-safe Code 128 renderer. JsBarcode only calculates the binary modules; regular
 * React Native Views draw the bars, avoiding outdated SVG wrappers that mutate native
 * props during mounting on Android's New Architecture.
 */
export function Code128Barcode({ value, width = 300, height = 72 }: Props) {
  const modules = useMemo(() => {
    const output: BarcodeOutput = {};
    JsBarcode(output, value, { format: 'CODE128', displayValue: false, margin: 0 });
    return (output.encodings ?? []).map((encoding) => encoding.data).join('');
  }, [value]);

  const bars = useMemo(() => {
    const result: Array<{ start: number; length: number }> = [];
    let start = -1;
    for (let index = 0; index <= modules.length; index += 1) {
      if (modules[index] === '1' && start < 0) start = index;
      if (modules[index] !== '1' && start >= 0) {
        result.push({ start, length: index - start });
        start = -1;
      }
    }
    return result;
  }, [modules]);

  if (!modules) return null;

  const quietZone = 10;
  const moduleWidth = width / (modules.length + quietZone * 2);

  return (
    <View testID="code128-barcode" style={{ width, height, backgroundColor: '#ffffff', position: 'relative' }}>
      {bars.map((bar) => (
        <View
          key={`${bar.start}-${bar.length}`}
          style={{
            position: 'absolute',
            left: (bar.start + quietZone) * moduleWidth,
            top: 0,
            width: Math.max(moduleWidth, bar.length * moduleWidth),
            height,
            backgroundColor: '#000000',
          }}
        />
      ))}
    </View>
  );
}
