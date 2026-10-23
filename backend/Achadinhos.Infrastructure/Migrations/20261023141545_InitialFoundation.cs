using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Achadinhos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    JobType = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ItemsReceived = table.Column<int>(type: "integer", nullable: false),
                    ItemsUpserted = table.Column<int>(type: "integer", nullable: false),
                    ErrorSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "marketplaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketplaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "offers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketplaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalOfferId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExternalSellerId = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ProductUrl = table.Column<string>(type: "text", nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    CurrentPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    OriginalPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Availability = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RawPayload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offers", x => x.Id);
                    table.CheckConstraint("ck_offers_prices", "\"CurrentPrice\" >= 0 AND \"OriginalPrice\" >= 0");
                    table.ForeignKey(
                        name: "FK_offers_marketplaces_MarketplaceId",
                        column: x => x.MarketplaceId,
                        principalTable: "marketplaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "price_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    PriceType = table.Column<string>(type: "text", nullable: false),
                    CouponConditions = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_snapshots", x => x.Id);
                    table.CheckConstraint("ck_snapshot_price", "\"Price\" >= 0");
                    table.ForeignKey(
                        name: "FK_price_snapshots_offers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_integration_runs_Provider_StartedAt",
                table: "integration_runs",
                columns: new[] { "Provider", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_marketplaces_Code",
                table: "marketplaces",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_offers_MarketplaceId_ExternalOfferId",
                table: "offers",
                columns: new[] { "MarketplaceId", "ExternalOfferId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_price_snapshots_OfferId_CapturedAt",
                table: "price_snapshots",
                columns: new[] { "OfferId", "CapturedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_runs");

            migrationBuilder.DropTable(
                name: "price_snapshots");

            migrationBuilder.DropTable(
                name: "offers");

            migrationBuilder.DropTable(
                name: "marketplaces");
        }
    }
}
