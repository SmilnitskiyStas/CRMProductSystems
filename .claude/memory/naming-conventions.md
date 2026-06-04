# Naming Conventions

**Last updated:** 2026-06-03

## Backend (C#)
Service:         IProductService, ProductService
Repository:      IProductRepository, ProductRepository
Controller:      ProductsController
DTO:             ProductDto, CreateProductRequest, UpdateProductRequest
Entity:          Product, ProductStock
Feature folder:  Features/Inventory/, Features/Shelf/

## Frontend (TypeScript)
Hook:            useProducts, useCreateProduct, useDeleteProduct
API module:      productsApi
Query key:       ["products"], ["products", id]
Component:       ProductsTable, ProductForm
Page:            app/(dashboard)/inventory/page.tsx
Feature:         features/inventory/

## Database (PostgreSQL)
Tables:          snake_case plural: products, product_stock, stock_movements
Columns:         snake_case: tenant_id, expiry_date, batch_number
Indexes:         idx_{table}_{columns}: idx_stock_expiry_active
Policies:        tenant_isolation, provider_bypass

## Files
Task log:        TASK-ID_YYYY-MM-DD_description_agent.md
Handoff:         TASK-ID_YYYY-MM-DD_from-agent_to-agent.md
ADR:             decisions.md (all ADRs in one file)
Daily log:       YYYY-MM-DD.md
