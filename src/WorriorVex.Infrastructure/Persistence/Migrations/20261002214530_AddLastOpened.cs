using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorriorVex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLastOpened : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastOpenedAt",
                table: "Nodes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_LastOpenedAt",
                table: "Nodes",
                column: "LastOpenedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Nodes_LastOpenedAt",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "LastOpenedAt",
                table: "Nodes");
        }
    }
}
