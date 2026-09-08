// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails;

/// <summary>Covers what a rebuilding walk judges stale once an attachment carries a stamp of its own.</summary>
/// <remarks>
/// <para>
/// A body and an attachment are read by different stages, so a posture republished between the two leaves one of them
/// current. Before this the walk read the message's search document alone: a contract's extracted text and a model's
/// description of a picture stayed exactly as they were written, under-redacted and indefinitely so, while the report
/// called the mailbox clean.
/// </para>
/// <para>
/// The predicate is composed here and evaluated by PostgreSQL, so what these tests establish is which rows it selects
/// rather than what SQL it becomes. What it does with an absent stamp — the reading taken before any scanner was
/// switched on — is the one part C# and SQL could disagree about, and
/// <c>OrchestratedStaleDerivedDataTests</c> is where the translation itself is held against a database.
/// </para>
/// </remarks>
public sealed class StoredEmailStaleDerivedDataSelectionTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);

    private static readonly SensitiveContentDerivationStamp CurrentStamp =
        SensitiveContentDerivationStamp.Create(new string('c', SensitiveContentDerivationStamp.Length));

    private static readonly SensitiveContentDerivationStamp OlderStamp =
        SensitiveContentDerivationStamp.Create(new string('0', SensitiveContentDerivationStamp.Length));

    private readonly SensitiveContentScanConcurrency concurrency =
        new(SensitiveContentScanBounds.Default.MaximumConcurrentScans);

    /// <inheritdoc />
    public void Dispose() => this.concurrency.Dispose();

    /// <summary>The gap this change closes: the body was re-derived and the file's words were left as they were.</summary>
    [Fact]
    public void StaleOrReadUnderAnOlderPostureFor_AMessageWhoseAttachmentAloneIsStale_SelectsIt()
    {
        // Arrange
        var email = Email(SyntheticMailUser.Deployment, CurrentStamp);
        AddReading(email, OlderStamp, position: 0);

        // Act
        var selected = this.Stale(email);

        // Assert
        Assert.Single(selected);
    }

    /// <summary>A reading taken before any scanner was on is a different value from a stamp, never a missing one.</summary>
    [Fact]
    public void StaleOrReadUnderAnOlderPostureFor_AnAttachmentReadBeforeAnyScannerWasOn_SelectsIt()
    {
        // Arrange
        var email = Email(SyntheticMailUser.Deployment, CurrentStamp);
        AddReading(email, stamp: null, position: 0);

        // Act
        var selected = this.Stale(email);

        // Assert
        Assert.Single(selected);
    }

    /// <summary>A mailbox nothing changed for costs nothing, which is the whole of what makes the rebuild rerunnable.</summary>
    [Fact]
    public void StaleOrReadUnderAnOlderPostureFor_AMessageAndItsAttachmentsBothCurrent_LeavesItOut()
    {
        // Arrange
        var email = Email(SyntheticMailUser.Deployment, CurrentStamp);
        AddReading(email, CurrentStamp, position: 0);
        AddReading(email, CurrentStamp, position: 1);

        // Act
        var selected = this.Stale(email);

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>
    /// One branch per user rather than a body set unioned with an attachment set: a message stale on both counts would
    /// otherwise arrive in the same batch twice, be read twice, and have the position committed past itself.
    /// </summary>
    [Fact]
    public void StaleOrReadUnderAnOlderPostureFor_AMessageStaleOnBothCounts_SelectsItOnce()
    {
        // Arrange
        var email = Email(SyntheticMailUser.Deployment, OlderStamp);
        AddReading(email, OlderStamp, position: 0);
        AddReading(email, OlderStamp, position: 1);

        // Act
        var selected = this.Stale(email);

        // Assert
        Assert.Single(selected);
    }

    /// <summary>Mail still stored for somebody the roster no longer names is judged by the deployment's own posture.</summary>
    [Fact]
    public void StaleOrReadUnderAnOlderPostureFor_AnUnrosteredUsersStaleAttachment_SelectsIt()
    {
        // Arrange
        var email = Email(SyntheticMailUser.Another, CurrentStamp);
        AddReading(email, OlderStamp, position: 0);

        // Act
        var selected = this.Stale(email);

        // Assert
        Assert.Single(selected);
    }

    /// <summary>The figure an operator reads is per attachment, because that is the unit of the work a rebuild causes.</summary>
    [Fact]
    public void StaleReadingsFor_AMessageWhoseAttachmentsWereReadUnderSeveralPostures_CountsOnlyTheOnesThatAreStale()
    {
        // Arrange
        var email = Email(SyntheticMailUser.Deployment, CurrentStamp);
        AddReading(email, OlderStamp, position: 0);
        AddReading(email, stamp: null, position: 1);
        AddReading(email, CurrentStamp, position: 2);

        // Act
        var stale = StoredEmailExtractionBackfillStore.StaleReadingsFor(
            new[] { email }.AsQueryable(),
            this.PosturesScanning(),
            CurrentStamp);

        // Assert
        Assert.Equal([0, 1], stale.Select(reading => reading.AttachmentPosition).Order());
    }

    /// <summary>A user whose mail nothing scans has no stamp to be stale against, so their readings are never counted.</summary>
    [Fact]
    public void StaleReadingsFor_ADeploymentThatScansNobody_CountsNothing()
    {
        // Arrange
        var email = Email(SyntheticMailUser.Deployment, OlderStamp);
        AddReading(email, OlderStamp, position: 0);

        // Act
        var stale = StoredEmailExtractionBackfillStore.StaleReadingsFor(
            new[] { email }.AsQueryable(),
            [],
            unrostered: null);

        // Assert
        Assert.Empty(stale);
    }

    private IReadOnlyList<StoredEmailEntity> Stale(StoredEmailEntity email) =>
        [.. StoredEmailExtractionBackfillStore.StaleOrReadUnderAnOlderPostureFor(
            new[] { email }.AsQueryable(),
            this.PosturesScanning(),
            CurrentStamp)];

    /// <summary>The roster of a deployment scanning one user's mail towards the current stamp, and nobody else's.</summary>
    private IReadOnlyList<UserSensitiveContentPosture> PosturesScanning()
    {
        var plan = SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Default,
            [
                SensitiveContentScannerPlan.Create(
                    SensitiveContentScannerKind.Secrets,
                    [SensitiveContentCategory.Create("ProviderToken")],
                    []),
            ]);

        return
        [
            new UserSensitiveContentPosture(
                SyntheticMailUser.Deployment,
                SensitiveContentPosture.Scanning(
                    [SensitiveContentScannerKind.Secrets],
                    new SensitiveContentRedactor(plan, [], TimeProvider.System, this.concurrency),
                    SensitiveContentScreeningPolicy.ScreeningNothing(),
                    CurrentStamp)),
        ];
    }

    /// <summary>Builds one message already derived under a stated configuration, carrying no attachment reading yet.</summary>
    private static StoredEmailEntity Email(MailUserId user, SensitiveContentDerivationStamp stamp)
    {
        var email = new StoredEmailEntity
        {
            UserId = user.Value,
            MailboxAccountId = "work",
            MailFolder = new MailFolderEntity
            {
                UserId = user.Value,
                MailboxAccountId = "work",
                Alias = "INBOX",
                RemotePath = "INBOX",
                MailboxAccount = new MailboxAccountEntity { UserId = user.Value, Id = "work" },
            },
            StoredAt = Now,
            ContentAvailability = StoredEmailContentAvailability.Available,
            AttachmentCount = 1,
        };

        email.SearchDocument = new EmailSearchDocumentEntity
        {
            StoredEmailId = email.Id,
            StoredEmail = email,
            BodyText = "a body",
            BodyTextBeforeTrimming = "a body",
            ExtractedAt = Now,
            SensitiveContentStamp = stamp.Value,
        };

        return email;
    }

    /// <summary>Records what one of the message's attachments yielded, under a stated configuration or under none.</summary>
    private static void AddReading(
        StoredEmailEntity email,
        SensitiveContentDerivationStamp? stamp,
        int position) =>
        email.AttachmentTexts.Add(new EmailAttachmentTextEntity
        {
            StoredEmailId = email.Id,
            StoredEmail = email,
            AttachmentPosition = position,
            Kind = AttachmentTextKind.Document,
            DeclaredMediaType = "application/pdf",
            Outcome = "Extracted",
            Text = "a contract",
            DerivedAt = Now,
            SensitiveContentStamp = stamp?.Value,
        });
}
