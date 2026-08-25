"use client";

import { useRouter } from "next/navigation";
import { LifeBuoy } from "lucide-react";
import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";

interface Props {
  customerId: string;
  openTicketCount: number;
}

/**
 * TASK-621 (§4 "Звернення" tab). The customer-detail endpoint only carries `openTicketCount` —
 * the full ticket list/thread lives on the separate `/customer-support` staff inbox page
 * (TASK-616's CustomerSupportInboxController), per the plan's explicit scoping. This tab is
 * deliberately just a count + a deep link, not an inline list.
 */
export function CustomerTicketsTab({ customerId, openTicketCount }: Props) {
  const t = useTranslations("Dashboard.customers.tickets");
  const router = useRouter();

  return (
    <div
      style={{
        background: "#0A1020",
        border: "1px solid #1F2937",
        borderRadius: 10,
        padding: 20,
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        gap: 12,
        textAlign: "center",
      }}
    >
      <LifeBuoy size={22} style={{ color: openTicketCount > 0 ? "#FCD34D" : "#374151" }} />
      <div>
        <div style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700 }}>{openTicketCount}</div>
        <div style={{ color: "#6B7280", fontSize: 12, marginTop: 2 }}>{t("openCountLabel")}</div>
      </div>
      <Btn
        size="sm"
        variant="ghost"
        onClick={() => router.push(`/customer-support?customerId=${customerId}`)}
      >
        {t("openInboxButton")}
      </Btn>
    </div>
  );
}
