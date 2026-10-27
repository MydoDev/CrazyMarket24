using CrazyMarket.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrazyMarket.Api.Data.Configurations;

public class StoreSessionConfiguration : IEntityTypeConfiguration<StoreSession>
{
    public void Configure(EntityTypeBuilder<StoreSession> b)
    {
        b.HasKey(s => s.Id);

        b.Property(s => s.CurrentState).HasConversion<string>().HasMaxLength(32);

        b.HasOne(s => s.Customer)
            .WithMany(c => c.Sessions)
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(s => s.Supplier)
            .WithMany(sup => sup.Sessions)
            .HasForeignKey(s => s.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        // Rychlé dotazy „kdo je právě v obchodě“.
        b.HasIndex(s => new { s.CurrentState, s.ExitedAt });
    }
}
