"use client";

import { useEffect, useMemo } from "react";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";
import { buildNavGroups, buildSupplierNavGroups } from "@/components/layout/Sidebar";

interface TitleEntry {
  href: string;
  label: string;
  exact?: boolean;
}

function matches(pathname: string, entry: TitleEntry): boolean {
  return entry.exact
    ? pathname === entry.href
    : pathname === entry.href || pathname.startsWith(entry.href + "/");
}

/**
 * Sets the browser tab title to the sidebar label of the current route,
 * reusing the same href → label data the Sidebar renders (no separate list
 * to keep in sync). Longest matching href wins, same precedence as
 * Sidebar's `isActive()`, so detail pages (e.g. /inventory/[id]) inherit
 * their parent section's title.
 */
export function usePageTitle() {
  const pathname = usePathname();
  const t = useTranslations("Dashboard.sidebar");
  const tGroups = useTranslations("Dashboard.sidebar.groups");

  const entries = useMemo<TitleEntry[]>(() => {
    const groups = [...buildNavGroups(tGroups), ...buildSupplierNavGroups(tGroups)];
    return [
      { href: "/dashboard", label: t("dashboard"), exact: true },
      { href: "/settings", label: t("settings") },
      ...groups.flatMap((group) =>
        group.items.map((item) => ({ href: item.href, label: item.label, exact: item.exact })),
      ),
    ];
  }, [t, tGroups]);

  useEffect(() => {
    const match = entries
      .filter((entry) => matches(pathname, entry))
      .sort((a, b) => b.href.length - a.href.length)[0];
    document.title = match ? `${match.label} — ShelfGuard` : "ShelfGuard";
  }, [pathname, entries]);
}
