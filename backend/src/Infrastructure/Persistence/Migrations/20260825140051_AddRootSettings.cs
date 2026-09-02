// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MailFathom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// The row is provisioned by the migration rather than by the first write, so the layer the host composes its
    /// settings over exists on every database this build runs against. An empty document contributes no configuration
    /// key at all, which is the state a deployment that has persisted nothing is in — and is exactly what the layer
    /// means by a key it does not carry: inherit from the source beneath it.
    /// </remarks>
    public partial class AddRootSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "settings_root",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Document = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settings_root", x => x.Id);
                    table.CheckConstraint("ck_settings_root_singleton", "\"Id\" = 1");
                });

            // Guarded rather than unconditional so that re-applying the migration set to a database that already
            // carries the row is the no-op an operator expects, and the check constraint keeps a second one out.
            migrationBuilder.Sql(
                """
                INSERT INTO settings_root ("Id", "Document", "Version", "CreatedAt", "UpdatedAt")
                SELECT 1, '{}'::jsonb, 1, now(), now()
                WHERE NOT EXISTS (SELECT 1 FROM settings_root);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "settings_root");
        }
    }
}
