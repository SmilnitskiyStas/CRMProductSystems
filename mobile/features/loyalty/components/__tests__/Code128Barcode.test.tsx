import { render } from '@testing-library/react-native';
import { Code128Barcode } from '../Code128Barcode';

describe('Code128Barcode', () => {
  it('renders the universal loyalty payload without a native SVG wrapper', async () => {
    const screen = await render(
      <Code128Barcode value="SGCUS1.12345678-1234-1234-1234-123456789012.123456" />
    );

    expect(screen.getByTestId('code128-barcode')).toBeTruthy();
  });
});
