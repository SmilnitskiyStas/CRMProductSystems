export const PASSWORD_RECOVERY_EVENT = "auth.password_reset_link_requested";
type RecoveryPayload = { resetUrl?: string; expiresInMinutes?: number; expiresAt?: string };

function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char]!);
}

export function recoveryExpired(payload: unknown, now = Date.now()): boolean {
  const expiry = Date.parse((payload as RecoveryPayload | null)?.expiresAt ?? "");
  return !Number.isFinite(expiry) || expiry <= now;
}

/** Reset proof is delivered only to the existing account email, never Telegram/push. */
export function formatPasswordRecoveryEmail(payload: unknown): { subject: string; html: string } {
  const data = payload as RecoveryPayload | null;
  let url: URL;
  try { url = new URL(data?.resetUrl ?? ""); } catch { throw new Error("Invalid password recovery notification."); }
  const proof = new URLSearchParams(url.hash.slice(1));
  if (url.protocol !== "https:" || url.username || url.password || url.search
      || !url.pathname.endsWith("/reset-password") || !proof.get("email")
      || !/^[a-zA-Z0-9_-]{43}$/.test(proof.get("token") ?? ""))
    throw new Error("Invalid password recovery notification.");
  const link = escapeHtml(url.href);
  return { subject: "[ShelfGuard] Відновлення пароля",
    html: `<h2>ShelfGuard</h2><p><a href="${link}">Встановити новий пароль</a></p>`
      + "<p>Посилання дійсне 15 хвилин і використовується один раз. Ваш чинний пароль не змінено.</p>"
      + "<p>Якщо це не ви — проігноруйте лист. Не передавайте посилання іншим.</p>" };
}

export function redactedTargetedPayload(eventType: string, payload: unknown): unknown {
  return eventType === PASSWORD_RECOVERY_EVENT || eventType === "auth.password_reset_requested"
    ? { expiresInMinutes: (payload as RecoveryPayload | null)?.expiresInMinutes ?? 15 }
    : payload;
}
