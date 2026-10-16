using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wolsels.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WoolPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WoolPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    WoolId = table.Column<int>(type: "INTEGER", nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Thumbnail = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WoolPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WoolPhotos_Wools_WoolId",
                        column: x => x.WoolId,
                        principalTable: "Wools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WoolPhotos_HouseholdId",
                table: "WoolPhotos",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_WoolPhotos_WoolId",
                table: "WoolPhotos",
                column: "WoolId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WoolPhotos");
        }
    }
}
