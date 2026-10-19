"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Switch } from "@/components/ui/switch";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useLocations } from "@/features/locations/hooks/useLocations";
import { LocationsMultiSelectDropdown } from "@/features/users/components/LocationsMultiSelectDropdown";
import { CategoryTypeahead } from "@/features/catalog/components/CategoryTypeahead";
import { isKnownTimeZone, timeZoneKey } from "@/features/locations/lib/timeZones";
import { useTierLookup } from "../hooks/usePromotionRules";
import type { RuleType } from "../types";
import { isStoreScopedRole, type WizardState } from "../lib/definition";
import { ItemPicker } from "./ItemPicker";
import { Chip, ChoiceCard, Field, Note, helpStyle, inputStyle, labelStyle } from "./shared";

export interface StepProps {
  state: WizardState;
  patch: (p: Partial<WizardState>) => void;
}

const TYPES: RuleType[] = ["percent_off", "fixed_off", "fixed_price", "buy_n_get_m"];

/** Step 1 - what kind of promotion. */
export function StepType({ state, patch }: StepProps) {
  const t = useTranslations("Dashboard.promotionRules");
  return (
    <div>
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))", gap: 14, marginBottom: 22 }}>
        {TYPES.map((k) => (
          <ChoiceCard
            key={k}
            selected={state.type === k}
            onClick={() => patch({ type: k })}
            title={t(`types.${k}.title`)}
            text={t(`types.${k}.text`)}
            example={t(`types.${k}.example`)}
          />
        ))}
      </div>
      <Field label={t("wizard.name")} help={t("wizard.nameHelp")} htmlFor="rule-name">
        <input
          id="rule-name"
          style={inputStyle}
          value={state.name}
          maxLength={120}
          placeholder={t("wizard.namePlaceholder")}
          onChange={(e) => patch({ name: e.target.value })}
        />
      </Field>
      <Field label={t("wizard.description")} htmlFor="rule-desc">
        <textarea
          id="rule-desc"
          style={{ ...inputStyle, minHeight: 60, resize: "vertical" }}
          value={state.description}
          onChange={(e) => patch({ description: e.target.value })}
        />
      </Field>
    </div>
  );
}

/** Step 2 - items and/or categories. */
export function StepTargets({ state, patch }: StepProps) {
  const t = useTranslations("Dashboard.promotionRules");
  const [catKey, setCatKey] = useState(0);
  return (
    <div>
      <Note tone="info">{t("targets.intro")}</Note>
      <Field label={t("targets.items")} help={t("targets.itemsHelp")}>
        <ItemPicker
          excludeIds={state.items.map((i) => i.id)}
          onPick={(i) => patch({ items: [...state.items, { id: i.id, name: i.name }] })}
        />
        <div style={{ display: "flex", flexWrap: "wrap", gap: 8, marginTop: 10 }}>
          {state.items.map((i) => (
            <Chip key={i.id} onRemove={() => patch({ items: state.items.filter((x) => x.id !== i.id) })}>
              {i.name || t("targets.unknownItem", { id: i.id.slice(0, 6) })}
            </Chip>
          ))}
        </div>
      </Field>
      <Field label={t("targets.categories")} help={t("targets.categoriesHelp")}>
        <CategoryTypeahead
          key={catKey}
          value={null}
          placeholder={t("targets.categoryPlaceholder")}
          onChange={(c) => {
            if (c && !state.categories.some((x) => x.id === c.id)) {
              patch({ categories: [...state.categories, { id: c.id, name: c.name }] });
            }
            setCatKey((k) => k + 1); // reset the typeahead so several categories can be added in a row
          }}
        />
        <div style={{ display: "flex", flexWrap: "wrap", gap: 8, marginTop: 10 }}>
          {state.categories.map((c) => (
            <Chip key={c.id} onRemove={() => patch({ categories: state.categories.filter((x) => x.id !== c.id) })}>
              {c.name || t("targets.unknownCategory", { id: c.id.slice(0, 6) })}
            </Chip>
          ))}
        </div>
      </Field>
      {state.items.length + state.categories.length === 0 && <Note tone="warn">{t("targets.needOne")}</Note>}
    </div>
  );
}

function Money({
  id,
  value,
  onChange,
  suffix,
}: {
  id: string;
  value: string;
  onChange: (v: string) => void;
  suffix?: string;
}) {
  return (
    <div style={{ display: "flex", alignItems: "center", gap: 8, maxWidth: 220 }}>
      <input
        id={id}
        inputMode="decimal"
        style={inputStyle}
        value={value}
        onChange={(e) => onChange(e.target.value.replace(/[^\d.,]/g, ""))}
      />
      {suffix && <span style={{ color: "#9CA3AF", fontSize: 13 }}>{suffix}</span>}
    </div>
  );
}

/** Step 3 - parameters by type. */
export function StepParams({ state, patch }: StepProps) {
  const t = useTranslations("Dashboard.promotionRules");
  return (
    <div>
      {state.type === "percent_off" && (
        <Field label={t("params.percent")} help={t("params.percentHelp")} htmlFor="p-percent">
          <Money id="p-percent" value={state.percent} onChange={(v) => patch({ percent: v })} suffix="%" />
        </Field>
      )}
      {state.type === "fixed_off" && (
        <Field label={t("params.amount")} help={t("params.amountHelp")} htmlFor="p-amount">
          <Money id="p-amount" value={state.amount} onChange={(v) => patch({ amount: v })} suffix={t("currency")} />
        </Field>
      )}
      {state.type === "fixed_price" && (
        <Field label={t("params.price")} help={t("params.priceHelp")} htmlFor="p-price">
          <Money id="p-price" value={state.price} onChange={(v) => patch({ price: v })} suffix={t("currency")} />
        </Field>
      )}
      {state.type === "buy_n_get_m" && (
        <>
          <div style={{ display: "flex", gap: 16, flexWrap: "wrap" }}>
            <Field label={t("params.buyQty")} htmlFor="p-buy">
              <input
                id="p-buy"
                inputMode="numeric"
                style={{ ...inputStyle, width: 110 }}
                value={state.buyQty}
                onChange={(e) => patch({ buyQty: e.target.value.replace(/\D/g, "") })}
              />
            </Field>
            <Field label={t("params.getQty")} htmlFor="p-get">
              <input
                id="p-get"
                inputMode="numeric"
                style={{ ...inputStyle, width: 110 }}
                value={state.getQty}
                onChange={(e) => patch({ getQty: e.target.value.replace(/\D/g, "") })}
              />
            </Field>
          </div>
          <div style={helpStyle}>{t("params.buyGetHelp")}</div>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 12, margin: "16px 0" }}>
            <ChoiceCard
              selected={state.getMode === "price"}
              onClick={() => patch({ getMode: "price" })}
              title={t("params.getModePrice")}
              text={t("params.getModePriceText")}
            />
            <ChoiceCard
              selected={state.getMode === "percent"}
              onClick={() => patch({ getMode: "percent" })}
              title={t("params.getModePercent")}
              text={t("params.getModePercentText")}
            />
          </div>
          {state.getMode === "price" ? (
            <Field label={t("params.getPrice")} help={t("params.getPriceHelp")} htmlFor="p-gp">
              <Money id="p-gp" value={state.getPrice} onChange={(v) => patch({ getPrice: v })} suffix={t("currency")} />
            </Field>
          ) : (
            <Field label={t("params.getPercent")} help={t("params.getPercentHelp")} htmlFor="p-gpc">
              <Money id="p-gpc" value={state.getPercent} onChange={(v) => patch({ getPercent: v })} suffix="%" />
            </Field>
          )}
          <div style={labelStyle}>{t("params.matchMode")}</div>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))", gap: 12, marginBottom: 16 }}>
            <ChoiceCard
              selected={state.matchMode === "per_item"}
              onClick={() => patch({ matchMode: "per_item" })}
              title={t("params.perItem")}
              text={t("params.perItemText")}
            />
            <ChoiceCard
              selected={state.matchMode === "across_set"}
              onClick={() => patch({ matchMode: "across_set" })}
              title={t("params.acrossSet")}
              text={t("params.acrossSetText")}
            />
          </div>
        </>
      )}

      <Field label={t("params.maxApplications")} help={t("params.maxApplicationsHelp")} htmlFor="p-max">
        <input
          id="p-max"
          inputMode="numeric"
          style={{ ...inputStyle, width: 140 }}
          value={state.maxApplications}
          placeholder={t("params.noLimit")}
          onChange={(e) => patch({ maxApplications: e.target.value.replace(/\D/g, "") })}
        />
      </Field>
      <div style={{ display: "flex", alignItems: "flex-start", gap: 12 }}>
        <Switch
          checked={state.excludeAlreadyDiscounted}
          onCheckedChange={(v) => patch({ excludeAlreadyDiscounted: v })}
          aria-label={t("params.excludeDiscounted")}
        />
        <div>
          <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>{t("params.excludeDiscounted")}</div>
          <div style={helpStyle}>{t("params.excludeDiscountedHelp")}</div>
        </div>
      </div>
    </div>
  );
}

/** Step 4 - stores, period, weekdays, time window, priority. */
export function StepScope({ state, patch }: StepProps) {
  const t = useTranslations("Dashboard.promotionRules");
  const { data: stores = [] } = useLocations();
  const { data: me } = useMe();
  // Store/network managers can only manage their own stores; useLocations() is already scoped server-side.
  const restricted = isStoreScopedRole(me?.role);
  const tLoc = useTranslations("Dashboard.locations.form");
  // Which store's clock applies: the single chosen store, else "each store's own time".
  const effectiveIds = state.allStores ? stores.map((s) => s.id) : state.storeIds;
  const singleStore = effectiveIds.length === 1 ? stores.find((s) => s.id === effectiveIds[0]) : undefined;
  const zoneId = singleStore?.timeZone;
  const zoneText = singleStore
    ? t("scope.zoneOne", {
        zone: zoneId && isKnownTimeZone(zoneId)
          ? tLoc(`timeZones.${timeZoneKey(zoneId)}`).replace(/\s*\(.*\)$/, "")
          : (zoneId ?? ""),
      })
    : t("scope.zoneMany");
  const [advanced, setAdvanced] = useState(false);
  const toggleDay = (d: number) =>
    patch({ weekdays: state.weekdays.includes(d) ? state.weekdays.filter((x) => x !== d) : [...state.weekdays, d] });

  return (
    <div>
      <div style={{ marginBottom: 18 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
          <Switch
            checked={state.allStores && !restricted}
            disabled={restricted}
            onCheckedChange={(v) => patch({ allStores: v })}
            aria-label={t("scope.allStores")}
          />
          <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>{t("scope.allStores")}</span>
        </div>
        <div style={helpStyle}>{restricted ? t("scope.ownStoresOnly") : t("scope.allStoresHelp")}</div>
        {(!state.allStores || restricted) && (
          <div style={{ marginTop: 12, maxWidth: 360 }}>
            <LocationsMultiSelectDropdown
              locations={stores.map((s) => ({ id: s.id, name: s.name }))}
              selectedIds={state.storeIds}
              onToggle={(id) =>
                patch({
                  storeIds: state.storeIds.includes(id)
                    ? state.storeIds.filter((x) => x !== id)
                    : [...state.storeIds, id],
                })
              }
              summaryLabel={t("scope.storesChosen", { count: state.storeIds.length })}
              placeholderLabel={t("scope.chooseStores")}
              doneLabel={t("scope.done")}
            />
          </div>
        )}
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 14 }}>
        <Field label={t("scope.validFrom")} help={t("scope.validFromHelp")} htmlFor="s-from">
          <input
            id="s-from"
            type="datetime-local"
            style={inputStyle}
            value={state.validFrom}
            onChange={(e) => patch({ validFrom: e.target.value })}
          />
        </Field>
        <Field label={t("scope.validTo")} help={t("scope.validToHelp")} htmlFor="s-to">
          <input
            id="s-to"
            type="datetime-local"
            style={inputStyle}
            value={state.validTo}
            onChange={(e) => patch({ validTo: e.target.value })}
          />
        </Field>
      </div>

      <Field label={t("scope.weekdays")} help={t("scope.weekdaysHelp")}>
        <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
          {[1, 2, 3, 4, 5, 6, 7].map((d) => (
            <Chip key={d} active={state.weekdays.includes(d)} onClick={() => toggleDay(d)}>
              {t(`scope.day.${d}`)}
            </Chip>
          ))}
        </div>
      </Field>

      <div style={{ display: "flex", gap: 14, flexWrap: "wrap" }}>
        <Field label={t("scope.timeFrom")} htmlFor="s-tf">
          <input
            id="s-tf"
            type="time"
            style={{ ...inputStyle, width: 140 }}
            value={state.timeFrom}
            onChange={(e) => patch({ timeFrom: e.target.value })}
          />
        </Field>
        <Field label={t("scope.timeTo")} htmlFor="s-tt">
          <input
            id="s-tt"
            type="time"
            style={{ ...inputStyle, width: 140 }}
            value={state.timeTo}
            onChange={(e) => patch({ timeTo: e.target.value })}
          />
        </Field>
      </div>
      <div style={{ ...helpStyle, marginTop: -8, marginBottom: 16 }}>{t("scope.timeHelp", { zone: zoneText })}</div>

      <button
        type="button"
        onClick={() => setAdvanced((a) => !a)}
        style={{ background: "none", border: "none", color: "#60A5FA", fontSize: 13, cursor: "pointer", padding: 0 }}
      >
        {advanced ? t("scope.hideAdvanced") : t("scope.showAdvanced")}
      </button>
      {advanced && (
        <div style={{ marginTop: 12 }}>
          <Field label={t("scope.priority")} help={t("scope.priorityHelp")} htmlFor="s-prio">
            <input
              id="s-prio"
              inputMode="numeric"
              style={{ ...inputStyle, width: 140 }}
              value={state.priority}
              onChange={(e) => patch({ priority: e.target.value.replace(/\D/g, "") })}
            />
          </Field>
        </div>
      )}
    </div>
  );
}

/** Step 5 - audience. */
export function StepAudience({ state, patch }: StepProps) {
  const t = useTranslations("Dashboard.promotionRules");
  const { data: tiers = [] } = useTierLookup(state.audienceKind === "loyalty_tier");
  const toggleTier = (id: string) =>
    patch({ tierIds: state.tierIds.includes(id) ? state.tierIds.filter((x) => x !== id) : [...state.tierIds, id] });

  return (
    <div>
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 14, marginBottom: 18 }}>
        {(["all", "loyalty_member", "loyalty_tier"] as const).map((k) => (
          <ChoiceCard
            key={k}
            selected={state.audienceKind === k}
            onClick={() => patch({ audienceKind: k })}
            title={t(`audience.${k}.title`)}
            text={t(`audience.${k}.text`)}
          />
        ))}
      </div>
      {state.audienceKind === "loyalty_tier" && (
        <Field label={t("audience.tiers")} help={tiers.length === 0 ? t("audience.noTiers") : undefined}>
          <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
            {tiers.map((tier) => (
              <Chip key={tier.id} active={state.tierIds.includes(tier.id)} onClick={() => toggleTier(tier.id)}>
                {tier.name} ({tier.discountPercent}%)
              </Chip>
            ))}
          </div>
        </Field>
      )}
    </div>
  );
}
