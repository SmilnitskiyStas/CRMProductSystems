import type { PlatformCategoryDto } from "../types";

export interface FlatNode {
  category: PlatformCategoryDto;
  depth: number;
}

/** parentId → children, siblings sorted by (sortOrder, name). Unknown parents fold to roots. */
export function buildChildrenMap(
  categories: PlatformCategoryDto[],
): Map<string | null, PlatformCategoryDto[]> {
  const ids = new Set(categories.map((c) => c.id));
  const map = new Map<string | null, PlatformCategoryDto[]>();
  for (const category of categories) {
    const parent = category.parentId && ids.has(category.parentId) ? category.parentId : null;
    map.set(parent, [...(map.get(parent) ?? []), category]);
  }
  for (const children of map.values()) {
    children.sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name, "uk"));
  }
  return map;
}

/** Depth-first, tree-ordered list. `depth` is 0 for roots. */
export function flattenPlatformTree(categories: PlatformCategoryDto[]): FlatNode[] {
  const childrenByParent = buildChildrenMap(categories);
  const out: FlatNode[] = [];
  const walk = (parentId: string | null, depth: number) => {
    for (const category of childrenByParent.get(parentId) ?? []) {
      out.push({ category, depth });
      walk(category.id, depth + 1);
    }
  };
  walk(null, 0);
  return out;
}

/** The id itself plus every descendant — the set a node may NOT be re-parented under. */
export function subtreeIds(categories: PlatformCategoryDto[], id: string): Set<string> {
  const childrenByParent = buildChildrenMap(categories);
  const out = new Set<string>();
  const walk = (current: string) => {
    out.add(current);
    for (const child of childrenByParent.get(current) ?? []) walk(child.id);
  };
  walk(id);
  return out;
}

export function indentLabel(name: string, depth: number): string {
  return depth > 0 ? `${"— ".repeat(depth)}${name}` : name;
}
