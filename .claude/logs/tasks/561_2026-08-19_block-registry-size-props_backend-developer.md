# TASK-561 — Block Registry: 4 new resizable size props

**Status:** done
**Agent:** backend-developer

## What changed
`backend/ShelfGuard.Application/Features/MobileConfig/BlockRegistry/BlockRegistry.cs` — appended
one `BlockPropDefinition` to each of 4 block types' `Props` lists per ADR-031 / TASK-560's table:

| Block type | Prop | Default | Min | Max |
|---|---|---|---|---|
| `heroBanner` | `heightPx` | 190 | 120 | 260 |
| `bannerCarousel` | `cardWidthPx` | 280 | 200 | 360 |
| `promotionCarousel` | `cardWidthPx` | 210 | 150 | 270 |
| `productCarousel` | `cardWidthPx` | 170 | 120 | 220 |

`promotionGrid`/`productGrid` untouched (out of scope, keep only `columns`).
`MobileConfigWhitelists.cs`/`MobileConfigValidator.cs` untouched — `props` stays free-form JSON at
save-time by design (see `BlockRegistry`'s own class doc comment).

`backend/ShelfGuard.Tests/MobileConfig/BlockRegistryTests.cs` — added
`Resizable_block_types_declare_their_new_size_prop_with_expected_bounds` (`[Theory]`, 4
`[InlineData]` cases) asserting type/default/min/max/required for each new prop.

## Build/test
- `dotnet build` — clean, 0 errors (1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs`).
- `dotnet test --filter MobileConfig` — 251/251 passed, including the new test.

## Scope check
`git status --short backend/` shows only the two files above changed.
