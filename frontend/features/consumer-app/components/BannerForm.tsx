"use client";

import { useEffect, useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { Switch } from "@/components/ui/switch";
import { useLocations } from "@/features/locations/hooks/useLocations";
import { useCatalogProducts } from "@/features/catalog/hooks/useCatalog";
import { LocationsMultiSelectDropdown } from "@/features/users/components/LocationsMultiSelectDropdown";
import {
  useBanner,
  useCreateBanner,
  usePublishBanner,
  useUpdateBanner,
  useUploadBannerImage,
} from "../hooks/useBanners";
import type { BannerDetailMode } from "../types";

const inputStyle: React.CSSProperties = {
  width: "100%",
  background: "#0D1117",
  border: "1px solid #374151",
  borderRadius: 8,
  padding: "9px 12px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};

const textareaStyle: React.CSSProperties = { ...inputStyle, resize: "vertical", fontFamily: "inherit" };

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 6,
};

const hintStyle: React.CSSProperties = { color: "#4B5563", fontSize: 11, marginTop: 4 };

const sectionStyle: React.CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  background: "#10151D",
  border: "1px solid #202938",
  borderRadius: 10,
  padding: 16,
  display: "flex",
  flexDirection: "column",
  gap: 14,
};

const sectionTitleStyle: React.CSSProperties = {
  color: "#E8EDF5",
  fontSize: 13,
  fontWeight: 700,
  margin: 0,
};

const selectStyle: React.CSSProperties = {
  ...inputStyle,
  cursor: "pointer",
  appearance: "none",
  backgroundImage:
    "url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath fill='%236B7280' d='M6 8L1 3h10z'/%3E%3C/svg%3E\")",
  backgroundRepeat: "no-repeat",
  backgroundPosition: "right 12px center",
  paddingRight: 36,
};

const DEFAULT_ICON = "pricetag-outline";
const DEFAULT_BG = "#1E40AF";
const DEFAULT_ACCENT = "#F59E0B";

/** ISO datetime (or already-plain date) -> "YYYY-MM-DD" for a native date input. */
function toDateInputValue(value: string | null | undefined): string {
  if (!value) return "";
  return value.slice(0, 10);
}

function todayInputValue(): string {
  return new Date().toISOString().slice(0, 10);
}

/**
 * A native date input gives "YYYY-MM-DD" with no timezone — System.Text.Json parses that as
 * DateTime.Kind=Unspecified, which Npgsql then rejects for Banner.ValidFrom/ValidUntil's
 * `timestamp with time zone` columns ("Cannot write DateTime with Kind=Unspecified..."). Pin it
 * to UTC midnight so the wire value round-trips as Kind=Utc.
 */
function toUtcIso(dateInputValue: string): string {
  return `${dateInputValue}T00:00:00.000Z`;
}

interface Props {
  /** null = create mode. A banner id = edit mode, prefilled from GET /api/banners/{id}. */
  bannerId: string | null;
  onClose: () => void;
  presentation?: "modal" | "page";
}

/**
 * TASK-522: create/edit modal for a promotional banner. Mirrors ScheduleForm/ProductForm's
 * modal shape (backdrop + centered panel + form). Image upload happens after the banner
 * itself is created/saved (POST/PUT first, then POST /{id}/image if a file was picked) —
 * there is no id to upload against before the first save.
 *
 * TASK-525: added the "Опублікувати одразу" toggle (create mode only — `publishImmediately`
 * has no meaning on PUT, `UpdateBannerRequest` doesn't carry it) and, when editing an existing
 * draft (`lifecycleStatus === "draft"`), a standalone "Опублікувати" button in place of that
 * toggle (publishing post-creation is a one-way action via POST /{id}/publish, not a form field).
 */
export function BannerForm({ bannerId, onClose, presentation = "modal" }: Props) {
  const t = useTranslations("Dashboard.consumerApp.banners.form");
  const isEdit = bannerId !== null;

  const { data: banner, isLoading: bannerLoading } = useBanner(bannerId);
  const { data: locations } = useLocations();
  const [productsOpen, setProductsOpen] = useState(false);
  const { data: catalogProducts, isLoading: productsLoading } = useCatalogProducts(
    undefined,
    { enabled: productsOpen },
  );
  const activeLocations = useMemo(() => (locations ?? []).filter((l) => l.isActive), [locations]);
  const activeProducts = useMemo(() => (catalogProducts ?? []).filter((p) => p.isActive), [catalogProducts]);
  const isDraftEdit = isEdit && banner?.lifecycleStatus === "draft";

  const createBanner = useCreateBanner();
  const updateBanner = useUpdateBanner();
  const uploadImage = useUploadBannerImage();
  const publishBanner = usePublishBanner();
  const isPending = createBanner.isPending || updateBanner.isPending || uploadImage.isPending;

  const [title, setTitle] = useState("");
  const [eyebrow, setEyebrow] = useState("");
  const [description, setDescription] = useState("");
  const [body, setBody] = useState("");
  const [terms, setTerms] = useState("");
  const [icon, setIcon] = useState(DEFAULT_ICON);
  const [backgroundColor, setBackgroundColor] = useState(DEFAULT_BG);
  const [accentColor, setAccentColor] = useState(DEFAULT_ACCENT);
  const [detailMode, setDetailMode] = useState<BannerDetailMode>("internal");
  const [externalUrl, setExternalUrl] = useState("");
  const [validFrom, setValidFrom] = useState(todayInputValue());
  const [validUntil, setValidUntil] = useState("");
  const [locationIds, setLocationIds] = useState<string[]>([]);
  const [productIds, setProductIds] = useState<string[]>([]);
  const [productSearch, setProductSearch] = useState("");
  const [imageFile, setImageFile] = useState<File | null>(null);
  const [imagePreview, setImagePreview] = useState<string | null>(null);
  const [initialized, setInitialized] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  // Create-mode only — see the component doc comment for why edit mode never touches this.
  const [publishImmediately, setPublishImmediately] = useState(true);

  // Prefill once the edit target loads (same `initialized` guard as UserLocationsEditor —
  // never clobber the admin's in-progress edits on a background refetch).
  useEffect(() => {
    if (!isEdit || !banner || initialized) return;
    setTitle(banner.title);
    setEyebrow(banner.eyebrow ?? "");
    setDescription(banner.description);
    setBody(banner.body);
    setTerms(banner.terms);
    setIcon(banner.icon);
    setBackgroundColor(banner.backgroundColor);
    setAccentColor(banner.accentColor);
    setDetailMode(banner.detailMode);
    setExternalUrl(banner.externalUrl ?? "");
    setValidFrom(toDateInputValue(banner.validFrom) || todayInputValue());
    setValidUntil(toDateInputValue(banner.validUntil));
    setLocationIds(banner.locationIds);
    setProductIds(banner.productIds);
    setImagePreview(banner.imageUrl);
    setInitialized(true);
  }, [isEdit, banner, initialized]);

  const filteredProducts = useMemo(() => {
    const q = productSearch.trim().toLowerCase();
    if (!q) return activeProducts;
    return activeProducts.filter((p) => p.name.toLowerCase().includes(q));
  }, [activeProducts, productSearch]);

  function toggleLocation(id: string) {
    setLocationIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  function toggleProduct(id: string) {
    setProductIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  function validate(): boolean {
    const e: Record<string, string> = {};
    if (!title.trim()) e.title = t("titleRequiredError");
    if (!description.trim()) e.description = t("descriptionRequiredError");
    if (!validFrom) e.validFrom = t("validFromRequiredError");
    if (detailMode === "external" && !externalUrl.trim()) e.externalUrl = t("externalUrlRequiredError");
    if (validUntil && validFrom && validUntil <= validFrom) e.validUntil = t("validUntilAfterError");
    setErrors(e);
    return Object.keys(e).length === 0;
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setFormError(null);
    if (!validate() || isPending) return;

    const shared = {
      title: title.trim(),
      description: description.trim(),
      body,
      terms,
      icon: icon.trim() || DEFAULT_ICON,
      backgroundColor,
      accentColor,
      detailMode,
      eyebrow: eyebrow.trim() || null,
      externalUrl: detailMode === "external" ? externalUrl.trim() : null,
      validFrom: toUtcIso(validFrom),
      validUntil: validUntil ? toUtcIso(validUntil) : null,
      locationIds,
      productIds,
    };

    try {
      // publishImmediately only exists on CreateBannerRequest — UpdateBannerRequest has no such
      // field (editing a banner never changes its publish state, see BannerService.UpdateAsync).
      const saved = isEdit
        ? await updateBanner.mutateAsync({ id: bannerId!, body: shared })
        : await createBanner.mutateAsync({ ...shared, publishImmediately });

      if (imageFile) {
        await uploadImage.mutateAsync({ id: saved.id, file: imageFile });
      }

      toast.success(isEdit ? t("updateSuccess") : t("createSuccess"));
      onClose();
    } catch (err) {
      setFormError(err instanceof Error ? err.message : t("saveError"));
    }
  }

  async function handlePublishNow() {
    if (!bannerId) return;
    try {
      await publishBanner.mutateAsync(bannerId);
      toast.success(t("publishSuccess"));
      onClose();
    } catch (err) {
      setFormError(err instanceof Error ? err.message : t("publishError"));
    }
  }

  return (
    <>
      {presentation === "modal" && (
        <div
          onClick={onClose}
          style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,0.6)", zIndex: 300, backdropFilter: "blur(2px)" }}
        />
      )}
      <div
        style={{
          position: presentation === "modal" ? "fixed" : "relative",
          top: presentation === "modal" ? "50%" : undefined,
          left: presentation === "modal" ? "50%" : undefined,
          transform: presentation === "modal" ? "translate(-50%, -50%)" : undefined,
          width: presentation === "modal" ? "min(640px, 95vw)" : "100%",
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 14,
          zIndex: presentation === "modal" ? 301 : undefined,
          display: "flex",
          flexDirection: "column",
          maxHeight: presentation === "modal" ? "90vh" : undefined,
          overflow: "hidden",
        }}
      >
        <div
          style={{
            display: "flex", alignItems: "center", justifyContent: "space-between",
            padding: "18px 22px", borderBottom: "1px solid #1F2937", flexShrink: 0,
          }}
        >
          <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>
            {isEdit ? t("editTitle") : t("createTitle")}
          </h2>
          <button
            onClick={onClose}
            style={{
              background: "transparent", border: "1px solid #1F2937", borderRadius: 8,
              padding: "5px 9px", color: "#4B5563", fontSize: 16, cursor: "pointer",
            }}
          >
            ✕
          </button>
        </div>

        {isEdit && bannerLoading ? (
          <div style={{ padding: 22, color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>
        ) : (
          <form
            onSubmit={handleSubmit}
            style={{
              padding: presentation === "page" ? 18 : 22,
              overflowY: presentation === "modal" ? "auto" : "visible",
              display: presentation === "page" ? "grid" : "flex",
              gridTemplateColumns: presentation === "page"
                ? "repeat(auto-fit, minmax(min(100%, 440px), 1fr))"
                : undefined,
              flexDirection: presentation === "modal" ? "column" : undefined,
              alignItems: "start",
              gap: presentation === "page" ? 16 : 14,
              maxWidth: presentation === "page" ? 1240 : undefined,
              width: "100%",
              boxSizing: "border-box",
              margin: presentation === "page" ? "0 auto" : undefined,
            }}
          >
            <section style={sectionStyle}>
              <h3 style={sectionTitleStyle}>Основний контент</h3>
            {/* Title / Eyebrow */}
            <div style={{ display: "grid", gridTemplateColumns: "2fr 1fr", gap: 12 }}>
              <div>
                <label style={{ ...labelStyle, minHeight: 30, display: "flex", alignItems: "flex-end" }}>
                  {t("titleLabel")}
                </label>
                <input
                  value={title}
                  onChange={(e) => setTitle(e.target.value)}
                  placeholder={t("titlePlaceholder")}
                  style={{ ...inputStyle, borderColor: errors.title ? "#EF4444" : "#374151" }}
                />
                {errors.title && <p style={{ ...hintStyle, color: "#EF4444" }}>{errors.title}</p>}
              </div>
              <div>
                <label style={{ ...labelStyle, minHeight: 30, display: "flex", alignItems: "flex-end" }}>
                  {t("eyebrowLabel")}
                </label>
                <input
                  value={eyebrow}
                  onChange={(e) => setEyebrow(e.target.value)}
                  placeholder={t("eyebrowPlaceholder")}
                  style={inputStyle}
                />
              </div>
            </div>

            {/* Description */}
            <div>
              <label style={labelStyle}>{t("descriptionLabel")}</label>
              <textarea
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder={t("descriptionPlaceholder")}
                rows={2}
                style={{ ...textareaStyle, borderColor: errors.description ? "#EF4444" : "#374151" }}
              />
              {errors.description && <p style={{ ...hintStyle, color: "#EF4444" }}>{errors.description}</p>}
            </div>

            {/* Body */}
            <div>
              <label style={labelStyle}>{t("bodyLabel")}</label>
              <textarea
                value={body}
                onChange={(e) => setBody(e.target.value)}
                placeholder={t("bodyPlaceholder")}
                rows={4}
                style={textareaStyle}
              />
              <p style={hintStyle}>{t("bodyHint")}</p>
            </div>

            {/* Terms */}
            <div>
              <label style={labelStyle}>{t("termsLabel")}</label>
              <textarea
                value={terms}
                onChange={(e) => setTerms(e.target.value)}
                placeholder={t("termsPlaceholder")}
                rows={3}
                style={textareaStyle}
              />
              <p style={hintStyle}>{t("termsHint")}</p>
            </div>
            </section>

            <section style={sectionStyle}>
              <h3 style={sectionTitleStyle}>Оформлення</h3>
            {/* Image */}
            <div>
              <label style={labelStyle}>{t("imageLabel")}</label>
              <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
                {imagePreview && (
                  <div
                    role="img"
                    aria-label={t("imageLabel")}
                    style={{
                      width: 60,
                      height: 60,
                      flexShrink: 0,
                      backgroundImage: `url("${imagePreview}")`,
                      backgroundSize: "cover",
                      backgroundPosition: "center",
                      borderRadius: 8,
                      border: "1px solid #1F2937",
                    }}
                  />
                )}
                <label style={{
                  padding: "8px 14px", borderRadius: 8, cursor: "pointer",
                  background: "#111827", border: "1px solid #374151", color: "#9CA3AF", fontSize: 12,
                }}>
                  {imagePreview ? t("imageChange") : t("imageUpload")}
                  <input
                    type="file"
                    accept="image/jpeg,image/png,image/webp,image/gif"
                    style={{ display: "none" }}
                    onChange={(e) => {
                      const file = e.target.files?.[0];
                      if (!file) return;
                      setImageFile(file);
                      setImagePreview(URL.createObjectURL(file));
                    }}
                  />
                </label>
                {imagePreview && (
                  <button
                    type="button"
                    onClick={() => { setImageFile(null); setImagePreview(null); }}
                    style={{ background: "none", border: "none", color: "#EF4444", cursor: "pointer", fontSize: 12 }}
                  >
                    {t("imageRemove")}
                  </button>
                )}
              </div>
              <p style={hintStyle}>{t("imageHint")}</p>
            </div>

            {/* Icon + colors */}
            <div style={{ display: "grid", gridTemplateColumns: "2fr 1fr 1fr", gap: 12 }}>
              <div>
                <label style={labelStyle}>{t("iconLabel")}</label>
                <input
                  value={icon}
                  onChange={(e) => setIcon(e.target.value)}
                  placeholder={DEFAULT_ICON}
                  style={inputStyle}
                />
                <p style={hintStyle}>{t("iconHint")}</p>
              </div>
              <div>
                <label style={labelStyle}>{t("backgroundColorLabel")}</label>
                <input
                  type="color"
                  value={backgroundColor}
                  onChange={(e) => setBackgroundColor(e.target.value)}
                  style={{ ...inputStyle, padding: 4, height: 38, cursor: "pointer" }}
                />
              </div>
              <div>
                <label style={labelStyle}>{t("accentColorLabel")}</label>
                <input
                  type="color"
                  value={accentColor}
                  onChange={(e) => setAccentColor(e.target.value)}
                  style={{ ...inputStyle, padding: 4, height: 38, cursor: "pointer" }}
                />
              </div>
            </div>
            </section>

            <section style={sectionStyle}>
              <h3 style={sectionTitleStyle}>Перехід і аудиторія</h3>
            {/* Detail mode */}
            <div>
              <label style={labelStyle}>{t("detailModeLabel")}</label>
              <select
                value={detailMode}
                onChange={(e) => setDetailMode(e.target.value as BannerDetailMode)}
                style={selectStyle}
              >
                <option value="internal">{t("detailModeInternal")}</option>
                <option value="external">{t("detailModeExternal")}</option>
              </select>
              {detailMode === "external" && (
                <div style={{ marginTop: 10 }}>
                  <label style={labelStyle}>{t("externalUrlLabel")}</label>
                  <input
                    value={externalUrl}
                    onChange={(e) => setExternalUrl(e.target.value)}
                    placeholder="https://…"
                    style={{ ...inputStyle, borderColor: errors.externalUrl ? "#EF4444" : "#374151" }}
                  />
                  {errors.externalUrl && <p style={{ ...hintStyle, color: "#EF4444" }}>{errors.externalUrl}</p>}
                </div>
              )}
            </div>

            {/* Stores */}
            <div>
              <label style={labelStyle}>{t("locationsLabel")}</label>
              {activeLocations.length === 0 ? (
                <p style={hintStyle}>{t("locationsEmptyHint")}</p>
              ) : (
                <LocationsMultiSelectDropdown
                  locations={activeLocations}
                  selectedIds={locationIds}
                  onToggle={toggleLocation}
                  summaryLabel={t("locationsSelectedCount", { count: locationIds.length })}
                  placeholderLabel={t("locationsPlaceholder")}
                  doneLabel={t("locationsDoneButton")}
                />
              )}
            </div>

            {/* Products */}
            <div>
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 12 }}>
                <div>
                  <label style={{ ...labelStyle, marginBottom: 2 }}>{t("productsLabel")}</label>
                  <span style={hintStyle}>{productIds.length > 0 ? t("productsSelectedCount", { count: productIds.length }) : "Банер може працювати без прив’язаних товарів."}</span>
                </div>
                <Btn type="button" size="sm" variant="ghost" onClick={() => setProductsOpen((value) => !value)}>
                  {productsOpen ? "Згорнути" : productIds.length > 0 ? "Змінити" : "Додати товари"}
                </Btn>
              </div>
              {productsOpen && (
                <div style={{ marginTop: 10 }}>
                  <input
                    autoFocus
                    value={productSearch}
                    onChange={(e) => setProductSearch(e.target.value)}
                    placeholder={t("productsSearchPlaceholder")}
                    style={{ ...inputStyle, marginBottom: 8 }}
                  />
                  <div style={{ maxHeight: 190, overflowY: "auto", border: "1px solid #293241", background: "#0D1117", borderRadius: 8, padding: 8, display: "flex", flexDirection: "column", gap: 2 }}>
                    {productsLoading ? (
                      <p style={{ ...hintStyle, margin: 4 }}>Завантаження товарів…</p>
                    ) : filteredProducts.length === 0 ? (
                      <p style={{ ...hintStyle, margin: 4 }}>{t("productsEmptyHint")}</p>
                    ) : filteredProducts.map((p) => (
                      <label key={p.id} style={{ display: "flex", alignItems: "center", gap: 8, fontSize: 13, color: "#E8EDF5", cursor: "pointer", padding: "6px", borderRadius: 6 }}>
                        <input type="checkbox" checked={productIds.includes(p.id)} onChange={() => toggleProduct(p.id)} />
                        {p.name}
                      </label>
                    ))}
                  </div>
                </div>
              )}
            </div>
            </section>

            <section style={sectionStyle}>
              <h3 style={sectionTitleStyle}>Публікація</h3>
            {/* Dates */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
              <div>
                <label style={labelStyle}>{t("validFromLabel")}</label>
                <input
                  type="date"
                  value={validFrom}
                  onChange={(e) => setValidFrom(e.target.value)}
                  style={{ ...inputStyle, borderColor: errors.validFrom ? "#EF4444" : "#374151" }}
                />
                {errors.validFrom && <p style={{ ...hintStyle, color: "#EF4444" }}>{errors.validFrom}</p>}
              </div>
              <div>
                <label style={labelStyle}>{t("validUntilLabel")}</label>
                <input
                  type="date"
                  value={validUntil}
                  onChange={(e) => setValidUntil(e.target.value)}
                  style={{ ...inputStyle, borderColor: errors.validUntil ? "#EF4444" : "#374151" }}
                />
                {errors.validUntil && <p style={{ ...hintStyle, color: "#EF4444" }}>{errors.validUntil}</p>}
              </div>
            </div>

            {/* Publish state — create mode: toggle (default ON, mirrors today's behavior when
                left on). Edit mode on a draft: a standalone publish action instead, since
                publishing post-creation is a one-way call to POST /{id}/publish, not a form
                field UpdateBannerRequest even carries. Edit mode on an already-published banner:
                nothing to show here at all. */}
            {!isEdit && (
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 12 }}>
                <div>
                  <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>{t("publishImmediatelyLabel")}</div>
                  <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2 }}>{t("publishImmediatelyHint")}</div>
                </div>
                <Switch checked={publishImmediately} onCheckedChange={setPublishImmediately} />
              </div>
            )}
            {isDraftEdit && (
              <div
                style={{
                  display: "flex", alignItems: "center", justifyContent: "space-between", gap: 12,
                  padding: "10px 12px", background: "#111827", border: "1px solid #1F2937", borderRadius: 8,
                }}
              >
                <div style={{ color: "#9CA3AF", fontSize: 12 }}>{t("draftHint")}</div>
                <Btn
                  type="button"
                  variant="success"
                  size="sm"
                  onClick={handlePublishNow}
                  disabled={publishBanner.isPending}
                >
                  {publishBanner.isPending ? t("publishingButton") : t("publishNowButton")}
                </Btn>
              </div>
            )}
            </section>

            {formError && <p style={{ color: "#F87171", fontSize: 12 }}>{formError}</p>}

            <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, paddingTop: 12, borderTop: "1px solid #1F2937", gridColumn: presentation === "page" ? "1 / -1" : undefined }}>
              <Btn type="submit" disabled={isPending}>
                {isPending ? t("savingButton") : isEdit ? t("saveButton") : t("createButton")}
              </Btn>
              <Btn type="button" variant="ghost" onClick={onClose}>
                {t("cancelButton")}
              </Btn>
            </div>
          </form>
        )}
      </div>
    </>
  );
}
