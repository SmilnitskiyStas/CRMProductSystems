import { NextRequest, NextResponse } from "next/server";
import createIntlMiddleware from "next-intl/middleware";
import { routing } from "@/i18n/routing";

// Runs on the Edge. Redirects unauthenticated users away from protected routes.
// Only checks the HttpOnly refreshToken cookie as a lightweight proxy;
// the real auth validation happens server-side on /api/auth/me.

const PROTECTED = ["/dashboard", "/stock", "/products", "/analytics", "/provider"];
const AUTH_ROUTES = ["/login", "/forgot-password"];

// next-intl handles locale routing/detection only for public, unauthenticated
// pages under app/[locale]/ — the landing page (`/` = uk, `/en` = en), the
// QR/deep-link retailer join page (`/join/{slug}` = uk, `/en/join/{slug}` = en,
// TASK-549), and the retail vertical marketing page (`/retail` = uk,
// `/en/retail` = en, TASK-713), plus /features, /roadmap, /how-it-works and
// /for-whom (TASK-719). Everything else (dashboard, auth, API routes)
// keeps going through the existing auth logic below, untouched.
//
// This rewrite is load-bearing, not cosmetic: `uk` has no URL prefix, so an
// unprefixed request like `/join/abc` must be rewritten (internally, URL stays
// unprefixed) to `/uk/join/abc` before Next's router can match it to
// app/[locale]/join/[slug]/page.tsx — without this, that route 404s for every
// default-locale visitor and only ever works via the `/en` prefix. Any new
// top-level page under app/[locale]/ must be added to INTL_PATH_PREFIXES below
// or it will 404 on its unprefixed (uk) URL.
const intlMiddleware = createIntlMiddleware(routing);
const INTL_PATH_PREFIXES = [
  "/en",
  "/join",
  "/retail",
  "/features",
  "/roadmap",
  "/how-it-works",
  "/for-whom",
];

export function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;

  // SEO-01: `/uk/...` duplicates the unprefixed default-locale URL. Redirect before
  // intl rewriting; nextUrl.clone() keeps the query string.
  if (pathname === "/uk" || pathname.startsWith("/uk/")) {
    const url = request.nextUrl.clone();
    url.pathname = pathname.slice(3) || "/";
    return NextResponse.redirect(url, 308);
  }

  const isIntlPath =
    pathname === "/" || INTL_PATH_PREFIXES.some((prefix) => pathname.startsWith(prefix));
  if (isIntlPath) {
    return intlMiddleware(request);
  }

  // SEO-04: everything outside the public marketing pages (login, dashboard, ...)
  // is not for search engines. Header form works for the client-rendered layouts.
  const res = routeApp(request, pathname);
  if (pathname !== "/robots.txt" && pathname !== "/sitemap.xml") {
    res.headers.set("X-Robots-Tag", "noindex");
  }
  return res;
}

function routeApp(request: NextRequest, pathname: string): NextResponse {
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
