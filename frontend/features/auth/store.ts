// Simple module-level store for the current authenticated user.
// Token persistence lives in lib/api.ts (localStorage + in-memory).

import type { AuthUserDto } from "./types";

const USER_KEY = "sg_user";
let _user: AuthUserDto | null = null;

export function getStoredUser(): AuthUserDto | null {
  if (_user) return _user;
  if (typeof window === "undefined") return null;
  try {
    const raw = localStorage.getItem(USER_KEY);
    if (raw) _user = JSON.parse(raw) as AuthUserDto;
  } catch { /* ignore */ }
  return _user;
}

export function setStoredUser(user: AuthUserDto): void {
  _user = user;
  if (typeof window !== "undefined")
    localStorage.setItem(USER_KEY, JSON.stringify(user));
}

export function clearStoredUser(): void {
  _user = null;
  if (typeof window !== "undefined") localStorage.removeItem(USER_KEY);
}
