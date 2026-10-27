using CrazyMarket.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrazyMarket.Api.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.HasKey(o => o.Id);

        b.Property(o => o.PaymentStatus).HasConversion<string>().HasMaxLength(32);
        b.Property(o => o.WeightValidation).HasConversion<string>().HasMaxLength(32);
        b.Property(o => o.PaymentReference).HasMaxLength(64);

        b.HasOne(o => o.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(o => o.StoreSession)
            .WithMany(s => s.Orders)
            .HasForeignKey(o => o.StoreSessionId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasMany(o => o.Items)
            .WithOne(i => i.Order)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Fronta pro export do POHODY.
        b.HasIndex(o => new { o.PohodaXmlExported, o.PaymentStatus });
    }
}

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.HasKey(i => i.Id);

        b.Property(i => i.Ean).HasMaxLength(13).IsRequired();
        b.Property(i => i.ProductName).HasMaxLength(200).IsRequired();

        // Produkt s historií prodejů nelze smazat – doklady musí zůstat dohledatelné.
        b.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
