using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Achadinhos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LocalPublicationSimulation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "publication_simulations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SimulatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Operator = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PreviewText = table.Column<string>(type: "text", nullable: false),
                    ProductUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Snapshot = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publication_simulations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_publication_simulations_publications_PublicationId",
                        column: x => x.PublicationId,
                        principalTable: "publications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_publication_simulations_PublicationId",
                table: "publication_simulations",
                column: "PublicationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "publication_simulations");
        }
    }
}
