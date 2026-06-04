using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace wallet.domain.entities;

public class AppUser
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public string Email { get; set; } = default!;
    public string Alias { get; set; } = default!;
    public string FullName { get; set; } = default!;
    public string GoogleId { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
    public bool IsActive { get; set; }
    public virtual WalletAccount WalletAccount { get; set; } = default!;
}

public sealed class AppUserConfiguration
    : IEntityTypeConfiguration<AppUser>
{
    public void Configure(
        EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("users");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Email)
            .HasMaxLength(50)
            .IsRequired();
        builder.HasIndex(x => x.Email)
            .IsUnique();

        builder.Property(x => x.Alias)
            .HasMaxLength(30);
        builder.HasIndex(x => x.Alias)
            .IsUnique();

        builder.Property(x => x.FullName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.GoogleId)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => x.GoogleId)
            .IsUnique();

        builder.HasOne(x => x.WalletAccount)
            .WithOne(x => x.User)
            .HasForeignKey<WalletAccount>(
                x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}