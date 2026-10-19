/**
 * TypeScript mirror of backend PromotionEngine (ShelfGuard.Application/Features/Promotions).
 * Identical semantics, pinned by the shared golden vectors (golden-vectors.json). Pure: no Electron,
 * no DOM. Money is exact fixed-point (BigInt, 1e-12 scale) to reproduce C# `decimal` rounding.
 * DO NOT "fix" an expectation here - fix the engine (and the backend one in lockstep).
 */

// ── types (same model as PromotionDefinition.cs, camelCase JSON) ─────────────

export type PromotionRuleType = 'percent_off' | 'fixed_off' | 'fixed_price' | 'buy_n_get_m';

export interface PromotionDefinition {
  type: string;
  targets?: { itemIds?: string[]; categoryIds?: string[] } | null;
  scope?: {
    storeIds?: string[];
    validFrom?: string | null;
    validTo?: string | null;
    weekdays?: number[];
    timeFrom?: string | null;
    timeTo?: string | null;
  } | null;
  audience?: { kind?: string; tierIds?: string[] } | null;
  params?: {
    percent?: number | null;
    amount?: number | null;
    price?: number | null;
    buyQty?: number | null;
    getQty?: number | null;
    getPrice?: number | null;
    getPercent?: number | null;
    matchMode?: string | null;
  } | null;
  limits?: { maxApplicationsPerReceipt?: number | null } | null;
  excludeAlreadyDiscounted?: boolean;
  priority?: number;
}

export interface PromotionRule {
  id: string;
  versionId: string;
  /** Receipt label. */
  name: string;
  /** "Older rule" tie-break. */
  createdAt: string;
  definition: PromotionDefinition;
}

export interface PromotionLine {
  itemId: string;
  categoryId: string | null;
  /** Price per unit before promotions. */
  unitPrice: number;
  quantity: number;
  hasStockDiscount?: boolean;
  regularPrice?: number | null;
}

export interface PromotionMembership {
  id: string;
  tierId: string | null;
  tierPercent?: number;
}

export interface PromotionContext {
  storeId: string;
  /** Pricing instant (ISO string, Date or epoch ms). */
  at: string | Date | number;
  /** Legacy fixed store-local offset, used only when storeTimeZone is null/unknown. */
  utcOffsetMinutes?: number;
  /** IANA zone of the store (e.g. Europe/Kyiv): weekday/time-window scope is evaluated in it, DST aware. */
  storeTimeZone?: string | null;
  membership?: PromotionMembership | null;
}

export interface PromotionSegment {
  quantity: number;
  unitPrice: number;
  ruleId: string | null;
  label: string | null;
}

export const MIN_PROMO_PRICE = 0.01;
export const MAX_UNITS_PER_RECEIPT = 20_000;

// ── exact decimal helpers ────────────────────────────────────────────────────

const SCALE = 1_000_000_000_000n;
const dec = (x: number): bigint => {
  const s = x.toFixed(12);
  const neg = s.startsWith('-');
  const [i, f] = (neg ? s.slice(1) : s).split('.');
  const v = BigInt(i) * SCALE + BigInt(f);
  return neg ? -v : v;
};
const toNum = (v: bigint): number => Number(v) / 1e12;
const mul = (a: bigint, b: bigint): bigint => (a * b) / SCALE;
const div = (a: bigint, b: bigint): bigint => (a * SCALE) / b;
const HUNDRED = 100n * SCALE;
const CENT = SCALE / 100n;
const MIN_PRICE = dec(MIN_PROMO_PRICE);

type RoundMode = 'away' | 'even';
/** Round to 2 decimal places like Math.Round(x, 2, mode). */
function round2(v: bigint, mode: RoundMode): bigint {
  const neg = v < 0n;
  const a = neg ? -v : v;
  let q = a / CENT;
  const r = a % CENT;
  if (mode === 'away') {
    if (r * 2n >= CENT) q += 1n;
  } else if (r * 2n > CENT || (r * 2n === CENT && q % 2n === 1n)) q += 1n;
  const out = q * CENT;
  return neg ? -out : out;
}
const minD = (a: bigint, b: bigint): bigint => (a < b ? a : b);
const maxD = (a: bigint, b: bigint): bigint => (a > b ? a : b);

// ── normalized rule ──────────────────────────────────────────────────────────

interface Norm {
  id: string;
  name: string;
  createdAtMs: number;
  type: string;
  itemIds: Set<string>;
  categoryIds: Set<string>;
  storeIds: Set<string>;
  validFromMs: number | null;
  validToMs: number | null;
  weekdays: number[];
  timeFromMs: number | null;
  timeToMs: number | null;
  audienceKind: string;
  tierIds: Set<string>;
  percent?: bigint;
  amount?: bigint;
  price?: bigint;
  buyQty?: number;
  getQty?: number;
  getPrice?: bigint;
  getPercent?: bigint;
  matchMode: string | null;
  maxApplications: number | null;
  excludeAlreadyDiscounted: boolean;
  priority: number;
}

const lc = (s: string): string => s.toLowerCase();
const opt = (x: number | null | undefined): bigint | undefined => (x === null || x === undefined ? undefined : dec(x));

/** "HH:mm" or "HH:mm:ss" -> ms since midnight; null when absent; undefined when malformed. */
function parseTime(v: string | null | undefined): number | null | undefined {
  if (v === null || v === undefined || v.trim() === '') return null;
  const m = /^([01]\d|2[0-3]):([0-5]\d)(?::([0-5]\d))?$/.exec(v.trim());
  if (!m) return undefined;
  return ((Number(m[1]) * 60 + Number(m[2])) * 60 + Number(m[3] ?? 0)) * 1000;
}

/** Structural validity (mirror of PromotionEngine.IsValid); returns the normalized rule or null. */
function normalize(rule: PromotionRule): Norm | null {
  const def = rule.definition;
  if (!def) return null;
  // C# defaults apply to OMITTED fields; an explicit null stays null and makes the rule invalid.
  const targets = def.targets === undefined ? { itemIds: [], categoryIds: [] } : def.targets;
  const scope = def.scope === undefined ? {} : def.scope;
  const audience = def.audience === undefined ? {} : def.audience;
  const params = def.params === undefined ? {} : def.params;
  const limits = def.limits === undefined ? {} : def.limits;
  if (targets === null || scope === null || audience === null || params === null || limits === null) return null;

  const itemIds = (targets.itemIds ?? []).map(lc);
  const categoryIds = (targets.categoryIds ?? []).map(lc);
  if (itemIds.length === 0 && categoryIds.length === 0) return null;

  const kind = audience.kind ?? 'all';
  if (kind !== 'all' && kind !== 'loyalty_member' && kind !== 'loyalty_tier') return null;

  const timeFrom = parseTime(scope.timeFrom);
  const timeTo = parseTime(scope.timeTo);
  if (timeFrom === undefined || timeTo === undefined) return null;
  const weekdays = scope.weekdays ?? [];
  if (weekdays.some((d) => d < 1 || d > 7)) return null;

  const p = params;
  const percent = opt(p.percent);
  const amount = opt(p.amount);
  const price = opt(p.price);
  const getPrice = opt(p.getPrice);
  const getPercent = opt(p.getPercent);
  const matchMode = p.matchMode ?? null;

  let valid: boolean;
  switch (def.type) {
    case 'percent_off':
      valid = percent !== undefined && percent > 0n && percent <= HUNDRED;
      break;
    case 'fixed_off':
      valid = amount !== undefined && amount > 0n;
      break;
    case 'fixed_price':
      valid = price !== undefined && price >= MIN_PRICE;
      break;
    case 'buy_n_get_m':
      valid =
        p.buyQty !== null && p.buyQty !== undefined && p.buyQty >= 1 &&
        p.getQty !== null && p.getQty !== undefined && p.getQty >= 1 &&
        ((getPrice !== undefined && getPrice >= 0n) ||
          (getPrice === undefined && getPercent !== undefined && getPercent > 0n && getPercent <= HUNDRED)) &&
        (matchMode === null || matchMode === 'per_item' || matchMode === 'across_set');
      break;
    default:
      valid = false;
  }
  if (!valid) return null;

  return {
    id: lc(rule.id),
    name: rule.name,
    createdAtMs: Date.parse(rule.createdAt),
    type: def.type,
    itemIds: new Set(itemIds),
    categoryIds: new Set(categoryIds),
    storeIds: new Set((scope.storeIds ?? []).map(lc)),
    validFromMs: scope.validFrom ? Date.parse(scope.validFrom) : null,
    validToMs: scope.validTo ? Date.parse(scope.validTo) : null,
    weekdays,
    timeFromMs: timeFrom,
    timeToMs: timeTo,
    audienceKind: kind,
    tierIds: new Set((audience.tierIds ?? []).map(lc)),
    percent,
    amount,
    price,
    buyQty: p.buyQty ?? undefined,
    getQty: p.getQty ?? undefined,
    getPrice,
    getPercent,
    matchMode,
    maxApplications: limits.maxApplicationsPerReceipt ?? null,
    excludeAlreadyDiscounted: def.excludeAlreadyDiscounted ?? false,
    priority: def.priority ?? 100,
  };
}

// ── store-local time ─────────────────────────────────────────────────────────

const formatters = new Map<string, Intl.DateTimeFormat | null>();

/** Intl formatter for an IANA zone; null when blank/unknown (same fallback as StoreTimeZones.Resolve in C#). */
function zoneFormatter(id: string | null | undefined): Intl.DateTimeFormat | null {
  if (!id || id.trim() === '') return null;
  const key = id.trim();
  if (formatters.has(key)) return formatters.get(key)!;
  const candidates = key === 'Europe/Kyiv' ? [key, 'Europe/Kiev'] : key === 'Europe/Kiev' ? [key, 'Europe/Kyiv'] : [key];
  let f: Intl.DateTimeFormat | null = null;
  for (const tz of candidates) {
    try {
      f = new Intl.DateTimeFormat('en-US', {
        timeZone: tz, hourCycle: 'h23', year: 'numeric', month: 'numeric', day: 'numeric',
        hour: 'numeric', minute: 'numeric', second: 'numeric',
      });
      break;
    } catch {
      f = null;
    }
  }
  formatters.set(key, f);
  return f;
}

interface LocalTime {
  /** Local calendar date as a UTC-midnight epoch (only used for weekday arithmetic). */
  dateMs: number;
  /** Time of day in ms. */
  timeMs: number;
}

/** Instant -> store-local date + time of day: the IANA zone (DST aware) when known, else UTC + fixed offset. */
function toLocal(atMs: number, zone: string | null | undefined, offsetMin: number): LocalTime {
  const f = zoneFormatter(zone);
  if (f) {
    const p: Record<string, number> = {};
    for (const part of f.formatToParts(new Date(atMs))) if (part.type !== 'literal') p[part.type] = Number(part.value);
    const ms = ((atMs % 1000) + 1000) % 1000;
    return {
      dateMs: Date.UTC(p.year, p.month - 1, p.day),
      timeMs: ((p.hour % 24) * 3600 + p.minute * 60 + p.second) * 1000 + ms,
    };
  }
  const localMs = atMs + offsetMin * 60_000;
  const timeMs = ((localMs % 86_400_000) + 86_400_000) % 86_400_000;
  return { dateMs: localMs - timeMs, timeMs };
}

function inScope(r: Norm, storeId: string, atMs: number, offsetMin: number, zone: string | null | undefined): boolean {
  if (r.storeIds.size > 0 && !r.storeIds.has(lc(storeId))) return false;
  if (r.validFromMs !== null && atMs < r.validFromMs) return false;
  if (r.validToMs !== null && atMs >= r.validToMs) return false;

  const local = toLocal(atMs, zone, offsetMin);
  const t = local.timeMs;
  const f = r.timeFromMs;
  const e = r.timeToMs;

  // A window crossing midnight (timeFrom > timeTo) belongs to the day it STARTS on: in its
  // after-midnight part the weekday that counts is the previous local day.
  let weekdayDateMs = local.dateMs;
  if (f !== null && e !== null && f > e && t < e) weekdayDateMs -= 86_400_000;
  if (r.weekdays.length > 0) {
    const dow = new Date(weekdayDateMs).getUTCDay(); // 0 = Sunday
    const iso = dow === 0 ? 7 : dow;
    if (!r.weekdays.includes(iso)) return false;
  }

  if (f !== null && e !== null) return f <= e ? t >= f && t < e : t >= f || t < e;
  if (f !== null) return t >= f;
  if (e !== null) return t < e;
  return true;
}

function audienceMatch(r: Norm, m: PromotionMembership | null | undefined): boolean {
  switch (r.audienceKind) {
    case 'all':
      return true;
    case 'loyalty_member':
      return !!m;
    case 'loyalty_tier':
      return !!m && !!m.tierId && r.tierIds.has(lc(m.tierId));
    default:
      return false;
  }
}

// ── prices ───────────────────────────────────────────────────────────────────

const clampPromo = (price: bigint, unitPrice: bigint): bigint =>
  minD(unitPrice, maxD(MIN_PRICE, round2(price, 'away')));

function simplePrice(r: Norm, unitPrice: bigint): bigint {
  switch (r.type) {
    case 'percent_off':
      return clampPromo(div(mul(unitPrice, HUNDRED - r.percent!), HUNDRED), unitPrice);
    case 'fixed_off':
      return clampPromo(unitPrice - r.amount!, unitPrice);
    case 'fixed_price':
      return clampPromo(r.price!, unitPrice);
    default:
      return unitPrice;
  }
}

function getUnitPrice(r: Norm, unitPrice: bigint): bigint {
  return r.getPrice !== undefined
    ? clampPromo(r.getPrice, unitPrice)
    : clampPromo(div(mul(unitPrice, HUNDRED - r.getPercent!), HUNDRED), unitPrice);
}

// ── engine ───────────────────────────────────────────────────────────────────

interface LineD {
  itemId: string;
  categoryId: string | null;
  unitPrice: bigint;
  quantity: bigint;
  hasStockDiscount: boolean;
  regularPrice: bigint | null;
}

interface Cand {
  price: bigint;
  rank: number;
}

interface Unit {
  lineIndex: number;
  unitIndex: number;
  weight: bigint;
  whole: boolean;
  line: LineD;
  base: bigint;
  candidates: Cand[];
  bestSimple: bigint;
  claimed: boolean;
  price: bigint;
  ruleId: string | null;
  label: string | null;
}

const TIER_RANK = -1;

function isEligible(r: Norm, u: Unit): boolean {
  if (r.excludeAlreadyDiscounted && u.line.hasStockDiscount) return false;
  return r.itemIds.has(lc(u.line.itemId)) || (u.line.categoryId !== null && r.categoryIds.has(lc(u.line.categoryId)));
}

/** Compare helper for stable multi-key sorts. */
const cmp = (a: number | bigint, b: number | bigint): number => (a < b ? -1 : a > b ? 1 : 0);

export function applyPromotions(
  lines: readonly PromotionLine[],
  rules: readonly PromotionRule[],
  ctx: PromotionContext,
): PromotionSegment[][] {
  const atMs = typeof ctx.at === 'number' ? ctx.at : ctx.at instanceof Date ? ctx.at.getTime() : Date.parse(ctx.at);
  const offset = ctx.utcOffsetMinutes ?? 0;

  let active: Norm[] = rules
    .map(normalize)
    .filter((r): r is Norm => r !== null && inScope(r, ctx.storeId, atMs, offset, ctx.storeTimeZone) && audienceMatch(r, ctx.membership))
    .sort(
      (a, b) =>
        cmp(a.priority, b.priority) ||
        cmp(a.createdAtMs, b.createdAtMs) ||
        (a.id < b.id ? -1 : a.id > b.id ? 1 : 0),
    );

  const linesD: LineD[] = lines.map((l) => ({
    itemId: l.itemId,
    categoryId: l.categoryId,
    unitPrice: dec(l.unitPrice),
    quantity: dec(l.quantity),
    hasStockDiscount: l.hasStockDiscount ?? false,
    regularPrice: l.regularPrice === null || l.regularPrice === undefined ? null : dec(l.regularPrice),
  }));

  let totalWhole = 0n;
  for (const l of linesD) if (l.quantity > 0n) totalWhole += l.quantity / SCALE;
  if (totalWhole > BigInt(MAX_UNITS_PER_RECEIPT)) active = [];

  const tierPercent = ctx.membership?.tierPercent ? dec(ctx.membership.tierPercent) : 0n;

  // 1. units
  const units: Unit[] = [];
  const mk = (li: number, ui: number, weight: bigint, whole: boolean, line: LineD): Unit => ({
    lineIndex: li, unitIndex: ui, weight, whole, line, base: line.unitPrice,
    candidates: [], bestSimple: line.unitPrice, claimed: false, price: 0n, ruleId: null, label: null,
  });
  linesD.forEach((line, li) => {
    if (line.quantity <= 0n) return;
    if (active.length === 0) {
      units.push(mk(li, 0, line.quantity, false, line));
      return;
    }
    const whole = Number(line.quantity / SCALE);
    for (let k = 0; k < whole; k++) units.push(mk(li, k, SCALE, true, line));
    const frac = line.quantity - BigInt(whole) * SCALE;
    if (frac > 0n) units.push(mk(li, whole, frac, false, line));
  });

  const used = new Array<number>(active.length).fill(0);
  const capacity = (rank: number): number => {
    const max = active[rank].maxApplications;
    return max === null ? Number.POSITIVE_INFINITY : Math.max(0, max) - used[rank];
  };

  // 2. simple candidates + best simple price
  for (const u of units) {
    const cands: Cand[] = [];
    if (tierPercent > 0n) {
      const regular = u.line.regularPrice ?? u.line.unitPrice;
      const tierPrice = maxD(0n, u.base - round2(div(mul(regular, tierPercent), HUNDRED), 'even'));
      if (tierPrice < u.base) cands.push({ price: tierPrice, rank: TIER_RANK });
    }
    for (let rank = 0; rank < active.length; rank++) {
      const r = active[rank];
      if (r.type === 'buy_n_get_m' || !isEligible(r, u)) continue;
      const p = simplePrice(r, u.base);
      if (p < u.base) cands.push({ price: p, rank });
    }
    u.candidates = cands.sort((a, b) => cmp(a.price, b.price) || cmp(a.rank, b.rank));
    u.bestSimple = u.candidates.length > 0 ? u.candidates[0].price : u.base;
  }

  // 3. buy_n_get_m groups
  const formGroups = (r: Norm, cap: number): { groups: Unit[][]; gain: bigint } => {
    const buy = r.buyQty!;
    const get = r.getQty!;
    const size = buy + get;
    const eligible = units.filter((u) => !u.claimed && u.whole && isEligible(r, u));

    let partitions: Unit[][];
    if ((r.matchMode ?? 'per_item') === 'across_set') partitions = [eligible];
    else {
      const byItem = new Map<string, Unit[]>();
      for (const u of eligible) {
        const key = lc(u.line.itemId);
        const arr = byItem.get(key);
        if (arr) arr.push(u);
        else byItem.set(key, [u]);
      }
      partitions = [...byItem.values()].sort(
        (a, b) => Math.min(...a.map((u) => u.lineIndex)) - Math.min(...b.map((u) => u.lineIndex)),
      );
    }

    const groups: Unit[][] = [];
    let gain = 0n;
    for (const part of partitions) {
      const sorted = [...part].sort((a, b) => cmp(b.base, a.base) || cmp(a.lineIndex, b.lineIndex) || cmp(a.unitIndex, b.unitIndex));
      for (let start = 0; start + size <= sorted.length; start += size) {
        if (groups.length >= cap) return { groups, gain };
        const chunk = sorted.slice(start, start + size);
        let cost = 0n;
        let alt = 0n;
        for (let i = 0; i < size; i++) {
          cost += i < buy ? chunk[i].base : getUnitPrice(r, chunk[i].base);
          alt += chunk[i].bestSimple;
        }
        if (alt - cost > 0n) {
          groups.push(chunk);
          gain += alt - cost;
        }
      }
    }
    return { groups, gain };
  };

  for (;;) {
    let bestRank = -1;
    let bestGain = 0n;
    let bestGroups: Unit[][] | null = null;
    for (let rank = 0; rank < active.length; rank++) {
      const r = active[rank];
      if (r.type !== 'buy_n_get_m') continue;
      const cap = capacity(rank);
      if (cap <= 0) continue;
      const { groups, gain } = formGroups(r, cap);
      if (groups.length > 0 && gain > bestGain) {
        bestRank = rank;
        bestGain = gain;
        bestGroups = groups;
      }
    }
    if (bestRank < 0 || !bestGroups) break;

    const rule = active[bestRank];
    const buy = rule.buyQty!;
    for (const group of bestGroups) {
      group.forEach((u, i) => {
        u.claimed = true;
        if (i < buy) u.price = u.base;
        else {
          u.price = getUnitPrice(rule, u.base);
          u.ruleId = rule.id;
          u.label = rule.name;
        }
      });
    }
    used[bestRank] += bestGroups.length;
  }

  // 4. simple assignment for the rest
  const rest = units
    .filter((u) => !u.claimed)
    .sort(
      (a, b) =>
        cmp(mul(b.base - b.bestSimple, b.weight), mul(a.base - a.bestSimple, a.weight)) ||
        cmp(a.lineIndex, b.lineIndex) ||
        cmp(a.unitIndex, b.unitIndex),
    );
  for (const u of rest) {
    u.price = u.base;
    for (const c of u.candidates) {
      if (c.rank === TIER_RANK) {
        u.price = c.price;
        break;
      }
      if (capacity(c.rank) <= 0) continue;
      used[c.rank]++;
      u.price = c.price;
      u.ruleId = active[c.rank].id;
      u.label = active[c.rank].name;
      break;
    }
    u.claimed = true;
  }

  // 5. segments
  return linesD.map((line, li) => {
    if (line.quantity <= 0n) {
      return [{ quantity: lines[li].quantity, unitPrice: lines[li].unitPrice, ruleId: null, label: null }];
    }
    const segs: { price: bigint; ruleId: string | null; label: string | null; qty: bigint }[] = [];
    for (const u of units.filter((x) => x.lineIndex === li).sort((a, b) => a.unitIndex - b.unitIndex)) {
      const s = segs.find((x) => x.price === u.price && x.ruleId === u.ruleId);
      if (s) s.qty += u.weight;
      else segs.push({ price: u.price, ruleId: u.ruleId, label: u.label, qty: u.weight });
    }
    return segs.map((s) => ({ quantity: toNum(s.qty), unitPrice: toNum(s.price), ruleId: s.ruleId, label: s.label }));
  });
}
