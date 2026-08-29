"use client";

import { useMemo, useState } from "react";
import { ChevronDown, ChevronRight, FolderTree, Search } from "lucide-react";
import type { CategoryDto } from "@/features/inventory/types";

interface Props {
  categories: CategoryDto[];
  selectedIds: string[];
  onChange: (ids: string[]) => void;
}

export function CategoryExclusionTree({ categories, selectedIds, onChange }: Props) {
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());
  const [query, setQuery] = useState("");
  const selected = useMemo(() => new Set(selectedIds), [selectedIds]);
  const childrenByParent = useMemo(() => {
    const map = new Map<string | null, CategoryDto[]>();
    const ids = new Set(categories.map((category) => category.id));
    for (const category of categories) {
      const parent = category.parentId && ids.has(category.parentId) ? category.parentId : null;
      map.set(parent, [...(map.get(parent) ?? []), category]);
    }
    for (const children of map.values()) children.sort((a, b) => a.name.localeCompare(b.name, "uk"));
    return map;
  }, [categories]);

  function subtreeIds(id: string): string[] {
    return [id, ...(childrenByParent.get(id) ?? []).flatMap((child) => subtreeIds(child.id))];
  }

  function toggle(category: CategoryDto) {
    const branch = subtreeIds(category.id);
    const shouldSelect = branch.some((id) => !selected.has(id));
    const next = new Set(selected);
    for (const id of branch) shouldSelect ? next.add(id) : next.delete(id);
    onChange([...next]);
  }

  function render(category: CategoryDto, depth: number): React.ReactNode {
    const children = childrenByParent.get(category.id) ?? [];
    const branch = subtreeIds(category.id);
    const selectedCount = branch.filter((id) => selected.has(id)).length;
    const checked = selectedCount === branch.length;
    const partial = selectedCount > 0 && !checked;
    const isExpanded = expanded.has(category.id);
    return <div key={category.id}>
      <div style={{ display: "flex", alignItems: "center", gap: 7, minHeight: 34, paddingLeft: depth * 22 }}>
        {children.length > 0 ? <button type="button" aria-label={isExpanded ? "Згорнути категорію" : "Розгорнути категорію"} onClick={() => setExpanded((current) => { const next = new Set(current); next.has(category.id) ? next.delete(category.id) : next.add(category.id); return next; })} style={{ display: "flex", padding: 2, border: 0, background: "transparent", color: "#6B7280", cursor: "pointer" }}>{isExpanded ? <ChevronDown size={15} /> : <ChevronRight size={15} />}</button> : <span style={{ width: 19 }} />}
        <input type="checkbox" checked={checked} ref={(element) => { if (element) element.indeterminate = partial; }} onChange={() => toggle(category)} />
        <button type="button" onClick={() => toggle(category)} style={{ border: 0, padding: 0, background: "transparent", color: checked || partial ? "#BFDBFE" : "#D1D5DB", fontSize: 12, cursor: "pointer", textAlign: "left" }}>{category.name}</button>
        {children.length > 0 && <span style={{ color: "#4B5563", fontSize: 10 }}>({branch.length})</span>}
      </div>
      {isExpanded && children.map((child) => render(child, depth + 1))}
    </div>;
  }

  const roots = childrenByParent.get(null) ?? [];
  if (roots.length === 0) return <p style={{ color: "#6B7280", fontSize: 12 }}>Категорій ще немає.</p>;
  const normalizedQuery = query.trim().toLocaleLowerCase("uk");
  const visibleRoots = normalizedQuery ? categories.filter((category) => category.name.toLocaleLowerCase("uk").includes(normalizedQuery)) : roots;
  return <div><div style={{ position: "relative", marginBottom: 8 }}><Search size={13} style={{ position: "absolute", left: 10, top: "50%", transform: "translateY(-50%)", color: "#6B7280" }} /><input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Пошук категорії" style={{ width: "100%", boxSizing: "border-box", background: "#0D1117", border: "1px solid #374151", borderRadius: 8, padding: "8px 10px 8px 30px", color: "#E8EDF5", fontSize: 12 }} /></div><div style={{ maxHeight: 340, overflowY: "auto", background: "#0D1117", border: "1px solid #293241", borderRadius: 8, padding: 10 }}><div style={{ display: "flex", alignItems: "center", gap: 7, color: "#6B7280", fontSize: 11, marginBottom: 5 }}><FolderTree size={14} /> Вибрано: {selectedIds.length}. Батьківська категорія охоплює всю підгілку</div>{visibleRoots.map((root) => render(root, normalizedQuery ? 0 : 0))}{visibleRoots.length === 0 && <p style={{ color: "#6B7280", fontSize: 12 }}>Нічого не знайдено.</p>}</div></div>;
}
