using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NarsApi.Migrations
{
    /// <inheritdoc />
    public partial class AddErrorLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "areas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    layer = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_areas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "city_centers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    layer = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_city_centers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "communes",
                columns: table => new
                {
                    commune_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    daira_id = table.Column<int>(type: "integer", nullable: false),
                    commune_code = table.Column<int>(type: "integer", nullable: true),
                    commune_ar = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    commune_fr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    commune_latitude = table.Column<double>(type: "double precision", nullable: true),
                    commune_longitude = table.Column<double>(type: "double precision", nullable: true),
                    commune_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_communes", x => x.commune_id);
                });

            migrationBuilder.CreateTable(
                name: "communes_boundaries",
                columns: table => new
                {
                    commune_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    geometry = table.Column<Geometry>(type: "geometry", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_communes_boundaries", x => x.commune_id);
                });

            migrationBuilder.CreateTable(
                name: "dairas",
                columns: table => new
                {
                    daira_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    wilaya_id = table.Column<int>(type: "integer", nullable: false),
                    daira_ar = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    daira_fr = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    daira_latitude = table.Column<double>(type: "double precision", nullable: true),
                    daira_longitude = table.Column<double>(type: "double precision", nullable: true),
                    daira_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dairas", x => x.daira_id);
                });

            migrationBuilder.CreateTable(
                name: "districts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    layer = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_districts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "error_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    context = table.Column<string>(type: "text", nullable: true),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_error_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feature_registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feature_registry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "house_entrances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    road_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    layer = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_house_entrances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "naming_panels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    layer = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_naming_panels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "public_buildings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    layer = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_buildings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "public_spaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    layer = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_spaces", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    layer = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roads", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    commune_id = table.Column<int>(type: "integer", nullable: true),
                    daira_id = table.Column<int>(type: "integer", nullable: true),
                    wilaya_id = table.Column<int>(type: "integer", nullable: true),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    failed_login_attempts = table.Column<int>(type: "integer", nullable: true),
                    locked_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wilayas",
                columns: table => new
                {
                    wilaya_id = table.Column<int>(type: "integer", nullable: false),
                    wilaya_ar = table.Column<string>(type: "text", nullable: true),
                    wilaya_fr = table.Column<string>(type: "text", nullable: true),
                    wilaya_latitude = table.Column<double>(type: "double precision", nullable: true),
                    wilaya_longitude = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wilayas", x => x.wilaya_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_areas_user_id",
                table: "areas",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_areas_user_layer",
                table: "areas",
                columns: ["user_id", "layer"]);

            migrationBuilder.CreateIndex(
                name: "ix_city_centers_user_id",
                table: "city_centers",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_communes_boundaries_geometry",
                table: "communes_boundaries",
                column: "geometry")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "ix_districts_user_id",
                table: "districts",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_house_entrances_road_id",
                table: "house_entrances",
                column: "road_id",
                filter: "road_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_house_entrances_user_id",
                table: "house_entrances",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_house_entrances_user_layer",
                table: "house_entrances",
                columns: ["user_id", "layer"]);

            migrationBuilder.CreateIndex(
                name: "ix_naming_panels_user_id",
                table: "naming_panels",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_public_buildings_user_id",
                table: "public_buildings",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_public_spaces_user_id",
                table: "public_spaces",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash");

            migrationBuilder.CreateIndex(
                name: "ix_roads_user_id",
                table: "roads",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_roads_user_layer",
                table: "roads",
                columns: ["user_id", "layer"]);

            migrationBuilder.CreateIndex(
                name: "ix_users_commune_role",
                table: "users",
                columns: ["commune_id", "role"],
                filter: "commune_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_users_daira_id",
                table: "users",
                column: "daira_id",
                filter: "daira_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_role",
                table: "users",
                column: "role");

            migrationBuilder.CreateIndex(
                name: "IX_users_username",
                table: "users",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_wilaya_id",
                table: "users",
                column: "wilaya_id",
                filter: "wilaya_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "areas");

            migrationBuilder.DropTable(
                name: "city_centers");

            migrationBuilder.DropTable(
                name: "communes");

            migrationBuilder.DropTable(
                name: "communes_boundaries");

            migrationBuilder.DropTable(
                name: "dairas");

            migrationBuilder.DropTable(
                name: "districts");

            migrationBuilder.DropTable(
                name: "error_logs");

            migrationBuilder.DropTable(
                name: "feature_registry");

            migrationBuilder.DropTable(
                name: "house_entrances");

            migrationBuilder.DropTable(
                name: "naming_panels");

            migrationBuilder.DropTable(
                name: "public_buildings");

            migrationBuilder.DropTable(
                name: "public_spaces");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "roads");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "wilayas");
        }
    }
}
