import MockAdapter from 'axios-mock-adapter';
import { personalApiClient } from '@/lib/api-client';
import { publishedMobileConfigRepository } from '../repository';

const mock = new MockAdapter(personalApiClient);

test('published repository uses the anonymous/consumer config endpoint with explicit tenant', async () => {
  mock.onGet('/v1/mobile/config').reply((request) => {
    expect(request.params).toEqual({ tenantId: 'tenant-a' });
    return [200, { schemaVersion: 1 }];
  });
  await expect(publishedMobileConfigRepository.getConfig('tenant-a')).resolves.toEqual({ schemaVersion: 1 });
  mock.reset();
});
