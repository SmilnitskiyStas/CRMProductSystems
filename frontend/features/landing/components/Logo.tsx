// ShelfGuard logo — shield with shelf lines + wordmark. Pure SVG, server-safe.

export function LogoMark({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      className={className}
      aria-hidden="true"
    >
      <path
        d="M12 2.4 4.4 5.5v5.6c0 5 3.1 8.7 7.6 10.5 4.5-1.8 7.6-5.5 7.6-10.5V5.5L12 2.4Z"
        fill="#2D7DD2"
        fillOpacity="0.12"
        stroke="#2D7DD2"
        strokeWidth="1.7"
        strokeLinejoin="round"
      />
      <path d="M8.2 9.2h7.6" stroke="#E8EDF5" strokeWidth="1.5" strokeLinecap="round" />
      <path d="M8.2 12.7h7.6" stroke="#E8EDF5" strokeWidth="1.5" strokeLinecap="round" />
      <path d="M9.8 16.2h4.4" stroke="#E8EDF5" strokeWidth="1.5" strokeLinecap="round" />
    </svg>
  );
}

export function Logo({ byline = true }: { byline?: boolean }) {
  return (
    <span className="inline-flex items-center gap-2.5">
      <LogoMark className="h-8 w-8 shrink-0" />
      <span className="flex flex-col justify-center leading-none">
        <span className="text-[17px] font-semibold tracking-tight text-white">
          ShelfGuard
        </span>
        {byline && (
          <span className="mt-0.5 text-[10px] font-medium tracking-wide text-slate-500">
            by AgruSystems
          </span>
        )}
      </span>
    </span>
  );
}
