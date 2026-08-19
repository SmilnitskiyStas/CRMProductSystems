import { NextRequest, NextResponse } from "next/server";
import createIntlMiddleware from "next-intl/middleware";
import { routing } from "@/i18n/routing";

// Runs on the Edge. Redirects unauthenticated users away from protected routes.
// Only checks the HttpOnly refreshToken cookie as a lightweight proxy;
// the real auth validation happens server-side on /api/auth/me.

const PROTECTED = ["/dashboard", "/stock", "/products", "/analytics", "/provider"];
const AUTH_ROUTES = ["/login", "/forgot-password"];

// next-intl handles locale routing/detection only for public, unauthenticated
// pages under app/[locale]/ — the landing page (`/` = uk, `/en` = en) and the
// QR/deep-link retailer join page (`/join/{slug}` = uk, `/en/join/{slug}` = en,
// TASK-549). Everything else (dashboard, auth, API routes) keeps going through
// the existing auth logic below, untouched.
//
// This rewrite is load-bearing, not cosmetic: `uk` has no URL prefix, so an
// unprefixed request like `/join/abc` must be rewritten (internally, URL stays
// unprefixed) to `/uk/join/abc` before Next's router can match it to
// app/[locale]/join/[slug]/page.tsx — without this, that route 404s for every
// default-locale visitor and only ever works via the `/en` prefix.
const intlMiddleware = createIntlMiddleware(routing);
const INTL_PATH_PREFIXES = ["/en", "/join"];

export function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;

  const isIntlPath =
    pathname === "/" || INTL_PATH_PREFIXES.some((prefix) => pathname.startsWith(prefix));
  if (isIntlPath) {
    return intlMiddleware(request);
  }

  const isProtected = PROTECTED.some((p) => pathname.startsWith(p));
  const isAuth = AUTH_ROUTES.some((p) => pathname.startsWith(p));
  const hasSession =
    request.cookies.has("sg_session") || request.cookies.has("refreshToken");

  // Redirect unauthenticated users away from protected routes
  if (isProtected && !hasSession) {
    return NextResponse.redirect(new URL("/login", request.url));
  }

  // Redirect authenticated users away from /login
  if (isAuth && hasSession) {
    return NextResponse.redirect(new URL("/dashboard", request.url));
  }

  return NextResponse.next();
}

export const config = {
  matcher: [
    // Skip static files, images, and API routes
    "/((?!_next|api|favicon.ico|.*\\.(?:svg|png|jpg|jpeg|gif|webp)).*)",
  ],
};
