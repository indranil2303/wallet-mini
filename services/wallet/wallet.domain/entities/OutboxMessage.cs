using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wallet.domain.entities;

public class OutboxMessage
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    public string EventKey { get; set; } = default!;
    public Guid RequestId { get; set; }
    public string Payload { get; set; } = default!;
    public DateTime OccurredOnUtc { get; set; }
    public bool Processed { get; set; }
    public int? ProcessingAttempts { get; set; }
}

public sealed class OutboxMessageConfiguration
    : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(
        EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.EventKey)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.RequestId)
            .HasMaxLength(200)
            .HasDefaultValue(Guid.Empty);

        builder.Property(x => x.OccurredOnUtc)
            .IsRequired();

        builder.Property(x => x.Processed)
            .HasDefaultValue(false);

        builder.Property(x => x.ProcessingAttempts)
            .HasDefaultValue(0);

        builder.HasIndex(x => new
        {
            x.RequestId,
            x.EventKey
        });
    }
}