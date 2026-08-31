import type { Region } from "../types";

/**
 * Human label for a region code. Falls back to the raw code when the registry
 * has not loaded yet or the code is unknown.
 */
export function regionLabel(code: string, regions: Region[]): string {
  return regions.find((r) => r.code === code)?.nameUa ?? code;
}

export interface RegionGroup {
  oblast: Region;
  cities: Region[];
}

/**
 * Groups the flat region list into `{ oblast, cities }[]`. Oblasts are sorted by
 * `nameUa`; cities inside each oblast are sorted by `nameUa` too. Cities whose
 * `parentCode` has no matching oblast in the list are dropped. Used by the select
 * components to build `<optgroup>`s / grouped checklists.
 */
export function groupRegions(regions: Region[]): RegionGroup[] {
  const collator = new Intl.Collator("uk");

  const oblasts = regions
    .filter((r) => r.kind === "oblast")
    .sort((a, b) => collator.compare(a.nameUa, b.nameUa));

  const citiesByParent = new Map<string, Region[]>();
  for (const r of regions) {
    if (r.kind === "city" && r.parentCode) {
      const list = citiesByParent.get(r.parentCode) ?? [];
      list.push(r);
      citiesByParent.set(r.parentCode, list);
    }
  }

  return oblasts.map((oblast) => ({
    oblast,
    cities: (citiesByParent.get(oblast.code) ?? []).sort((a, b) =>
      collator.compare(a.nameUa, b.nameUa),
    ),
  }));
}
