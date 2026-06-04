"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { useMe } from "@/features/auth/hooks/useAuth";
import { getToken } from "@/lib/api";
import { Sidebar } from "@/components/layout/Sidebar";
import { TopBar } from "@/components/layout/TopBar";

const Loading = () => (
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
      Завантаження…
    </div>
  </div>
);

export default function DashboardLayout({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const { error, isLoading } = useMe();
  // Prevent hydration mismatch: server has no localStorage, so first render must
  // always be the loading state on both server and client.
  const [mounted, setMounted] = useState(false);

  useEffect(() => {
    setMounted(true);
  }, []);

  useEffect(() => {
    if (mounted && !getToken()) {
      router.replace("/login");
    }
  }, [mounted, router]);

  useEffect(() => {
    if (error) {
      router.replace("/login");
    }
  }, [error, router]);

  if (!mounted || isLoading) return <Loading />;

  return (
    <div style={{ display: "flex", minHeight: "100vh", background: "#0F1117" }}>
      <Sidebar />
      <div style={{ flex: 1, display: "flex", flexDirection: "column", minWidth: 0 }}>
        <TopBar />
        <main style={{ flex: 1, overflowY: "auto" }}>{children}</main>
      </div>
    </div>
  );
}
