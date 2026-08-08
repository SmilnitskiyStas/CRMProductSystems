import { apiClient } from '@/lib/api-client';
import { mobileLogin, mobileRegister } from '../mobileAuthApi';

jest.mock('@/lib/api-client', () => ({
  apiClient: { post: jest.fn() },
}));

const mockPost = apiClient.post as jest.MockedFunction<typeof apiClient.post>;

describe('mobileAuthApi', () => {
  beforeEach(() => jest.clearAllMocks());

  test('uses the single mobile login endpoint for an email identifier, returning both tokens for a linked staff member', async () => {
    const response = {
      personalAccessToken: 'consumer-token',
      workspaceAccessToken: 'staff-token',
      user: {
        id: 'staff-1', fullName: 'Staff User', email: 'staff@example.com', phone: null,
        tenantId: 'tenant-1', storeId: 'store-1',
      },
      access: {
        canAccessWorkspace: true, role: 'staff', permissions: {}, capabilities: [], tabs: [],
      },
    };
    mockPost.mockResolvedValueOnce({ data: response });

    await expect(mobileLogin({ identifier: 'staff@example.com', password: 'secret' }))
      .resolves.toEqual(response);
    expect(mockPost).toHaveBeenCalledWith('/mobile-auth/login', {
      identifier: 'staff@example.com', password: 'secret',
    });
  });

  test('returns a personal-only token for a plain consumer with no workspace access', async () => {
    const response = {
      personalAccessToken: 'consumer-token',
      workspaceAccessToken: null,
      user: {
        id: 'consumer-1', fullName: 'Consumer User', email: null, phone: '+380501234567',
        tenantId: null, storeId: null,
      },
      access: {
        canAccessWorkspace: false, role: 'consumer', permissions: {}, capabilities: [], tabs: [],
      },
    };
    mockPost.mockResolvedValueOnce({ data: response });

    await expect(mobileLogin({ identifier: '+380501234567', password: 'secret' }))
      .resolves.toEqual(response);
  });

  test('preserves a two-factor challenge from the gateway, including the mid-challenge personal token', async () => {
    const challenge = {
      requiresTwoFactor: true as const,
      challengeToken: 'challenge',
      personalAccessToken: 'consumer-token',
    };
    mockPost.mockResolvedValueOnce({ data: challenge });

    await expect(mobileLogin({ identifier: 'staff@example.com', password: 'secret' }))
      .resolves.toEqual(challenge);
  });

  test('registers through the same gateway so linked workspace access is returned immediately', async () => {
    const response = {
      personalAccessToken: 'consumer-token',
      workspaceAccessToken: 'staff-token',
      user: {
        id: 'staff-1', fullName: 'Staff User', email: 'staff@example.com', phone: null,
        tenantId: 'tenant-1', storeId: 'store-1',
      },
      access: {
        canAccessWorkspace: true, role: 'staff', permissions: {}, capabilities: [], tabs: [],
      },
    };
    mockPost.mockResolvedValueOnce({ data: response });
    const request = {
      phone: '+380501234567', password: 'StrongPassword1', fullName: 'Staff User',
    };

    await expect(mobileRegister(request)).resolves.toEqual(response);
    expect(mockPost).toHaveBeenCalledWith('/mobile-auth/register', request);
  });
});
