using CrazyMarket.Api.Contracts;
using CrazyMarket.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CrazyMarket.Api.Endpoints;

public static class CustomerEndpoints
{
    public static RouteGroupBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/customers").WithTags("Customers");

        group.MapGet("/", async Task<Ok<List<CustomerDto>>> (CrazyMarketDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var today = DateOnly.FromDateTime(time.GetLocalNow().Date);
            var customers = await db.Customers.AsNoTracking().OrderBy(c => c.FullName).ToListAsync(ct);
            return TypedResults.Ok(customers.Select(c => CustomerDto.From(c, today)).ToList());
        }).WithName("GetCustomers");

        group.MapGet("/{customerId:guid}", async Task<Results<Ok<CustomerDto>, NotFound>> (
            Guid customerId, CrazyMarketDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var today = DateOnly.FromDateTime(time.GetLocalNow().Date);
            var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, ct);
            return customer is null ? TypedResults.NotFound() : TypedResults.Ok(CustomerDto.From(customer, today));
        }).WithName("GetCustomerById");

        group.MapGet("/{customerId:guid}/orders", async Task<Ok<List<CustomerOrderHistoryDto>>> (
            Guid customerId, CrazyMarketDbContext db, CancellationToken ct) =>
        {
            var orders = await db.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                .Where(o => o.CustomerId == customerId)
                .OrderByDescending(o => o.CreatedAt)
                .Take(20)
                .ToListAsync(ct);

            var result = orders.Select(o => new CustomerOrderHistoryDto(
                o.Id,
                o.CreatedAt,
                o.PaidAt,
                o.TotalAmount,
                o.IsNightPricing,
                o.PaymentStatus.ToString(),
                o.PaymentReference,
                o.PohodaXmlExported,
                o.Items.Select(i => new CustomerOrderItemHistoryDto(
                    i.Ean, i.ProductName, i.Quantity, i.UnitPrice, i.LineTotal)).ToList()
            )).ToList();

            return TypedResults.Ok(result);
        }).WithName("GetCustomerOrderHistory");

        return group;
    }
}
