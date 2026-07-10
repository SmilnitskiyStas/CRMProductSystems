import Image from "next/image";
import { Button } from "@/components/ui/button";
import { BrowserFrame } from "./BrowserFrame";

export function HeroSection() {
  return (
    <section id="top" className="relative overflow-hidden pb-16 pt-32 sm:pb-24 sm:pt-40">
      {/* Backdrop: subtle grid + top glow */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 -z-10 bg-[linear-gradient(to_right,rgba(255,255,255,0.025)_1px,transparent_1px),linear-gradient(to_bottom,rgba(255,255,255,0.025)_1px,transparent_1px)] bg-[size:56px_56px] [mask-image:radial-gradient(ellipse_60%_50%_at_50%_0%,black,transparent)]"
      />
      <div
        aria-hidden="true"
        className="pointer-events-none absolute left-1/2 top-0 -z-10 h-[480px] w-[900px] -translate-x-1/2 rounded-full bg-[#2D7DD2]/[0.12] blur-[140px]"
      />

      <div className="mx-auto max-w-6xl px-4 sm:px-6 lg:px-8">
        <div className="mx-auto max-w-3xl text-center">
          <p className="hero-fade hero-fade-1 inline-flex items-center gap-2 rounded-full border border-white/10 bg-white/[0.04] px-4 py-1.5 text-[13px] text-slate-300">
            <span className="h-1.5 w-1.5 rounded-full bg-[#22c55e]" />
            Система обліку для продуктових магазинів і мереж
          </p>

          <h1 className="hero-fade hero-fade-2 mt-6 text-4xl font-bold leading-[1.1] tracking-tight text-white sm:text-5xl lg:text-6xl">
            Прострочення видно за тижні.{" "}
            <span className="text-[#5EA3E8]">Покупцям — ніколи.</span>
          </h1>

          <p className="hero-fade hero-fade-3 mx-auto mt-6 max-w-2xl text-lg leading-relaxed text-slate-400">
            ShelfGuard відстежує термін придатності кожної партії за принципом FEFO,
            підказує, що і скільки замовити, пробиває чеки через ПРРО та показує стан
            усіх магазинів на одному екрані.
          </p>

          <div className="hero-fade hero-fade-4 mt-9 flex flex-col items-center justify-center gap-3 sm:flex-row">
            <Button asChild size="lg" className="w-full sm:w-auto">
              <a href="#lead-form">Залишити заявку</a>
            </Button>
            <Button asChild variant="outline" size="lg" className="w-full sm:w-auto">
              <a href="#features">Подивитись можливості</a>
            </Button>
          </div>
        </div>

        <div className="hero-fade hero-fade-4 mx-auto mt-16 max-w-5xl sm:mt-20">
          <BrowserFrame glow>
            <Image
              src="/landing/dashboard-1.jpg"
              alt="Дашборд ShelfGuard: статуси партій Safe, Warning, Critical, Expired, список товарів, що потребують уваги, та швидкі дії"
              width={1280}
              height={574}
              priority
              sizes="(max-width: 640px) 100vw, (max-width: 1100px) 92vw, 1024px"
              className="h-auto w-full"
            />
          </BrowserFrame>
        </div>
      </div>
    </section>
  );
}
