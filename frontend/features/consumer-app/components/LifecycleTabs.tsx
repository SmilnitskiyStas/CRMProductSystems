"use client";

import { SectionTabs } from "./SectionTabs";

/**
 * TASK-525: shared 3-tab strip (Активні / Минулі / Чернетки) reused identically by
 * BannersSection and PromoProductsSection — both filter an already-fetched list client-side
 * by a lifecycle bucket, no per-tab API call. Same visual pattern as
 * marketing-analytics/price-segments/components/ModeTabs.tsx (underline tabs), extended with
 * an optional count badge per tab.
 */
export type LifecycleTab = "running" | "past" | "draft";

const TABS: LifecycleTab[] = ["running", "past", "draft"];

interface Props {
  tab: LifecycleTab;
  onTabChange: (tab: LifecycleTab) => void;
  labels: Record<LifecycleTab, string>;
  counts?: Partial<Record<LifecycleTab, number>>;
}

export function LifecycleTabs({ tab, onTabChange, labels, counts }: Props) {
  return <SectionTabs items={TABS.map((key) => ({ key, label: labels[key], count: counts?.[key] }))} activeKey={tab} onChange={onTabChange} ariaLabel="Статус" />;
}
