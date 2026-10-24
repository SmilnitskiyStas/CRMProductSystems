import { renderToStaticMarkup } from "react-dom/server";
import { NextIntlClientProvider } from "next-intl";
import { describe, it, expect, vi } from "vitest";
import { MfaSetupReminder } from "./MfaSetupReminder";
import uk from "@/messages/uk.json";
import en from "@/messages/en.json";

const profile = vi.hoisted(() => ({ user: undefined as undefined | { twoFactorEnabled: boolean; role: string } }));
vi.mock("../hooks/useAuth", () => ({ useMe: () => ({ data: profile.user }) }));

function render(locale = "uk") {
  return renderToStaticMarkup(<NextIntlClientProvider locale={locale} timeZone="Europe/Kyiv" messages={locale === "uk" ? uk : en}><MfaSetupReminder /></NextIntlClientProvider>);
}

describe("MFA settings reminder", () => {
  it.each(["provider", "enterprise_admin", "network_manager", "store_manager", "staff", "supplier_admin"])("shows a non-blocking settings link for unenrolled %s", role => {
    profile.user = { role, twoFactorEnabled: false };
    const html = render();
    expect(html).toContain('role="status"');
    expect(html).toContain('href="/settings-user#security"');
    expect(html).toContain(uk.Dashboard.auth.mfaReminder.message);
    expect(html).not.toContain('role="dialog"');
    expect(html).not.toContain("<input");
  });
  it("hides as soon as the account enables MFA", () => {
    profile.user = { role: "provider", twoFactorEnabled: true };
    expect(render()).toBe("");
  });
  it("does not show an enrollment prompt for missing or consumer identities", () => {
    profile.user = undefined; expect(render()).toBe("");
    profile.user = { role: "consumer", twoFactorEnabled: false }; expect(render()).toBe("");
  });
  it("uses English account-settings text when selected", () => {
    profile.user = { role: "staff", twoFactorEnabled: false };
    expect(render("en")).toContain(en.Dashboard.auth.mfaReminder.message);
  });
});
