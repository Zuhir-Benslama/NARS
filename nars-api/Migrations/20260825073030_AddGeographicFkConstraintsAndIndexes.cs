using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NarsApi.Migrations
{
    /// <inheritdoc />
    public partial class AddGeographicFkConstraintsAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_inspections_feature_id",
                table: "inspections");

            migrationBuilder.CreateIndex(
                name: "ix_dairas_wilaya_id",
                table: "dairas",
                column: "wilaya_id");

            migrationBuilder.CreateIndex(
                name: "ix_communes_daira_id",
                table: "communes",
                column: "daira_id");

            migrationBuilder.AddForeignKey(
                name: "FK_communes_dairas_daira_id",
                table: "communes",
                column: "daira_id",
                principalTable: "dairas",
                principalColumn: "daira_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_dairas_wilayas_wilaya_id",
                table: "dairas",
                column: "wilaya_id",
                principalTable: "wilayas",
                principalColumn: "wilaya_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_communes_dairas_daira_id",
                table: "communes");

            migrationBuilder.DropForeignKey(
                name: "FK_dairas_wilayas_wilaya_id",
                table: "dairas");

            migrationBuilder.DropIndex(
                name: "ix_dairas_wilaya_id",
                table: "dairas");

            migrationBuilder.DropIndex(
                name: "ix_communes_daira_id",
                table: "communes");

            migrationBuilder.CreateIndex(
                name: "ix_inspections_feature_id",
                table: "inspections",
                column: "feature_id");
        }
    }
}
