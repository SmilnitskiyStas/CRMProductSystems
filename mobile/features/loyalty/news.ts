import type { ComponentProps } from 'react';
import type { Ionicons } from '@expo/vector-icons';

export interface NewsPromotionProduct {
  id: string;
  barcode: string | null;
  name: string;
  unit: string;
  regularPrice: number | null;
  appPrice: number | null;
  discountPercent: number | null;
  imageUrl?: string | null;
  icon: ComponentProps<typeof Ionicons>['name'];
  background: string;
  manufacturer: string | null;
  countryOfOrigin: string | null;
  nutrition?: {
    basis: string;
    caloriesKcal: number;
    proteinG: number;
    fatG: number;
    carbsG: number;
  };
}

export interface ConsumerNewsItem {
  id: string;
  icon: ComponentProps<typeof Ionicons>['name'];
  eyebrow: string | null;
  title: string;
  description: string;
  body: string[];
  validUntil: string;
  terms: string[];
  promotionProducts?: NewsPromotionProduct[];
  background: string;
  accent: string;
  imageUrl?: string | null;
  detailMode?: string;
  externalUrl?: string | null;
}

export const CONSUMER_NEWS: ConsumerNewsItem[] = [
  {
    id: 'welcome-bonus',
    icon: 'gift-outline',
    eyebrow: 'Бонусна програма',
    title: 'Бонуси за кожну покупку',
    description: 'Показуйте картку на касі та отримуйте більше вигоди.',
    body: [
      'Накопичуйте бонуси під час покупок у вибраному магазині та використовуйте їх для наступних покупок.',
      'Щоб бонуси були нараховані, відкрийте картку покупця в застосунку та покажіть код касиру до завершення оплати.',
    ],
    validUntil: 'Постійна пропозиція',
    terms: [
      'Картку покупця потрібно показати до закриття чека.',
      'Розмір нарахування визначається правилами вибраної мережі.',
      'Бонуси можуть не нараховуватися на окремі категорії товарів.',
      'Баланс оновлюється після успішного завершення покупки.',
    ],
    background: '#14532d',
    accent: '#86efac',
  },
  {
    id: 'fresh-every-day',
    icon: 'leaf-outline',
    eyebrow: 'Свіже щодня',
    title: 'Обирайте найкраще поруч',
    description: 'Ваш улюблений магазин і актуальні пропозиції завжди під рукою.',
    body: [
      'Виберіть магазин у верхній частині головної сторінки, щоб бачити новини та знижки, актуальні саме для цієї точки.',
      'Асортимент і наявність можуть відрізнятися залежно від адреси магазину та часу відвідування.',
    ],
    validUntil: 'Діє щодня',
    terms: [
      'Пропозиції відображаються для вибраного магазину.',
      'Наявність товару необхідно уточнювати безпосередньо в магазині.',
      'Ціни можуть змінюватися після завершення строку акції.',
    ],
    background: '#1e3a8a',
    accent: '#93c5fd',
  },
  {
    id: 'personal-offers',
    icon: 'pricetag-outline',
    eyebrow: 'Знижка із застосунком',
    title: 'Додаткова знижка на обрані товари',
    description: 'Покажіть картку покупця в застосунку та придбайте акційні товари вигідніше.',
    body: [
      'Для учасників програми лояльності діє спеціальна ціна на товари з переліку нижче. Щоб отримати додаткову знижку, відкрийте картку покупця в застосунку та покажіть її касиру.',
      'Ціна в застосунку застосовується після ідентифікації покупця на касі та за наявності товару у вибраному магазині.',
    ],
    validUntil: 'До 31 серпня 2026 року',
    terms: [
      'Персональні пропозиції доступні лише авторизованим учасникам програми.',
      'Картку покупця потрібно показати касиру до завершення оплати.',
      'Знижка діє лише на товари, позначені в переліку цієї пропозиції.',
      'Пропозиція діє за наявності товару та може не поєднуватися з іншими акціями.',
      'Кількість акційного товару в одному чеку може бути обмежена.',
    ],
    promotionProducts: [
      {
        id: 'app-coffee',
        barcode: '4820000100017',
        name: 'Кава мелена',
        unit: '250 г',
        regularPrice: 199.9,
        appPrice: 139.9,
        discountPercent: 30,
        icon: 'cafe-outline',
        background: '#fef3c7',
        manufacturer: 'ТОВ «Кава Україна»',
        countryOfOrigin: 'Україна',
        nutrition: { basis: '100 г', caloriesKcal: 331, proteinG: 13.9, fatG: 14.4, carbsG: 4.1 },
      },
      {
        id: 'app-milk',
        barcode: '4820000100024',
        name: 'Молоко 2,5%',
        unit: '900 мл',
        regularPrice: 49.9,
        appPrice: 34.9,
        discountPercent: 30,
        icon: 'water-outline',
        background: '#dbeafe',
        manufacturer: 'ПрАТ «Молочний дім»',
        countryOfOrigin: 'Україна',
        nutrition: { basis: '100 мл', caloriesKcal: 52, proteinG: 3, fatG: 2.5, carbsG: 4.7 },
      },
      {
        id: 'app-apples',
        barcode: '4820000100031',
        name: 'Яблука сезонні',
        unit: '1 кг',
        regularPrice: 59.9,
        appPrice: 39.9,
        discountPercent: 33,
        icon: 'nutrition-outline',
        background: '#dcfce7',
        manufacturer: 'ФГ «Зелений сад»',
        countryOfOrigin: 'Україна',
        nutrition: { basis: '100 г', caloriesKcal: 47, proteinG: 0.4, fatG: 0.4, carbsG: 9.8 },
      },
      {
        id: 'app-croissant',
        barcode: '4820000100048',
        name: 'Круасан класичний',
        unit: '70 г',
        regularPrice: 32.9,
        appPrice: 22.9,
        discountPercent: 30,
        icon: 'restaurant-outline',
        background: '#ffedd5',
        manufacturer: 'ТОВ «Міська пекарня»',
        countryOfOrigin: 'Україна',
        nutrition: { basis: '100 г', caloriesKcal: 406, proteinG: 8.2, fatG: 21, carbsG: 45.8 },
      },
    ],
    background: '#7c2d12',
    accent: '#fdba74',
  },
];

export function findConsumerNews(id: string | undefined): ConsumerNewsItem | undefined {
  return CONSUMER_NEWS.find((item) => item.id === id);
}
