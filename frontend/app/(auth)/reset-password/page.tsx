import { ResetPasswordCard } from "@/features/auth/components/ResetPasswordCard";

// Server Component so `metadata` stays valid — same reasoning as login/page.tsx
// (TASK-376): the actual translated markup lives in ResetPasswordCard.tsx, which needs
// `useTranslations` and so must be a Client Component.
export const metadata = { title: "Новий пароль — ShelfGuard" };

export default function ResetPasswordPage() {
  return <ResetPasswordCard />;
}
