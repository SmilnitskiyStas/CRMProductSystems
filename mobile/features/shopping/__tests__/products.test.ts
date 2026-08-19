import { findConsumerProduct, registerConsumerProduct } from '../products';
import type { NewsPromotionProduct } from '@/features/loyalty/news';

function product(name: string): NewsPromotionProduct {
  return {
    id: 'shared-id', barcode: null, name, unit: 'шт', regularPrice: 10, appPrice: null,
    discountPercent: null, icon: 'cube-outline', background: '#ffffff', manufacturer: null,
    countryOfOrigin: null,
  };
}

test('runtime product details are isolated by tenant', () => {
  registerConsumerProduct(product('Tenant A product'), 'tenant-a');
  registerConsumerProduct(product('Tenant B product'), 'tenant-b');
  expect(findConsumerProduct('shared-id', 'tenant-a')?.name).toBe('Tenant A product');
  expect(findConsumerProduct('shared-id', 'tenant-b')?.name).toBe('Tenant B product');
  expect(findConsumerProduct('shared-id', 'tenant-c')).toBeUndefined();
});
