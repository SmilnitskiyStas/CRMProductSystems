export type ModuleKey =
  | "inventory"
  | "procurement"
  | "pos"
  | "auto_service"
  | "production"
  | "marketplace";

/** GET /api/settings/modules response */
export interface ModulesSettings {
  businessType: string;
  modules: ModuleKey[];
}

export interface ModuleMeta {
  key: ModuleKey;
  label: string;
  description: string;
}

/** Full module catalog (ADR-015) — used to render the toggle list with descriptions. */
export const ALL_MODULES: ModuleMeta[] = [
  {
    key: "inventory",
    label: "Інвентаризація",
    description: "Каталог товарів, залишки, прийомка, переміщення, списання.",
  },
  {
    key: "procurement",
    label: "Постачання",
    description: "Постачальники, замовлення постачання, AI-прогнозування закупівель.",
  },
  {
    key: "pos",
    label: "Каса (POS)",
    description: "Робота з кассовим апаратом, продажі, фіскалізація чеків.",
  },
  {
    key: "auto_service",
    label: "Автосервіс",
    description: "Клієнти, автомобілі, наряд-замовлення, облік запчастин.",
  },
  {
    key: "production",
    label: "Виробництво",
    description: "Рецепти, виробничі замовлення, списання сировини.",
  },
  {
    key: "marketplace",
    label: "Маркетплейс постачальників",
    description: "Пошук і порівняння постачальників, відгуки, рейтинги.",
  },
];

export const BUSINESS_TYPE_LABELS: Record<string, string> = {
  retail: "Роздрібна торгівля",
  auto_service: "Автосервіс",
  warehouse: "Склад",
  restaurant: "Ресторан",
  production: "Виробництво",
  distribution: "Дистрибуція",
};
