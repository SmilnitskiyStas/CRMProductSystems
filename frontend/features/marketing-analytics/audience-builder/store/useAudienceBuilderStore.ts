"use client";

import { create } from "zustand";
import type { AudienceCombineMode, AudienceTerm, CompetitorHorizon } from "../types";

/**
 * UI/session state for the query builder (CLAUDE.md: "Zustand — тільки UI state"). Deliberately
 * NOT persisted (no `zustand/middleware` `persist`, unlike `lib/useStoreContext.ts`'s selected
 * store) — the brief calls the competitor sub-state "сесійний" (session-only), and the same holds
 * for the whole builder: a page reload starts a fresh audience, matching the competitor's own
 * "Скинути" returning to the initial empty state rather than surviving a refresh.
 *
 * Deep-nested control components (TermChips, ThresholdInputs, MatchedItemsTable's checkboxes,
 * CompetitorTermInput, HorizonToggle) read/write this store directly via selectors — this is the
 * reason a Zustand store exists here at all when Фаза 1/2 don't use one: those pages are simple
 * period+store dashboards, this is a multi-step form spread across ~10 leaf components 3 folders
 * deep, where prop-drilling every setter down through ResultTabs → BuyersTab/CompetitorTab/
 * MatchedItemsTab would be worse than a small local store.
 *
 * Query-owning components (page.tsx and the *Table.tsx components it feeds) still go through
 * React Query for all server data — this store only ever holds the FILTER the caller is about to
 * ask for, never a fetched result.
 */

function makeTermId(): string {
  return typeof crypto !== "undefined" && "randomUUID" in crypto
    ? crypto.randomUUID()
    : `term_${Date.now()}_${Math.random().toString(36).slice(2)}`;
}

interface AudienceBuilderState {
  // ── Own-product term builder ──────────────────────────────────────────────────────────────
  terms: AudienceTerm[];
  mode: AudienceCombineMode;
  minQuantity: number | null;
  minAmount: number | null;
  /** SKUs manually unchecked in "Знайдені товари". Empty = nothing excluded. */
  excludedItemIds: string[];
  /** Flips true on "Сформувати список" (only possible once `terms.length > 0`). Once true, ANY
   * further change below (terms/mode/thresholds/exclusions/competitor terms/horizon) live-
   * recalculates via React Query's query-key change — never requires re-clicking the build
   * button (mandatory brief requirement: exclusion → "миттєвий перерахунок"). Only "Скинути"
   * flips it back to false. */
  isBuilt: boolean;

  // ── Competitor tab (design doc: "competitor-стан (сесійний)") ────────────────────────────
  competitorTerms: AudienceTerm[];
  horizon: CompetitorHorizon;

  addTextTerm: (text: string) => void;
  addCategoryTerm: (categoryId: string, name: string, itemCount: number) => void;
  removeTerm: (id: string) => void;
  setMode: (mode: AudienceCombineMode) => void;
  setMinQuantity: (v: number | null) => void;
  setMinAmount: (v: number | null) => void;
  excludeItem: (itemId: string) => void;
  includeItem: (itemId: string) => void;
  /** "Обрати всі" — clears the whole exclusion list (design doc §6). */
  selectAllItems: () => void;
  /** "Зняти показані" — excludes only the given (current-page) item ids, union'd with whatever
   * was already excluded (design doc §6: never touches SKUs outside the currently loaded page). */
  deselectShownItems: (itemIds: string[]) => void;
  build: () => void;

  addCompetitorTerm: (text: string) => void;
  removeCompetitorTerm: (id: string) => void;
  setHorizon: (h: CompetitorHorizon) => void;

  /** Returns the whole builder to its initial (pre-build) state. Deliberately does NOT touch
   * period/store selection (owned by page.tsx, not this store) — "Скинути" clears the query
   * builder, not the surrounding date/store context the user was already working in. */
  reset: () => void;
}

const INITIAL: Pick<
  AudienceBuilderState,
  "terms" | "mode" | "minQuantity" | "minAmount" | "excludedItemIds" | "isBuilt" | "competitorTerms" | "horizon"
> = {
  terms: [],
  mode: "Any",
  minQuantity: null,
  minAmount: null,
  excludedItemIds: [],
  isBuilt: false,
  competitorTerms: [],
  horizon: "InPeriod",
};

export const useAudienceBuilderStore = create<AudienceBuilderState>()((set, get) => ({
  ...INITIAL,

  addTextTerm: (text) => {
    const trimmed = text.trim();
    if (!trimmed) return;
    set((s) => ({
      terms: [...s.terms, { id: makeTermId(), kind: "Text", text: trimmed, categoryId: null, categoryName: null, categoryItemCount: null }],
    }));
  },

  addCategoryTerm: (categoryId, name, itemCount) =>
    set((s) => {
      if (s.terms.some((t) => t.kind === "Category" && t.categoryId === categoryId)) return s;
      return {
        terms: [
          ...s.terms,
          { id: makeTermId(), kind: "Category", text: null, categoryId, categoryName: name, categoryItemCount: itemCount },
        ],
      };
    }),

  removeTerm: (id) => set((s) => ({ terms: s.terms.filter((t) => t.id !== id) })),
  setMode: (mode) => set({ mode }),
  setMinQuantity: (v) => set({ minQuantity: v }),
  setMinAmount: (v) => set({ minAmount: v }),

  excludeItem: (itemId) =>
    set((s) => (s.excludedItemIds.includes(itemId) ? s : { excludedItemIds: [...s.excludedItemIds, itemId] })),
  includeItem: (itemId) => set((s) => ({ excludedItemIds: s.excludedItemIds.filter((id) => id !== itemId) })),
  selectAllItems: () => set({ excludedItemIds: [] }),
  deselectShownItems: (itemIds) =>
    set((s) => {
      const merged = new Set(s.excludedItemIds);
      for (const id of itemIds) merged.add(id);
      return { excludedItemIds: [...merged] };
    }),

  build: () => {
    if (get().terms.length === 0) return;
    set({ isBuilt: true });
  },

  addCompetitorTerm: (text) => {
    const trimmed = text.trim();
    if (!trimmed) return;
    set((s) => ({
      competitorTerms: [
        ...s.competitorTerms,
        { id: makeTermId(), kind: "Text", text: trimmed, categoryId: null, categoryName: null, categoryItemCount: null },
      ],
    }));
  },
  removeCompetitorTerm: (id) => set((s) => ({ competitorTerms: s.competitorTerms.filter((t) => t.id !== id) })),
  setHorizon: (h) => set({ horizon: h }),

  reset: () => set({ ...INITIAL }),
}));
