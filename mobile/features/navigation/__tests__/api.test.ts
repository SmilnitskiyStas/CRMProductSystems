import MockAdapter from 'axios-mock-adapter';
import { apiClient } from '@/lib/api-client';
import { getModulesSettings } from '../api';

describe('modules settings API', () => {
  test('loads and sanitizes own-tenant business/module context', async () => {
    const mock = new MockAdapter(apiClient);
    mock.onGet('/settings/modules').reply(200, {
      businessType: 'retail',
      modules: ['inventory', 'pos', 42],
    });
    await expect(getModulesSettings()).resolves.toEqual({
      businessType: 'retail',
      modules: ['inventory', 'pos'],
    });
    mock.restore();
  });
});
