CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023141545_InitialFoundation') THEN
    CREATE TABLE integration_runs (
        "Id" uuid NOT NULL,
        "Provider" text NOT NULL,
        "JobType" text NOT NULL,
        "Status" text NOT NULL,
        "StartedAt" timestamp with time zone NOT NULL,
        "FinishedAt" timestamp with time zone,
        "ItemsReceived" integer NOT NULL,
        "ItemsUpserted" integer NOT NULL,
        "ErrorSummary" character varying(2000),
        CONSTRAINT "PK_integration_runs" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023141545_InitialFoundation') THEN
    CREATE TABLE marketplaces (
        "Id" uuid NOT NULL,
        "Code" character varying(50) NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Enabled" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_marketplaces" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023141545_InitialFoundation') THEN
    CREATE TABLE offers (
        "Id" uuid NOT NULL,
        "MarketplaceId" uuid NOT NULL,
        "ExternalOfferId" character varying(200) NOT NULL,
        "ExternalSellerId" text,
        "Title" character varying(1000) NOT NULL,
        "ProductUrl" text NOT NULL,
        "ImageUrl" text,
        "Currency" character(3) NOT NULL,
        "CurrentPrice" numeric(18,2),
        "OriginalPrice" numeric(18,2),
        "Availability" text NOT NULL,
        "Status" text NOT NULL,
        "LastSeenAt" timestamp with time zone NOT NULL,
        "RawPayload" jsonb NOT NULL,
        CONSTRAINT "PK_offers" PRIMARY KEY ("Id"),
        CONSTRAINT ck_offers_prices CHECK ("CurrentPrice" >= 0 AND "OriginalPrice" >= 0),
        CONSTRAINT "FK_offers_marketplaces_MarketplaceId" FOREIGN KEY ("MarketplaceId") REFERENCES marketplaces ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023141545_InitialFoundation') THEN
    CREATE TABLE price_snapshots (
        "Id" uuid NOT NULL,
        "OfferId" uuid NOT NULL,
        "Price" numeric(18,2) NOT NULL,
        "Currency" character(3) NOT NULL,
        "PriceType" text NOT NULL,
        "CouponConditions" text,
        "Source" text NOT NULL,
        "CapturedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_price_snapshots" PRIMARY KEY ("Id"),
        CONSTRAINT ck_snapshot_price CHECK ("Price" >= 0),
        CONSTRAINT "FK_price_snapshots_offers_OfferId" FOREIGN KEY ("OfferId") REFERENCES offers ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023141545_InitialFoundation') THEN
    CREATE INDEX "IX_integration_runs_Provider_StartedAt" ON integration_runs ("Provider", "StartedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023141545_InitialFoundation') THEN
    CREATE UNIQUE INDEX "IX_marketplaces_Code" ON marketplaces ("Code");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023141545_InitialFoundation') THEN
    CREATE UNIQUE INDEX "IX_offers_MarketplaceId_ExternalOfferId" ON offers ("MarketplaceId", "ExternalOfferId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023141545_InitialFoundation') THEN
    CREATE INDEX "IX_price_snapshots_OfferId_CapturedAt" ON price_snapshots ("OfferId", "CapturedAt" DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023141545_InitialFoundation') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261023141545_InitialFoundation', '10.0.12');
    END IF;
END $EF$;
COMMIT;

