using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NarsApi.Migrations
{
    /// <inheritdoc />
    public partial class AddGenerationJobDistrictsPhase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: generation_jobs comes from SQL DDL
            // (0002_create_generation_jobs.sql and create_nars_db.sql section 11),
            // and GenerationJobConfiguration excludes it from migrations. The two
            // columns the districts phase adds — generate_districts and
            // districts_result — are created by
            // 0003_districts_generation_phase.sql, which also widens the
            // feature_type / stage CHECK constraints the phase relies on. Keeping
            // a migration here (even an empty one) is what records the snapshot
            // below, so EF reads and writes the two columns; the database objects
            // themselves stay owned by the SQL file.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No DDL to reverse: dropping the columns is 0003's concern, and it
            // must not happen implicitly on rollback.
        }
    }
}
