import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { Logo } from "./Logo";

export async function LandingFooter() {
  const t = await getTranslations("Landing.footer");

  return (
    <footer className="border-t border-white/[0.07] py-10">
      <div className="mx-auto flex max-w-6xl flex-col items-center justify-between gap-6 px-4 sm:flex-row sm:px-6 lg:px-8">
        <Logo />
        <p className="text-sm text-slate-500">{t("rights")}</p>
        <Link href="/login" className="text-sm text-slate-400 transition-colors hover:text-white">
          {t("login")}
        </Link>
      </div>
    </footer>
  );
}
