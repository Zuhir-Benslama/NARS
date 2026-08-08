using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NarsApi.Migrations
{
    /// <inheritdoc />
    public partial class AddForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-edited: the generator emitted `ADD COLUMN "xmin" xid` on the
            // eight feature tables, but xmin is a PostgreSQL SYSTEM column
            // (reserved name) — Npgsql maps [Timestamp] uint Version to the
            // implicit xmin that already exists on every table, so no column is
            // added or dropped here.
            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens");

            migrationBuilder.AlterColumn<string>(
                name: "wilaya_fr",
                table: "wilayas",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "wilaya_ar",
                table: "wilayas",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_error_logs_created_at",
                table: "error_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_error_logs_level",
                table: "error_logs",
                column: "level");

            migrationBuilder.CreateIndex(
                name: "ix_error_logs_user_id",
                table: "error_logs",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_error_logs_users_user_id",
                table: "error_logs",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_inspections_users_user_id",
                table: "inspections",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_refresh_tokens_users_user_id",
                table: "refresh_tokens",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_error_logs_users_user_id",
                table: "error_logs");

            migrationBuilder.DropForeignKey(
                name: "FK_inspections_users_user_id",
                table: "inspections");

            migrationBuilder.DropForeignKey(
                name: "FK_refresh_tokens_users_user_id",
                table: "refresh_tokens");

            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens");

            migrationBuilder.DropIndex(
                name: "IX_refresh_tokens_user_id",
                table: "refresh_tokens");

            migrationBuilder.DropIndex(
                name: "ix_error_logs_created_at",
                table: "error_logs");

            migrationBuilder.DropIndex(
                name: "ix_error_logs_level",
                table: "error_logs");

            migrationBuilder.DropIndex(
                name: "ix_error_logs_user_id",
                table: "error_logs");

            migrationBuilder.AlterColumn<string>(
                name: "wilaya_fr",
                table: "wilayas",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "wilaya_ar",
                table: "wilayas",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash");
        }
    }
}
