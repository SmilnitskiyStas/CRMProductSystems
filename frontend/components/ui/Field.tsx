import type { ReactNode } from "react";
import { labelStyle } from "./fieldStyles";

/** Label + control wrapper. */
export function Field({ label, required, children }: { label: ReactNode; required?: boolean; children: ReactNode }) {
  return (
    <div>
      <label style={labelStyle}>
        {label}
        {required && <span style={{ color: "#F87171" }}> *</span>}
      </label>
      {children}
    </div>
  );
}
