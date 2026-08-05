import { NextRequest, NextResponse } from "next/server";
import createIntlMiddleware from "next-intl/middleware";
import { routing } from "@/i18n/routing";

// Runs on the Edge. Redirects unauthenticated users away from protected routes.
// Only checks the HttpOnly refreshToken cookie as a lightweight proxy;
// the real auth validation happens server-side on /api/auth/me.

const PROTECTED = ["/dashboard", "/stock", "/products", "/analytics", "/provider"];
const AUTH_ROUTES = ["/login", "/forgot-password"];

// next-intl handles locale routing/detection only for the public landing
// (`/` = uk, `/en` = en). Everything else (dashboard, auth, API routes) keeps
// going through the existing auth logic below, untouched.
const intlMiddleware = createIntlMiddleware(routing);

export function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;

  const isLandingPath = pathname === "/" || pathname.startsWith("/en");
  if (isLandingPath) {
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
