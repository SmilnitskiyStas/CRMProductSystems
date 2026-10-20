"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { RuleWizard } from "@/features/promotion-rules/components/RuleWizard";

export default function EditPromotionRulePage() {
  const t = useTranslations("Dashboard.promotionRules");
  const { id } = useParams<{ id: string }>();
  return (
    <div style={{ padding: "28px 32px" }}>
      <Link href="/promotion-rules" style={{ color: "#60A5FA", fontSize: 13, textDecoration: "none" }}>
        {t("backToList")}
      </Link>
      <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: "10px 0 20px" }}>{t("wizard.editTitle")}</h1>
      <RuleWizard ruleId={id} />
    </div>
  );
}
