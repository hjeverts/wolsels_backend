using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wolsels.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Blends",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Basis = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    BasisComponentIndex = table.Column<int>(type: "INTEGER", nullable: true),
                    InputWeightGrams = table.Column<double>(type: "REAL", nullable: false),
                    SpunOn = table.Column<string>(type: "TEXT", nullable: true),
                    OutputWeightGrams = table.Column<double>(type: "REAL", nullable: true),
                    OutputLengthMeters = table.Column<double>(type: "REAL", nullable: true),
                    YarnWeight = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    WrapsPerInch = table.Column<int>(type: "INTEGER", nullable: true),
                    PlyCount = table.Column<int>(type: "INTEGER", nullable: true),
                    PlyMethod = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SpinDirection = table.Column<string>(type: "TEXT", maxLength: 1, nullable: true),
                    IsDyed = table.Column<bool>(type: "INTEGER", nullable: false),
                    DyeColor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DyeColorHex = table.Column<string>(type: "TEXT", maxLength: 7, nullable: true),
                    DyeProduct = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DyeMethod = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Blends", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Wools",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    WoolType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ColorHex = table.Column<string>(type: "TEXT", maxLength: 7, nullable: true),
                    WeightGrams = table.Column<double>(type: "REAL", nullable: false),
                    Origin = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wools", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Animals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Species = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Breed = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Color = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    BirthDate = table.Column<string>(type: "TEXT", nullable: true),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: true),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Animals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Animals_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "BlendComponents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BlendId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    WoolId = table.Column<int>(type: "INTEGER", nullable: false),
                    Ratio = table.Column<double>(type: "REAL", nullable: false),
                    WeightGrams = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlendComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlendComponents_Blends_BlendId",
                        column: x => x.BlendId,
                        principalTable: "Blends",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BlendComponents_Wools_WoolId",
                        column: x => x.WoolId,
                        principalTable: "Wools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Harvests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Date = table.Column<string>(type: "TEXT", nullable: false),
                    AnimalId = table.Column<int>(type: "INTEGER", nullable: true),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: true),
                    WeightGrams = table.Column<double>(type: "REAL", nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Grade = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    WoolId = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Harvests", x => x.Id);
                    table.CheckConstraint("CK_Harvest_AnimalOrLocation", "(\"AnimalId\" IS NOT NULL AND \"LocationId\" IS NULL) OR (\"AnimalId\" IS NULL AND \"LocationId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Harvests_Animals_AnimalId",
                        column: x => x.AnimalId,
                        principalTable: "Animals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Harvests_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Harvests_Wools_WoolId",
                        column: x => x.WoolId,
                        principalTable: "Wools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Animals_LocationId",
                table: "Animals",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_BlendComponents_BlendId",
                table: "BlendComponents",
                column: "BlendId");

            migrationBuilder.CreateIndex(
                name: "IX_BlendComponents_WoolId",
                table: "BlendComponents",
                column: "WoolId");

            migrationBuilder.CreateIndex(
                name: "IX_Harvests_AnimalId",
                table: "Harvests",
                column: "AnimalId");

            migrationBuilder.CreateIndex(
                name: "IX_Harvests_Date",
                table: "Harvests",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Harvests_LocationId",
                table: "Harvests",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Harvests_WoolId",
                table: "Harvests",
                column: "WoolId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlendComponents");

            migrationBuilder.DropTable(
                name: "Harvests");

            migrationBuilder.DropTable(
                name: "Blends");

            migrationBuilder.DropTable(
                name: "Animals");

            migrationBuilder.DropTable(
                name: "Wools");

            migrationBuilder.DropTable(
                name: "Locations");
        }
    }
}
