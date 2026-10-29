using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Subscribely.Modules.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "billing");

            migrationBuilder.CreateTable(
                name: "plans",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plan_versions",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    monthly_price_cents = table.Column<long>(type: "bigint", nullable: false),
                    annual_price_cents = table.Column<long>(type: "bigint", nullable: false),
                    request_quota_monthly = table.Column<int>(type: "integer", nullable: false),
                    max_seats = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_versions", x => x.id);
                    table.CheckConstraint("ck_plan_versions_limits", "request_quota_monthly > 0 AND max_seats > 0");
                    table.CheckConstraint("ck_plan_versions_prices", "monthly_price_cents >= 0 AND annual_price_cents >= 0");
                    table.ForeignKey(
                        name: "fk_plan_versions_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "billing",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_plan_versions_plan_id_version",
                schema: "billing",
                table: "plan_versions",
                columns: new[] { "plan_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plans_code",
                schema: "billing",
                table: "plans",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plan_versions",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "plans",
                schema: "billing");
        }
    }
}
