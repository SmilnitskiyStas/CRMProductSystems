"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useUpdateProfile } from "../hooks/useProfile";
import { resolveDashboardLocale, setDashboardLocale, type DashboardLocale } from "@/i18n/dashboard-locale";

// Language names are shown in themselves, not translated — standard practice for a
// language picker (an English speaker still needs to recognize "Українська").
const OPTIONS: { value: DashboardLocale; label: string }[] = [
  { value: "uk", label: "Українська" },
  { value: "en", label: "English" },
];

/**
 * Dashboard language switcher (i18n rollout Block 1, TASK-376). Lives in the Profile
 * settings tab (Settings -> Profile) per the approved rollout plan.
 *
 * Flips the `sg_locale` cookie immediately — `DashboardIntlProvider` re-renders via the
 * `sg-locale-changed` event, no page reload needed — and separately best-effort-saves
 * `user.preferredLocale` through the existing profile-update mutation (PUT /api/auth/me,
 * extended with an optional `preferredLocale` field on the backend). A failure there
 * (e.g. the field/endpoint isn't deployed yet) never blocks the switch itself, which
 * already works from the cookie alone — `useUpdateProfile`'s `mutate()` call is
 * fire-and-forget by design (React Query never throws to the caller from `.mutate()`).
 */
export function LanguageSwitcher() {
  const t = useTranslations("Dashboard.profile.language");
  const { data: me } = useMe();
  const update = useUpdateProfile();

  // Mirrors DashboardIntlProvider's own resolution so the highlighted option always
  // matches what's actually rendered. Recomputed after mount only — avoids an
  // SSR/hydration mismatch (resolution reads cookie/localStorage/navigator).
  const [active, setActive] = useState<DashboardLocale>("uk");
  useEffect(() => {
    setActive(resolveDashboardLocale());
  }, []);

  function choose(locale: DashboardLocale) {
    if (locale === active) return;
    setActive(locale);
    setDashboardLocale(locale);

    if (me) {
      update.mutate({
        fullName: me.fullName,
        phone: me.phone ?? undefined,
        preferredLocale: locale,
      });
    }
  }

  return (
    <div>
      <h3 style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 600, margin: "0 0 4px" }}>
        {t("title")}
      </h3>
      <p style={{ color: "#4B5563", fontSize: 12, margin: "0 0 12px" }}>{t("subtitle")}</p>
      <div style={{ display: "flex", gap: 8 }}>
        {OPTIONS.map((opt) => {
          const isActive = opt.value === active;
          return (
            <button
              key={opt.value}
              type="button"
              onClick={() => choose(opt.value)}
              style={{
                padding: "8px 16px",
                borderRadius: 8,
                border: `1px solid ${isActive ? "#3B82F6" : "#374151"}`,
                background: isActive ? "#1D3461" : "transparent",
                color: isActive ? "#93C5FD" : "#9CA3AF",
                fontSize: 13,
                fontWeight: isActive ? 600 : 400,
                cursor: "pointer",
                transition: "background 0.15s, color 0.15s, border-color 0.15s",
              }}
            >
              {opt.label}
            </button>
          );
        })}
      </div>
    </div>
  );
}
