import { canEnableMobilePreview, previewRequestHeaders } from '../policy';

describe('mobile preview security policy', () => {
  test('production builds can never enable preview', () => {
    expect(canEnableMobilePreview(false, 'valid-preview-token')).toBe(false);
  });

  test('development requires an explicit non-trivial token', () => {
    expect(canEnableMobilePreview(true, 'short')).toBe(false);
    expect(canEnableMobilePreview(true, ' valid-preview-token ')).toBe(true);
  });

  test('token is sent only through the dedicated header', () => {
    expect(previewRequestHeaders(' token-value-1234 ')).toEqual({
      'X-Mobile-Preview-Token': 'token-value-1234',
    });
  });
});
