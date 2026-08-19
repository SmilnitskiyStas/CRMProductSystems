import { apiClient } from '@/lib/api-client';
import { getMe, login, verifyTwoFactor } from '../authApi';

jest.mock('@/lib/api-client', () => ({
  apiClient: {
    get: jest.fn(),
    post: jest.fn(),
  },
}));

const mockGet = apiClient.get as jest.MockedFunction<typeof apiClient.get>;
const mockPost = apiClient.post as jest.MockedFunction<typeof apiClient.post>;

const wireUser = {
  id: 'user-1',
  email: 'manager@example.com',
  fullName: 'Store Manager',
  role: 'store_manager',
  tenantId: 'tenant-1',
  storeId: 'location-1',
};

describe('authApi', () => {
  test('maps backend storeId to the mobile locationId on login', async () => {
    mockPost.mockResolvedValueOnce({
      data: { accessToken: 'token', user: wireUser },
    });

    await expect(login({ email: wireUser.email, password: 'password' })).resolves.toEqual({
      accessToken: 'token',
      user: {
        id: wireUser.id,
        email: wireUser.email,
        fullName: wireUser.fullName,
        role: wireUser.role,
        tenantId: wireUser.tenantId,
        locationId: wireUser.storeId,
        permissions: {},
        capabilities: [],
        tabs: [],
      },
    });
  });

  test('maps backend storeId to locationId when restoring the user', async () => {
    mockGet.mockResolvedValueOnce({ data: wireUser });

    await expect(getMe()).resolves.toMatchObject({ locationId: 'location-1' });
  });

  test('returns the two-factor challenge without treating it as a login failure', async () => {
    mockPost.mockResolvedValueOnce({
      data: { requiresTwoFactor: true, challengeToken: 'challenge' },
    });

    await expect(login({ email: wireUser.email, password: 'password' })).resolves.toEqual({
      requiresTwoFactor: true,
      challengeToken: 'challenge',
    });
  });

  test('rejects a malformed two-factor challenge response', async () => {
    mockPost.mockResolvedValueOnce({
      data: { requiresTwoFactor: true },
    });

    await expect(login({ email: wireUser.email, password: 'password' })).rejects.toThrow(
      'INVALID_TWO_FACTOR_CHALLENGE'
    );
  });

  test('verifies a two-factor code and maps the authenticated user', async () => {
    mockPost.mockResolvedValueOnce({
      data: { accessToken: 'verified-token', user: wireUser },
    });

    await expect(
      verifyTwoFactor({ challengeToken: 'challenge', code: '123456' })
    ).resolves.toEqual({
      accessToken: 'verified-token',
      user: expect.objectContaining({ locationId: 'location-1' }),
    });
    expect(mockPost).toHaveBeenCalledWith('/auth/2fa/verify', {
      challengeToken: 'challenge',
      code: '123456',
    });
  });
});
