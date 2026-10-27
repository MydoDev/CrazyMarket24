using CrazyMarket.Api.Abstractions;
using CrazyMarket.Api.Contracts;
using CrazyMarket.Api.Data;
using CrazyMarket.Api.Domain.Entities;
using CrazyMarket.Api.Domain.Enums;
using CrazyMarket.Api.Hubs;
using CrazyMarket.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CrazyMarket.Api.Endpoints;

public static class SupplierAndPohodaEndpoints
{
    public static void MapSupplierAndPohodaEndpoints(this IEndpointRouteBuilder app)
    {
        var suppliers = app.MapGroup("/api/suppliers").WithTags("Suppliers & Deliveries");
        suppliers.MapGet("/", GetSuppliers).WithName("GetSuppliers");
        suppliers.MapGet("/{supplierId:guid}/products", GetSupplierProducts).WithName("GetSupplierScopedProducts");
        suppliers.MapPut("/{supplierId:guid}/products/{productId:int}/action-price", SetSupplierActionPrice)
            .WithName("SetSupplierProductActionPrice");

        var deliveries = app.MapGroup("/api/deliveries").WithTags("Suppliers & Deliveries");
        deliveries.MapGet("/", GetDeliveries).WithName("GetStockDeliveries");
        deliveries.MapPost("/", CreateDelivery).WithName("CreateStockDelivery");

        var pohoda = app.MapGroup("/api/pohoda").WithTags("Pohoda XML Export");
        pohoda.MapPost("/export-delivery/{deliveryId:guid}", ExportDeliveryToPohoda).WithName("ExportDeliveryToPohoda");
        pohoda.MapPost("/export-order/{orderId:guid}", ExportOrderToPohoda).WithName("ExportOrderToPohoda");
    }

    private static async Task<Ok<List<SupplierDto>>> GetSuppliers(CrazyMarketDbContext db, CancellationToken ct)
    {
        var list = await db.Suppliers
            .AsNoTracking()
            .OrderBy(s => s.Role)
            .ThenBy(s => s.CompanyName)
            .ToListAsync(ct);

        return TypedResults.Ok(list.Select(SupplierDto.From).ToList());
    }

    /// <summary>
    /// Vrátí produkty podle role přihlášeného účtu:
    /// - <see cref="SupplierRole.ExternalVendor"/> vidí pouze produkty se svým <c>SupplierId</c>,
    /// - <see cref="SupplierRole.InternalStaff"/> a <see cref="SupplierRole.Admin"/> vidí všechny produkty v databázi.
    /// </summary>
    private static async Task<Results<Ok<List<ProductDto>>, NotFound>> GetSupplierProducts(
        Guid supplierId,
        CrazyMarketDbContext db,
        PricingService pricing,
        CancellationToken ct)
    {
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId, ct);
        if (supplier is null)
            return TypedResults.NotFound();

        var query = db.Products.AsNoTracking();
        if (!supplier.CanManageAllProducts)
        {
            query = query.Where(p => p.SupplierId == supplier.Id);
        }

        var products = await query.OrderBy(p => p.Name).ToListAsync(ct);
        return TypedResults.Ok(products.Select(p => ProductDto.From(p, pricing)).ToList());
    }

    /// <summary>
    /// Nastaví nebo vypne akční cenu (<c>ActionPrice</c> – sleva před expirací) a okamžitě překreslí 1-bit E-ink cenovku.
    /// </summary>
    private static async Task<Results<Ok<SetActionPriceResponse>, NotFound, ProblemHttpResult, BadRequest<ProblemDetails>>> SetSupplierActionPrice(
        Guid supplierId,
        int productId,
        SetActionPriceRequest request,
        CrazyMarketDbContext db,
        PricingService pricing,
        IEslGateway eslGateway,
        IHubContext<StoreHub, IStoreHubClient> hub,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId, ct);
        if (supplier is null)
            return TypedResults.NotFound();

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null)
            return TypedResults.NotFound();

        if (!supplier.CanManageAllProducts && product.SupplierId != supplier.Id)
        {
            return TypedResults.Problem(
                title: "Přístup odepřen",
                detail: "Externí dodavatel může upravovat pouze své vlastní produkty.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (!supplier.CanManageAllProducts && !supplier.AllowDynamicPricing)
        {
            return TypedResults.Problem(
                title: "Dynamické přeceňování není povoleno",
                detail: $"Dodavatel '{supplier.CompanyName}' nemá povoleno dynamické akční přeceňování.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (request.ActionPrice is <= 0m)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Neplatná akční cena",
                Detail = "Akční cena musí být kladná (nebo null pro vypnutí akce).",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        if (request.ActionPrice is null)
        {
            product.ActionPrice = null;
            product.ActionValidUntil = null;
        }
        else
        {
            product.ActionPrice = decimal.Round(request.ActionPrice.Value, 2);
            product.ActionValidUntil = utcNow.AddDays(request.ValidDays is > 0 ? request.ValidDays.Value : 3);
        }

        var activePrice = pricing.GetActivePrice(product);
        var eslResult = await eslGateway.RenderAndPushTagAsync(product, activePrice, ct);

        await db.SaveChangesAsync(ct);

        var productDto = ProductDto.From(product, pricing);
        var eslPreview = new EslPreviewDto(
            product.Id,
            product.Ean,
            product.Name,
            product.EslMacAddress,
            eslResult.ActivePrice,
            eslResult.BannerText,
            eslResult.PushedToGateway,
            eslResult.Base64Png);

        await hub.Clients.All.ProductChanged(productDto);
        await hub.Clients.All.EslTagUpdated(eslPreview);

        return TypedResults.Ok(new SetActionPriceResponse(productDto, eslPreview));
    }

    private static async Task<Ok<List<StockDeliveryDto>>> GetDeliveries(CrazyMarketDbContext db, CancellationToken ct)
    {
        var list = await db.StockDeliveries
            .AsNoTracking()
            .Include(d => d.Supplier)
            .Include(d => d.Items)
            .OrderByDescending(d => d.DeliveredAt)
            .ToListAsync(ct);

        var dtos = list.Select(d => new StockDeliveryDto(
            d.Id,
            d.SupplierId,
            d.Supplier.CompanyName,
            d.Supplier.Role,
            d.DeliveredAt,
            d.PohodaSynced,
            d.PohodaSyncedAt,
            PohodaXmlPath: null,
            d.Items.Sum(i => i.Quantity))).ToList();

        return TypedResults.Ok(dtos);
    }

    /// <summary>
    /// Zaeviduje nový závoz (od ExternalVendor pro vlastní položky, nebo od InternalStaff / Admin pro libovolné položky),
    /// navýší stav skladu produktů a automaticky vygeneruje POHODA XML příjemku do <c>/pohoda-export/</c>.
    /// </summary>
    private static async Task<Results<Created<StockDeliveryDto>, BadRequest<ProblemDetails>, NotFound<ProblemDetails>, ProblemHttpResult>> CreateDelivery(
        CreateStockDeliveryRequest request,
        CrazyMarketDbContext db,
        PricingService pricing,
        IPohodaClient pohodaClient,
        IHubContext<StoreHub, IStoreHubClient> hub,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(i => i.Quantity <= 0))
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Neplatné položky závozu",
                Detail = "Závoz musí obsahovat alespoň 1 položku s kladným množstvím.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId, ct);
        if (supplier is null)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Účet dodavatele/personálu nenalezen",
                Detail = $"Účet s ID {request.SupplierId} neexistuje.",
                Status = StatusCodes.Status404NotFound
            });
        }

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        if (products.Count != productIds.Count)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Neznámý produkt v závozu",
                Detail = "Některý z uvedených ProductId v databázi neexistuje.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Kontrola oprávnění podle role: ExternalVendor smí naskladňovat jen své vlastní produkty
        if (!supplier.CanManageAllProducts)
        {
            var unauthorizedProduct = products.Values.FirstOrDefault(p => p.SupplierId != supplier.Id);
            if (unauthorizedProduct is not null)
            {
                return TypedResults.Problem(
                    title: "Nepovolený produkt pro externího dodavatele",
                    detail: $"Externí dodavatel '{supplier.CompanyName}' nemůže naskladnit cizí produkt '{unauthorizedProduct.Name}'.",
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        // Sloučení duplicitních řádků se stejným produktem a expirací do jedné položky na souhrnné příjemce
        var consolidatedItems = request.Items
            .GroupBy(i => new { i.ProductId, i.ExpirationDate })
            .Select(g => new
            {
                g.Key.ProductId,
                Quantity = g.Sum(x => x.Quantity),
                g.Key.ExpirationDate
            })
            .ToList();

        var delivery = new StockDelivery
        {
            Id = Guid.NewGuid(),
            SupplierId = supplier.Id,
            Supplier = supplier,
            DeliveredAt = utcNow,
            Items = consolidatedItems.Select(i =>
            {
                var prod = products[i.ProductId];
                prod.StockCount += i.Quantity;
                return new StockDeliveryItem
                {
                    ProductId = prod.Id,
                    Product = prod,
                    Quantity = i.Quantity,
                    ExpirationDate = i.ExpirationDate
                };
            }).ToList()
        };

        db.StockDeliveries.Add(delivery);

        var exportResult = await pohodaClient.ExportStockDeliveryToPohodaXmlAsync(delivery, ct);
        delivery.PohodaSynced = true;
        delivery.PohodaSyncedAt = utcNow;

        await db.SaveChangesAsync(ct);

        foreach (var updatedProd in products.Values)
        {
            await hub.Clients.All.ProductChanged(ProductDto.From(updatedProd, pricing));
        }

        var dto = new StockDeliveryDto(
            delivery.Id,
            supplier.Id,
            supplier.CompanyName,
            supplier.Role,
            delivery.DeliveredAt,
            delivery.PohodaSynced,
            delivery.PohodaSyncedAt,
            exportResult.FilePath,
            delivery.Items.Sum(i => i.Quantity));

        return TypedResults.Created($"/api/deliveries/{delivery.Id}", dto);
    }

    private static async Task<Results<Ok<PohodaExportResult>, NotFound>> ExportDeliveryToPohoda(
        Guid deliveryId,
        CrazyMarketDbContext db,
        IPohodaClient pohodaClient,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var delivery = await db.StockDeliveries
            .Include(d => d.Supplier)
            .Include(d => d.Items)
            .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(d => d.Id == deliveryId, ct);

        if (delivery is null)
            return TypedResults.NotFound();

        var result = await pohodaClient.ExportStockDeliveryToPohodaXmlAsync(delivery, ct);
        delivery.PohodaSynced = true;
        delivery.PohodaSyncedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<PohodaExportResult>, NotFound>> ExportOrderToPohoda(
        Guid orderId,
        CrazyMarketDbContext db,
        IPohodaClient pohodaClient,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var order = await db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order is null)
            return TypedResults.NotFound();

        var result = await pohodaClient.ExportOrderToPohodaXmlAsync(order, ct);
        order.PohodaXmlExported = true;
        order.PohodaExportedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(result);
    }
}
