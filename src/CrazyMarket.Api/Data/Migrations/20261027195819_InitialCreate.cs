using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrazyMarket.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BankIdSub = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BirthDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    IsAdult = table.Column<bool>(type: "INTEGER", nullable: false),
                    TotpSecret = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IsBanned = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PohodaId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Ean = table.Column<string>(type: "TEXT", maxLength: 13, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BasePrice = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    NightPrice = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    WeightGrams = table.Column<int>(type: "INTEGER", nullable: false),
                    ToleranceGrams = table.Column<int>(type: "INTEGER", nullable: false),
                    StockCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsAgeRestricted = table.Column<bool>(type: "INTEGER", nullable: false),
                    EslMacAddress = table.Column<string>(type: "TEXT", maxLength: 17, nullable: true),
                    LastEslImageBase64 = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoreSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnteredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExitedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CurrentState = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreSessions_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoreSessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    IsNightPricing = table.Column<bool>(type: "INTEGER", nullable: false),
                    PaymentStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PaymentReference = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    WeightValidation = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ExpectedWeightGrams = table.Column<int>(type: "INTEGER", nullable: false),
                    MeasuredWeightGrams = table.Column<int>(type: "INTEGER", nullable: true),
                    PohodaXmlExported = table.Column<bool>(type: "INTEGER", nullable: false),
                    PohodaExportedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_StoreSessions_StoreSessionId",
                        column: x => x.StoreSessionId,
                        principalTable: "StoreSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Ean = table.Column<string>(type: "TEXT", maxLength: 13, nullable: false),
                    ProductName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    UnitWeightGrams = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitToleranceGrams = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_BankIdSub",
                table: "Customers",
                column: "BankIdSub",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductId",
                table: "OrderItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerId",
                table: "Orders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PohodaXmlExported_PaymentStatus",
                table: "Orders",
                columns: new[] { "PohodaXmlExported", "PaymentStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreSessionId",
                table: "Orders",
                column: "StoreSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Ean",
                table: "Products",
                column: "Ean",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_EslMacAddress",
                table: "Products",
                column: "EslMacAddress",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_PohodaId",
                table: "Products",
                column: "PohodaId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreSessions_CurrentState_ExitedAt",
                table: "StoreSessions",
                columns: new[] { "CurrentState", "ExitedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreSessions_CustomerId",
                table: "StoreSessions",
                column: "CustomerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "StoreSessions");

            migrationBuilder.DropTable(
                name: "Customers");
        }
    }
}
