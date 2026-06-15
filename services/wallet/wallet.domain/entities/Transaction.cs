using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql.EntityFrameworkCore.PostgreSQL.ValueGeneration;

namespace wallet.domain.entities;
public class Transaction
{
    [Key]
    public Guid Id { get; set; }
    public Guid SenderWalletId { get; set; }
    [ForeignKey(nameof(SenderWalletId))]
    public virtual WalletAccount SenderWallet { get; set; } = default!;
    [MaxLength(3)]
    public string SourceCurrency { get; set; } = default!;
    [Precision(18, 4)]
    public decimal SourceAmount { get; set; }
    public Guid ReceiverWalletId { get; set; }
    [ForeignKey(nameof(ReceiverWalletId))]
    public virtual WalletAccount ReceiverWallet { get; set; } = default!;
    [MaxLength(3)]
    public string DestinationCurrency { get; set; } = default!;
    [Precision(18, 4)]
    public decimal DestinationAmount { get; set; }
    [Precision(18, 8)]
    public decimal FxRate { get; set; }
    [Precision(18,8)]
    public decimal? ModifiedFxRate {get; set;}
    [MaxLength(3)]
    public string FeeCurrency { get; set; } = default!;
    [Precision(18, 4)]
    public decimal TransactionFee { get; set; }
    public TransactionStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    [Timestamp]
    public uint xmin { get; private set; } = default!;
}

public enum TransactionStatus
{
    Pending,
    Success,
    Failed
}

public sealed class TransactionConfiguration
    : IEntityTypeConfiguration<Transaction>
{
    public void Configure(
        EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasValueGenerator<NpgsqlSequentialGuidValueGenerator>();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(x => x.Status)
            .HasDefaultValue(TransactionStatus.Pending);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasOne(x => x.SenderWallet)
            .WithMany()
            .HasForeignKey(x => x.SenderWalletId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.ReceiverWallet)
            .WithMany()
            .HasForeignKey(x => x.ReceiverWalletId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(x => x.SourceAmount)
            .HasPrecision(18, 4);

        builder.Property(x => x.DestinationAmount)
            .HasPrecision(18, 4);

        builder.Property(x => x.TransactionFee)
            .HasPrecision(18, 4);

        builder.Property(x => x.FxRate)
            .HasPrecision(18, 8);

        builder.Property(x => x.ModifiedFxRate)
            .HasPrecision(18, 8)
                .IsRequired(false);

        builder.Property(x => x.xmin)
            .HasColumnName("xmin")
                .HasColumnType("xid")
                    .IsRowVersion();

        builder.HasIndex(x => new
        {
            x.SenderWalletId,
            x.CreatedAtUtc
        });

        builder.HasIndex(x => new
        {
            x.ReceiverWalletId,
            x.CreatedAtUtc
        });

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_transaction_amount_positive",
                "\"Amount\" > 0");
        });
    }
}