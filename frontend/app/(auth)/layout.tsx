import { DashboardIntlProvider } from "@/i18n/DashboardIntlProvider";

export default function AuthLayout({ children }: { children: React.ReactNode }) {
  return (
    <DashboardIntlProvider defaultLocale="en">
      <div
        style={{
          minHeight: "100vh",
          background: "#0F1117",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
        }}
      >
        {children}
      </div>
    </DashboardIntlProvider>
  );
}
