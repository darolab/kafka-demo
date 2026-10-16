using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KafkaDemo.Migrations
{
    /// <inheritdoc />
    public partial class RestrictSupportedRoleNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Roles_Name_Supported",
                table: "Roles",
                sql: "[Name] IN ('Admin', 'User')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Roles_Name_Supported",
                table: "Roles");
        }
    }
}
