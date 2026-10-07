using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KafkaDemo.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationOutboxLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[dbo].[Transactions]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[Transactions]
                    (
                        [Id] bigint IDENTITY(1,1) NOT NULL,
                        [TransactionNumber] nvarchar(50) NOT NULL,
                        [FromAccount] nvarchar(50) NOT NULL,
                        [ToAccount] nvarchar(50) NOT NULL,
                        [Amount] decimal(18,2) NOT NULL,
                        [Currency] nvarchar(3) NOT NULL,
                        [Status] nvarchar(20) NOT NULL,
                        [CreatedAt] datetime2 NOT NULL,
                        CONSTRAINT [PK_Transactions] PRIMARY KEY ([Id])
                    );
                    CREATE UNIQUE INDEX [IX_Transactions_TransactionNumber]
                        ON [dbo].[Transactions] ([TransactionNumber]);
                END;

                IF OBJECT_ID(N'[dbo].[OutboxMessages]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[OutboxMessages]
                    (
                        [Id] bigint IDENTITY(1,1) NOT NULL,
                        [EventType] nvarchar(100) NOT NULL,
                        [Payload] nvarchar(max) NOT NULL,
                        [Topic] nvarchar(200) NOT NULL,
                        [CreatedAt] datetime2 NOT NULL,
                        [PublishedAt] datetime2 NULL,
                        [RetryCount] int NOT NULL,
                        CONSTRAINT [PK_OutboxMessages] PRIMARY KEY ([Id])
                    );
                END;
                """);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAtUtc",
                table: "OutboxMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseOwner",
                table: "OutboxMessages",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MessageKey",
                table: "OutboxMessages",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_PublishedAt_LeaseExpiresAtUtc_Id",
                table: "OutboxMessages",
                columns: new[] { "PublishedAt", "LeaseExpiresAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_PublishedAt_LeaseExpiresAtUtc_Id",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAtUtc",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "MessageKey",
                table: "OutboxMessages");
        }
    }
}
