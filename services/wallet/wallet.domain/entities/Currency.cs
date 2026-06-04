using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wallet.domain.contracts;
public class Currency
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    [MaxLength(3)]
    public string Code { get; set; } = default!;
    [MaxLength(100)]
    public string Name { get; set; } = default!;
    public bool IsActive { get; set; }
}

public sealed class CurrencyConfiguration
    : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("currencies");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Code)
            .HasMaxLength(3)
            .IsRequired();
        builder.HasIndex(x => x.Code)
            .IsUnique();

        builder.Property(x => x.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);

        builder.HasIndex(x => x.Code)
            .IsUnique();

        builder.HasData(
            new Currency { Id = 1, Code = "INR", Name = "Indian Rupee", IsActive = true },
            new Currency { Id = 2, Code = "USD", Name = "US Dollar", IsActive = true },
            new Currency { Id = 3, Code = "EUR", Name = "Euro", IsActive = true },
            new Currency { Id = 4, Code = "GBP", Name = "British Pound", IsActive = true },
            new Currency { Id = 5, Code = "AUD", Name = "Australian Dollar", IsActive = true },
            new Currency { Id = 6, Code = "CAD", Name = "Canadian Dollar", IsActive = true }
        );
    }
}