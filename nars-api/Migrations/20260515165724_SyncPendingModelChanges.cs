using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NarsApi.Migrations
{
    /// <inheritdoc />
    public partial class SyncPendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No rollback needed — this migration synchronizes the EF model
            // with the database schema after pending model changes were detected.
            // The schema is already in the desired state; no DDL changes were applied.
        }
    }
}
