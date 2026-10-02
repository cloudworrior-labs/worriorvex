using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorriorVex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Full-text index over the notes. It is derived data, filled and kept up to date by the
            // application (the text is the note's HTML without its markup), and rebuilt at startup
            // when incomplete. Accents are ignored when matching.
            migrationBuilder.Sql(
                """
                CREATE VIRTUAL TABLE NoteSearch USING fts5(
                    NoteId UNINDEXED,
                    Title,
                    Body,
                    Tags,
                    tokenize = 'unicode61 remove_diacritics 2'
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE NoteSearch;");
        }
    }
}
