import { LoginCard } from "@/features/auth/components/LoginCard";

// Kept as a Server Component (no "use client") specifically so this `metadata` export
// stays valid — the actual translated markup lives in LoginCard.tsx (i18n Block 1,
// TASK-376), which needs `useTranslations` and so must be a Client Component. The tab
// title itself stays static/Ukrainian regardless of the dashboard locale — it's resolved
// client-side (cookie/browser language) and a Server Component has no access to that
// without reading cookies here, which isn't worth the added complexity for a browser-tab
// title only ever glanced at, not part of the actual UI being localized.
export const metadata = { title: "Вхід — ShelfGuard" };

export default function LoginPage() {
  return <LoginCard />;
}
