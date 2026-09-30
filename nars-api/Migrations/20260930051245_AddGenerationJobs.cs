using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NarsApi.Migrations
{
    /// <inheritdoc />
    public partial class AddGenerationJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: these tables come from SQL DDL (0002_create_generation_jobs
            // and create_nars_db.sql section 11). Both entity configs exclude the tables from
            // migrations, so the snapshot here only keeps EF in sync; the database objects are
            // created and owned by the SQL files.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No DDL to reverse: dropping the tables is the SQL files' concern.
        }
    }
}
