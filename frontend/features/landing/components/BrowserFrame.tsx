// Decorative browser-chrome frame around product screenshots. Server-safe.

export function BrowserFrame({
  children,
  glow = false,
}: {
  children: React.ReactNode;
  glow?: boolean;
}) {
  return (
    <div className="relative">
      {glow && (
        <div
          aria-hidden="true"
          className="absolute -inset-x-8 -top-10 bottom-0 -z-10 rounded-[40px] bg-[radial-gradient(ellipse_at_top,rgba(45,125,210,0.22),transparent_65%)] blur-2xl"
        />
      )}
      <div className="overflow-hidden rounded-xl border border-white/10 bg-[#0D1220] shadow-2xl shadow-black/50">
        <div className="flex items-center gap-2 border-b border-white/[0.07] bg-white/[0.03] px-4 py-2.5">
          <span className="h-2.5 w-2.5 rounded-full bg-[#ef4444]/70" />
          <span className="h-2.5 w-2.5 rounded-full bg-[#f59e0b]/70" />
          <span className="h-2.5 w-2.5 rounded-full bg-[#22c55e]/70" />
          <span className="ml-3 hidden rounded-md bg-white/[0.05] px-3 py-0.5 text-[11px] text-slate-500 sm:inline-block">
            agrusystems.pp.ua
          </span>
        </div>
        {children}
      </div>
    </div>
  );
}
