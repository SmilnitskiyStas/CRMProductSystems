import { fireEvent, render } from '@testing-library/react-native';
import { MfaSetupReminder } from '../components/MfaSetupReminder';
import { useAuthStore } from '../store';

const mockNavigate = jest.fn();
jest.mock('expo-router', () => ({ useRouter: () => ({ navigate: mockNavigate }) }));
jest.mock('../store', () => ({ useAuthStore: jest.fn() }));
function user(value?: { role: string; twoFactorEnabled: boolean }) {
  (useAuthStore as unknown as jest.Mock).mockImplementation(select => select({ user: value }));
}
test('offers settings without opening setup when an unenrolled user enters the app', async () => {
  user({ role: 'network_manager', twoFactorEnabled: false });
  const screen = await render(<MfaSetupReminder />);
  expect(screen.getByText(/Для більшого захисту даних/)).toBeOnTheScreen();
  expect(mockNavigate).not.toHaveBeenCalled();
  await fireEvent.press(screen.getByRole('button', { name: 'Налаштувати захист' }));
  expect(mockNavigate).toHaveBeenCalledWith('/(app)/profile');
});
test('hides for an enrolled, missing or consumer identity', async () => {
  user({ role: 'network_manager', twoFactorEnabled: true });
  const screen = await render(<MfaSetupReminder />);
  expect(screen.queryByText('Налаштувати захист')).toBeNull();
  user(); await screen.rerender(<MfaSetupReminder />);
  expect(screen.queryByText('Налаштувати захист')).toBeNull();
  user({ role: 'consumer', twoFactorEnabled: false }); await screen.rerender(<MfaSetupReminder />);
  expect(screen.queryByText('Налаштувати захист')).toBeNull();
});
