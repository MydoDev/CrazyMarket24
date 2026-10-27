using System.Security.Cryptography;
using CrazyMarket.Api.Abstractions;
using CrazyMarket.Api.Domain;
using CrazyMarket.Api.Domain.Entities;
using CrazyMarket.Api.Domain.Enums;
using CrazyMarket.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CrazyMarket.Api.Data;

/// <summary>
/// Při startu aplikuje migrace a do prázdné DB vloží vývojová data
/// (4 zásobovací účty: 2 ExternalVendor + 1 InternalStaff + 1 Admin, 10 produktů s vyrenderovanými E-ink cenovkami,
/// 2 zákazníci, 1 ukázkový závoz). Opakované spuštění data neduplikuje.
/// </summary>
public static class DbInitializer
{
    public static readonly Guid SupplierPrazdrojId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
    public static readonly Guid SupplierSumavaId = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");
    public static readonly Guid StaffBrigadnikId = Guid.Parse("dddddddd-4444-4444-4444-444444444444");
    public static readonly Guid AdminOwnerId = Guid.Parse("eeeeeeee-5555-5555-5555-555555555555");
    public static readonly Guid SampleDeliveryId = Guid.Parse("cccccccc-3333-3333-3333-333333333333");

    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CrazyMarketDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DbInitializer));
        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var pricing = scope.ServiceProvider.GetRequiredService<PricingService>();
        var eslGateway = scope.ServiceProvider.GetRequiredService<IEslGateway>();

        await db.Database.MigrateAsync(ct);

        var utcNow = time.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(time.GetLocalNow().Date);

        if (!await db.Suppliers.AnyAsync(ct))
        {
            db.Suppliers.AddRange(CreateSuppliers());
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seed: vloženi dodavatelé a interní zásobovací účty.");
        }
        else
        {
            // Doplnění nových účtů InternalStaff a Admin, pokud DB vznikla v dřívějším sprintu
            if (!await db.Suppliers.AnyAsync(s => s.Id == StaffBrigadnikId, ct))
            {
                db.Suppliers.Add(new Supplier
                {
                    Id = StaffBrigadnikId,
                    CompanyName = "Brigádník DPP – Globální závoz (Makro)",
                    Ico = "99000001",
                    Role = SupplierRole.InternalStaff,
                    TotpSecret = GenerateTotpSecret(),
                    AllowDynamicPricing = true
                });
            }

            if (!await db.Suppliers.AnyAsync(s => s.Id == AdminOwnerId, ct))
            {
                db.Suppliers.Add(new Supplier
                {
                    Id = AdminOwnerId,
                    CompanyName = "Majitel prodejny (Admin)",
                    Ico = "99000002",
                    Role = SupplierRole.Admin,
                    TotpSecret = GenerateTotpSecret(),
                    AllowDynamicPricing = true
                });
            }

            await db.SaveChangesAsync(ct);
        }

        if (!await db.Products.AnyAsync(ct))
        {
            db.Products.AddRange(CreateProducts(utcNow));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seed: vloženy testovací produkty.");
        }

        // Vyrenderujeme výchozí 1-bit E-ink cenovky pro produkty, které ještě nemají LastEslImageBase64
        var productsWithoutEsl = await db.Products.Where(p => p.LastEslImageBase64 == null).ToListAsync(ct);
        if (productsWithoutEsl.Count > 0)
        {
            foreach (var prod in productsWithoutEsl)
            {
                var activePrice = pricing.GetActivePrice(prod);
                await eslGateway.RenderAndPushTagAsync(prod, activePrice, ct);
            }
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seed: vyrenderováno {Count} výchozích 1-bit E-ink cenovek (296x128 px).", productsWithoutEsl.Count);
        }

        if (!await db.Customers.AnyAsync(ct))
        {
            db.Customers.AddRange(CreateCustomers(today));
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seed: vloženi testovací zákazníci.");
        }

        if (!await db.StockDeliveries.AnyAsync(ct))
        {
            var products = await db.Products.OrderBy(p => p.Id).Take(2).ToListAsync(ct);
            if (products.Count == 2)
            {
                db.StockDeliveries.Add(new StockDelivery
                {
                    Id = SampleDeliveryId,
                    SupplierId = SupplierSumavaId,
                    DeliveredAt = utcNow.AddHours(-2),
                    PohodaSynced = false,
                    Items =
                    [
                        new StockDeliveryItem
                        {
                            ProductId = products[0].Id,
                            Quantity = 24,
                            ExpirationDate = today.AddMonths(6)
                        },
                        new StockDeliveryItem
                        {
                            ProductId = products[1].Id,
                            Quantity = 50,
                            ExpirationDate = today.AddDays(2)
                        }
                    ]
                });
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Seed: vložen ukázkový závoz zboží (StockDelivery).");
            }
        }
    }

    private static IEnumerable<Supplier> CreateSuppliers()
    {
        yield return new Supplier
        {
            Id = SupplierSumavaId,
            CompanyName = "Pekárna a farma Šumava s.r.o.",
            Ico = "26098765",
            Role = SupplierRole.ExternalVendor,
            TotpSecret = GenerateTotpSecret(),
            AllowDynamicPricing = true
        };

        yield return new Supplier
        {
            Id = SupplierPrazdrojId,
            CompanyName = "Plzeňský Prazdroj, a.s.",
            Ico = "45357366",
            Role = SupplierRole.ExternalVendor,
            TotpSecret = GenerateTotpSecret(),
            AllowDynamicPricing = false
        };

        yield return new Supplier
        {
            Id = StaffBrigadnikId,
            CompanyName = "Brigádník DPP – Globální závoz (Makro)",
            Ico = "99000001",
            Role = SupplierRole.InternalStaff,
            TotpSecret = GenerateTotpSecret(),
            AllowDynamicPricing = true
        };

        yield return new Supplier
        {
            Id = AdminOwnerId,
            CompanyName = "Majitel prodejny (Admin)",
            Ico = "99000002",
            Role = SupplierRole.Admin,
            TotpSecret = GenerateTotpSecret(),
            AllowDynamicPricing = true
        };
    }

    private static IEnumerable<Product> CreateProducts(DateTime utcNow)
    {
        (string Name, int Grams, int Tol, decimal Day, decimal Night, decimal? Action, DateTime? ActionUntil, int Points, int Stock, bool Adult, Guid? SupplierId)[] data =
        [
            ("Red Bull Energy Drink 250 ml",     250, 15,  39.90m,  44.90m,  34.90m, utcNow.AddDays(3), 4,  48, false, SupplierPrazdrojId),
            ("Rohlík tukový 43 g",                43, 10,   3.90m,   4.90m,    null,              null, 1, 120, false, SupplierSumavaId),
            ("Pilsner Urquell 12° 0,5 l plech",  500, 15,  34.90m,  39.90m,    null,              null, 3,  72, true,  SupplierPrazdrojId),
            ("Domácí vejce M 10 ks",             600, 30,  59.90m,  64.90m,  49.90m, utcNow.AddDays(2), 6,  30, false, SupplierSumavaId),
            ("Mléko polotučné 1,5 % 1 l",       1030, 20,  24.90m,  27.90m,    null,              null, 2,  40, false, SupplierSumavaId),
            ("Chléb Šumava 1200 g",             1200, 40,  54.90m,  59.90m,    null,              null, 5,  15, false, SupplierSumavaId),
            ("Coca-Cola 0,5 l PET",              540, 15,  32.90m,  36.90m,    null,              null, 3,  60, false, SupplierPrazdrojId),
            ("Becherovka Original 0,5 l",        950, 20, 249.00m, 269.00m,    null,              null, 25, 12, true,  SupplierPrazdrojId),
            ("Bohemia Chips solené 60 g",         65, 10,  29.90m,  34.90m,    null,              null, 3,  35, false, SupplierSumavaId),
            ("Snickers 50 g",                     52, 10,  19.90m,  22.90m,    null,              null, 2,  80, false, SupplierSumavaId),
        ];

        return data.Select((p, i) => new Product
        {
            PohodaId = $"{1001 + i}",
            SupplierId = p.SupplierId,
            Ean = Ean.WithCheckDigit($"2990000000{i + 1:00}"),
            Name = p.Name,
            WeightGrams = p.Grams,
            ToleranceGrams = p.Tol,
            BasePrice = p.Day,
            NightPrice = p.Night,
            ActionPrice = p.Action,
            ActionValidUntil = p.ActionUntil,
            BonusPointsReward = p.Points,
            StockCount = p.Stock,
            IsAgeRestricted = p.Adult,
            EslMacAddress = $"CM:24:00:00:00:{i + 1:X2}",
        });
    }

    private static IEnumerable<Customer> CreateCustomers(DateOnly today)
    {
        var adultBirth = new DateOnly(1990, 5, 14);
        var minorBirth = today.AddYears(-16).AddDays(-30);

        yield return new Customer
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            BankIdSub = "mock-bankid-jan-novak",
            FullName = "Jan Novák",
            BirthDate = adultBirth,
            IsAdult = true,
            LoyaltyPoints = 150,
            TotpSecret = GenerateTotpSecret(),
        };

        yield return new Customer
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            BankIdSub = "mock-bankid-tereza-mala",
            FullName = "Tereza Malá",
            BirthDate = minorBirth,
            IsAdult = false,
            LoyaltyPoints = 25,
            TotpSecret = GenerateTotpSecret(),
        };
    }

    /// <summary>160bitové náhodné tajemství v Base32 (RFC 4648, bez paddingu) – formát kompatibilní s TOTP aplikacemi.</summary>
    public static string GenerateTotpSecret()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = RandomNumberGenerator.GetBytes(20);
        var chars = new char[32];
        int buffer = 0, bitsLeft = 0, index = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                chars[index++] = alphabet[(buffer >> (bitsLeft - 5)) & 31];
                bitsLeft -= 5;
            }
        }
        return new string(chars);
    }
}
