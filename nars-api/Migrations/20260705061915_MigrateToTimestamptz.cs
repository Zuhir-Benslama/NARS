using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NarsApi.Migrations
{
    /// <inheritdoc />
    public partial class MigrateToTimestamptz : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Migrate all timestamp columns from 'timestamp without time zone' to
            // 'timestamp with time zone'. Existing data is stored as UTC (all code
            // paths use DateTime.UtcNow or timeProvider.UtcNow), so we interpret
            // the stored values as UTC when converting.
            foreach (var (table, columns) in TimestampColumns)
            {
                foreach (var col in columns)
                {
                    var sql = $"""
                        ALTER TABLE "{table}" ALTER COLUMN "{col}" TYPE timestamp with time zone
                        USING "{col}" AT TIME ZONE 'UTC'
                        """;
                    migrationBuilder.Sql(sql);
                }
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, columns) in TimestampColumns)
            {
                foreach (var col in columns)
                {
                    var sql = $"""
                        ALTER TABLE "{table}" ALTER COLUMN "{col}" TYPE timestamp without time zone
                        USING "{col}" AT TIME ZONE 'UTC'
                        """;
                    migrationBuilder.Sql(sql);
                }
            }
        }

        private static readonly (string Table, string[] Columns)[] TimestampColumns =
        [
            ("areas",             ["created_at", "updated_at"]),
            ("districts",         ["created_at", "updated_at"]),
            ("city_centers",      ["created_at", "updated_at"]),
            ("roads",             ["created_at", "updated_at"]),
            ("house_entrances",   ["created_at", "updated_at"]),
            ("public_buildings",  ["created_at", "updated_at"]),
            ("public_spaces",     ["created_at", "updated_at"]),
            ("naming_panels",     ["created_at", "updated_at"]),
            ("inspections",       ["created_at", "updated_at"]),
            ("users",             ["created_at", "locked_until"]),
            ("refresh_tokens",    ["created_at", "expires_at"]),
            ("error_logs",        ["created_at"]),
        ];
    }
}
