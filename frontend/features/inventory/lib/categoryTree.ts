import type { CategoryDto } from "../types";

export interface FlatCategoryNode {
  category: CategoryDto;
  depth: number;
}

/**
 * Flattens the flat `CategoryDto[]` (each row carrying only `parentId`) into a
 * depth-first, tree-ordered list — roots first, each node immediately followed by its
 * subtree, siblings sorted by name. `depth` is 0 for roots.
 *
 * Used to render the category tree as an indented native `<select>` in the product form
 * and the inventory filter. Mirrors the `childrenByParent` map built in
 * `features/consumer-app/components/CategoryExclusionTree.tsx`.
 */
export function flattenTree(categories: CategoryDto[]): FlatCategoryNode[] {
  const ids = new Set(categories.map((c) => c.id));
  const childrenByParent = new Map<string | null, CategoryDto[]>();
  for (const category of categories) {
    const parent = category.parentId && ids.has(category.parentId) ? category.parentId : null;
    childrenByParent.set(parent, [...(childrenByParent.get(parent) ?? []), category]);
  }
  for (const children of childrenByParent.values()) {
    children.sort((a, b) => a.name.localeCompare(b.name, "uk"));
  }

  const out: FlatCategoryNode[] = [];
  const walk = (parentId: string | null, depth: number) => {
    for (const category of childrenByParent.get(parentId) ?? []) {
      out.push({ category, depth });
      walk(category.id, depth + 1);
    }
  };
  walk(null, 0);
  return out;
}

/** Prefix a category label with an indent showing its depth in the tree. */
export function indentLabel(name: string, depth: number): string {
  return depth > 0 ? `${"— ".repeat(depth)}${name}` : name;
}
