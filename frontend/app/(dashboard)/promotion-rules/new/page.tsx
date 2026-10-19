"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { RuleWizard } from "@/features/promotion-rules/components/RuleWizard";

export default function NewPromotionRulePage() {
  const t = useTranslations("Dashboard.promotionRules");
  return (
    <div style={{ padding: "28px 32px" }}>
      <Link href="/promotion-rules" style={{ color: "#60A5FA", fontSize: 13, textDecoration: "none" }}>
        {t("backToList")}
      </Link>
      <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: "10px 0 20px" }}>{t("wizard.createTitle")}</h1>
      <RuleWizard />
    </div>
  );
}
