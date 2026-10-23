START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023200537_LocalPublicationSimulation') THEN
    CREATE TABLE publication_simulations (
        "Id" uuid NOT NULL,
        "PublicationId" uuid NOT NULL,
        "SimulatedAt" timestamp with time zone NOT NULL,
        "Operator" character varying(200) NOT NULL,
        "PreviewText" text NOT NULL,
        "ProductUrl" character varying(2048) NOT NULL,
        "Snapshot" jsonb NOT NULL,
        CONSTRAINT "PK_publication_simulations" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_publication_simulations_publications_PublicationId" FOREIGN KEY ("PublicationId") REFERENCES publications ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023200537_LocalPublicationSimulation') THEN
    CREATE UNIQUE INDEX "IX_publication_simulations_PublicationId" ON publication_simulations ("PublicationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023200537_LocalPublicationSimulation') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261023200537_LocalPublicationSimulation', '10.0.12');
    END IF;
END $EF$;
COMMIT;

