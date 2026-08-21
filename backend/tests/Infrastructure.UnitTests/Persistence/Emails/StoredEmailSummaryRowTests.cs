// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Infrastructure.Persistence.Emails;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails;

/// <summary>
/// Covers how the returned columns are read back into a summary where the row holds something no writer produces.
/// </summary>
/// <remarks>
/// The projection itself is the integration suite's to prove, because only a database can say which columns a query
/// returns. What the row decides after they arrive is ordinary logic, and the authorship likelihood is the one place it
/// decides something a corrupted column would otherwise decide for it.
/// </remarks>
public sealed class StoredEmailSummaryRowTests
{
    /// <summary>A likelihood outside the scale is read as none of a reading rather than as the strongest one.</summary>
    /// <remarks>
    /// Reading it as the nearest end would republish a column nobody can account for as maximum confidence, which is
    /// the one answer an informational reading must never reach by accident.
    /// </remarks>
    [Theory]
    [InlineData(5)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ToSummary_ALikelihoodOutsideTheScale_IsReadAsZero(double stored)
    {
        // Arrange
        var row = RowWith(MachineAuthorshipBand.Likely, stored);

        // Act
        var summary = row.ToSummary();

        // Assert
        Assert.Equal(0, summary.MachineAuthorship.Likelihood);
        Assert.Equal(MachineAuthorshipBand.Likely, summary.MachineAuthorship.Band);
    }

    /// <summary>A likelihood the reading actually produced is read back exactly, because two of them are compared.</summary>
    [Fact]
    public void ToSummary_AStoredLikelihood_IsReadBackUnchanged()
    {
        // Arrange
        var row = RowWith(MachineAuthorshipBand.Possible, 0.42);

        // Act
        var summary = row.ToSummary();

        // Assert
        Assert.Equal(0.42, summary.MachineAuthorship.Likelihood);
        Assert.Equal(MachineAuthorshipSignals.HiddenCharacters, summary.MachineAuthorship.Signals);
        Assert.Equal(MachineAuthorshipProfile.Standard.Revision, summary.MachineAuthorship.ProfileRevision);
    }

    /// <summary>A row nothing assessed carries the not-assessed state whole, whatever the other three columns hold.</summary>
    [Fact]
    public void ToSummary_ARowNothingAssessed_IsReadAsNotAssessed()
    {
        // Arrange
        var row = RowWith(MachineAuthorshipBand.NotAssessed, 0.9);

        // Act
        var summary = row.ToSummary();

        // Assert
        Assert.Same(MachineAuthorshipAssessment.NotAssessed, summary.MachineAuthorship);
        Assert.False(summary.MachineAuthorship.ProfileRevision.NamesAProfile);
    }

    private static StoredEmailSummaryRow RowWith(MachineAuthorshipBand band, double likelihood) => new(
        Guid.CreateVersion7(),
        "primary",
        "INBOX",
        ThreadId: null,
        InternetMessageId: null,
        Subject: "Quarterly invoice",
        SentAt: null,
        ReceivedAt: null,
        SizeOctets: 4096,
        SenderDisplayName: null,
        SenderAddress: "billing@partner.example",
        ToAddresses: [],
        AttachmentCount: 0,
        AttachmentTotalSizeOctets: 0,
        InlineResourceCount: 0,
        IsEncrypted: false,
        CarriesUnverifiedSignature: false,
        ContainsUnexpandedTnefPart: false,
        StoredEmailContentAvailability.Available,
        RemoteFlagsObservedAt: null,
        IsRemotelySeen: false,
        IsRemotelyAnswered: false,
        IsRemotelyFlagged: false,
        IsRemotelyDraft: false,
        IsRemotelyDeleted: false,
        RemoteKeywords: [],
        AuthorAuthenticationOutcome.NotEstablished,
        SenderTrustLevel.Unknown,
        AuthenticatedSenderDomain: null,
        DisplayedAuthorDomain: null,
        SenderAuthenticationMethod.None,
        DmarcOutcome.NotReported,
        SenderAuthenticationSource.ReceivingServer,
        band,
        likelihood,
        MachineAuthorshipSignals.HiddenCharacters,
        MachineAuthorshipProfile.Standard.Revision.Value);
}
