import { describe, expect, it } from "vitest";
import {
  INITIAL_STATE,
  buildDefinition,
  defaultStoreIds,
  parseDecimal,
  stateFromDefinition,
  stepProblems,
  type WizardState,
} from "./definition";
import {
  ISSUE_CODES,
  fieldKey,
  issueKey,
  segmentsText,
  stepOfField,
  storesSummary,
  summarizeDefinition,
} from "./messages";
import uk from "../../../messages/uk.json";

const base = (over: Partial<WizardState>): WizardState => ({
  ...INITIAL_STATE,
  name: "Test",
  items: [{ id: "i1", name: "Milk" }],
  ...over,
});

describe("parseDecimal", () => {
  it("accepts comma and dot decimals", () => {
    expect(parseDecimal("9,99")).toBe(9.99);
    expect(parseDecimal(" 10.5 ")).toBe(10.5);
    expect(parseDecimal("0,01")).toBe(0.01);
  });
  it("rejects garbage and empty", () => {
    expect(parseDecimal("")).toBeNull();
    expect(parseDecimal("abc")).toBeNull();
    expect(parseDecimal("1,2,3")).toBeNull();
    expect(parseDecimal("-5")).toBeNull();
  });
});

describe("buildDefinition", () => {
  it("percent_off emits only percent", () => {
    const d = buildDefinition(base({ type: "percent_off", percent: "20" }));
    expect(d.params).toEqual({ percent: 20 });
    expect(d.targets).toEqual({ itemIds: ["i1"], categoryIds: [] });
    expect(d.scope.storeIds).toEqual([]);
    expect(d.audience).toEqual({ kind: "all" });
  });
  it("fixed_price with comma", () => {
    expect(buildDefinition(base({ type: "fixed_price", price: "9,99" })).params).toEqual({ price: 9.99 });
  });
  it("buy_n_get_m with getPrice", () => {
    const d = buildDefinition(base({ type: "buy_n_get_m", buyQty: "2", getQty: "1", getPrice: "0,01" }));
    expect(d.params).toEqual({ buyQty: 2, getQty: 1, getPrice: 0.01, matchMode: "per_item" });
  });
  it("buy_n_get_m with getPercent omits getPrice", () => {
    const d = buildDefinition(
      base({ type: "buy_n_get_m", getMode: "percent", getPercent: "50", matchMode: "across_set" }),
    );
    expect(d.params.getPercent).toBe(50);
    expect(d.params.getPrice).toBeUndefined();
    expect(d.params.matchMode).toBe("across_set");
  });
  it("scope: stores only when not allStores, weekdays sorted, empty time -> null", () => {
    const d = buildDefinition(
      base({ type: "percent_off", allStores: false, storeIds: ["s1"], weekdays: [5, 1], timeFrom: "08:00" }),
    );
    expect(d.scope.storeIds).toEqual(["s1"]);
    expect(d.scope.weekdays).toEqual([1, 5]);
    expect(d.scope.timeFrom).toBe("08:00");
    expect(d.scope.timeTo).toBeNull();
    expect(d.scope.validFrom).toBeNull();
  });
  it("tier audience carries tierIds", () => {
    const d = buildDefinition(base({ type: "percent_off", audienceKind: "loyalty_tier", tierIds: ["t1"] }));
    expect(d.audience).toEqual({ kind: "loyalty_tier", tierIds: ["t1"] });
  });
  it("round-trips through stateFromDefinition", () => {
    const s = base({
      type: "buy_n_get_m",
      buyQty: "3",
      getQty: "1",
      getPrice: "1,5",
      maxApplications: "4",
      allStores: false,
      storeIds: ["s1", "s2"],
      weekdays: [6, 7],
      priority: "50",
    });
    const d = buildDefinition(s);
    const back = stateFromDefinition("Test", null, d, { items: new Map([["i1", "Milk"]]) });
    expect(buildDefinition(back)).toEqual(d);
    expect(back.items[0].name).toBe("Milk");
  });
});

describe("stepProblems", () => {
  it("requires a type, name and targets progressively", () => {
    expect(stepProblems(INITIAL_STATE, 1)).toEqual(["type_required", "name_required"]);
    expect(stepProblems(base({ type: "percent_off", items: [] }), 2)).toContain("no_targets");
  });
  it("validates params per type", () => {
    expect(stepProblems(base({ type: "percent_off", percent: "150" }), 3)).toContain("percent_out_of_range");
    expect(stepProblems(base({ type: "fixed_price", price: "0" }), 3)).toContain("price_below_minimum");
    expect(stepProblems(base({ type: "fixed_off", amount: "" }), 3)).toContain("missing_param");
    expect(stepProblems(base({ type: "buy_n_get_m", buyQty: "0" }), 3)).toContain("invalid_quantity");
    expect(stepProblems(base({ type: "percent_off", percent: "10" }), 3)).toEqual([]);
  });
  it("checks period, stores and tiers", () => {
    expect(
      stepProblems(base({ type: "percent_off", validFrom: "2026-10-20T10:00", validTo: "2026-10-19T10:00" }), 4),
    ).toContain("invalid_period");
    expect(stepProblems(base({ type: "percent_off", allStores: false }), 4)).toContain("no_stores_selected");
    expect(stepProblems(base({ type: "percent_off", audienceKind: "loyalty_tier" }), 5)).toContain("tier_required");
  });
});

describe("validation-code localization", () => {
  const issues = (uk as unknown as { Dashboard: { promotionRules: { issues: Record<string, string>; fields: Record<string, string> } } })
    .Dashboard.promotionRules;
  it("every contract code has a Ukrainian message", () => {
    for (const c of ISSUE_CODES) expect(issues.issues[issueKey(c)], c).toBeTruthy();
    expect(issues.issues[issueKey("brand_new_code")]).toBeTruthy();
  });
  it("maps field paths", () => {
    expect(fieldKey("params.percent")).toBe("params_percent");
    expect(fieldKey("nope")).toBeNull();
    expect(issues.fields[fieldKey("scope.validTo")!]).toBeTruthy();
  });
  it("routes fields to wizard steps", () => {
    expect(stepOfField("targets.itemIds")).toBe(2);
    expect(stepOfField("params.price")).toBe(3);
    expect(stepOfField("scope.storeIds")).toBe(4);
    expect(stepOfField("audience.tierIds")).toBe(5);
    expect(stepOfField(null)).toBe(6);
  });
});

describe("summary text", () => {
  const t = (key: string, v?: Record<string, string | number>) => `${key}:${JSON.stringify(v ?? {})}`;
  it("describes each type", () => {
    const d = buildDefinition(base({ type: "percent_off", percent: "20" }));
    expect(summarizeDefinition(d, t)).toContain("summary.percent_off");
    expect(summarizeDefinition(buildDefinition(base({ type: "buy_n_get_m" })), t)).toContain("buy_n_get_m_price");
    expect(
      summarizeDefinition(buildDefinition(base({ type: "buy_n_get_m", getMode: "percent" })), t),
    ).toContain("buy_n_get_m_percent");
  });
  it("stores summary and segments", () => {
    expect(storesSummary([], t)).toContain("stores.all");
    expect(storesSummary(["a", "b"], t)).toContain('"count":2');
    expect(segmentsText([{ quantity: 2, unitPrice: 30 }, { quantity: 1, unitPrice: 0.01 }])).toMatch(
      /^2 × 30[,.]00 \+ 1 × 0[,.]01$/,
    );
  });
});

describe("defaultStoreIds", () => {
  it("scoped roles get the header store if it is theirs, else the first location", () => {
    expect(defaultStoreIds("store_manager", "b", ["a", "b"])).toEqual(["b"]);
    expect(defaultStoreIds("network_manager", "zzz", ["a", "b"])).toEqual(["a"]);
    expect(defaultStoreIds("store_manager", undefined, ["a"])).toEqual(["a"]);
    expect(defaultStoreIds("store_manager", undefined, [])).toEqual([]);
  });
  it("admins keep all stores", () => {
    expect(defaultStoreIds("enterprise_admin", "a", ["a"])).toEqual([]);
    expect(defaultStoreIds(undefined, "a", ["a"])).toEqual([]);
  });
});
