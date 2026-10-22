import type { CSSProperties, ReactNode } from "react";

/** Status / stage / source badge. Callers keep their own {bg, text} colour maps. */
export function StatusPill({
  bg,
  text,
  children,
  style,
}: {
  bg: string;
  text: string;
  children: ReactNode;
  style?: CSSProperties;
}) {
  return (
    <span
      style={{
        display: "inline-block",
        padding: "3px 8px",
        borderRadius: 20,
        fontSize: 11,
        fontWeight: 600,
        background: bg,
        color: text,
        whiteSpace: "nowrap",
        ...style,
      }}
    >
      {children}
    </span>
  );
}
