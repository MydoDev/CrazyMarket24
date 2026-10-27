using CrazyMarket.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrazyMarket.Api.Data.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.HasKey(s => s.Id);

        b.Property(s => s.CompanyName).HasMaxLength(200).IsRequired();
        b.Property(s => s.Ico).HasMaxLength(16).IsRequired();
        b.HasIndex(s => s.Ico).IsUnique();

        b.Property(s => s.Role).HasConversion<string>().HasMaxLength(32);
        b.Property(s => s.TotpSecret).HasMaxLength(64).IsRequired();
    }
}

public class StockDeliveryConfiguration : IEntityTypeConfiguration<StockDelivery>
{
    public void Configure(EntityTypeBuilder<StockDelivery> b)
    {
        b.HasKey(d => d.Id);

        b.HasOne(d => d.Supplier)
            .WithMany(s => s.Deliveries)
            .HasForeignKey(d => d.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(d => d.Items)
            .WithOne(i => i.StockDelivery)
            .HasForeignKey(i => i.StockDeliveryId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(d => new { d.PohodaSynced, d.DeliveredAt });
    }
}

public class StockDeliveryItemConfiguration : IEntityTypeConfiguration<StockDeliveryItem>
{
    public void Configure(EntityTypeBuilder<StockDeliveryItem> b)
    {
        b.HasKey(i => i.Id);

        b.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
