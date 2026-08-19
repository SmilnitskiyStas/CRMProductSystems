import {
  AppRoles,
  AT_LEAST_STORE_MANAGER,
  CAN_ACCESS_POS,
  hasRole,
} from '../roles';

describe('mobile role gates', () => {
  test('allows the canonical cashier role into POS', () => {
    expect(hasRole(AppRoles.Cashier, CAN_ACCESS_POS)).toBe(true);
  });

  test('does not allow a merchandiser into POS', () => {
    expect(hasRole(AppRoles.Merchandiser, CAN_ACCESS_POS)).toBe(false);
  });

  test('uses the real lowercase role names for manager gates', () => {
    expect(hasRole('store_manager', AT_LEAST_STORE_MANAGER)).toBe(true);
    expect(hasRole('StoreManager', AT_LEAST_STORE_MANAGER)).toBe(false);
  });

  test('fails closed for a missing role', () => {
    expect(hasRole(undefined, CAN_ACCESS_POS)).toBe(false);
    expect(hasRole(null, CAN_ACCESS_POS)).toBe(false);
  });
});
