"use client";

import {
  useExpirySummary,
  useWriteOffAnalytics,
  useZoneAnalytics,
  useCategoryAnalytics,
  useLosses,
} from "@/features/analytics/hooks/useAnalytics";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { CAN_VIEW_ANALYTICS, hasRole } from "@/lib/roles";
import { ExpiryDonut } from "@/features/analytics/components/ExpiryDonut";
import { LossesByReasonChart } from "@/features/analytics/components/LossesByReasonChart";
import { LossesByStoreChart } from "@/features/analytics/components/LossesByStoreChart";
import { CategoryStatusChart } from "@/features/analytics/components/CategoryStatusChart";

function MetricCard({
  label,
  value,
  sub,
  color,
}: {
  label: string;
  value: string | number;
  sub?: string;
  color?: string;
}) {
  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 10,
        padding: "16px 20px",
      }}
    >
      <div style={{ color: "#4B5563", fontSize: 12, marginBottom: 6 }}>{label}</div>
      <div style={{ color: color ?? "#E8EDF5", fontSize: 22, fontWeight: 700, fontFamily: "monospace" }}>
        {value}
      </div>
      {sub && <div style={{ color: "#4B5563", fontSize: 11, marginTop: 4 }}>{sub}</div>}
    </div>
  );
}

// ── Shared table style tokens ──────────────────────────────────────────────────
const ROW_BORDER = "1px solid #1F2937";

/** Base cell padding + border */
const baseTd: React.CSSProperties = {
  padding: "10px 16px",
  fontSize: 13,
  borderBottom: ROW_BORDER,
  borderRight: "1px solid #1F2937",
  textAlign: "center",
};

/** Text column */
const tdText: React.CSSProperties = {
  ...baseTd,
  color: "#E8EDF5",
  fontWeight: 500,
};

/** Secondary text (store name in zone table etc.) */
const tdMuted: React.CSSProperties = {
  ...baseTd,
  color: "#6B7280",
};

/** Numeric column — monospace */
const tdNum: React.CSSProperties = {
  ...baseTd,
  color: "#9CA3AF",
  fontFamily: "monospace",
};

/** Header */
function thStyle(_align: "left" | "right" = "left"): React.CSSProperties {
  return {
    padding: "10px 16px",
    color: "#4B5563",
    fontSize: 11,
    fontWeight: 600,
    textTransform: "uppercase",
    letterSpacing: "0.05em",
    borderBottom: "1px solid #374151",
    borderRight: "1px solid #374151",
    textAlign: "center",
    background: "#0A0F1A",
  };
}

const sectionTitle: React.CSSProperties = {
  color: "#E8EDF5",
  fontSize: 15,
  fontWeight: 700,
  margin: 0,
  marginBottom: 12,
};

const tableWrapper: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 10,
  overflow: "hidden",
};

const REASON_LABELS: Record<string, string> = {
  expired: "Прострочено",
  damaged: "Пошкоджено",
  theft: "Крадіжка",
  production_loss: "Виробничі втрати",
  other: "Інше",
};

export default function AnalyticsPage() {
  const { data: me } = useMe();
  const access = me ? hasRole(me.role, CAN_VIEW_ANALYTICS) : null;

  const enabled = access === true;
  const { data: expiry, isLoading: expiryLoading } = useExpirySummary(undefined, enabled);
  const { data: writeoffs } = useWriteOffAnalytics(undefined, enabled);
  const { data: zones } = useZoneAnalytics(undefined, enabled);
  const { data: categories } = useCategoryAnalytics(undefined, enabled);
  const { data: losses } = useLosses(undefined, enabled);

  if (access === null) return null;
  if (!access) return <AccessDenied title="Аналітика" />;

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 28, width: "100%" }}>
      <div>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>Аналітика</h1>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
          Зведена аналітика по термінах, списаннях та рухах товарів
        </p>
      </div>

      {/* ── Expiry summary ────────────────────────────────────────── */}
      <section>
        <h2 style={sectionTitle}>Стан залишків</h2>
        {expiryLoading ? (
          <div style={{ color: "#4B5563", fontSize: 13 }}>Завантаження…</div>
        ) : expiry ? (
          <>
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(160px, 1fr))",
                gap: 12,
                marginBottom: 16,
              }}
            >
              <MetricCard label="Норма" value={expiry.safe} color="#4ADE80" />
              <MetricCard label="Попередження" value={expiry.warning} color="#FBBF24" />
              <MetricCard label="Критично" value={expiry.critical} color="#F87171" />
              <MetricCard label="Прострочено" value={expiry.expired} color="#DC2626" />
              <MetricCard label="Перевірка" value={expiry.needsVerification} color="#A78BFA" />
              <MetricCard label="Всього партій" value={expiry.total} />
            </div>

            <ExpiryDonut
              safe={expiry.safe}
              warning={expiry.warning}
              critical={expiry.critical}
              expired={expiry.expired}
              needsVerification={expiry.needsVerification}
            />

            {expiry.stores.length > 0 && (
              <div style={{ ...tableWrapper, marginTop: 16 }}>
                <table style={{ width: "100%", borderCollapse: "collapse" }}>
                  <thead>
                    <tr>
                      <th style={thStyle("left")}>Магазин</th>
                      <th style={thStyle("right")}>Норма</th>
                      <th style={thStyle("right")}>Попередж.</th>
                      <th style={thStyle("right")}>Критично</th>
                      <th style={thStyle("right")}>Прострочено</th>
                    </tr>
                  </thead>
                  <tbody>
                    {expiry.stores.map((s) => (
                      <tr key={s.storeId}>
                        <td style={tdText}>{s.storeName}</td>
                        <td style={{ ...tdNum, color: "#4ADE80" }}>{s.safe}</td>
                        <td style={{ ...tdNum, color: "#FBBF24" }}>{s.warning}</td>
                        <td style={{ ...tdNum, color: "#F87171" }}>{s.critical}</td>
                        <td style={{ ...tdNum, color: "#DC2626" }}>{s.expired}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </>
        ) : null}
      </section>

      {/* ── Write-off analytics ───────────────────────────────────── */}
      {writeoffs && (
        <section>
          <h2 style={sectionTitle}>Списання</h2>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
              gap: 12,
              marginBottom: 16,
            }}
          >
            <MetricCard label="Всього документів" value={writeoffs.totalDocuments} />
            <MetricCard
              label="Загальні збитки"
              value={`${writeoffs.totalLoss.toLocaleString("uk-UA")} ₴`}
              color="#F87171"
            />
          </div>

          <LossesByReasonChart data={writeoffs.byReason} />

          {writeoffs.byReason.length > 0 && (
            <div style={{ ...tableWrapper, marginTop: 16 }}>
              <table style={{ width: "100%", borderCollapse: "collapse" }}>
                <thead>
                  <tr>
                    <th style={thStyle("left")}>Причина</th>
                    <th style={thStyle("right")}>К-сть документів</th>
                    <th style={thStyle("right")}>Збитки</th>
                  </tr>
                </thead>
                <tbody>
                  {writeoffs.byReason.map((r) => (
                    <tr key={r.reason}>
                      <td style={tdText}>{REASON_LABELS[r.reason] ?? r.reason}</td>
                      <td style={tdNum}>{r.count}</td>
                      <td style={{ ...tdNum, color: "#F87171" }}>
                        {r.totalLoss.toLocaleString("uk-UA")} ₴
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}

      {/* ── By zone ───────────────────────────────────────────────── */}
      {zones && zones.length > 0 && (
        <section>
          <h2 style={sectionTitle}>По зонах</h2>
          <div style={tableWrapper}>
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr>
                  <th style={thStyle("left")}>Зона</th>
                  <th style={thStyle("left")}>Магазин</th>
                  <th style={thStyle("right")}>Норма</th>
                  <th style={thStyle("right")}>Попередж.</th>
                  <th style={thStyle("right")}>Критично</th>
                  <th style={thStyle("right")}>Прострочено</th>
                  <th style={thStyle("right")}>Всього</th>
                </tr>
              </thead>
              <tbody>
                {zones.map((z) => (
                  <tr key={z.zoneId}>
                    <td style={tdText}>{z.zoneName}</td>
                    <td style={tdMuted}>{z.storeName}</td>
                    <td style={{ ...tdNum, color: "#4ADE80" }}>{z.safe}</td>
                    <td style={{ ...tdNum, color: "#FBBF24" }}>{z.warning}</td>
                    <td style={{ ...tdNum, color: "#F87171" }}>{z.critical}</td>
                    <td style={{ ...tdNum, color: "#DC2626" }}>{z.expired}</td>
                    <td style={tdNum}>{z.totalBatches}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}

      {/* ── By category ───────────────────────────────────────────── */}
      {categories && categories.length > 0 && (
        <section>
          <h2 style={sectionTitle}>По категоріях</h2>
          <CategoryStatusChart data={categories} />
          <div style={{ ...tableWrapper, marginTop: 16 }}>
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr>
                  <th style={thStyle("left")}>Категорія</th>
                  <th style={thStyle("right")}>Норма</th>
                  <th style={thStyle("right")}>Попередж.</th>
                  <th style={thStyle("right")}>Критично</th>
                  <th style={thStyle("right")}>Прострочено</th>
                  <th style={thStyle("right")}>Партій</th>
                  <th style={thStyle("right")}>К-сть</th>
                </tr>
              </thead>
              <tbody>
                {categories.map((c) => (
                  <tr key={c.categoryId ?? "uncategorized"}>
                    <td style={tdText}>{c.categoryName}</td>
                    <td style={{ ...tdNum, color: "#4ADE80" }}>{c.safe}</td>
                    <td style={{ ...tdNum, color: "#FBBF24" }}>{c.warning}</td>
                    <td style={{ ...tdNum, color: "#F87171" }}>{c.critical}</td>
                    <td style={{ ...tdNum, color: "#DC2626" }}>{c.expired}</td>
                    <td style={tdNum}>{c.totalBatches}</td>
                    <td style={tdNum}>{c.totalQuantity.toLocaleString("uk-UA")}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}

      {/* ── Losses by store ───────────────────────────────────────── */}
      {losses && losses.byStore.length > 0 && (
        <section>
          <h2 style={sectionTitle}>Збитки по магазинах</h2>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
              gap: 12,
              marginBottom: 16,
            }}
          >
            <MetricCard
              label="Загальні збитки"
              value={`${losses.totalLoss.toLocaleString("uk-UA")} ₴`}
              color="#F87171"
            />
            <MetricCard label="Всього списань" value={losses.totalWriteOffs} />
            <MetricCard
              label="Середнє на документ"
              value={`${losses.averageLossPerWriteOff.toLocaleString("uk-UA")} ₴`}
            />
          </div>
          <LossesByStoreChart data={losses.byStore} />
          <div style={{ ...tableWrapper, marginTop: 16 }}>
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr>
                  <th style={thStyle("left")}>Магазин</th>
                  <th style={thStyle("right")}>Документів</th>
                  <th style={thStyle("right")}>Збитки</th>
                </tr>
              </thead>
              <tbody>
                {losses.byStore.map((s) => (
                  <tr key={s.storeId}>
                    <td style={tdText}>{s.storeName}</td>
                    <td style={tdNum}>{s.writeOffCount}</td>
                    <td style={{ ...tdNum, color: "#F87171" }}>
                      {s.totalLoss.toLocaleString("uk-UA")} ₴
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}
    </div>
  );
}
