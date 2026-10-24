import { act, fireEvent, render } from '@testing-library/react-native';
import { MfaStepUpDialog } from '../components/MfaStepUpDialog';
import { requestMfaCode } from '../mfaPrompt';
import { getMe } from '../api/authApi';

jest.setTimeout(30_000);

let mockUser = { twoFactorEnabled: false };
const mockGetState = jest.fn(() => ({ workspaceAccessToken: 'synthetic-session', setUser: jest.fn(), user: mockUser }));
jest.mock('../store', () => ({ useAuthStore: { getState: () => mockGetState() } }));
jest.mock('../api/authApi', () => ({ getMe: jest.fn() }));
jest.mock('../components/MfaSetupPanel', () => ({ MfaSetupPanel: ({ onClose }: { onClose: () => void }) => {
  const { Text, TouchableOpacity, View } = require('react-native');
  return <View><Text>One-time recovery codes</Text><TouchableOpacity onPress={onClose}><Text>Codes saved</Text></TouchableOpacity></View>;
} }));

beforeEach(() => { mockUser = { twoFactorEnabled: false }; (getMe as jest.Mock).mockImplementation(async () => mockUser); });

test('keeps recovery codes visible after successful enrollment changes the profile', async () => {
  const screen = await render(<MfaStepUpDialog />);
  let pending!: Promise<string | null>;
  await act(async () => { pending = requestMfaCode(); });
  expect(screen.getByText('One-time recovery codes')).toBeOnTheScreen();
  mockUser = { twoFactorEnabled: true };
  await screen.rerender(<MfaStepUpDialog />);
  expect(screen.getByText('One-time recovery codes')).toBeOnTheScreen();
  await fireEvent.press(screen.getByText('Codes saved'));
  await expect(pending).resolves.toBeNull();
});

test('returns the entered second factor only after explicit confirmation', async () => {
  mockUser = { twoFactorEnabled: true };
  const screen = await render(<MfaStepUpDialog />);
  let pending!: Promise<string | null>;
  await act(async () => { pending = requestMfaCode(); });
  await fireEvent.changeText(screen.getByLabelText('Код MFA'), '123456');
  await fireEvent.press(screen.getByText('Підтвердити'));
  await expect(pending).resolves.toBe('123456');
});
