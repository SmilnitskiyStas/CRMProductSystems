export const DEFAULT_TIME_ZONE = "Europe/Kyiv";

export const TIME_ZONES = [
  "Europe/Kyiv",
  "Europe/Warsaw",
  "Europe/Berlin",
  "Europe/London",
  "Europe/Chisinau",
  "Europe/Vilnius",
  "Asia/Tbilisi",
  "UTC",
] as const;

/** Translation key (Dashboard.locations.form.timeZones.<key>) for an IANA id. */
export function timeZoneKey(id: string): string {
  return id.replace("/", "_");
}

export function isKnownTimeZone(id: string): boolean {
  return (TIME_ZONES as readonly string[]).includes(id);
}
