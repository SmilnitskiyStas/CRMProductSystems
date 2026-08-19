import Ajv, { type ErrorObject, type ValidateFunction } from 'ajv';
import type { MobileConfig } from './types';

const draftMobileConfigSchema = {
  $id: 'shelfguard.mobile-config.stage3-draft.v0',
  type: 'object',
  additionalProperties: false,
  required: [
    'schemaVersion',
    'configVersion',
    'tenant',
    'theme',
    'features',
    'navigation',
    'pages',
  ],
  properties: {
    schemaVersion: { enum: [0, 1] },
    configVersion: { type: 'integer', minimum: 1 },
    tenant: {
      type: 'object',
      additionalProperties: false,
      required: ['id', 'slug', 'name', 'logoUrl'],
      properties: {
        id: { type: 'string', minLength: 1, maxLength: 128 },
        slug: { type: 'string', minLength: 1, maxLength: 128 },
        name: { type: 'string', minLength: 1, maxLength: 200 },
        logoUrl: { type: ['string', 'null'], maxLength: 2048 },
      },
    },
    theme: {
      type: 'object',
      additionalProperties: false,
      required: ['colors', 'buttons', 'cards', 'spacing'],
      properties: {
        logoUrl: { type: ['string', 'null'], maxLength: 2048 },
        colors: {
          type: 'object',
          additionalProperties: false,
          required: [
            'primary',
            'secondary',
            'background',
            'surface',
            'textPrimary',
            'textSecondary',
          ],
          properties: {
            primary: { type: 'string', pattern: '^#[0-9A-Fa-f]{6}$' },
            secondary: { type: 'string', pattern: '^#[0-9A-Fa-f]{6}$' },
            background: { type: 'string', pattern: '^#[0-9A-Fa-f]{6}$' },
            surface: { type: 'string', pattern: '^#[0-9A-Fa-f]{6}$' },
            textPrimary: { type: 'string', pattern: '^#[0-9A-Fa-f]{6}$' },
            textSecondary: { type: 'string', pattern: '^#[0-9A-Fa-f]{6}$' },
          },
        },
        buttons: {
          type: 'object',
          additionalProperties: false,
          required: ['radius'],
          properties: { radius: { type: 'number', minimum: 0, maximum: 32 } },
        },
        cards: {
          type: 'object',
          additionalProperties: false,
          required: ['radius'],
          properties: { radius: { type: 'number', minimum: 0, maximum: 40 } },
        },
        spacing: { enum: ['compact', 'comfortable'] },
      },
    },
    features: {
      type: 'object',
      additionalProperties: false,
      required: [
        'loyalty',
        'promotions',
        'catalog',
        'coupons',
        'news',
        'receipts',
        'delivery',
        'personalOffers',
      ],
      properties: Object.fromEntries(
        [
          'loyalty',
          'promotions',
          'catalog',
          'coupons',
          'news',
          'receipts',
          'delivery',
          'personalOffers',
        ].map((key) => [key, { type: 'boolean' }])
      ),
    },
    navigation: {
      type: 'array',
      minItems: 2,
      maxItems: 5,
      items: {
        type: 'object',
        additionalProperties: false,
        required: ['type', 'label', 'icon'],
        properties: {
          type: {
            enum: ['home', 'promotions', 'catalog', 'loyalty', 'coupons', 'stores', 'news', 'profile'],
          },
          label: { type: 'string', minLength: 1, maxLength: 30 },
          icon: { enum: ['home', 'tag', 'grid', 'qr', 'ticket', 'map', 'news', 'user'] },
        },
      },
    },
    pages: {
      type: 'object',
      additionalProperties: {
        type: 'object',
        additionalProperties: false,
        required: ['blocks'],
        properties: {
          blocks: {
            type: 'array',
            items: {
              type: 'object',
              additionalProperties: false,
              required: ['id', 'type', 'props'],
              properties: {
                id: { type: 'string', minLength: 1, maxLength: 128 },
                type: { type: 'string', minLength: 1, maxLength: 64 },
                order: { type: 'integer', minimum: 0 },
                feature: {
                  enum: [
                    'loyalty',
                    'promotions',
                    'catalog',
                    'coupons',
                    'news',
                    'receipts',
                    'delivery',
                    'personalOffers',
                  ],
                },
                props: { type: 'object' },
              },
            },
          },
        },
      },
    },
  },
} as const;

const ajv = new Ajv({ allErrors: true, strict: false });
const validateDraft = ajv.compile(draftMobileConfigSchema) as ValidateFunction<MobileConfig>;

const featureKeys = [
  'loyalty', 'promotions', 'catalog', 'coupons', 'news', 'receipts', 'delivery', 'personalOffers',
] as const;

const canonicalIconMap: Record<string, string> = {
  'home-outline': 'home', 'pricetag-outline': 'tag', 'grid-outline': 'grid',
  'qr-code-outline': 'qr', 'ticket-outline': 'ticket', 'map-outline': 'map',
  'newspaper-outline': 'news', 'person-outline': 'user',
};

function normalizeCanonicalConfig(value: unknown): unknown {
  if (!value || typeof value !== 'object' || (value as { schemaVersion?: unknown }).schemaVersion !== 1) return value;
  const candidate = value as Record<string, unknown>;
  const rawFeatures = candidate.features && typeof candidate.features === 'object'
    ? candidate.features as Record<string, unknown>
    : {};
  const defaults = Object.fromEntries(featureKeys.map((key) => [key, false]));
  const features = { ...defaults, ...rawFeatures };
  const navigation = Array.isArray(candidate.navigation)
    ? candidate.navigation.map((item) => {
        if (!item || typeof item !== 'object') return item;
        const record = item as Record<string, unknown>;
        const icon = typeof record.icon === 'string' ? canonicalIconMap[record.icon] ?? record.icon : record.icon;
        return { ...record, icon };
      })
    : candidate.navigation;
  return { ...candidate, features, navigation };
}

export interface MobileConfigValidationResult {
  valid: boolean;
  config: MobileConfig | null;
  errors: readonly ErrorObject[];
}

export function validateMobileConfig(value: unknown): MobileConfigValidationResult {
  const normalized = normalizeCanonicalConfig(value);
  const valid = validateDraft(normalized);
  if (!valid) {
    return {
      valid: false,
      config: null,
      errors: validateDraft.errors ? [...validateDraft.errors] : [],
    };
  }

  const routeTypes = normalized.navigation.map((item) => item.type);
  const navigationIsSafe =
    new Set(routeTypes).size === routeTypes.length &&
    routeTypes.includes('home') &&
    routeTypes.includes('profile');
  return {
    valid: navigationIsSafe,
    config: navigationIsSafe ? normalized : null,
    errors: [],
  };
}
