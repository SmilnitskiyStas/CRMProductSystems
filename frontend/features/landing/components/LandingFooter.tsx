import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { Link as LocaleLink } from "@/i18n/navigation";
import { Button } from "@/components/ui/button";
import { Container } from "./Section";
import { Logo } from "./Logo";
import { CookieSettingsLink } from "./CookieSettingsLink";

interface FooterColumn {
  title: string;
  links: { href: string; label: string }[];
}

export async function LandingFooter() {
  const t = await getTranslations("Landing.footer");
  const columns = t.raw("columns") as FooterColumn[];

  return (
    <footer className="border-t border-[var(--l-border)] pb-8 pt-14">
      <Container>
        <div className="grid gap-10 sm:grid-cols-2 lg:grid-cols-5">
          <div className="lg:col-span-2">
            <Logo />
            <p className="mt-4 max-w-xs text-sm leading-relaxed text-[var(--l-muted)]">
              {t("tagline")}
            </p>
            <div className="mt-5 max-w-xs">
              <p className="text-sm font-semibold text-white">{t("contactTitle")}</p>
              <p className="mt-1 text-sm text-[var(--l-muted)]">{t("contactText")}</p>
              <Button asChild size="sm" className="mt-3">
                <LocaleLink href="/for-whom#lead-form">{t("contactCta")}</LocaleLink>
              </Button>
            </div>
          </div>

          {columns.map((col) => (
            <nav key={col.title} aria-label={col.title}>
              <p className="text-sm font-semibold text-white">{col.title}</p>
              <ul className="mt-4 space-y-2.5">
                {col.links.map((link) => (
                  <li key={link.href + link.label}>
                    <LocaleLink
                      href={link.href}
                      className="text-sm text-[var(--l-muted)] transition-colors hover:text-white"
                    >
                      {link.label}
                    </LocaleLink>
                  </li>
                ))}
              </ul>
            </nav>
          ))}
        </div>

        <div className="mt-12 flex flex-col items-center justify-between gap-4 border-t border-[var(--l-border)] pt-6 sm:flex-row">
          <p className="text-sm text-slate-500">{t("rights")}</p>
          <div className="flex items-center gap-5 text-sm">
            <span className="flex items-center gap-2 text-slate-500">
              {t("languageLabel")}:
              <LocaleLink
                href="/"
                locale="uk"
                className="text-[var(--l-muted)] transition-colors hover:text-white"
              >
                UA
              </LocaleLink>
              <LocaleLink
                href="/"
                locale="en"
                className="text-[var(--l-muted)] transition-colors hover:text-white"
              >
                EN
              </LocaleLink>
            </span>
            <CookieSettingsLink />
            <Link
              href="/login"
              className="text-[var(--l-muted)] transition-colors hover:text-white"
            >
              {t("login")}
            </Link>
          </div>
        </div>
      </Container>
    </footer>
  );
}
