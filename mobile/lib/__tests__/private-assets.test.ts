import { privateAssetSource } from '../private-assets';

const base = 'https://api.example.com/api';
const tenant = '11111111-1111-1111-1111-111111111111';
const path = `/uploads/shelf-scans/${tenant}/synthetic.jpg`;

describe('private asset sources', () => {
  it('uses header authorization on the configured API origin', () => {
    expect(privateAssetSource(path, 'synthetic-token', base)).toEqual({
      uri: `https://api.example.com/api/private-files/shelf-scans/${tenant}/synthetic.jpg`,
      headers: { Authorization: 'Bearer synthetic-token' }, cache: 'reload',
    });
  });

  it.each([`https://external.example${path}`, `//external.example${path}`, `${path}?access_token=marker`,
    `/uploads/items/${tenant}/public.jpg`, `/uploads/contracts/${tenant}/contract.pdf`, 'javascript:alert(1)'])
  ('never forwards a workspace token to unsafe or unrelated sources: %s', (source) => {
    expect(privateAssetSource(source, 'synthetic-token', base)).toBeNull();
  });

  it('requires a workspace session', () => {
    expect(privateAssetSource(path, null, base)).toBeNull();
    expect(privateAssetSource(null, 'synthetic-token', base)).toBeNull();
  });
});
