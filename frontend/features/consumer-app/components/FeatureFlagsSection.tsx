"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { AlertTriangle } from "lucide-react";
import { Switch } from "@/components/ui/switch";
import { Btn } from "@/components/ui/Btn";
import { extractDraftValidationErrors } from "../api/mobileConfigDraft";
import { useMobileConfigDraft, useSaveMobileConfigDraft } from "../hooks/useMobileConfigDraft";
import { useUnsavedChangesGuard } from "../hooks/useUnsavedChangesGuard";
import {
  MOBILE_CONFIG_CURRENT_SCHEMA_VERSION,
  MOBILE_CONFIG_FEATURE_KEYS,
  MOBILE_CONFIG_PAGE_NAMES,
  type MobileConfigDocument,
  type MobileConfigFeatureKey,
  type MobileConfigFeatures,
  type MobileConfigPage,
  type MobileConfigPageName,
} from "../types";

// ── Style constants (mirrors ThemeEditorSection.tsx / NavigationBuilderSection.tsx's conventions
// — this feature area has no shadcn form primitives of its own beyond Switch/Btn) ──────────────

const cardStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 12,
  padding: 20,
};

const rowStyle: React.CSSProperties = {
  display: "flex",
  alignItems: "center",
  justifyContent: "space-between",
  gap: 12,
  padding: "12px 4px",
  borderBottom: "1px solid #1F2937",
};

// ── Seed document (byte-identical to AppBuilderCanvas.tsx's/NavigationBuilderSection.tsx's own
// `buildSeedDocument` — every feature key defaults to `false` for a brand-new tenant, same as
// every other whole-document draft editor in this feature area. Deliberately duplicated rather
// than shared/exported, matching NavigationBuilderSection.tsx's own stated rationale: this
// screen's scope is itself only, and if the shared seed shape ever changes, every copy needs
// updating in lockstep anyway.) ──────────────────────────────────────────────────────────────

function buildSeedDocument(): MobileConfigDocument {
  const features = Object.fromEntries(
    MOBILE_CONFIG_FEATURE_KEYS.map((key) => [key, false]),
  ) as MobileConfigFeatures;

  const pages = {} as Record<MobileConfigPageName, MobileConfigPage>;
  for (const page of MOBILE_CONFIG_PAGE_NAMES) {
    pages[page] = { blocks: [] };
  }

  return {
    schemaVersion: MOBILE_CONFIG_CURRENT_SCHEMA_VERSION,
    features,
    navigation: [
      { type: "home", label: "Головна", icon: "home" },
      { type: "profile", label: "Профіль", icon: "user" },
    ],
    pages,
  };
}

/**
 * TASK-557: "Функції" section of the Retailer Admin shell (fills the `/consumer-app/features`
 * placeholder TASK-535 scaffolded — no task in the original Stage D breakdown ever scheduled this
 * UI; TASK-543 only built the backend `IConsumerFeatureFlagService`). Editing the tenant's
 * `features` object on the same whole-document draft AppBuilderCanvas.tsx (TASK-539/541),
 * ThemeEditorSection.tsx (TASK-537) and NavigationBuilderSection.tsx (TASK-542) already
 * read/write, via the same `useMobileConfigDraft`/`useSaveMobileConfigDraft` hooks (TASK-538b).
 * This screen only ever touches `document.features` — `navigation`/`pages`/`schemaVersion` are
 * carried through untouched on every save (kept in `restOfDoc` state, merged back in on submit),
 * same read-modify-write shape as `NavigationBuilderSection.tsx`'s `restOfDoc`, just for a fixed
 * 8-key boolean map instead of a variable-length array — no react-hook-form/zod needed here, a
 * plain `useState` is enough (matches `BonusProgramSection.tsx`'s simpler `Switch`-only pattern,
 * not `NavigationBuilderSection.tsx`'s array-validation one).
 *
 * REQUIRED WARNING (per TASK-557 brief, grounded in `IConsumerFeatureFlagService`'s own doc
 * comments, not paraphrased from memory): as of this task, `RequireConsumerFeatureAttribute`/
 * `IConsumerFeatureFlagService` are registered in DI and unit-tested, but no controller — not
 * `ConsumerContentController`, not `ConsumerLoyaltyController`, nor any other consumer-facing
 * endpoint — actually calls them (verified: zero references under `ShelfGuard.Api`). So unlike
 * every other section here, this one deliberately does NOT show the usual "takes effect after
 * publish" `draftNotice` — that would imply publishing matters, when today it doesn't: saving
 * (and even publishing) these toggles has no effect on the live consumer app at all, because
 * nothing reads them yet.
 */
export function FeatureFlagsSection() {
  const t = useTranslations("Dashboard.consumerApp.featureFlags");
  const draftQuery = useMobileConfigDraft();
  const save = useSaveMobileConfigDraft();

  // Holds the full document minus `features` (which this screen owns as local toggle state below)
  // — same read-modify-write shape as NavigationBuilderSection.tsx's `restOfDoc`.
  const [restOfDoc, setRestOfDoc] = useState<Omit<MobileConfigDocument, "features"> | null>(null);
  const [features, setFeatures] = useState<MobileConfigFeatures | null>(null);
  const [savedFeatures, setSavedFeatures] = useState<MobileConfigFeatures | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  const isDirty =
    !!features && !!savedFeatures && JSON.stringify(features) !== JSON.stringify(savedFeatures);

  // TASK-546: warns before losing unsaved edits (tab-close/refresh always; in-app link-click
  // navigation best-effort) — see useUnsavedChangesGuard.ts's remarks for exact coverage.
  useUnsavedChangesGuard(isDirty, t("unsavedChangesWarning"));

  // Hydrate from the server — brand-new tenant gets the seed document instead of a fabricated
  // draft (see buildSeedDocument's remarks above). Re-runs whenever draftQuery.data's reference
  // changes, including right after a successful save (same convention every sibling editor in
  // this feature area uses).
  useEffect(() => {
    if (!draftQuery.data) return;
    const doc: MobileConfigDocument =
      draftQuery.data.hasDraft && draftQuery.data.configurationJson
        ? (JSON.parse(draftQuery.data.configurationJson) as MobileConfigDocument)
        : buildSeedDocument();
    const { features: loadedFeatures, ...rest } = doc;
    // `MobileConfigDocument.features` is typed `Partial` (a document saved by another surface
    // before every key existed could omit one — see its remarks in ../types.ts); any missing key
    // defaults to `false`, same default buildSeedDocument itself uses for a brand-new tenant.
    const normalized = Object.fromEntries(
      MOBILE_CONFIG_FEATURE_KEYS.map((key) => [key, loadedFeatures?.[key] ?? false]),
    ) as MobileConfigFeatures;
    setRestOfDoc(rest);
    setFeatures(normalized);
    setSavedFeatures(normalized);
  }, [draftQuery.data]);

  function handleToggle(key: MobileConfigFeatureKey, value: boolean) {
    setFeatures((prev) => (prev ? { ...prev, [key]: value } : prev));
  }

  async function handleSave() {
    if (!restOfDoc || !features || save.isPending) return;
    setSaveError(null);
    const nextDoc: MobileConfigDocument = { ...restOfDoc, features };
    try {
      await save.mutateAsync({ configurationJson: JSON.stringify(nextDoc) });
      setSavedFeatures(features);
      toast.success(t("saveSuccess"));
    } catch (err) {
      const fieldErrors = extractDraftValidationErrors(err);
      if (fieldErrors) {
        setSaveError(fieldErrors.map((e) => e.message).join(" "));
      } else {
        setSaveError(err instanceof Error ? err.message : t("saveError"));
      }
    }
  }

  if (draftQuery.isLoading || !features) {
    return <div style={{ ...cardStyle, color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>;
  }
  if (draftQuery.isError) {
    return <div style={{ ...cardStyle, color: "#F87171", fontSize: 13 }}>{t("loadError")}</div>;
  }

  return (
    <div style={{ ...cardStyle, maxWidth: 720 }}>
      {/* Required warning — not the usual blue "draftNotice" (see this component's doc comment
          for why): amber/AlertTriangle matches TemporaryPasswordBanner.tsx's established
          "actionable warning" convention, stronger than the neutral Info-icon notices the other
          three editor screens use for their ordinary draft/publish caveat. */}
      <div
        style={{
          display: "flex",
          alignItems: "flex-start",
          gap: 8,
          padding: "10px 12px",
          background: "#2D1B05",
          border: "1px solid #F59E0B40",
          borderRadius: 8,
          marginBottom: 16,
        }}
      >
        <AlertTriangle size={16} style={{ color: "#F59E0B", flexShrink: 0, marginTop: 1 }} />
        <p style={{ color: "#D1D5DB", fontSize: 12, margin: 0 }}>
          <strong style={{ color: "#F59E0B" }}>{t("warningTitle")}</strong> {t("warningText")}
        </p>
      </div>

      <div style={{ display: "flex", flexDirection: "column" }}>
        {MOBILE_CONFIG_FEATURE_KEYS.map((key) => (
          <div key={key} style={rowStyle}>
            <div>
              <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>
                {t(`keys.${key}`)}
              </div>
              <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2 }}>
                {t(`hints.${key}`)}
              </div>
            </div>
            <Switch
              checked={features[key]}
              onCheckedChange={(value) => handleToggle(key, value)}
              aria-label={t(`keys.${key}`)}
            />
          </div>
        ))}
      </div>

      {saveError && <p style={{ color: "#F87171", fontSize: 12, marginTop: 12 }}>{saveError}</p>}

      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 12,
          marginTop: 16,
          paddingTop: 16,
          borderTop: "1px solid #1F2937",
        }}
      >
        <Btn onClick={handleSave} disabled={save.isPending || !isDirty}>
          {save.isPending ? t("savingButton") : t("saveButton")}
        </Btn>
        {!isDirty && <span style={{ color: "#4B5563", fontSize: 12 }}>{t("noChanges")}</span>}
      </div>
    </div>
  );
}
