# TASK-325 — Collapsed sidebar nav groups unreachable

**Agent:** frontend-developer
**Status:** done

## Bug

`NavGroupSection` in `frontend/components/layout/Sidebar.tsx`, collapsed branch, rendered only
a static `<div>` with an icon + tooltip and no click handler. Every nav group's children became
unreachable whenever the sidebar was collapsed — including `SUPPLIER_NAV_GROUP`
(`alwaysExpanded: true`), so supplier_admin users lost access to "Реквізити договору" and other
cabinet pages if the sidebar happened to be collapsed. Both reported symptoms were this one bug.

## Fix

`frontend/components/layout/Sidebar.tsx`:
- New `CollapsedGroupTrigger` component (inserted above `NavGroupSection`) replaces the old static
  `<div>` in the collapsed branch.
- Multi-item groups: clickable icon button opens a portal-rendered popover next to the icon,
  listing `visibleItems` as `Link`s (label + icon, badge preserved). Positioning/dismissal follows
  the same pattern as `components/ui/ActionMenu.tsx` (portal to `document.body`, outside-click via
  `mousedown`, reposition on scroll/resize, viewport-aware flip — here flipping up if the popover
  would overflow the bottom of the screen, since the trigger sits in a vertical rail rather than a
  toolbar). Popover closes on item click (navigation) or outside click.
- Single-item groups (e.g. `support`, or `admin` when filtered down to one item for a role):
  navigate directly via the existing `NavLink`, no 1-row popover.
- Non-collapsed branch untouched.

## Verification

- `npx tsc --noEmit` in `frontend/` — passes, no errors.
- No live browser test performed (no login credentials in this session) — logic verified by
  read-through; a separate pass should manually test in the browser.

## Reviewer should double-check

- Popover positioning near bottom of viewport (flip-up heuristic uses an estimated height:
  `40 + items.length * 36`, not measured — could be slightly off for very long labels wrapping,
  though current nav items are all single-line).
- z-index: popover uses `9999` (same as `ActionMenu`), confirm nothing else in the dashboard layout
  stacks above that.
- Popover is left-anchored to `rect.right + 8`, i.e. always opens to the right of the 64px collapsed
  rail — fine at normal viewport widths, but on a very narrow browser window it could overflow the
  right edge (no right-edge flip implemented, unlike `ActionMenu`'s left-edge flip). Not expected to
  matter in practice since the app isn't used at very narrow widths, but worth a visual check.
