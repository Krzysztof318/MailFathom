// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HoldMailAccountSettingsInColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ClassifiesSpam",
                table: "settings_mail_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasReadableSettings",
                table: "settings_mail_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int[]>(
                name: "ScansFor",
                table: "settings_mail_accounts",
                type: "integer[]",
                nullable: false,
                defaultValueSql: "'{}'::integer[]");

            migrationBuilder.AddColumn<int[]>(
                name: "ScreensOutgoingMailFor",
                table: "settings_mail_accounts",
                type: "integer[]",
                nullable: false,
                defaultValueSql: "'{}'::integer[]");

            migrationBuilder.AddColumn<int>(
                name: "SynchronizationMode",
                table: "settings_mail_accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "mail_account_folder_settings",
                columns: table => new
                {
                    MailAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Alias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SpecialUse = table.Column<int>(type: "integer", nullable: true),
                    IsSynchronized = table.Column<bool>(type: "boolean", nullable: false),
                    IsVisibleToTools = table.Column<bool>(type: "boolean", nullable: false),
                    GeneratesEmbeddings = table.Column<bool>(type: "boolean", nullable: false),
                    IsClassifiedForSpam = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mail_account_folder_settings", x => new { x.MailAccountId, x.Alias });
                    table.ForeignKey(
                        name: "fk_mail_account_folder_settings_settings_mail_accounts",
                        column: x => x.MailAccountId,
                        principalTable: "settings_mail_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Every existing account's settings are read out of its document the way the host binds one: property names
            // in any case, a list written as an array or as an object keyed by position, a switch written as a boolean or
            // as text, an unset participation switch reading as true, no folders reading as the inbox alone, and spam
            // classification scanning the inbox aliases when it names no folders. The helpers live in pg_temp, so they
            // end with the session that ran the migration and leave nothing in the schema.
            // ponytail: every object document is marked readable; one the host's strict binding would refuse is served
            // by the deployment-wide answers until its next write re-reads it, where the projection is computed in C#.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION pg_temp.mf_member(document jsonb, name text) RETURNS jsonb
                LANGUAGE sql IMMUTABLE AS $$
                    SELECT entry.value
                    FROM jsonb_each(CASE jsonb_typeof(document) WHEN 'object' THEN document ELSE '{}'::jsonb END) AS entry
                    WHERE lower(entry.key) = lower(name)
                    ORDER BY entry.key
                    LIMIT 1
                $$;

                CREATE FUNCTION pg_temp.mf_list(list jsonb) RETURNS jsonb
                LANGUAGE sql IMMUTABLE AS $$
                    SELECT CASE jsonb_typeof(list)
                        WHEN 'array' THEN list
                        WHEN 'object' THEN (
                            SELECT coalesce(jsonb_agg(e.value ORDER BY CASE WHEN e.key ~ '^[0-9]{1,9}$' THEN e.key::int END, e.key), '[]'::jsonb)
                            FROM jsonb_each(list) AS e)
                        ELSE '[]'::jsonb
                    END
                $$;

                CREATE FUNCTION pg_temp.mf_text(value jsonb) RETURNS text
                LANGUAGE sql IMMUTABLE AS $$
                    SELECT CASE WHEN jsonb_typeof(value) IN ('string', 'number', 'boolean') THEN nullif(btrim(value #>> '{}'), '') END
                $$;

                CREATE FUNCTION pg_temp.mf_switch(value jsonb, unset boolean) RETURNS boolean
                LANGUAGE sql IMMUTABLE AS $$
                    SELECT coalesce(lower(pg_temp.mf_text(value)) = 'true', unset)
                $$;

                UPDATE settings_mail_accounts AS account
                SET "HasReadableSettings" = jsonb_typeof(account."Document") = 'object',
                    "SynchronizationMode" = CASE lower(pg_temp.mf_text(pg_temp.mf_member(account."Document", 'Mode')))
                        WHEN 'push' THEN 1
                        WHEN '1' THEN 1
                        ELSE 0
                    END,
                    "ClassifiesSpam" = pg_temp.mf_switch(
                        pg_temp.mf_member(pg_temp.mf_member(account."Document", 'SpamClassification'), 'Enabled'),
                        false),
                    "ScansFor" = ARRAY(
                        SELECT scanner.kind
                        FROM (VALUES ('Secrets', 0), ('Pii', 1)) AS scanner(name, kind)
                        WHERE pg_temp.mf_switch(
                            pg_temp.mf_member(
                                pg_temp.mf_member(pg_temp.mf_member(account."Document", 'SensitiveContent'), scanner.name),
                                'Enabled'),
                            false)
                        ORDER BY scanner.kind),
                    "ScreensOutgoingMailFor" = ARRAY(
                        SELECT DISTINCT scanner.kind
                        FROM jsonb_array_elements(pg_temp.mf_list(pg_temp.mf_member(
                            pg_temp.mf_member(account."Document", 'SensitiveContent'),
                            'ScreenOutgoingMailFor'))) AS named(value)
                        JOIN (VALUES ('secrets', 0), ('pii', 1)) AS scanner(name, kind)
                            ON scanner.name = lower(pg_temp.mf_text(named.value))
                        ORDER BY scanner.kind);

                WITH declared AS (
                    SELECT account."Id" AS account_id,
                           pg_temp.mf_member(account."Document", 'SpamClassification') AS classification,
                           CASE WHEN jsonb_array_length(pg_temp.mf_list(pg_temp.mf_member(account."Document", 'Folders'))) = 0
                                THEN '[{"Alias": "Inbox", "SpecialUse": "Inbox"}]'::jsonb
                                ELSE pg_temp.mf_list(pg_temp.mf_member(account."Document", 'Folders'))
                           END AS folders
                    FROM settings_mail_accounts AS account
                    WHERE account."HasReadableSettings"
                ),
                folder AS (
                    SELECT declared.account_id,
                           declared.classification,
                           upper(pg_temp.mf_text(pg_temp.mf_member(entry.value, 'Alias'))) AS alias,
                           role.special_use,
                           pg_temp.mf_switch(pg_temp.mf_member(entry.value, 'Synchronize'), true) AS is_synchronized,
                           pg_temp.mf_switch(pg_temp.mf_member(entry.value, 'VisibleToTools'), true) AS is_visible_to_tools,
                           pg_temp.mf_switch(pg_temp.mf_member(entry.value, 'GenerateEmbeddings'), true) AS generates_embeddings,
                           entry.position
                    FROM declared
                    CROSS JOIN LATERAL jsonb_array_elements(declared.folders) WITH ORDINALITY AS entry(value, position)
                    LEFT JOIN (VALUES ('inbox', 0), ('archive', 1), ('drafts', 2), ('sent', 3), ('junk', 4),
                                      ('trash', 5), ('all', 6), ('flagged', 7), ('important', 8), ('outbox', 9))
                        AS role(name, special_use)
                        ON role.name = lower(pg_temp.mf_text(pg_temp.mf_member(entry.value, 'SpecialUse')))
                ),
                usable AS (
                    SELECT DISTINCT ON (account_id, alias) *
                    FROM folder
                    WHERE alias IS NOT NULL AND length(alias) <= 128
                    ORDER BY account_id, alias, position
                ),
                scanned AS (
                    SELECT usable.account_id, usable.alias
                    FROM usable
                    WHERE CASE
                        WHEN jsonb_typeof(pg_temp.mf_member(usable.classification, 'ScannedFolders')) IN ('array', 'object')
                            THEN EXISTS (
                                SELECT 1
                                FROM jsonb_array_elements(pg_temp.mf_list(pg_temp.mf_member(usable.classification, 'ScannedFolders'))) AS named(value)
                                WHERE upper(pg_temp.mf_text(named.value)) = usable.alias)
                        ELSE usable.special_use IS NOT DISTINCT FROM 0
                    END
                )
                INSERT INTO mail_account_folder_settings
                    ("MailAccountId", "Alias", "SpecialUse", "IsSynchronized", "IsVisibleToTools", "GeneratesEmbeddings", "IsClassifiedForSpam")
                SELECT usable.account_id, usable.alias, usable.special_use, usable.is_synchronized, usable.is_visible_to_tools,
                       usable.generates_embeddings,
                       EXISTS (SELECT 1 FROM scanned WHERE scanned.account_id = usable.account_id AND scanned.alias = usable.alias)
                FROM usable;

                DROP FUNCTION pg_temp.mf_switch(jsonb, boolean);
                DROP FUNCTION pg_temp.mf_text(jsonb);
                DROP FUNCTION pg_temp.mf_list(jsonb);
                DROP FUNCTION pg_temp.mf_member(jsonb, text);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mail_account_folder_settings");

            migrationBuilder.DropColumn(
                name: "ClassifiesSpam",
                table: "settings_mail_accounts");

            migrationBuilder.DropColumn(
                name: "HasReadableSettings",
                table: "settings_mail_accounts");

            migrationBuilder.DropColumn(
                name: "ScansFor",
                table: "settings_mail_accounts");

            migrationBuilder.DropColumn(
                name: "ScreensOutgoingMailFor",
                table: "settings_mail_accounts");

            migrationBuilder.DropColumn(
                name: "SynchronizationMode",
                table: "settings_mail_accounts");
        }
    }
}
