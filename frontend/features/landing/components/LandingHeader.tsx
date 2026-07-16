"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { Menu, X } from "lucide-react";
import { useLocale, useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Link as LocaleLink, usePathname } from "@/i18n/navigation";
import { Logo } from "./Logo";

export function LandingHeader() {
  const t = useTranslations("Landing.header");
  const locale = useLocale();
  const pathname = usePathname();
  const [scrolled, setScrolled] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);

  const nav = t.raw("nav") as { href: string; label: string }[];
  const [hash, setHash] = useState("");

  useEffect(() => {
    const syncHash = () => setHash(window.location.hash);
    syncHash();
    window.addEventListener("hashchange", syncHash);
    return () => window.removeEventListener("hashchange", syncHash);
  }, []);

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 8);
    onScroll();
    window.addEventListener("scroll", onScroll, { passive: true });
    return () => window.removeEventListener("scroll", onScroll);
  }, []);

  return (
    <header
      className={`fixed inset-x-0 top-0 z-50 transition-colors duration-300 ${
        scrolled || menuOpen
          ? "border-b border-white/[0.07] bg-[#0B0F17]/90 backdrop-blur-md"
          : "border-b border-transparent bg-transparent"
      }`}
    >
      <div className="mx-auto flex h-16 max-w-6xl items-center justify-between px-4 sm:px-6 lg:px-8">
        <a href="#top" aria-label={t("logoAriaLabel")}>
          <Logo />
        </a>

        <nav className="hidden items-center gap-7 md:flex" aria-label={t("mainNavAriaLabel")}>
          {nav.map((item) => (
            <a
              key={item.href}
              href={item.href}
              className="text-sm text-slate-400 transition-colors hover:text-white"
            >
              {item.label}
            </a>
          ))}
        </nav>

        <div className="hidden items-center gap-3 md:flex">
          <LangSwitch locale={locale} pathname={pathname} hash={hash} t={t} />
          <Button asChild variant="ghost">
            <Link href="/login">{t("login")}</Link>
          </Button>
          <Button asChild>
            <a href="#lead-form">{t("cta")}</a>
          </Button>
        </div>

        <button
          type="button"
          className="rounded-md p-2 text-slate-300 hover:bg-white/5 hover:text-white md:hidden"
          onClick={() => setMenuOpen((v) => !v)}
          aria-expanded={menuOpen}
          aria-label={menuOpen ? t("menuCloseAriaLabel") : t("menuOpenAriaLabel")}
        >
          {menuOpen ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
        </button>
      </div>

      {menuOpen && (
        <nav
          className="border-t border-white/[0.07] bg-[#0B0F17]/95 px-4 pb-5 pt-3 backdrop-blur-md md:hidden"
          aria-label={t("mobileNavAriaLabel")}
        >
          <div className="flex flex-col gap-1">
            {nav.map((item) => (
              <a
                key={item.href}
                href={item.href}
                onClick={() => setMenuOpen(false)}
                className="rounded-md px-3 py-2.5 text-[15px] text-slate-300 hover:bg-white/5 hover:text-white"
              >
                {item.label}
              </a>
            ))}
          </div>
          <div className="mt-4 flex flex-col gap-2.5">
            <div className="flex justify-center">
              <LangSwitch locale={locale} pathname={pathname} hash={hash} t={t} />
            </div>
            <Button asChild className="w-full">
              <a href="#lead-form" onClick={() => setMenuOpen(false)}>
                {t("cta")}
              </a>
            </Button>
            <Button asChild variant="outline" className="w-full">
              <Link href="/login">{t("login")}</Link>
            </Button>
          </div>
        </nav>
      )}
    </header>
  );
}

function LangSwitch({
  locale,
  pathname,
  hash,
  t,
}: {
  locale: string;
  pathname: string;
  hash: string;
  t: ReturnType<typeof useTranslations>;
}) {
  const href = { pathname, hash: hash.replace(/^#/, "") };

  return (
    <div
      className="flex items-center rounded-full border border-white/10 bg-white/[0.03] p-0.5 text-xs font-medium"
      aria-label={t("langSwitchLabel")}
    >
      <LocaleLink
        href={href}
        locale="uk"
        className={`rounded-full px-2.5 py-1 transition-colors ${
          locale === "uk" ? "bg-white/10 text-white" : "text-slate-400 hover:text-white"
        }`}
      >
        {t("langUk")}
      </LocaleLink>
      <LocaleLink
        href={href}
        locale="en"
        className={`rounded-full px-2.5 py-1 transition-colors ${
          locale === "en" ? "bg-white/10 text-white" : "text-slate-400 hover:text-white"
        }`}
      >
        {t("langEn")}
      </LocaleLink>
    </div>
  );
}
