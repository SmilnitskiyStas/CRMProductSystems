// Floor-plan object types. `zone.type` is a free string on the backend, so legacy values
// (refrigerated, fresh, dry, frozen, freezer, display, production, warehouse) must keep rendering.
export interface ZoneTypeMeta {
  icon: string;
  /** Accent color for border/labels of non-stock types. */
  color: string;
  /** Whether the object holds stock (shows FEFO status) or is a pure map marker. */
  holdsStock: boolean;
}

// Palette offered when creating an object (order = display order).
export const ZONE_PALETTE_TYPES = [
  "shelf",
  "fridge",
  "checkout",
  "entrance_exit",
  "department",
  "storage",
  "other",
] as const;

export const ZONE_TYPE_META: Record<string, ZoneTypeMeta> = {
  shelf: { icon: "📦", color: "#6366F1", holdsStock: true },
  fridge: { icon: "❄️", color: "#38BDF8", holdsStock: true },
  checkout: { icon: "🧾", color: "#F59E0B", holdsStock: false },
  entrance_exit: { icon: "🚪", color: "#22C55E", holdsStock: false },
  department: { icon: "🏷️", color: "#A855F7", holdsStock: true },
  storage: { icon: "🏭", color: "#94A3B8", holdsStock: true },
  other: { icon: "🗂️", color: "#9CA3AF", holdsStock: true },
  // legacy
  refrigerated: { icon: "❄️", color: "#38BDF8", holdsStock: true },
  fresh: { icon: "🌿", color: "#22C55E", holdsStock: true },
  dry: { icon: "📦", color: "#6366F1", holdsStock: true },
  frozen: { icon: "🧊", color: "#7DD3FC", holdsStock: true },
  freezer: { icon: "🧊", color: "#7DD3FC", holdsStock: true },
  display: { icon: "🪟", color: "#A855F7", holdsStock: true },
  production: { icon: "⚙️", color: "#F97316", holdsStock: true },
  warehouse: { icon: "🏭", color: "#94A3B8", holdsStock: true },
};

const FALLBACK: ZoneTypeMeta = { icon: "🗂️", color: "#9CA3AF", holdsStock: true };

export function zoneTypeMeta(type: string): ZoneTypeMeta {
  return ZONE_TYPE_META[type] ?? FALLBACK;
}
