# TASK-729 — Item multi-image (backend)
- New `ItemImage` entity/`item_images` (+ migration `20261019173912_AddItemImages`: RLS tenant/provider/worker, partial unique one-primary index, data-migrates existing `items.ImageUrl` as primary). Migration NOT applied to any DB.
- `ItemImageService`/`IItemImageRepository`: list/add/patch/delete, max 8, signature+ext+5MB checks, tenant guard (404), primary switch (two saves: unset old, then set new), delete promotes next, `Item.ImageUrl` mirror. Old `ItemService.UploadImageAsync` removed; legacy `POST/DELETE /items/{id}/image` routed to new service.
- `ItemsController`: GET/POST `/images`, PATCH/DELETE `/images/{imageId}`, legacy DELETE `/image`.
- `ShelfService`: up to 3 refs/item (primary first), breadth-first under 12 imgs / 8 MB, `<tenantId>/<file>` paths with traversal guard, legacy `Item.ImageUrl` fallback. `ShelfPhotoExpectedItem.Reference` -> `References`; advisor captions "Reference photo i/n [label] of item ...".
- Tests: ItemImageServiceTests (10), ShelfServiceTests extended, ItemImagesRlsIntegrationTests (soft-skips until migration applied). Full suite 2531 passed.
