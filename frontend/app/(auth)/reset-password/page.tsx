import type { Metadata } from "next";
import { ForgotPasswordCard } from "@/features/auth/components/ForgotPasswordCard";

export const metadata: Metadata = { title: "Reset password | ShelfGuard", robots: { index: false, follow: false }, referrer: "no-referrer" };

export default function ResetPasswordPage() {
  return <ForgotPasswordCard mode="confirm" />;
}
