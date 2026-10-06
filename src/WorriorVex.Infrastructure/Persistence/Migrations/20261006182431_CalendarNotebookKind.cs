using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorriorVex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CalendarNotebookKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notebooks_Kind",
                table: "Notebooks");

            // The notebook that 0.6.0 made for the calendar was an ordinary one named "Calendar". The one
            // holding the most dated notes (the oldest when tied) becomes the built-in Calendar notebook.
            migrationBuilder.Sql(
                """
                UPDATE Notebooks SET Kind = 2, SortOrder = 2147483647
                WHERE Id = (
                    SELECT nb.Id FROM Notebooks nb
                    WHERE nb.Kind = 0 AND nb.Name = 'Calendar'
                      AND EXISTS (SELECT 1 FROM Nodes n WHERE n.NotebookId = nb.Id AND n.CalendarDate IS NOT NULL)
                    ORDER BY (SELECT COUNT(*) FROM Nodes n WHERE n.NotebookId = nb.Id AND n.CalendarDate IS NOT NULL) DESC, nb.CreatedAt
                    LIMIT 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Notebooks_Kind",
                table: "Notebooks",
                column: "Kind",
                unique: true,
                filter: "\"Kind\" IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Notebooks SET Kind = 0 WHERE Kind = 2;");

            migrationBuilder.DropIndex(
                name: "IX_Notebooks_Kind",
                table: "Notebooks");

            migrationBuilder.CreateIndex(
                name: "IX_Notebooks_Kind",
                table: "Notebooks",
                column: "Kind",
                unique: true,
                filter: "\"Kind\" = 1");
        }
    }
}
