using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Subscribely.Modules.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Subscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscriptions",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interval = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    trial_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    trial_used = table.Column<bool>(type: "boolean", nullable: false),
                    trial_ending_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    billing_anchor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    current_period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    current_period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancel_at_period_end = table.Column<bool>(type: "boolean", nullable: false),
                    pending_plan_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pending_interval = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    suspended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscriptions_plan_versions_pending_plan_version_id",
                        column: x => x.pending_plan_version_id,
                        principalSchema: "billing",
                        principalTable: "plan_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscriptions_plan_versions_plan_version_id",
                        column: x => x.plan_version_id,
                        principalSchema: "billing",
                        principalTable: "plan_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_pending_plan_version_id",
                schema: "billing",
                table: "subscriptions",
                column: "pending_plan_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_plan_version_id",
                schema: "billing",
                table: "subscriptions",
                column: "plan_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_status_current_period_end",
                schema: "billing",
                table: "subscriptions",
                columns: new[] { "status", "current_period_end" });

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_tenant_id",
                schema: "billing",
                table: "subscriptions",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscriptions",
                schema: "billing");
        }
    }
}
