using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NarsApi.Migrations
{
    /// <inheritdoc />
    public partial class AddStampEvictionNotifyTrigger : Migration
    {
        /// <summary>
        /// Channel name shared with StampEvictionListener.ChannelName.
        /// The trigger fires on every UPDATE of users.security_stamp (lockout,
        /// password change, admin privilege change) so every API replica's
        /// StampEvictionListener evicts its cached stamp immediately.
        /// </summary>
        public const string NotifyChannel = "nars_stamp_evict";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent CREATE OR REPLACE keeps re-runs and manual baseline restores safe.
            migrationBuilder.Sql($"""
                CREATE OR REPLACE FUNCTION nars_notify_security_stamp_change() RETURNS trigger AS $$
                BEGIN
                    PERFORM pg_notify('{NotifyChannel}', NEW.id::text);
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS nars_users_security_stamp_notify ON users;

                CREATE TRIGGER nars_users_security_stamp_notify
                AFTER UPDATE OF security_stamp ON users
                FOR EACH ROW
                EXECUTE FUNCTION nars_notify_security_stamp_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS nars_users_security_stamp_notify ON users;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS nars_notify_security_stamp_change();");
        }
    }
}
