"use client";

import { useState } from "react";
import { Search } from "lucide-react";
import { useTranslations } from "next-intl";
import { useSuppliers, useMarketplaceSearch } from "@/features/marketplace/hooks/useMarketplace";
import { SupplierCard } from "@/features/marketplace/components/SupplierCard";
import { SupplierFilters } from "@/features/marketplace/components/SupplierFilters";
import { useModules } from "@/features/modules/hooks/useModules";
import type { MarketplaceFilters, SupplierListItemDto } from "@/features/marketplace/types";

const DEFAULT_FILTERS: MarketplaceFilters = {
  region: "",
  category: "",
  plan: "all",
};

export default function MarketplacePage() {
  const t = useTranslations("Dashboard.marketplace.page");
  const tGate = useTranslations("Dashboard.marketplace.moduleGate");
  const { data: modulesData } = useModules();
  const marketplaceActive =
    !modulesData || modulesData.modules.includes("marketplace");

  const [filters, setFilters] = useState<MarketplaceFilters>(DEFAULT_FILTERS);
  const [page, setPage] = useState(1);
  const [searchQuery, setSearchQuery] = useState("");
  const [searchResults, setSearchResults] = useState<SupplierListItemDto[] | null>(null);

  const { data, isLoading, isError } = useSuppliers(filters, page);
  const searchMutation = useMarketplaceSearch();

  // Derive unique categories from loaded suppliers for filter dropdown
  const knownCategories = Array.from(
    new Set((data?.items ?? []).flatMap((s) => s.categories ?? []))
  ).sort();

  function handleSearch() {
    if (!searchQuery.trim()) {
      setSearchResults(null);
      return;
    }
    searchMutation.mutate(
      { itemName: searchQuery.trim(), region: filters.region || undefined },
      {
        onSuccess: (results) => setSearchResults(results),
      }
    );
  }

  function handleClearSearch() {
    setSearchQuery("");
    setSearchResults(null);
  }

  const displayItems = searchResults ?? data?.items ?? [];
  const isSearchMode = searchResults !== null;

  if (!marketplaceActive) {
    return (
      <div
        style={{
          padding: "80px 32px",
          textAlign: "center",
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          gap: 16,
        }}
      >
        <div style={{ fontSize: 40 }}>🔒</div>
        <h2 style={{ color: "#E8EDF5", fontSize: 20, fontWeight: 700, margin: 0 }}>
          {tGate("title")}
        </h2>
        <p style={{ color: "#4B5563", fontSize: 14, maxWidth: 440 }}>
          {tGate("body")}
        </p>
      </div>
    );
  }

  return (
    <div style={{ padding: "28px 32px" }}>
      {/* Header */}
      <div style={{ marginBottom: 28 }}>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
          {t("title")}
        </h1>
        <p style={{ color: "#4B5563", fontSize: 14, marginTop: 6 }}>
          {t("subtitle")}
        </p>
      </div>

      {/* Search bar */}
      <div
        style={{
          display: "flex",
          gap: 10,
          marginBottom: 20,
          alignItems: "center",
        }}
      >
        <div style={{ position: "relative", flex: 1, maxWidth: 480 }}>
          <Search
            size={16}
            style={{
              position: "absolute",
              left: 12,
              top: "50%",
              transform: "translateY(-50%)",
              color: "#4B5563",
              pointerEvents: "none",
            }}
          />
          <input
            type="text"
            placeholder={t("searchPlaceholder")}
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && handleSearch()}
            style={{
              width: "100%",
              padding: "9px 12px 9px 38px",
              background: "#111827",
              border: "1px solid #1F2937",
              borderRadius: 8,
              color: "#E8EDF5",
              fontSize: 13,
              outline: "none",
              boxSizing: "border-box",
            }}
          />
        </div>
        <button
          onClick={handleSearch}
          disabled={searchMutation.isPending}
          style={{
            padding: "9px 20px",
            borderRadius: 8,
            border: "none",
            background: "#1D4ED8",
            color: "#E8EDF5",
            fontSize: 13,
            fontWeight: 600,
            cursor: "pointer",
          }}
        >
          {searchMutation.isPending ? t("searching") : t("searchButton")}
        </button>
        {isSearchMode && (
          <button
            onClick={handleClearSearch}
            style={{
              padding: "9px 16px",
              borderRadius: 8,
              border: "1px solid #1F2937",
              background: "transparent",
              color: "#6B7280",
              fontSize: 13,
              cursor: "pointer",
            }}
          >
            {t("reset")}
          </button>
        )}
      </div>

      {/* Filters (hidden in search mode) */}
      {!isSearchMode && (
        <div style={{ marginBottom: 24 }}>
          <SupplierFilters
            filters={filters}
            onChange={(f) => {
              setFilters(f);
              setPage(1);
            }}
            categories={knownCategories}
          />
        </div>
      )}

      {/* Content */}
      {isSearchMode && searchMutation.isError && (
        <div style={{ color: "#F87171", fontSize: 13, marginBottom: 16 }}>
          {t("searchError")}
        </div>
      )}

      {isLoading && !isSearchMode ? (
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fill, minmax(280px, 1fr))",
            gap: 16,
          }}
        >
          {[...Array(8)].map((_, i) => (
            <div
              key={i}
              style={{
                height: 160,
                background: "#111827",
                borderRadius: 12,
                border: "1px solid #1F2937",
              }}
            />
          ))}
        </div>
      ) : displayItems.length === 0 ? (
        <div
          style={{
            textAlign: "center",
            padding: "60px 0",
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            gap: 12,
          }}
        >
          <div style={{ fontSize: 36 }}>🔍</div>
          <div style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600 }}>
            {isSearchMode ? t("emptySearchTitle") : t("emptyBrowseTitle")}
          </div>
          <div style={{ color: "#4B5563", fontSize: 13 }}>
            {isSearchMode ? t("emptySearchBody") : t("emptyBrowseBody")}
          </div>
        </div>
      ) : (
        <>
          {isSearchMode && (
            <div style={{ color: "#4B5563", fontSize: 13, marginBottom: 16 }}>
              {t("resultsFound", { count: displayItems.length, query: searchQuery })}
            </div>
          )}
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fill, minmax(280px, 1fr))",
              gap: 16,
            }}
          >
            {displayItems.map((supplier) => (
              <SupplierCard key={supplier.id} supplier={supplier} />
            ))}
          </div>

          {/* Pagination (only in browse mode) */}
          {!isSearchMode && data && data.total > data.pageSize && (
            <div
              style={{
                display: "flex",
                justifyContent: "center",
                alignItems: "center",
                gap: 12,
                marginTop: 32,
              }}
            >
              <button
                disabled={page === 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                style={{
                  padding: "8px 18px",
                  borderRadius: 8,
                  border: "1px solid #1F2937",
                  background: "transparent",
                  color: page === 1 ? "#374151" : "#6B7280",
                  fontSize: 13,
                  cursor: page === 1 ? "not-allowed" : "pointer",
                }}
              >
                {t("prev")}
              </button>
              <span style={{ color: "#4B5563", fontSize: 13 }}>
                {t("pageOf", { page, total: Math.ceil(data.total / data.pageSize) })}
              </span>
              <button
                disabled={page >= Math.ceil(data.total / data.pageSize)}
                onClick={() => setPage((p) => p + 1)}
                style={{
                  padding: "8px 18px",
                  borderRadius: 8,
                  border: "1px solid #1F2937",
                  background: "transparent",
                  color:
                    page >= Math.ceil(data.total / data.pageSize)
                      ? "#374151"
                      : "#6B7280",
                  fontSize: 13,
                  cursor:
                    page >= Math.ceil(data.total / data.pageSize)
                      ? "not-allowed"
                      : "pointer",
                }}
              >
                {t("next")}
              </button>
            </div>
          )}
        </>
      )}

      {isError && !isSearchMode && (
        <div style={{ color: "#F87171", fontSize: 13, textAlign: "center", marginTop: 24 }}>
          {t("errorLoad")}
        </div>
      )}
    </div>
  );
}
