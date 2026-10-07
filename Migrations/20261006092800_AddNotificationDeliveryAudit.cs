using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KafkaDemo.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDeliveryAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationDeliveryAudits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InstanceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Result = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeliveredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorDetail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveryAudits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryAudits_NotificationId_InstanceId",
                table: "NotificationDeliveryAudits",
                columns: new[] { "NotificationId", "InstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveryAudits_UserId_CreatedAtUtc",
                table: "NotificationDeliveryAudits",
                columns: new[] { "UserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationDeliveryAudits");
        }
    }
}
