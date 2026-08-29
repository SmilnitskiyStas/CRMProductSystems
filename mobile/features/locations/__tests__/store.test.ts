import { useWorkspaceLocationStore } from '../store';

describe('workspace location selection', () => {
  beforeEach(() => useWorkspaceLocationStore.getState().reset());

  test('initializes once and preserves an explicit user selection across screens', () => {
    useWorkspaceLocationStore.getState().initializeLocation('store-1');
    useWorkspaceLocationStore.getState().selectLocation('store-2');
    useWorkspaceLocationStore.getState().initializeLocation('store-1');

    expect(useWorkspaceLocationStore.getState().selectedLocationId).toBe('store-2');
  });

  test('uses null as an explicit all-stores selection', () => {
    useWorkspaceLocationStore.getState().selectLocation(null);
    expect(useWorkspaceLocationStore.getState().selectedLocationId).toBeNull();
  });
});
