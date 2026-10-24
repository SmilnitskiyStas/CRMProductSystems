export interface ResetGrant { email: string; token: string }

export function readResetGrant(fragment: string): ResetGrant | null {
  const values = new URLSearchParams(fragment.replace(/^#/, ""));
  const email = values.get("email") ?? "";
  const token = values.get("token") ?? "";
  if (values.getAll("email").length !== 1 || values.getAll("token").length !== 1
      || email.length > 254 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)
      || !/^[A-Za-z0-9_-]{43}$/.test(token)) return null;
  return { email, token };
}
