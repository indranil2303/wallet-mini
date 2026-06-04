using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql.EntityFrameworkCore.PostgreSQL.ValueGeneration;

namespace wallet.domain.entities;

public class WalletAccount
{
    [Key]
    public Guid Id { get; private set; }
    [Precision(18, 4)]
    public decimal Balance { get; set; } = default!;
    [MaxLength(3)]
    public string CurrencyCode { get; set; } = default!;
    public bool IsDefaultCurrencySet =>
        !string.IsNullOrWhiteSpace(CurrencyCode);
    public DateTime CreatedAtUtc { get; private set; }
    public string Status { get; set; } = default!;
    public int UserId { get; private set; }
    [ForeignKey(nameof(UserId))]
    public AppUser User { get; set; } = default!;
    [Timestamp]
    public uint xmin { get; private set; } = default!;
    public WalletAccount(int userId)
    {
        Id = Guid.CreateVersion7();
        Balance = 0M;
        UserId = userId;
        CreatedAtUtc = DateTime.UtcNow;
        Status = WalletAccountStatus.Inactive.ToString();
    }
    public void Credit(decimal amount)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Invalid amount");
        Balance += amount;
    }
    public void Debit(decimal amount)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Invalid amount");
        Balance -= amount;
    }
}

public enum WalletAccountStatus
{
    Active,
    Inactive,
    Frozen,
    Closed
}

public sealed class WalletAccountConfiguration
    : IEntityTypeConfiguration<WalletAccount>
{
    public void Configure(
        EntityTypeBuilder<WalletAccount> builder)
    {
        builder.ToTable("wallet_account");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasValueGenerator<NpgsqlSequentialGuidValueGenerator>();

        builder.Property(x => x.UserId)
            .IsRequired();

        builder.Property(x => x.Balance)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasDefaultValue(WalletAccountStatus.Inactive.ToString());

        builder.Property(x => x.xmin)
            .HasColumnName("xmin")
                .HasColumnType("xid").IsRowVersion();
        
        builder.HasIndex(x => new
        {
            x.Id
        });

        builder.HasOne(x => x.User)
            .WithOne(x => x.WalletAccount)
            .HasForeignKey<WalletAccount>(
                x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}