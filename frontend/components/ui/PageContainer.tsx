import type { CSSProperties, ReactNode } from "react";

/** Standard page shell: padding 28px 32px, vertical flex with 20px gap. */
export function PageContainer({ children, style }: { children: ReactNode; style?: CSSProperties }) {
  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 20, ...style }}>
      {children}
    </div>
  );
}
