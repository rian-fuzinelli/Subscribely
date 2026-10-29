using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Subscribely.Modules.Metering.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMetering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "metering");

            migrationBuilder.CreateTable(
                name: "usage_periods",
                schema: "metering",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "char(7)", nullable: false),
                    requests_count = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_periods", x => new { x.tenant_id, x.period });
                    table.CheckConstraint("ck_usage_periods_count", "requests_count >= 0");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "usage_periods",
                schema: "metering");
        }
    }
}
