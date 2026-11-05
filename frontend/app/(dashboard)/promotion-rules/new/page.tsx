"use client";

import { useTranslations } from "next-intl";
import { BackLink } from "@/components/ui/BackLink";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { RuleWizard } from "@/features/promotion-rules/components/RuleWizard";

export default function NewPromotionRulePage() {
  const t = useTranslations("Dashboard.promotionRules");
  return (
    <PageContainer>
      <BackLink href="/promotion-rules">{t("backToList").replace(/^←\s*/, "")}</BackLink>
      <PageHeader title={t("wizard.createTitle")} />
      <RuleWizard />
    </PageContainer>
  );
}
