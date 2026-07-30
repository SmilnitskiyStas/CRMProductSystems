import { ForgotPasswordCard } from "@/features/auth/components/ForgotPasswordCard";

// Server Component so `metadata` stays valid — same reasoning as login/page.tsx
// (TASK-376): the actual translated markup lives in ForgotPasswordCard.tsx, which needs
// `useTranslations` and so must be a Client Component.
export const metadata = { title: "Відновлення пароля — ShelfGuard" };

export default function ForgotPasswordPage() {
  return <ForgotPasswordCard />;
}
