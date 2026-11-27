using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NarsApi.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeRoundaboutsFeatureRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Legacy rows kept their old registry type even after the table
            // rename. Normalize them so update/delete/registry lookups resolve
            // the roundabouts DbSet instead of returning "feature not found".
            migrationBuilder.Sql("""
                UPDATE public.feature_registry
                SET feature_type = 'roundabouts'
                WHERE feature_type IN ('city_center', 'roundabout');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort restore: the subtype stored in the roundabouts JSON
            // determines which legacy registry type the row used to have.
            migrationBuilder.Sql("""
                UPDATE public.feature_registry AS fr
                SET feature_type = CASE
                        WHEN COALESCE(r.data ->> 'roundaboutsTypeKey', 'city_center') = 'roundabout'
                            THEN 'roundabout'
                        ELSE 'city_center'
                    END
                FROM public.roundabouts AS r
                WHERE fr.id = r.id
                  AND fr.feature_type = 'roundabouts';
                """);
        }
    }
}
