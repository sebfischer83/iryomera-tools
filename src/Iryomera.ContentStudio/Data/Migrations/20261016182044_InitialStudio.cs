using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Iryomera.ContentStudio.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialStudio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Audits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScopeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Target = table.Column<string>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: false),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: false),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Audits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Imports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalJson = table.Column<string>(type: "TEXT", nullable: false),
                    WorkspaceJson = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Imports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordId = table.Column<string>(type: "TEXT", nullable: false),
                    Digest = table.Column<string>(type: "TEXT", nullable: false),
                    Permitted = table.Column<bool>(type: "INTEGER", nullable: false),
                    Reviewer = table.Column<string>(type: "TEXT", nullable: false),
                    Evidence = table.Column<string>(type: "TEXT", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    PackageJson = table.Column<string>(type: "TEXT", nullable: false),
                    CatalogsJson = table.Column<string>(type: "TEXT", nullable: false),
                    SourceImportId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceRecordIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    RightsConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    RightsReviewer = table.Column<string>(type: "TEXT", nullable: false),
                    RightsNote = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Definitions",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    NameEn = table.Column<string>(type: "TEXT", nullable: false),
                    NameDe = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Definitions", x => new { x.ProjectId, x.Id });
                    table.ForeignKey(
                        name: "FK_Definitions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Audits_ScopeId_Id",
                table: "Audits",
                columns: new[] { "ScopeId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Definitions_ProjectId_Kind_Status",
                table: "Definitions",
                columns: new[] { "ProjectId", "Kind", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Imports_Sha256",
                table: "Imports",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_ProjectId_RecordId_Id",
                table: "Permissions",
                columns: new[] { "ProjectId", "RecordId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Audits");

            migrationBuilder.DropTable(
                name: "Definitions");

            migrationBuilder.DropTable(
                name: "Imports");

            migrationBuilder.DropTable(
                name: "Permissions");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
