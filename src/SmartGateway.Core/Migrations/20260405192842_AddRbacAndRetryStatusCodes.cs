using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGateway.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddRbacAndRetryStatusCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RetryOnStatusCodes",
                table: "ResiliencePolicies",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                defaultValue: "502,503,504");

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "ApiKeys",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "admin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetryOnStatusCodes",
                table: "ResiliencePolicies");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "ApiKeys");
        }
    }
}
