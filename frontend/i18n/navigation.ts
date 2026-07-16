import { createNavigation } from "next-intl/navigation";
import { routing } from "./routing";

// Locale-aware Link/usePathname/useRouter, scoped to the landing routing
// config above. Only used inside app/[locale]/* — the rest of the app keeps
// using plain next/link and next/navigation.
export const { Link, usePathname, useRouter, redirect, getPathname } =
  createNavigation(routing);
