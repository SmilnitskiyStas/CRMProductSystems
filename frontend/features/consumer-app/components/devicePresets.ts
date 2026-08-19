// TASK-567: device-model picker for the App Builder live preview (`AppPreviewPanel.tsx`) — lets
// the admin see the draft rendered at an actual device's real screen proportions instead of the
// fixed 320px-wide, unconstrained-height mock box `PhoneFrame.tsx` used before this task.
//
// CSS-pixel (not hardware-pixel) viewport sizes, per https://blisk.io/devices/details/* — these
// are what `window.innerWidth`/`innerHeight` report on the real device, i.e. what CSS actually
// lays out against, not the marketing "physical resolution" figure (e.g. Pixel 8 Pro's panel is
// 1344×2992 hardware px at ~3x DPR, which is not usable directly for CSS sizing).
export const DEVICE_PRESETS = [
  { id: "pixel8pro", label: "Google Pixel 8 Pro", width: 448, height: 998 },
  { id: "pixel8", label: "Google Pixel 8", width: 412, height: 915 },
  { id: "iphone15promax", label: "iPhone 15 Pro Max", width: 430, height: 932 },
  { id: "iphone15", label: "iPhone 15 / 14", width: 390, height: 844 },
  { id: "galaxys23ultra", label: "Samsung Galaxy S23 Ultra", width: 384, height: 824 },
] as const;

export type DevicePreset = (typeof DEVICE_PRESETS)[number];
export type DevicePresetId = DevicePreset["id"];

/** Matches what the user specifically asked to see (Google Pixel 8 Pro). */
export const DEFAULT_DEVICE_PRESET_ID: DevicePresetId = "pixel8pro";

export function getDevicePreset(id: DevicePresetId): DevicePreset {
  return DEVICE_PRESETS.find((preset) => preset.id === id) ?? DEVICE_PRESETS[0];
}
