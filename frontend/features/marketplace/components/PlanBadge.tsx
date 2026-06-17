import type { SupplierPlan } from "../types";

interface Props {
  plan: SupplierPlan;
}

export function PlanBadge({ plan }: Props) {
  const isPremium = plan === "premium";
  return (
    <span
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: 4,
        padding: "2px 8px",
        borderRadius: 4,
        background: isPremium ? "#1c1400" : "#111827",
        border: `1px solid ${isPremium ? "#92400e" : "#374151"}`,
        color: isPremium ? "#F59E0B" : "#6B7280",
        fontSize: 11,
        fontWeight: 600,
        letterSpacing: "0.03em",
        textTransform: "uppercase",
      }}
    >
      {isPremium ? "★ Premium" : "Free"}
    </span>
  );
}
