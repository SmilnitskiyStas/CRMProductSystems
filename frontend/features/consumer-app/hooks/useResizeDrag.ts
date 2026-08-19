"use client";

import { useCallback, useRef, useState, type CSSProperties, type PointerEvent as ReactPointerEvent } from "react";

interface UseResizeDragOptions {
  /** "width" tracks horizontal pointer movement, "height" tracks vertical. */
  axis: "width" | "height";
  /** The block's current (last-committed or live) value — the drag gesture's starting point, and
   *  the value reported whenever a drag isn't in progress. */
  value: number;
  min: number;
  max: number;
  /** Fired exactly once per drag gesture, on release, with the final clamped value. Never fired
   *  on `pointermove` — callers relying on this to avoid churning save/dirty state mid-drag. */
  onCommit: (value: number) => void;
}

interface UseResizeDragResult {
  /** The value to render right now: the live in-drag value while dragging, `value` otherwise. */
  value: number;
  dragging: boolean;
  handleProps: {
    onPointerDown: (e: ReactPointerEvent) => void;
    onPointerMove: (e: ReactPointerEvent) => void;
    onPointerUp: (e: ReactPointerEvent) => void;
    onPointerCancel: (e: ReactPointerEvent) => void;
    style: CSSProperties;
  };
}

/**
 * TASK-565: small reusable single-axis pointer-drag hook backing the App Preview panel's 4
 * resize handles (`blockPreviews.tsx`'s `ResizeHandle`). No drag library — `@dnd-kit` (already in
 * this repo, driving `AppBuilderCanvas.tsx`'s canvas reorder) is built for sortable-list
 * drag-and-drop, not a single-axis numeric resize; a dozen lines of pointer-event code here is
 * simpler than fighting that API for something it isn't shaped for (explicit prior decision, see
 * task brief).
 *
 * Uses native Pointer Events + `setPointerCapture` so the handle keeps receiving move/up events
 * even if the cursor leaves its bounds mid-drag — no `document`-level listener wiring needed.
 * Shows the dragged value live via local state during the gesture (never touches the caller's
 * persisted state) and calls `onCommit` exactly once, on `pointerup`/`pointercancel`, clamped to
 * `[min, max]`.
 */
export function useResizeDrag({ axis, value, min, max, onCommit }: UseResizeDragOptions): UseResizeDragResult {
  const [dragging, setDragging] = useState(false);
  const [liveValue, setLiveValue] = useState(value);
  const startPos = useRef(0);
  const startValue = useRef(value);

  const clamp = useCallback((v: number) => Math.min(max, Math.max(min, v)), [min, max]);

  function onPointerDown(e: ReactPointerEvent) {
    e.preventDefault();
    e.stopPropagation();
    try {
      (e.target as Element).setPointerCapture(e.pointerId);
    } catch {
      // Some pointer sources (e.g. a synthetic/simulated pointer with no matching active device)
      // can reject capture — the drag still works via the handle's own move/up listeners, capture
      // is purely a "keep receiving events if the cursor leaves the handle" convenience.
    }
    startPos.current = axis === "width" ? e.clientX : e.clientY;
    startValue.current = value;
    setLiveValue(value);
    setDragging(true);
  }

  function onPointerMove(e: ReactPointerEvent) {
    if (!dragging) return;
    const pos = axis === "width" ? e.clientX : e.clientY;
    setLiveValue(clamp(startValue.current + (pos - startPos.current)));
  }

  function endDrag() {
    if (!dragging) return;
    setDragging(false);
    onCommit(liveValue);
  }

  return {
    value: dragging ? liveValue : value,
    dragging,
    handleProps: {
      onPointerDown,
      onPointerMove,
      onPointerUp: endDrag,
      onPointerCancel: endDrag,
      style: { touchAction: "none" },
    },
  };
}
