using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Achadinhos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LocalWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "offer_reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Operator = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offer_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_offer_reviews_offers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "publications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Operator = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_publications_offers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "offers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "marketplaces",
                columns: new[] { "Id", "Code", "CreatedAt", "Enabled", "Name" },
                values: new object[] { new Guid("5b70fb4d-7329-4da9-9167-3e049321a13e"), "manual", new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Cadastro manual" });

            migrationBuilder.CreateIndex(
                name: "IX_offer_reviews_OfferId_CreatedAt",
                table: "offer_reviews",
                columns: new[] { "OfferId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_publications_OfferId",
                table: "publications",
                column: "OfferId",
                unique: true,
                filter: "\"Status\" = 'Scheduled'");

            migrationBuilder.CreateIndex(
                name: "IX_publications_ScheduledAt",
                table: "publications",
                column: "ScheduledAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "offer_reviews");

            migrationBuilder.DropTable(
                name: "publications");

            migrationBuilder.DeleteData(
                table: "marketplaces",
                keyColumn: "Id",
                keyValue: new Guid("5b70fb4d-7329-4da9-9167-3e049321a13e"));
        }
    }
}
