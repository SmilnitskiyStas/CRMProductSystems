import { create } from 'zustand';
import type { NewsPromotionProduct } from '@/features/loyalty/news';

export interface ConsumerCartItem extends NewsPromotionProduct {
  quantity: number;
  tenantId: string | null;
  storeId: string | null;
  sourceNewsId: string;
}

interface ConsumerShoppingState {
  favoriteProductIds: string[];
  cart: ConsumerCartItem[];
  toggleFavorite: (productId: string) => void;
  addToCart: (
    product: NewsPromotionProduct,
    quantity: number,
    context: { tenantId: string | null; storeId: string | null; sourceNewsId: string }
  ) => void;
}

export const useConsumerShoppingStore = create<ConsumerShoppingState>((set) => ({
  favoriteProductIds: [],
  cart: [],
  toggleFavorite: (productId) =>
    set((state) => ({
      favoriteProductIds: state.favoriteProductIds.includes(productId)
        ? state.favoriteProductIds.filter((id) => id !== productId)
        : [...state.favoriteProductIds, productId],
    })),
  addToCart: (product, quantity, context) =>
    set((state) => {
      const existingIndex = state.cart.findIndex(
        (item) =>
          item.id === product.id &&
          item.tenantId === context.tenantId &&
          item.storeId === context.storeId
      );

      if (existingIndex === -1) {
        return {
          cart: [
            ...state.cart,
            {
              ...product,
              quantity,
              tenantId: context.tenantId,
              storeId: context.storeId,
              sourceNewsId: context.sourceNewsId,
            },
          ],
        };
      }

      return {
        cart: state.cart.map((item, index) =>
          index === existingIndex ? { ...item, quantity: item.quantity + quantity } : item
        ),
      };
    }),
}));
