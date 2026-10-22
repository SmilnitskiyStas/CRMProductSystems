import type { CSSProperties } from "react";

/** Form control (width 100%). */
export const inputStyle: CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "8px 12px",
  outline: "none",
};

/** Toolbar / filter control (auto width). */
export const filterInputStyle: CSSProperties = {
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "7px 12px",
  outline: "none",
};

export const selectStyle: CSSProperties = { ...inputStyle };

export const textareaStyle: CSSProperties = { ...inputStyle, resize: "vertical", fontFamily: "inherit" };

export const labelStyle: CSSProperties = { display: "block", color: "#9CA3AF", fontSize: 12, marginBottom: 5 };
