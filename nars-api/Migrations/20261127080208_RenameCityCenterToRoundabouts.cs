using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NarsApi.Migrations
{
    /// <inheritdoc />
    public partial class RenameCityCenterToRoundabouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Collapse the two former layers into one. The plain city center is
            // now a subtype of roundabouts, carried in data.roundaboutsTypeKey.
            // Do this before renaming the table so the JSON fallback can still
            // read the old `layer` value.
            migrationBuilder.Sql("""
                UPDATE public.city_centers
                SET layer = 'roundabouts',
                    data = CASE
                        WHEN jsonb_typeof(data) = 'object' THEN
                            (
                                jsonb_set(
                                    data,
                                    '{roundaboutsTypeKey}',
                                    to_jsonb(
                                        COALESCE(
                                            data ->> 'roundaboutsTypeKey',
                                            data ->> 'cityCenterTypeKey',
                                            CASE WHEN layer = 'roundabout' THEN 'roundabout' ELSE 'city_center' END
                                        )
                                    ),
                                    true
                                ) - 'cityCenterTypeKey'
                            ) || jsonb_build_object('type', 'roundabouts')
                        ELSE data
                    END;
                """);

            migrationBuilder.RenameTable(name: "city_centers", newName: "roundabouts");

            migrationBuilder.RenameIndex(
                name: "ix_city_centers_user_id",
                table: "roundabouts",
                newName: "ix_roundabouts_user_id");

            // New clusters name constraints after the table; EF-created clusters
            // use the PK_/FK_ convention. Normalize whichever form is present.
            migrationBuilder.Sql("""
                DO $rename_roundabouts_constraints$
                DECLARE
                    constraint_name text;
                BEGIN
                    SELECT conname INTO constraint_name
                    FROM pg_constraint
                    WHERE conrelid = 'public.roundabouts'::regclass AND contype = 'p'
                    LIMIT 1;
                    IF constraint_name IS NOT NULL AND constraint_name <> 'roundabouts_pkey' THEN
                        EXECUTE format(
                            'ALTER TABLE public.roundabouts RENAME CONSTRAINT %I TO roundabouts_pkey',
                            constraint_name);
                    END IF;

                    SELECT conname INTO constraint_name
                    FROM pg_constraint
                    WHERE conrelid = 'public.roundabouts'::regclass AND contype = 'f'
                    LIMIT 1;
                    IF constraint_name IS NOT NULL AND constraint_name <> 'roundabouts_user_fk' THEN
                        EXECUTE format(
                            'ALTER TABLE public.roundabouts RENAME CONSTRAINT %I TO roundabouts_user_fk',
                            constraint_name);
                    END IF;
                END
                $rename_roundabouts_constraints$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "roundabouts", newName: "city_centers");

            migrationBuilder.RenameIndex(
                name: "ix_roundabouts_user_id",
                table: "city_centers",
                newName: "ix_city_centers_user_id");

            migrationBuilder.Sql("""
                DO $rename_city_centers_constraints$
                DECLARE
                    constraint_name text;
                BEGIN
                    SELECT conname INTO constraint_name
                    FROM pg_constraint
                    WHERE conrelid = 'public.city_centers'::regclass AND contype = 'p'
                    LIMIT 1;
                    IF constraint_name IS NOT NULL AND constraint_name <> 'city_centers_pkey' THEN
                        EXECUTE format(
                            'ALTER TABLE public.city_centers RENAME CONSTRAINT %I TO city_centers_pkey',
                            constraint_name);
                    END IF;

                    SELECT conname INTO constraint_name
                    FROM pg_constraint
                    WHERE conrelid = 'public.city_centers'::regclass AND contype = 'f'
                    LIMIT 1;
                    IF constraint_name IS NOT NULL AND constraint_name <> 'city_centers_user_fk' THEN
                        EXECUTE format(
                            'ALTER TABLE public.city_centers RENAME CONSTRAINT %I TO city_centers_user_fk',
                            constraint_name);
                    END IF;
                END
                $rename_city_centers_constraints$;
                """);

            // Restore the two legacy layers and the old JSON key.
            migrationBuilder.Sql("""
                UPDATE public.city_centers
                SET layer = CASE
                        WHEN COALESCE(data ->> 'roundaboutsTypeKey', 'roundabout') = 'roundabout'
                            THEN 'roundabout'
                        ELSE 'city_center'
                    END,
                    data = CASE
                        WHEN jsonb_typeof(data) = 'object' THEN
                            (
                                jsonb_set(
                                    data,
                                    '{cityCenterTypeKey}',
                                    to_jsonb(
                                        COALESCE(data ->> 'roundaboutsTypeKey', 'city_center')
                                    ),
                                    true
                                ) - 'roundaboutsTypeKey'
                            ) || jsonb_build_object(
                                'type',
                                CASE
                                    WHEN COALESCE(data ->> 'roundaboutsTypeKey', 'roundabout') = 'roundabout'
                                        THEN 'roundabout'
                                    ELSE 'city_center'
                                END
                            )
                        ELSE data
                    END;
                """);
        }
    }
}
