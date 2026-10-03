using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorriorVex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchVocabulary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The words in the search index, read by "did you mean" suggestions. A view over NoteSearch; nothing is stored.
            migrationBuilder.Sql("CREATE VIRTUAL TABLE NoteSearchVocab USING fts5vocab('NoteSearch', 'row');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE NoteSearchVocab;");
        }
    }
}
