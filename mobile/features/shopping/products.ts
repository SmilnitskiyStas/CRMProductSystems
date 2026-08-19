import { CONSUMER_NEWS, type NewsPromotionProduct } from '@/features/loyalty/news';

export const DISCOUNTED_PRODUCTS_PREVIEW: NewsPromotionProduct[] = [
  {
    id: 'coffee',
    barcode: '4820000200014',
    name: 'Кава мелена',
    unit: '250 г',
    appPrice: 149.9,
    regularPrice: 199.9,
    discountPercent: 25,
    icon: 'cafe-outline',
    background: '#fef3c7',
    manufacturer: 'ТОВ «Кава Україна»',
    countryOfOrigin: 'Україна',
    nutrition: { basis: '100 г', caloriesKcal: 331, proteinG: 13.9, fatG: 14.4, carbsG: 4.1 },
  },
  {
    id: 'milk',
    barcode: '4820000200021',
    name: 'Молоко 2,5%',
    unit: '900 мл',
    appPrice: 39.9,
    regularPrice: 49.9,
    discountPercent: 20,
    icon: 'water-outline',
    background: '#dbeafe',
    manufacturer: 'ПрАТ «Молочний дім»',
    countryOfOrigin: 'Україна',
    nutrition: { basis: '100 мл', caloriesKcal: 52, proteinG: 3, fatG: 2.5, carbsG: 4.7 },
  },
  {
    id: 'apple',
    barcode: '4820000200038',
    name: 'Яблука сезонні',
    unit: '1 кг',
    appPrice: 44.9,
    regularPrice: 59.9,
    discountPercent: 25,
    icon: 'nutrition-outline',
    background: '#dcfce7',
    manufacturer: 'ФГ «Зелений сад»',
    countryOfOrigin: 'Україна',
    nutrition: { basis: '100 г', caloriesKcal: 47, proteinG: 0.4, fatG: 0.4, carbsG: 9.8 },
  },
  {
    id: 'bakery',
    barcode: '4820000200045',
    name: 'Круасан класичний',
    unit: '70 г',
    appPrice: 24.9,
    regularPrice: 32.9,
    discountPercent: 24,
    icon: 'restaurant-outline',
    background: '#ffedd5',
    manufacturer: 'ТОВ «Міська пекарня»',
    countryOfOrigin: 'Україна',
    nutrition: { basis: '100 г', caloriesKcal: 406, proteinG: 8.2, fatG: 21, carbsG: 45.8 },
  },
];

export function findConsumerProduct(
  id: string | undefined,
  tenantId?: string | null
): NewsPromotionProduct | undefined {
  if (id && tenantId) return runtimeProducts.get(`${tenantId}:${id}`);
  if (id && runtimeProducts.has(`preview:${id}`)) return runtimeProducts.get(`preview:${id}`);
  return (
    DISCOUNTED_PRODUCTS_PREVIEW.find((product) => product.id === id) ??
    CONSUMER_NEWS.flatMap((news) => news.promotionProducts ?? []).find(
      (product) => product.id === id
    )
  );
}

export function findConsumerProductByBarcode(
  barcode: string | undefined
): NewsPromotionProduct | undefined {
  if (!barcode) return undefined;
  return (
    DISCOUNTED_PRODUCTS_PREVIEW.find((product) => product.barcode === barcode) ??
    CONSUMER_NEWS.flatMap((news) => news.promotionProducts ?? []).find(
      (product) => product.barcode === barcode
    )
  );
}

const runtimeProducts = new Map<string, NewsPromotionProduct>();

export function registerConsumerProduct(product: NewsPromotionProduct, tenantId = 'preview'): void {
  runtimeProducts.set(`${tenantId}:${product.id}`, product);
}

export function registerConsumerProducts(products: NewsPromotionProduct[], tenantId = 'preview'): void {
  products.forEach((product) => registerConsumerProduct(product, tenantId));
}
