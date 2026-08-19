import { isPosOffline } from '../networkPolicy';

describe('POS online-only policy', () => {
  test.each([
    [{ isConnected: false, isInternetReachable: false }, true],
    [{ isConnected: true, isInternetReachable: false }, true],
    [{ isConnected: true, isInternetReachable: true }, false],
    [{ isConnected: true, isInternetReachable: null }, false],
  ] as const)('classifies reachability %p', (network, expected) => {
    expect(isPosOffline(network)).toBe(expected);
  });
});
