START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261102102841_AddReceiptCategoryWork') THEN
    ALTER TABLE stock_receipts ADD "ReceivingWorkJson" jsonb;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261102102841_AddReceiptCategoryWork') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261102102841_AddReceiptCategoryWork', '8.0.11');
    END IF;
END $EF$;
COMMIT;

