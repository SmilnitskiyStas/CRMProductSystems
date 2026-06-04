import { redirect } from "next/navigation";

// Root → always redirect. Authenticated users go to /dashboard, others to /login.
// The /dashboard layout handles the auth check.
export default function RootPage() {
  redirect("/dashboard");
}
