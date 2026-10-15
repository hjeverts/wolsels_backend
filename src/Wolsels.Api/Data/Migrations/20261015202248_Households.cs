using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wolsels.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Households : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Households",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Households", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HouseholdInvites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<string>(type: "TEXT", nullable: false),
                    UsedAt = table.Column<string>(type: "TEXT", nullable: true),
                    UsedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdInvites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseholdInvites_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HouseholdMembers",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    JoinedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdMembers", x => new { x.UserId, x.HouseholdId });
                    table.ForeignKey(
                        name: "FK_HouseholdMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HouseholdMembers_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddColumn<int>(
                name: "HouseholdId",
                table: "Wools",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HouseholdId",
                table: "Locations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HouseholdId",
                table: "Harvests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HouseholdId",
                table: "Blends",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HouseholdId",
                table: "Animals",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Bestaande gegevens bewaren: elk bestaand account krijgt een eigen huishouden en zijn gegevens
            // gaan daarheen. Dit moet gebeuren vóórdat de kolom OwnerId verdwijnt.
            migrationBuilder.Sql(@"
                INSERT INTO Households (Name, CreatedByUserId, CreatedAt)
                SELECT 'Huishouden van ' || COALESCE(Email, UserName, Id), Id, strftime('%Y-%m-%d %H:%M:%S', 'now') || '+00:00'
                FROM AspNetUsers;

                INSERT INTO HouseholdMembers (UserId, HouseholdId, JoinedAt)
                SELECT CreatedByUserId, Id, CreatedAt FROM Households;

                UPDATE Wools     SET HouseholdId = COALESCE((SELECT h.Id FROM Households h WHERE h.CreatedByUserId = Wools.OwnerId), 0);
                UPDATE Blends    SET HouseholdId = COALESCE((SELECT h.Id FROM Households h WHERE h.CreatedByUserId = Blends.OwnerId), 0);
                UPDATE Locations SET HouseholdId = COALESCE((SELECT h.Id FROM Households h WHERE h.CreatedByUserId = Locations.OwnerId), 0);
                UPDATE Animals   SET HouseholdId = COALESCE((SELECT h.Id FROM Households h WHERE h.CreatedByUserId = Animals.OwnerId), 0);
                UPDATE Harvests  SET HouseholdId = COALESCE((SELECT h.Id FROM Households h WHERE h.CreatedByUserId = Harvests.OwnerId), 0);
            ");

            migrationBuilder.DropIndex(
                name: "IX_Wools_OwnerId",
                table: "Wools");

            migrationBuilder.DropIndex(
                name: "IX_Locations_OwnerId",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Harvests_OwnerId",
                table: "Harvests");

            migrationBuilder.DropIndex(
                name: "IX_Blends_OwnerId",
                table: "Blends");

            migrationBuilder.DropIndex(
                name: "IX_Animals_OwnerId",
                table: "Animals");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Wools");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Harvests");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Blends");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Animals");

            migrationBuilder.CreateIndex(
                name: "IX_Wools_HouseholdId",
                table: "Wools",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_HouseholdId",
                table: "Locations",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_Harvests_HouseholdId",
                table: "Harvests",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_Blends_HouseholdId",
                table: "Blends",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_Animals_HouseholdId",
                table: "Animals",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdInvites_HouseholdId",
                table: "HouseholdInvites",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdInvites_TokenHash",
                table: "HouseholdInvites",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdMembers_HouseholdId",
                table: "HouseholdMembers",
                column: "HouseholdId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HouseholdInvites");

            migrationBuilder.DropTable(
                name: "HouseholdMembers");

            migrationBuilder.DropTable(
                name: "Households");

            migrationBuilder.DropIndex(
                name: "IX_Wools_HouseholdId",
                table: "Wools");

            migrationBuilder.DropIndex(
                name: "IX_Locations_HouseholdId",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Harvests_HouseholdId",
                table: "Harvests");

            migrationBuilder.DropIndex(
                name: "IX_Blends_HouseholdId",
                table: "Blends");

            migrationBuilder.DropIndex(
                name: "IX_Animals_HouseholdId",
                table: "Animals");

            migrationBuilder.DropColumn(
                name: "HouseholdId",
                table: "Wools");

            migrationBuilder.DropColumn(
                name: "HouseholdId",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "HouseholdId",
                table: "Harvests");

            migrationBuilder.DropColumn(
                name: "HouseholdId",
                table: "Blends");

            migrationBuilder.DropColumn(
                name: "HouseholdId",
                table: "Animals");

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "Wools",
                type: "TEXT",
                maxLength: 450,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "Locations",
                type: "TEXT",
                maxLength: 450,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "Harvests",
                type: "TEXT",
                maxLength: 450,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "Blends",
                type: "TEXT",
                maxLength: 450,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "Animals",
                type: "TEXT",
                maxLength: 450,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Wools_OwnerId",
                table: "Wools",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_OwnerId",
                table: "Locations",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Harvests_OwnerId",
                table: "Harvests",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Blends_OwnerId",
                table: "Blends",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Animals_OwnerId",
                table: "Animals",
                column: "OwnerId");
        }
    }
}
