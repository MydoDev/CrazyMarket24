using CrazyMarket.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrazyMarket.Api.Data.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.HasKey(c => c.Id);

        b.Property(c => c.BankIdSub).HasMaxLength(128).IsRequired();
        b.HasIndex(c => c.BankIdSub).IsUnique();

        b.Property(c => c.FullName).HasMaxLength(200).IsRequired();
        b.Property(c => c.TotpSecret).HasMaxLength(64).IsRequired();
    }
}
