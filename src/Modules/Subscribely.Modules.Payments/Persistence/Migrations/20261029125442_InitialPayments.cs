using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Subscribely.Modules.Payments.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.CreateTable(
                name: "charges",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    manual = table.Column<bool>(type: "boolean", nullable: false),
                    gateway_charge_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    failure_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_charges", x => x.id);
                    table.CheckConstraint("ck_charges_amount", "amount_cents > 0");
                });

            migrationBuilder.CreateTable(
                name: "dunning",
                schema: "payments",
                columns: table => new
                {
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    exhausted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dunning", x => x.invoice_id);
                });

            migrationBuilder.CreateTable(
                name: "payment_events",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_methods",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gateway_token = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    brand = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_methods", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_charges_gateway_charge_id",
                schema: "payments",
                table: "charges",
                column: "gateway_charge_id");

            migrationBuilder.CreateIndex(
                name: "ix_charges_idempotency_key",
                schema: "payments",
                table: "charges",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_charges_invoice_id_attempt",
                schema: "payments",
                table: "charges",
                columns: new[] { "invoice_id", "attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_charges_one_pending_per_invoice",
                schema: "payments",
                table: "charges",
                column: "invoice_id",
                unique: true,
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_dunning_next_attempt_at",
                schema: "payments",
                table: "dunning",
                column: "next_attempt_at",
                filter: "exhausted_at IS NULL AND resolved_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_events_provider_provider_event_id",
                schema: "payments",
                table: "payment_events",
                columns: new[] { "provider", "provider_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_methods_tenant_id",
                schema: "payments",
                table: "payment_methods",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "charges",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "dunning",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payment_events",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payment_methods",
                schema: "payments");
        }
    }
}
