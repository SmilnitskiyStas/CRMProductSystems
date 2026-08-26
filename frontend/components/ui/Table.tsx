"use client";

import { Fragment, useState, type CSSProperties, type ReactNode } from "react";
import { ChevronUp, ChevronDown } from "lucide-react";
import { Pagination } from "./Pagination";

/**
 * Shared dark-theme data table (Batch A of the table-unification migration — see
 * `.claude/logs/tasks/636_*_table-component_frontend-developer.md`). Visual language copied
 * verbatim from `features/inventory/components/ProductsTable.tsx`, the pre-migration baseline.
 *
 * Product rule (non-negotiable): column 0 (the name/label column) is always left-aligned,
 * every other column is center-aligned. This is a STRUCTURAL default derived from column
 * index — `column.align` only exists to break it for a column with a genuinely good reason
 * (e.g. a leading selection checkbox pushing the real label column to index 1).
 *
 * Presentation-only: no fetching, no sort-comparator logic, no pagination math. Everything is
 * controlled from outside via props, exactly like every table this replaces already worked.
 */

export interface TableColumn<T> {
  /** Stable id; also the default sortKey display target. */
  key: string;
  header: ReactNode;
  /**
   * Omit in the overwhelming majority of cases — column 0 defaults to "left", every other
   * column defaults to "center". Only set this when a column has a genuinely good reason to
   * break the default.
   */
  align?: "left" | "center" | "right";
  width?: number | string;
  /** Presence turns the header into a sort button. */
  sortKey?: string;
  render: (row: T, rowIndex: number) => ReactNode;
  cellStyle?: CSSProperties;
}

export interface TableProps<T> {
  columns: TableColumn<T>[];
  rows: T[];
  rowKey: (row: T) => string;
  sortBy?: string;
  sortDescending?: boolean;
  onSort?(key: string): void;
  page?: number;
  totalPages?: number;
  totalCount?: number;
  onPageChange?(page: number): void;
  onRowClick?(row: T, index: number): void;
  /** Drives the #0F1825 hover/selected background. */
  isRowSelected?: (row: T) => boolean;
  rowStyle?: (row: T) => CSSProperties;
  expandedRowKey?: string | null;
  /** Rendered in a colSpan={columns.length} row immediately after the matching row. */
  renderExpanded?: (row: T) => ReactNode;
  minWidth?: number;
  emptyMessage?: ReactNode;
  isLoading?: boolean;
}

const thStyle: CSSProperties = {
  padding: "10px 16px",
  color: "#4B5563",
  fontSize: 11,
  fontWeight: 600,
  textTransform: "uppercase",
  letterSpacing: "0.05em",
  borderBottom: "1px solid #374151",
  borderRight: "1px solid #374151",
  background: "#0A0F1A",
};

const tdStyle: CSSProperties = {
  padding: "10px 16px",
  color: "#9CA3AF",
  fontSize: 13,
  borderBottom: "1px solid #1F2937",
  borderRight: "1px solid #1F2937",
};

type Align = "left" | "center" | "right";

function resolveAlign<T>(column: TableColumn<T>, index: number): Align {
  return column.align ?? (index === 0 ? "left" : "center");
}

function justifyFor(align: Align): CSSProperties["justifyContent"] {
  if (align === "left") return "flex-start";
  if (align === "right") return "flex-end";
  return "center";
}

function SortHeaderButton({
  label,
  align,
  active,
  descending,
  onClick,
}: {
  label: ReactNode;
  align: Align;
  active: boolean;
  descending: boolean;
  onClick: () => void;
}) {
  return (
    <button
      onClick={onClick}
      style={{
        display: "flex",
        alignItems: "center",
        gap: 3,
        justifyContent: justifyFor(align),
        width: "100%",
        background: "transparent",
        border: "none",
        padding: 0,
        cursor: "pointer",
        color: active ? "#9CA3AF" : "#4B5563",
        fontSize: 11,
        fontWeight: 600,
        textTransform: "uppercase",
        letterSpacing: "0.05em",
        whiteSpace: "nowrap",
      }}
    >
      {label}
      {active && (descending ? <ChevronDown size={12} /> : <ChevronUp size={12} />)}
    </button>
  );
}

export function Table<T>({
  columns,
  rows,
  rowKey,
  sortBy,
  sortDescending = false,
  onSort,
  page,
  totalPages,
  totalCount,
  onPageChange,
  onRowClick,
  isRowSelected,
  rowStyle,
  expandedRowKey,
  renderExpanded,
  minWidth,
  emptyMessage,
  isLoading,
}: TableProps<T>) {
  const [hoveredKey, setHoveredKey] = useState<string | null>(null);
  const columnCount = columns.length;
  const showEmptyRow = isLoading || rows.length === 0;
  const showPagination = !isLoading && !!onPageChange && totalPages != null;

  return (
    <>
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 12,
          overflow: "auto",
        }}
      >
        <table
          style={{
            width: "100%",
            borderCollapse: "collapse",
            ...(minWidth != null ? { minWidth } : null),
          }}
        >
          <thead>
            <tr>
              {columns.map((column, index) => {
                const align = resolveAlign(column, index);
                const isLast = index === columnCount - 1;
                const style: CSSProperties = {
                  ...thStyle,
                  textAlign: align,
                  ...(isLast ? { borderRight: "none" } : null),
                  ...(column.width != null ? { width: column.width } : null),
                };
                return (
                  <th key={column.key} style={style}>
                    {column.sortKey ? (
                      <SortHeaderButton
                        label={column.header}
                        align={align}
                        active={column.sortKey === sortBy}
                        descending={sortDescending}
                        onClick={() => onSort?.(column.sortKey!)}
                      />
                    ) : (
                      column.header
                    )}
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody>
            {showEmptyRow ? (
              <tr>
                <td
                  colSpan={columnCount}
                  style={{ padding: "40px 0", textAlign: "center", color: "#4B5563", fontSize: 13 }}
                >
                  {emptyMessage}
                </td>
              </tr>
            ) : (
              rows.map((row, rowIndex) => {
                const key = rowKey(row);
                const selected = isRowSelected?.(row) ?? false;
                const hovered = hoveredKey === key;
                const custom = rowStyle?.(row);
                const trStyle: CSSProperties = {
                  background: selected || hovered ? "#0F1825" : "transparent",
                  transition: "background 0.1s",
                  ...(onRowClick ? { cursor: "pointer" } : null),
                  ...custom,
                };
                const isExpanded = expandedRowKey != null && expandedRowKey === key;

                return (
                  <Fragment key={key}>
                    <tr
                      style={trStyle}
                      onMouseEnter={() => setHoveredKey(key)}
                      onMouseLeave={() => setHoveredKey((prev) => (prev === key ? null : prev))}
                      onClick={onRowClick ? () => onRowClick(row, rowIndex) : undefined}
                    >
                      {columns.map((column, index) => {
                        const align = resolveAlign(column, index);
                        const isLast = index === columnCount - 1;
                        const cellStyle: CSSProperties = {
                          ...tdStyle,
                          textAlign: align,
                          ...(isLast ? { borderRight: "none" } : null),
                          ...(column.width != null ? { width: column.width } : null),
                          ...column.cellStyle,
                        };
                        return (
                          <td key={column.key} style={cellStyle}>
                            {column.render(row, rowIndex)}
                          </td>
                        );
                      })}
                    </tr>
                    {isExpanded && renderExpanded && (
                      <tr>
                        <td colSpan={columnCount} style={{ padding: 0, borderBottom: "1px solid #1F2937" }}>
                          {renderExpanded(row)}
                        </td>
                      </tr>
                    )}
                  </Fragment>
                );
              })
            )}
          </tbody>
        </table>
      </div>

      {showPagination && (
        <Pagination
          page={page ?? 1}
          totalPages={totalPages ?? 1}
          totalCount={totalCount ?? rows.length}
          onPageChange={onPageChange!}
        />
      )}
    </>
  );
}
