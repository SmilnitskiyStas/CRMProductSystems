# Mobile UI foundation

Import primitives and tokens from `@/components/ui`. Components use static NativeWind classes,
React Native accessibility props, 44–48 px touch targets, safe areas, keyboard avoidance, and font
scaling by default. `Screen` owns the screen boundary; `Header` owns title/back/action layout;
`Button`/`IconButton` expose disabled and busy state; fields expose label/error state and optional
trailing actions; state
components standardize empty, error and loading presentation; `Modal`, `Sheet` and
`ConfirmDialog` preserve Android Back through `onRequestClose`; `OfflineBanner` is display-only and
receives connectivity state from its screen. Tokens in `tokens.ts` are the canonical JS values and
match the `primary`/`status` Tailwind palette.
