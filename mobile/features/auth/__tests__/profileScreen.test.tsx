import { fireEvent, render } from '@testing-library/react-native';
import ProfileScreen from '../../../app/(app)/profile';
import { useAuthStore } from '../store';

const mockNavigate = jest.fn();
const mockReplace = jest.fn();

jest.mock('expo-router', () => ({ useRouter: () => ({ navigate: mockNavigate, replace: mockReplace }) }));
jest.mock('@expo/vector-icons', () => ({ Ionicons: 'Ionicons' }));
jest.mock('../store', () => ({ useAuthStore: jest.fn() }));
jest.mock('../api/authApi', () => ({ logout: jest.fn() }));
jest.mock('../session', () => ({ terminateSession: jest.fn() }));
jest.mock('@/lib/api-client', () => ({ apiClient: { post: jest.fn() } }));
jest.mock('@/features/loyalty/hooks/useLoyalty', () => ({
  useMyMembership: () => ({ data: null, isLoading: false, isError: false }),
  useJoinAsStaff: () => ({ mutate: jest.fn(), isPending: false, isError: false }),
}));

function setUser(role: string, fullName = 'Тестовий Користувач', email = 'test@example.com') {
  (useAuthStore as unknown as jest.Mock).mockImplementation((select) => select({
    user: { fullName, email, role },
  }));
}

test.each(['enterprise_admin', 'store_manager', 'cashier', 'storekeeper', 'provider'])(
  'shows account information instead of a second work menu for %s', async (role) => {
    setUser(role);
    const screen = await render(<ProfileScreen />);
    expect(screen.getByText('Дані користувача')).toBeOnTheScreen();
    expect(screen.getByText('Тестовий Користувач')).toBeOnTheScreen();
    expect(screen.getByText('test@example.com')).toBeOnTheScreen();
    for (const label of ['Мої інструменти', 'Каса', 'Залишки', 'Прийомка', 'Переміщення', 'Списання', 'Клієнти', 'Розклад', 'Service Desk']) {
      expect(screen.queryByText(label)).toBeNull();
    }
    expect(screen.getByText('Сповіщення (Telegram)')).toBeOnTheScreen();
    expect(screen.getByText('Підтримка')).toBeOnTheScreen();
    expect(screen.getByText('Про застосунок')).toBeOnTheScreen();
    expect(screen.getByText('Вийти з акаунту')).toBeOnTheScreen();
    expect(screen.getByText('Бонусна програма')).toBeOnTheScreen();
  },
);

test('shows the user role and navigates back to More', async () => {
  setUser('enterprise_admin');
  const screen = await render(<ProfileScreen />);
  expect(screen.getByText('Адміністратор підприємства')).toBeOnTheScreen();
  await fireEvent.press(screen.getByRole('button', { name: 'Назад до меню' }));
  expect(mockNavigate).toHaveBeenCalledWith('/(app)/more');
});

test('uses explicit placeholders when account details are unavailable', async () => {
  setUser('', '', '');
  const screen = await render(<ProfileScreen />);
  expect(screen.getAllByText('Не вказано')).toHaveLength(3);
});
