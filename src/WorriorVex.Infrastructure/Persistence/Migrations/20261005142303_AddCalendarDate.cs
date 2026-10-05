using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorriorVex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "CalendarDate",
                table: "Nodes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_CalendarDate",
                table: "Nodes",
                column: "CalendarDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Nodes_CalendarDate",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "CalendarDate",
                table: "Nodes");
        }
    }
}
