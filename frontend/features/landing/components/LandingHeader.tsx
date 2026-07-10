"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { Menu, X } from "lucide-react";
import { Logo } from "./Logo";

const NAV = [
  { href: "#features", label: "Можливості" },
  { href: "#how-it-works", label: "Як це працює" },
  { href: "#pricing", label: "Тарифи" },
  { href: "#faq", label: "FAQ" },
];

export function LandingHeader() {
  const [scrolled, setScrolled] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);

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
        <a href="#top" aria-label="ShelfGuard — на початок сторінки">
          <Logo />
        </a>

        <nav className="hidden items-center gap-7 md:flex" aria-label="Основна навігація">
          {NAV.map((item) => (
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
          <Link
            href="/login"
            className="rounded-md px-3.5 py-2 text-sm font-medium text-slate-300 transition-colors hover:bg-white/5 hover:text-white"
          >
            Увійти
          </Link>
          <a
            href="#lead-form"
            className="rounded-md bg-[#2D7DD2] px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-[#3E8CDD]"
          >
            Залишити заявку
          </a>
        </div>

        <button
          type="button"
          className="rounded-md p-2 text-slate-300 hover:bg-white/5 hover:text-white md:hidden"
          onClick={() => setMenuOpen((v) => !v)}
          aria-expanded={menuOpen}
          aria-label={menuOpen ? "Закрити меню" : "Відкрити меню"}
        >
          {menuOpen ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
        </button>
      </div>

      {menuOpen && (
        <nav
          className="border-t border-white/[0.07] bg-[#0B0F17]/95 px-4 pb-5 pt-3 backdrop-blur-md md:hidden"
          aria-label="Мобільна навігація"
        >
          <div className="flex flex-col gap-1">
            {NAV.map((item) => (
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
            <a
              href="#lead-form"
              onClick={() => setMenuOpen(false)}
              className="rounded-md bg-[#2D7DD2] px-4 py-2.5 text-center text-sm font-semibold text-white hover:bg-[#3E8CDD]"
            >
              Залишити заявку
            </a>
            <Link
              href="/login"
              className="rounded-md border border-white/10 px-4 py-2.5 text-center text-sm font-medium text-slate-300 hover:bg-white/5 hover:text-white"
            >
              Увійти
            </Link>
          </div>
        </nav>
      )}
    </header>
  );
}
