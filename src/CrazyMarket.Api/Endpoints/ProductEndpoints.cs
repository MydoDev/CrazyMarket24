using CrazyMarket.Api.Abstractions;
using CrazyMarket.Api.Contracts;
using CrazyMarket.Api.Data;
using CrazyMarket.Api.Domain.Entities;
using CrazyMarket.Api.Hubs;
using CrazyMarket.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CrazyMarket.Api.Endpoints;

public static class ProductEndpoints
{
    public static RouteGroupBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products").WithTags("Products");

        group.MapGet("/", GetAll).WithName("GetProducts");
        group.MapGet("/{id:int}", GetById).WithName("GetProductById");
        group.MapGet("/by-ean/{ean}", GetByEan).WithName("GetProductByEan");
        group.MapPost("/", Create).WithName("CreateProduct");
        group.MapPut("/{id:int}", Update).WithName("UpdateProduct");
        group.MapDelete("/{id:int}", Delete).WithName("DeleteProduct");
        group.MapPost("/{id:int}/esl/render", RenderEslTag).WithName("RenderProductEslTag");
        group.MapGet("/{id:int}/esl/image", GetEslPngImage).WithName("GetProductEslPngImage");

        return group;
    }

    /// <summary>Seznam produktů, volitelně filtrovaný fulltextem (název/EAN) a příznakem 18+.</summary>
    private static async Task<Ok<List<ProductDto>>> GetAll(
        CrazyMarketDbContext db, PricingService pricing, string? search, bool? ageRestricted, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(p => EF.Functions.Like(p.Name, pattern) || EF.Functions.Like(p.Ean, pattern));
        }

        if (ageRestricted is not null)
            query = query.Where(p => p.IsAgeRestricted == ageRestricted);

        var products = await query.OrderBy(p => p.Name).ToListAsync(ct);
        return TypedResults.Ok(products.Select(p => ProductDto.From(p, pricing)).ToList());
    }

    private static async Task<Results<Ok<ProductDto>, NotFound>> GetById(
        int id, CrazyMarketDbContext db, PricingService pricing, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return product is null ? TypedResults.NotFound() : TypedResults.Ok(ProductDto.From(product, pricing));
    }

    /// <summary>Vyhledání podle EAN – to volá skener na samoobslužném kiosku.</summary>
    private static async Task<Results<Ok<ProductDto>, NotFound>> GetByEan(
        string ean, CrazyMarketDbContext db, PricingService pricing, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Ean == ean, ct);
        return product is null ? TypedResults.NotFound() : TypedResults.Ok(ProductDto.From(product, pricing));
    }

    private static async Task<Results<Created<ProductDto>, ValidationProblem, Conflict<ProblemDetails>>> Create(
        ProductUpsertRequest request,
        CrazyMarketDbContext db,
        PricingService pricing,
        IEslGateway eslGateway,
        IHubContext<StoreHub, IStoreHubClient> hub,
        CancellationToken ct)
    {
        var errors = request.Validate();
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        if (await FindConflictAsync(db, request, excludeId: null, ct) is { } conflict)
            return TypedResults.Conflict(conflict);

        var product = new Product { Ean = request.Ean, Name = request.Name };
        request.ApplyTo(product);

        var activePrice = pricing.GetActivePrice(product);
        var eslResult = await eslGateway.RenderAndPushTagAsync(product, activePrice, ct);

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);

        var dto = ProductDto.From(product, pricing);
        await hub.Clients.All.ProductChanged(dto);
        await hub.Clients.All.EslTagUpdated(new EslPreviewDto(
            product.Id, product.Ean, product.Name, product.EslMacAddress,
            eslResult.ActivePrice, eslResult.BannerText, eslResult.PushedToGateway, eslResult.Base64Png));

        return TypedResults.Created($"/api/products/{product.Id}", dto);
    }

    private static async Task<Results<Ok<ProductDto>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> Update(
        int id,
        ProductUpsertRequest request,
        CrazyMarketDbContext db,
        PricingService pricing,
        IEslGateway eslGateway,
        IHubContext<StoreHub, IStoreHubClient> hub,
        CancellationToken ct)
    {
        var errors = request.Validate();
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
            return TypedResults.NotFound();

        if (await FindConflictAsync(db, request, excludeId: id, ct) is { } conflict)
            return TypedResults.Conflict(conflict);

        request.ApplyTo(product);

        var activePrice = pricing.GetActivePrice(product);
        var eslResult = await eslGateway.RenderAndPushTagAsync(product, activePrice, ct);

        await db.SaveChangesAsync(ct);

        var dto = ProductDto.From(product, pricing);
        await hub.Clients.All.ProductChanged(dto);
        await hub.Clients.All.EslTagUpdated(new EslPreviewDto(
            product.Id, product.Ean, product.Name, product.EslMacAddress,
            eslResult.ActivePrice, eslResult.BannerText, eslResult.PushedToGateway, eslResult.Base64Png));

        return TypedResults.Ok(dto);
    }

    private static async Task<Results<NoContent, NotFound, Conflict<ProblemDetails>>> Delete(
        int id,
        CrazyMarketDbContext db,
        IHubContext<StoreHub, IStoreHubClient> hub,
        CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
            return TypedResults.NotFound();

        if (await db.OrderItems.AnyAsync(i => i.ProductId == id, ct))
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Produkt nelze smazat",
                Detail = "Produkt figuruje v objednávkách. Místo smazání nastavte sklad na 0.",
                Status = StatusCodes.Status409Conflict,
            });
        }

        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);

        await hub.Clients.All.ProductDeleted(id);
        return TypedResults.NoContent();
    }

    /// <summary>Vynutí vyrenderování 1-bit E-ink cenovky (296×128 px) s aktuálně platnou cenou a odešle ji na bránu.</summary>
    private static async Task<Results<Ok<EslPreviewDto>, NotFound>> RenderEslTag(
        int id,
        CrazyMarketDbContext db,
        PricingService pricing,
        IEslGateway eslGateway,
        IHubContext<StoreHub, IStoreHubClient> hub,
        CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
            return TypedResults.NotFound();

        var activePrice = pricing.GetActivePrice(product);
        var result = await eslGateway.RenderAndPushTagAsync(product, activePrice, ct);
        await db.SaveChangesAsync(ct);

        var preview = new EslPreviewDto(
            product.Id, product.Ean, product.Name, product.EslMacAddress,
            result.ActivePrice, result.BannerText, result.PushedToGateway, result.Base64Png);

        await hub.Clients.All.EslTagUpdated(preview);
        return TypedResults.Ok(preview);
    }

    /// <summary>Vrátí 1-bit PNG obrázek E-ink cenovky přímo jako binární <c>image/png</c> (vyrenderuje jej, pokud ještě neexistuje).</summary>
    private static async Task<Results<FileContentHttpResult, NotFound>> GetEslPngImage(
        int id,
        CrazyMarketDbContext db,
        PricingService pricing,
        IEslGateway eslGateway,
        CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
            return TypedResults.NotFound();

        byte[] pngBytes;
        if (string.IsNullOrEmpty(product.LastEslImageBase64))
        {
            var activePrice = pricing.GetActivePrice(product);
            var res = await eslGateway.RenderAndPushTagAsync(product, activePrice, ct);
            await db.SaveChangesAsync(ct);
            pngBytes = res.PngBytes;
        }
        else
        {
            pngBytes = Convert.FromBase64String(product.LastEslImageBase64);
        }

        return TypedResults.File(pngBytes, "image/png", $"esl-{product.Ean}.png");
    }

    /// <summary>Kontrola unikátních polí (EAN, MAC cenovky) před zápisem – vrací srozumitelný 409 místo výjimky z DB.</summary>
    private static async Task<ProblemDetails?> FindConflictAsync(
        CrazyMarketDbContext db, ProductUpsertRequest request, int? excludeId, CancellationToken ct)
    {
        var others = db.Products.Where(p => excludeId == null || p.Id != excludeId);

        if (await others.AnyAsync(p => p.Ean == request.Ean, ct))
        {
            return new ProblemDetails
            {
                Title = "Duplicitní EAN",
                Detail = $"Produkt s EAN {request.Ean} již existuje.",
                Status = StatusCodes.Status409Conflict,
            };
        }

        var mac = string.IsNullOrWhiteSpace(request.EslMacAddress) ? null : request.EslMacAddress.ToUpperInvariant();
        if (mac is not null && await others.AnyAsync(p => p.EslMacAddress == mac, ct))
        {
            return new ProblemDetails
            {
                Title = "Cenovka je již přiřazena",
                Detail = $"E-ink cenovka {mac} je přiřazena jinému produktu.",
                Status = StatusCodes.Status409Conflict,
            };
        }

        return null;
    }
}
