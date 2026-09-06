"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useMe } from "@/features/auth/hooks/useAuth";
import { getToken } from "@/lib/api";
import { Sidebar } from "@/components/layout/Sidebar";
import { TopBar } from "@/components/layout/TopBar";
import { ImpersonationBanner } from "@/features/provider/components/ImpersonationBanner";
import { TemporaryPasswordBanner } from "@/features/auth/components/TemporaryPasswordBanner";
import { DashboardIntlProvider } from "@/i18n/DashboardIntlProvider";
import { usePageTitle } from "@/features/dashboard/hooks/usePageTitle";

interface ImpersonationInfo {
  tenantName: string;
  tenantId: string;
}

function readImpersonation(): ImpersonationInfo | null {
  if (typeof window === "undefined") return null;
  try {
    const raw = sessionStorage.getItem("sg_impersonation");
    return raw ? (JSON.parse(raw) as ImpersonationInfo) : null;
  } catch {
    return null;
  }
}

function Loading() {
  const t = useTranslations("Common");
  return (
    <div
      style={{
        minHeight: "100vh",
        background: "#0F1117",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
      }}
    >
      <div style={{ color: "#8A94A8", fontFamily: '"Inter", sans-serif', fontSize: 13 }}>
        {t("loading")}
      </div>
    </div>
  );
}

function DashboardChrome({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  usePageTitle();
  const { error, isLoading } = useMe();
  const [mounted, setMounted] = useState(false);
  const [collapsed, setCollapsed] = useState(false);
  const [impersonation, setImpersonation] = useState<ImpersonationInfo | null>(null);

  useEffect(() => {
    setMounted(true);
    setImpersonation(readImpersonation());

    // Layout does not remount on navigation — listen for explicit events instead
    const handler = () => setImpersonation(readImpersonation());
    window.addEventListener("sg-impersonation-changed", handler);
    return () => window.removeEventListener("sg-impersonation-changed", handler);
  }, []);

  useEffect(() => {
    if (mounted && !getToken()) router.replace("/login");
  }, [mounted, router]);

  useEffect(() => {
    if (error) router.replace("/login");
  }, [error, router]);

  if (!mounted || isLoading) return <Loading />;

  return (
    <>
      {impersonation && (
        <ImpersonationBanner
          tenantName={impersonation.tenantName}
          tenantId={impersonation.tenantId}
          onExit={() => {
            setImpersonation(null);
            router.replace("/provider");
          }}
        />
      )}
      <div
        style={{
          display: "flex",
          minHeight: "100vh",
          background: "#0F1117",
        }}
      >
        <Sidebar collapsed={collapsed} onToggle={() => setCollapsed((v) => !v)} />
        <div style={{ flex: 1, display: "flex", flexDirection: "column", minWidth: 0 }}>
          <TemporaryPasswordBanner />
          <TopBar />
          <main style={{ flex: 1, overflowY: "auto" }}>{children}</main>
        </div>
      </div>
    </>
  );
}

export default function DashboardLayout({ children }: { children: React.ReactNode }) {
  return (
    <DashboardIntlProvider>
      <DashboardChrome>{children}</DashboardChrome>
    </DashboardIntlProvider>
  );
}
