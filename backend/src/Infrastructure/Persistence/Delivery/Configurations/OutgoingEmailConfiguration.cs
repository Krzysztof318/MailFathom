// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Domain.Scheduling;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Delivery.Configurations;

/// <summary>Declares the durable record of every message this system has been asked to send.</summary>
/// <remarks>
/// <para>
/// The row exists before any SMTP command is issued, which is what makes a non-atomic submission survivable: the
/// stage says how far the attempt got, and the one stage that means "the body went out and the answer never came
/// back" is written before the transmission rather than after it.
/// </para>
/// <para>
/// Nothing here is mail content. The account, the requester identity, the reply codes, and the stage are this
/// system's own or the server's own names for things, and the message itself is a row of its own that no query
/// listing the outbox touches. The recipients are personal data and are the one thing on this record that could not
/// be left out: a send cannot be resumed without knowing who is still owed it.
/// </para>
/// </remarks>
internal sealed class OutgoingEmailConfiguration : IEntityTypeConfiguration<OutgoingEmailEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutgoingEmailEntity> entity)
    {
        entity.ToTable("outgoing_emails");
        entity.HasKey(message => message.Id);
        entity.Property(message => message.Id).ValueGeneratedNever();
        entity.Property(message => message.MailboxAccountId).HasMaxLength(128).IsRequired();
        entity.Property(message => message.RequesterIdentity)
            .HasMaxLength(OutgoingEmailRequester.MaximumIdentityLength)
            .IsRequired();

        // Optional because the column arrived after the table did, and a record written before it carries no
        // principal rather than an empty one: absence has to stay tellable from a value, since a caller matching
        // the empty string would be reading somebody else's send.
        entity.Property(message => message.PrincipalFingerprint)
            .HasMaxLength(OutgoingEmailPrincipal.FingerprintLength);

        // Stored as text for the reason the mutation stage is: both stay readable in an ad-hoc audit query and
        // survive any later reordering of their enum.
        entity.Property(message => message.RequesterOrigin).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(message => message.Stage).HasConversion<string>().HasMaxLength(64).IsRequired();

        // The zone the due time was written in, bounded by what the IANA database can name. It is stored beside the
        // instant rather than derived from it, because a message written for nine in the morning was written in a
        // place, and only the zone says which nine that was after the offset there changes.
        entity.Property(message => message.DueZoneId).HasMaxLength(ZonedInstant.MaximumZoneIdLength);

        // See the stored-email mapping: this is the PostgreSQL `xmin` system column, not a user-defined column.
        entity.Property(message => message.ConcurrencyVersion).IsRowVersion();

        entity.HasIndex(message => new
        {
            message.OwnerId,
            message.MailboxAccountId,
            message.RequesterOrigin,
            message.RequesterIdentity,
        })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.OutgoingEmailIdentityUniqueIndexName);

        // Filtered to the sends that have not finished, so the structure holds what is queued and in flight rather
        // than every message the deployment has ever sent. A refused send stays in for the reason an abandoned
        // mutation does: giving up on it is what stops it being attempted, and it would be worth nothing if it also
        // stopped it being seen — so the filter names the three terminal stages rather than only the successful one.
        entity.HasIndex(message => new { message.OwnerId, message.MailboxAccountId, message.RecordedAt })
            .HasDatabaseName(PersistenceConstraintNames.OutgoingEmailOutstandingIndexName)
            .HasFilter(
                $"\"{nameof(OutgoingEmailEntity.Stage)}\" NOT IN ("
                + $"'{nameof(OutgoingEmailStage.Sent)}', "
                + $"'{nameof(OutgoingEmailStage.Refused)}', "
                + $"'{nameof(OutgoingEmailStage.Cancelled)}')");

        // Filtered to the one stage a claim may take a record from, which is both what makes the structure small
        // and what lets PostgreSQL prove it applies to the claim's own predicate. Ordered the way that claim
        // orders, so the batch it takes is a range read rather than a sort over everything the account has queued.
        entity.HasIndex(message => new
        {
            message.OwnerId,
            message.MailboxAccountId,
            message.AvailableAt,
            message.Id,
        })
            .HasDatabaseName(PersistenceConstraintNames.OutgoingEmailClaimableIndexName)
            .HasFilter($"\"{nameof(OutgoingEmailEntity.Stage)}\" = '{nameof(OutgoingEmailStage.Recorded)}'");

        entity.HasIndex(message => new { message.RecordedAt, message.OwnerId, message.MailboxAccountId })
            .HasDatabaseName(PersistenceConstraintNames.OutgoingEmailPeriodUsageIndexName);
    }
}
