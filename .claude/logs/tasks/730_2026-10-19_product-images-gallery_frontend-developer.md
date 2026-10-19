# TASK-730 Product multi-photo gallery (frontend)
- New `features/inventory/components/ProductImagesSection.tsx`: thumbnail grid, add (client downscale to 1600px JPEG), label + quick chips (max 50), set primary, delete w/ confirm, max 8 counter, hint. `canEdit` prop (default true).
- types.ts (`ItemImage`, `UpdateItemImagePayload`), api/products.ts (get/add/update/deleteImage), hooks (`useItemImages` key `["products", id, "images"]`, add/update/delete invalidate `["products"]`).
- ProductForm: single-image control replaced by the gallery (edit mode) / hint (create mode); removed `onImageUpload` prop + old state; edit page no longer wires `useUploadProductImage` (hook/endpoint left intact).
- i18n: `Dashboard.inventory.itemImages.*`, `form.imagesCreateHint` (uk+en, additive).
- tsc clean; eslint 0 errors (pre-existing warnings only). Not browser-verified (needs backend endpoints).
