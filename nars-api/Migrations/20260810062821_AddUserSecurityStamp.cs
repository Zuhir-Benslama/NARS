using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NarsApi.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSecurityStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "security_stamp",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // Backfill legacy rows with a unique random stamp so existing
            // sessions can be invalidated via rotation going forward.
            // gen_random_uuid() is built into PostgreSQL 13+.
            migrationBuilder.Sql(
                "UPDATE users SET security_stamp = replace(gen_random_uuid()::text, '-', '') WHERE security_stamp = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
                name: "security_stamp",
                table: "users");
    }
}
