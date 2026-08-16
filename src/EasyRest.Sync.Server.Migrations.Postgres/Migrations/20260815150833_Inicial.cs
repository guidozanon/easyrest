using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyRest.Sync.Server.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthFlows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    RedirectUri = table.Column<string>(type: "text", nullable: false),
                    ClientState = table.Column<string>(type: "text", nullable: false),
                    CodeChallenge = table.Column<string>(type: "text", nullable: false),
                    AuthCodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Consumed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthFlows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Rev = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false),
                    Seq = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Invitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    CanReadSecrets = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revoked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invitations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    CanReadSecrets = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Memberships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecretOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanRead = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretOverrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecretValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    Ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    Tag = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretValues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    CanReadSecrets = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revoked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SessionTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RefreshHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AccessExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RefreshExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Revoked = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsServerAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    Disabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeqCounter = table.Column<long>(type: "bigint", nullable: false),
                    WrappedKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthFlows_AuthCodeHash",
                table: "AuthFlows",
                column: "AuthCodeHash");

            migrationBuilder.CreateIndex(
                name: "IX_AuthFlows_State",
                table: "AuthFlows",
                column: "State",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_WorkspaceId_Path",
                table: "Documents",
                columns: new[] { "WorkspaceId", "Path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_WorkspaceId_Seq",
                table: "Documents",
                columns: new[] { "WorkspaceId", "Seq" });

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_TokenHash",
                table: "Invitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_UserId",
                table: "Memberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_WorkspaceId_UserId",
                table: "Memberships",
                columns: new[] { "WorkspaceId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecretOverrides_MembershipId_DocumentId",
                table: "SecretOverrides",
                columns: new[] { "MembershipId", "DocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecretValues_DocumentId_Key",
                table: "SecretValues",
                columns: new[] { "DocumentId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTokens_TokenHash",
                table: "ServiceTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionTokens_AccessHash",
                table: "SessionTokens",
                column: "AccessHash");

            migrationBuilder.CreateIndex(
                name: "IX_SessionTokens_RefreshHash",
                table: "SessionTokens",
                column: "RefreshHash");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Provider_Subject",
                table: "Users",
                columns: new[] { "Provider", "Subject" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthFlows");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "Invitations");

            migrationBuilder.DropTable(
                name: "Memberships");

            migrationBuilder.DropTable(
                name: "SecretOverrides");

            migrationBuilder.DropTable(
                name: "SecretValues");

            migrationBuilder.DropTable(
                name: "ServiceTokens");

            migrationBuilder.DropTable(
                name: "SessionTokens");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Workspaces");
        }
    }
}
