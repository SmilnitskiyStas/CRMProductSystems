# TASK-652 (T5) — delivery-coverage section in the cooperation-contract PDF

**Agent:** backend-developer · **Date:** 2026-08-31 · **Status:** review (not merged)
**Branch:** `worktree-agent-ae1eafb63d6be4bf5` (isolated worktree) · Plan: `eventual-whistling-rabbit.md` §Backend API, T5 of T1–T16
**Depends on:** T1 (`UkraineRegions`), T3 (`DeliveryCoverageJson`, `SupplierProfile.DeliveryCoverage`) — both merged to `main`
(commits `db9e6cb7`, `c5f02043`). Worktree was branched before those merges; fast-forwarded my clean branch to `main`
(`e2ae3451`) at the start of the task to pick them up.

## Scope delivered

### `IContractPdfGenerator.cs` (`Features/Marketplace/`)
- `ContractPdfData` += 3 trailing optional params:
  - `IReadOnlyList<ContractDeliveryRegion>? DeliveryCoverageServed = null`
  - `IReadOnlyList<string>? DeliveryCoverageNotServed = null` — **resolved region NAMES, not codes**
  - `string? DeliveryCoverageNote = null`
- New record `ContractDeliveryRegion(string RegionName, string? Terms)` in the same file.
- Generator contract unchanged otherwise — stays IO-free / lookup-free, receives already-resolved Ukrainian names.

### `ContractPdfGenerator.cs` (`Infrastructure/Documents/`)
- New section **«5. РЕГІОНИ ТА УМОВИ ДОСТАВКИ»** inserted immediately before the signatures block.
  Signatures **renumbered `5. ПІДПИСИ СТОРІН` → `6. ПІДПИСИ СТОРІН`**.
- Renders **only when `DeliveryCoverageServed is { Count: > 0 }`** — same optional-block style as the existing
  `data.ClientLegalName is { Length: > 0 }` client-requisites block.
- Content:
  - `5.1. Постачальник здійснює доставку в такі регіони:` lead line.
  - 2-col `Table` (`ConstantColumn(150)` / `RelativeColumn()`, `PaddingVertical(2)`, `.SemiBold()` header cells
    `Регіон` / `Умови`) — one row per served region; `Terms` null/blank → `за домовленістю`.
  - `5.2. Доставка не здійснюється в такі регіони: {comma-joined names}.` — only when `DeliveryCoverageNotServed` non-empty.
  - `5.3. {note}` — only when `DeliveryCoverageNote` non-empty.
- Ukrainian text renders via the bundled DejaVu Sans (QuestPDF's default glyph check would throw otherwise —
  that is the font smoke coverage the new tests lean on).

### `SupplierAgreementService.cs` — `GenerateAndStoreContractAsync`
- New private helper `BuildDeliveryCoverageAsync(Guid supplierTenantId, ct)`:
  - Loads the supplier's own profile via **existing** `IMarketplaceRepository.GetOwnProfileAsync(supplierTenantId, ct)`
    (`_marketplace` already injected). That method applies **plain tenant RLS, no provider bypass** — correct here
    because `GenerateAndStoreContractAsync` always runs under the supplier's own RLS context (called from
    `ApproveAsync` / `RegenerateContractAsync`, both supplier-authenticated; `agreement.SupplierTenantId` already
    confirmed == caller by `GetOwnAsync`). Matches how the method already loads supplier-side data
    (`_settings.GetByTenantAsync`, `_tenantNames.GetTenantDisplayNameAsync`) — an existing repo call, no new repo code.
  - `DeliveryCoverageJson.Parse(profile?.DeliveryCoverage)`; if null or `Served.Count == 0` → returns `(null, null, null)`
    (section absent).
  - Otherwise resolves every code via `ResolveRegionName` = `UkraineRegions.Find(code)?.NameUa ?? code`
    (served + not-served); passes `coverage.Note` through.
- `ContractPdfData` construction extended with the 3 resolved values.
- No change to `MarketplaceRepository` / `MarketplaceController` / `MarketplaceService` / worker / frontend / mobile.

## Tests

### `ContractPdfGeneratorTests.cs` (+3)
- `Generate_WithDeliveryCoverage_RendersRegionsSection` — 2 served regions (one w/ terms, one without) + not-served +
  note → valid PDF (magic bytes + `> 1000`), and **larger than the same contract without coverage** (section rendered).
- `Generate_ServedWithoutNotServedOrNote_StillRendersSection` — served-only vs served + 5.2 + 5.3 → the latter is longer.
- `Generate_NullDeliveryCoverage_OmitsSection_KeepsSignatures` — no coverage args → still a valid PDF (signatures, now §6).
- **No existing assertion changes** — existing tests use a mocked `IContractPdfGenerator` or check magic-bytes/length
  only; none grep section numbers or `"5. ПІДПИСИ"`. (Test project has no PDF text extractor and adding a NuGet parser
  was out of scope — assertions use the byte-length-delta style the existing `Generate_WithImages_EmbedsThem` test uses.)

### `SupplierAgreementServiceTests.cs` (+2)
- `Approve_WithDeliveryCoverage_ResolvesRegionCodesToNamesForPdf` — stubs `GetOwnProfileAsync` with
  `{"served":[{"regionCode":"UA-32","terms":"2-3 дні"},{"regionCode":"UA-18-ZHYTOMYR"}],"notServed":["UA-43"],"note":"..."}`
  → asserts the `ContractPdfData` passed to `_pdf.Generate` carries `Київська` / `Житомир` / `Автономна Республіка Крим`
  and the note.
- `Approve_NoDeliveryCoverage_PassesNullCoverageToPdf` — `GetOwnProfileAsync` unstubbed (null) → all 3 coverage
  params null.

## Verification
- `dotnet build` (backend) — **0 errors, 0 warnings**.
- `dotnet test --filter "FullyQualifiedName~ContractPdf|FullyQualifiedName~SupplierAgreement"` — **29/29 green**.
- `dotnet test --filter "FullyQualifiedName~Marketplace"` — **272/272 green**.
- `dotnet test` (full suite) — **2109/2109 green**.

## Not done / follow-ups
- Not pushed, not merged (worktree branch). Merge order per plan: after T3 (done), alongside T4/T9.
- `backend/openapi.json` unaffected (no HTTP contract change) — nothing for T15 from this task beyond the ADR note that
  the contract PDF now has a §5 delivery section / §6 signatures.
