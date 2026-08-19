import { fireEvent, render } from '@testing-library/react-native';
import { OfflineReadStatus } from '../OfflineReadStatus';

describe('OfflineReadStatus', () => {
  it('announces stale cached data and exposes an accessible retry', async () => {
    const retry = jest.fn();
    const screen = await render(<OfflineReadStatus state={{ kind: 'stale', message: 'Дані можуть бути застарілими.', canRetry: true }} onRetry={retry} />);
    expect(screen.getByLabelText('Дані можуть бути застарілими.')).toBeTruthy();
    fireEvent.press(screen.getByRole('button', { name: 'Оновити дані' }));
    expect(retry).toHaveBeenCalledTimes(1);
  });

  it('does not render current online state', async () => {
    const screen = await render(<OfflineReadStatus state={{ kind: 'hidden', message: null, canRetry: false }} />);
    expect(screen.toJSON()).toBeNull();
  });
});
