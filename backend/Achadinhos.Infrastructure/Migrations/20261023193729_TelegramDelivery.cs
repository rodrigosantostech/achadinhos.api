using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Achadinhos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TelegramDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_publications_ScheduledAt",
                table: "publications");

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "publications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ChannelId",
                table: "publications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "publications",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTest",
                table: "publications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastAttemptAt",
                table: "publications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastErrorCode",
                table: "publications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseToken",
                table: "publications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseUntil",
                table: "publications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayloadSnapshot",
                table: "publications",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAt",
                table: "publications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TelegramChatId",
                table: "publications",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TelegramMessageId",
                table: "publications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "publications",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ValidUntil",
                table: "publications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "publication_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<string>(type: "text", nullable: false),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    TelegramErrorCode = table.Column<int>(type: "integer", nullable: true),
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: true),
                    TelegramMessageId = table.Column<int>(type: "integer", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publication_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_publication_attempts_publications_PublicationId",
                        column: x => x.PublicationId,
                        principalTable: "publications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "publication_channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: false),
                    Environment = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Blocked = table.Column<bool>(type: "boolean", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextSendAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publication_channels", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_publications_ChannelId",
                table: "publications",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_publications_IdempotencyKey",
                table: "publications",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_publications_OfferId_ChannelId",
                table: "publications",
                columns: new[] { "OfferId", "ChannelId" },
                unique: true,
                filter: "\"ChannelId\" IS NOT NULL AND \"Status\" IN ('Scheduled','Processing','RetryScheduled')");

            migrationBuilder.CreateIndex(
                name: "IX_publications_Status_LeaseUntil",
                table: "publications",
                columns: new[] { "Status", "LeaseUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_publications_Status_ScheduledAt",
                table: "publications",
                columns: new[] { "Status", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_publication_attempts_PublicationId_Number",
                table: "publication_attempts",
                columns: new[] { "PublicationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_publication_channels_Code",
                table: "publication_channels",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_publications_publication_channels_ChannelId",
                table: "publications",
                column: "ChannelId",
                principalTable: "publication_channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_publications_publication_channels_ChannelId",
                table: "publications");

            migrationBuilder.DropTable(
                name: "publication_attempts");

            migrationBuilder.DropTable(
                name: "publication_channels");

            migrationBuilder.DropIndex(
                name: "IX_publications_ChannelId",
                table: "publications");

            migrationBuilder.DropIndex(
                name: "IX_publications_IdempotencyKey",
                table: "publications");

            migrationBuilder.DropIndex(
                name: "IX_publications_OfferId_ChannelId",
                table: "publications");

            migrationBuilder.DropIndex(
                name: "IX_publications_Status_LeaseUntil",
                table: "publications");

            migrationBuilder.DropIndex(
                name: "IX_publications_Status_ScheduledAt",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "IsTest",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "LastErrorCode",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "LeaseToken",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "LeaseUntil",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "PayloadSnapshot",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "TelegramChatId",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "TelegramMessageId",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "ValidUntil",
                table: "publications");

            migrationBuilder.CreateIndex(
                name: "IX_publications_ScheduledAt",
                table: "publications",
                column: "ScheduledAt");
        }
    }
}
