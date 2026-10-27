using CrazyMarket.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrazyMarket.Api.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.HasKey(p => p.Id);

        b.Property(p => p.Ean).HasMaxLength(13).IsRequired();
        b.HasIndex(p => p.Ean).IsUnique();

        b.Property(p => p.PohodaId).HasMaxLength(64);
        b.HasIndex(p => p.PohodaId);

        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
        // Výchozí tolerance (15 g) nastavuje C# inicializátor v entitě. Záměrně NE HasDefaultValue:
        // EF by pak explicitně zadanou nulovou toleranci neodeslal a DB by ji přepsala na 15.
        b.Property(p => p.EslMacAddress).HasMaxLength(17); // AA:BB:CC:DD:EE:FF
        b.HasIndex(p => p.EslMacAddress).IsUnique();

        b.HasOne(p => p.Supplier)
            .WithMany(s => s.Products)
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
