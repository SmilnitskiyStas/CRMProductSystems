import { expect, it } from "vitest";
import { buildNavGroups } from "./Sidebar";
import en from "@/messages/en.json";
import uk from "@/messages/uk.json";

it("exposes the complete production workflow with translated labels in both locales", () => {
  for (const messages of [en, uk]) {
    const t = ((key: string) => key.split(".").reduce<unknown>((value, part) =>
      (value as Record<string, unknown>)[part], messages.Dashboard.sidebar.groups)) as Parameters<typeof buildNavGroups>[0];
    const group = buildNavGroups(t).find((item) => item.key === "production")!;
    expect(group.moduleKey).toBe("production");
    expect(group.items.map((item) => item.href)).toEqual([
      "/production/overview", "/production/requests", "/production/plan", "/production/orders",
      "/production/shipments", "/production/warehouse", "/production/analytics", "/production/recipes",
    ]);
    expect(group.items.every((item) => typeof item.label === "string" && item.label.length > 0)).toBe(true);
  }
});
