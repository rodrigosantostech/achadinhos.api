START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    DROP INDEX "IX_publications_ScheduledAt";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    ALTER TABLE publications ADD "AttemptCount" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    ALTER TABLE publications ADD "ChannelId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    ALTER TABLE publications ADD "IdempotencyKey" character varying(150);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    ALTER TABLE publications ADD "IsTest" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    ALTER TABLE publications ADD "LastAttemptAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    ALTER TABLE publications ADD "LastErrorCode" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    ALTER TABLE publications ADD "LeaseToken" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    ALTER TABLE publications ADD "LeaseUntil" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    ALTER TABLE publications ADD "PayloadSnapshot" jsonb NOT NULL DEFAULT '{}';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    ALTER TABLE publications ADD "PublishedAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    ALTER TABLE publications ADD "TelegramChatId" bigint;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    ALTER TABLE publications ADD "TelegramMessageId" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    ALTER TABLE publications ADD "UpdatedAt" timestamp with time zone NOT NULL DEFAULT TIMESTAMPTZ '-infinity';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    ALTER TABLE publications ADD "ValidUntil" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    CREATE TABLE publication_attempts (
        "Id" uuid NOT NULL,
        "PublicationId" uuid NOT NULL,
        "Number" integer NOT NULL,
        "StartedAt" timestamp with time zone NOT NULL,
        "FinishedAt" timestamp with time zone,
        "Outcome" text NOT NULL,
        "HttpStatus" integer,
        "TelegramErrorCode" integer,
        "TelegramChatId" bigint,
        "TelegramMessageId" integer,
        "CorrelationId" uuid NOT NULL,
        CONSTRAINT "PK_publication_attempts" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_publication_attempts_publications_PublicationId" FOREIGN KEY ("PublicationId") REFERENCES publications ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    CREATE TABLE publication_channels (
        "Id" uuid NOT NULL,
        "Code" character varying(100) NOT NULL,
        "TelegramChatId" bigint NOT NULL,
        "Environment" text NOT NULL,
        "Enabled" boolean NOT NULL,
        "Blocked" boolean NOT NULL,
        "VerifiedAt" timestamp with time zone NOT NULL,
        "NextSendAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_publication_channels" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    CREATE INDEX "IX_publications_ChannelId" ON publications ("ChannelId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    CREATE UNIQUE INDEX "IX_publications_IdempotencyKey" ON publications ("IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    CREATE UNIQUE INDEX "IX_publications_OfferId_ChannelId" ON publications ("OfferId", "ChannelId") WHERE "ChannelId" IS NOT NULL AND "Status" IN ('Scheduled','Processing','RetryScheduled');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    CREATE INDEX "IX_publications_Status_LeaseUntil" ON publications ("Status", "LeaseUntil");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    CREATE INDEX "IX_publications_Status_ScheduledAt" ON publications ("Status", "ScheduledAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    CREATE UNIQUE INDEX "IX_publication_attempts_PublicationId_Number" ON publication_attempts ("PublicationId", "Number");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    CREATE UNIQUE INDEX "IX_publication_channels_Code" ON publication_channels ("Code");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    ALTER TABLE publications ADD CONSTRAINT "FK_publications_publication_channels_ChannelId" FOREIGN KEY ("ChannelId") REFERENCES publication_channels ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023193729_TelegramDelivery') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261023193729_TelegramDelivery', '10.0.12');
    END IF;
END $EF$;
COMMIT;

