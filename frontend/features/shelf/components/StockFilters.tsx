interface StockFilters {
  status: string;
  search: string;
}

interface Props {
  filters: StockFilters;
  onChange: (f: StockFilters) => void;
}

const STATUS_OPTIONS = [
  { value: "", label: "Всі статуси" },
  { value: "warning", label: "Попередження" },
  { value: "critical", label: "Критично" },
  { value: "expired", label: "Прострочено" },
  { value: "safe", label: "Норма" },
  { value: "needs_verification", label: "Перевірка" },
];

const inputStyle: React.CSSProperties = {
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "7px 12px",
  outline: "none",
};

export function StockFilters({ filters, onChange }: Props) {
  return (
    <div style={{ display: "flex", gap: 10, flexWrap: "wrap", alignItems: "center" }}>
      <input
        type="text"
        placeholder="Пошук по назві або штрихкоду…"
        value={filters.search}
        onChange={(e) => onChange({ ...filters, search: e.target.value })}
        style={{ ...inputStyle, width: 260 }}
      />

      <select
        value={filters.status}
        onChange={(e) => onChange({ ...filters, status: e.target.value })}
        style={{ ...inputStyle, cursor: "pointer" }}
      >
        {STATUS_OPTIONS.map((opt) => (
          <option key={opt.value} value={opt.value}>
            {opt.label}
          </option>
        ))}
      </select>

      {(filters.status || filters.search) && (
        <button
          onClick={() => onChange({ status: "", search: "" })}
          style={{
            background: "transparent",
            border: "1px solid #374151",
            borderRadius: 8,
            color: "#6B7280",
            fontSize: 12,
            padding: "7px 12px",
            cursor: "pointer",
          }}
        >
          Скинути
        </button>
      )}
    </div>
  );
}
