// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Collection;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Contacts.Collection;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Folders;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Contacts.Collection;

/// <summary>Covers what one committed message contributes to the contact book, and what it never does.</summary>
public sealed class MailContactCollectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);

    /// <summary>An instance nobody switched collection on for never accumulates a record of who writes to its owner.</summary>
    [Fact]
    public async Task CollectFromAsync_CollectionSwitchedOff_WritesNothingAndReadsNothing()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var tally = StubAuthoredMailTally.Of("anna@example.test", 9);
        var telemetry = new RecordingContactCollectionTelemetry();
        var collector = CollectorOver(book, ContactCollectionSettings.CollectingNothing, tally, telemetry);

        // Act
        await collector.CollectFromAsync(
            MessageFrom("Anna Kowalska", "anna@example.test"),
            RunOver(collector, folderRole: null),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, book.ContactCount);
        Assert.Equal(0, tally.QueryCount);
        Assert.Empty(telemetry.Outcomes);
    }

    /// <summary>The author of a message in an ordinary folder is somebody writing to the owner, which is the whole feature.</summary>
    [Fact]
    public async Task CollectFromAsync_AnAuthorWhoHasWrittenOftenEnough_IsRecordedAsCollected()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var telemetry = new RecordingContactCollectionTelemetry();
        var collector = CollectorOver(
            book,
            SettingsCollecting(minimumMessages: 2),
            StubAuthoredMailTally.Of("anna@example.test", 2),
            telemetry);

        // Act
        await collector.CollectFromAsync(
            MessageFrom("Anna Kowalska", "anna@example.test"),
            RunOver(collector, folderRole: null),
            TestContext.Current.CancellationToken);

        // Assert
        var recorded = Assert.Single(book.Contacts);
        Assert.Equal("Anna Kowalska", recorded.DisplayName.Value);
        Assert.Equal(ContactOrigin.Collected, recorded.Origin);
        Assert.Equal("ANNA@EXAMPLE.TEST", recorded.PreferredAddress.NormalizedAddress);
        Assert.Equal([ContactCollectionOutcome.Recorded], telemetry.Outcomes);
    }

    /// <summary>The threshold is what separates a book of correspondents from a list of everyone who ever wrote once.</summary>
    [Theory]
    [InlineData(1, ContactCollectionOutcome.BelowThreshold)]
    [InlineData(2, ContactCollectionOutcome.Recorded)]
    [InlineData(3, ContactCollectionOutcome.Recorded)]
    public async Task CollectFromAsync_AtTheThresholdBoundary_RecordsOnlyOnceEnoughMessagesHaveArrived(
        int messagesWritten,
        ContactCollectionOutcome expected)
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var telemetry = new RecordingContactCollectionTelemetry();
        var collector = CollectorOver(
            book,
            SettingsCollecting(minimumMessages: 2),
            StubAuthoredMailTally.Of("anna@example.test", messagesWritten),
            telemetry);

        // Act
        await collector.CollectFromAsync(
            MessageFrom("Anna Kowalska", "anna@example.test"),
            RunOver(collector, folderRole: null),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([expected], telemetry.Outcomes);
        Assert.Equal(expected == ContactCollectionOutcome.Recorded ? 1 : 0, book.ContactCount);
    }

    /// <summary>A threshold of one is a deployment saying first sight is enough, and it asks the database nothing.</summary>
    [Fact]
    public async Task CollectFromAsync_AThresholdOfOne_RecordsOnFirstSightWithoutCounting()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var tally = StubAuthoredMailTally.NobodyHasWritten;
        var collector = CollectorOver(book, SettingsCollecting(minimumMessages: 1), tally);

        // Act
        await collector.CollectFromAsync(
            MessageFrom("Anna Kowalska", "anna@example.test"),
            RunOver(collector, folderRole: null),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, book.ContactCount);
        Assert.Equal(0, tally.QueryCount);
    }

    /// <summary>Somebody the owner wrote to is a correspondent on the first message, so no count stands in for that.</summary>
    [Fact]
    public async Task CollectFromAsync_ARecipientOfAMessageTheOwnerSent_IsRecordedWithoutAThreshold()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var tally = StubAuthoredMailTally.NobodyHasWritten;
        var collector = CollectorOver(book, SettingsCollecting(minimumMessages: 5), tally);

        // Act
        await collector.CollectFromAsync(
            MessageWith(
                new EmailParticipant(EmailAddressRole.From, AddressOf("owner@example.test", "The Owner")),
                new EmailParticipant(EmailAddressRole.To, AddressOf("anna@example.test", "Anna Kowalska"))),
            RunOver(collector, MailFolderSpecialUse.Sent),
            TestContext.Current.CancellationToken);

        // Assert
        var recorded = Assert.Single(book.Contacts);
        Assert.Equal("ANNA@EXAMPLE.TEST", recorded.PreferredAddress.NormalizedAddress);
        Assert.Equal(0, tally.QueryCount);
    }

    /// <summary>Cc is the copied recipients of somebody else's thread, which is what would fill a book with people nobody wants.</summary>
    [Theory]
    [InlineData(EmailAddressRole.Cc)]
    [InlineData(EmailAddressRole.Bcc)]
    [InlineData(EmailAddressRole.ReplyTo)]
    [InlineData(EmailAddressRole.Sender)]
    public async Task CollectFromAsync_AHeaderThatIsNotTheOneTheFolderContributes_IsNeverRead(EmailAddressRole role)
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var collector = CollectorOver(book, SettingsCollecting(minimumMessages: 1), StubAuthoredMailTally.NobodyHasWritten);

        // Act
        await collector.CollectFromAsync(
            MessageWith(new EmailParticipant(role, AddressOf("anna@example.test", "Anna Kowalska"))),
            RunOver(collector, folderRole: null),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, book.ContactCount);
    }

    /// <summary>Drafts are unsent, and junk and trash say the opposite of what a book of correspondents is for.</summary>
    [Theory]
    [InlineData(MailFolderSpecialUse.Drafts)]
    [InlineData(MailFolderSpecialUse.Junk)]
    [InlineData(MailFolderSpecialUse.Trash)]
    public async Task CollectFromAsync_AFolderThatSaysNothingAboutCorrespondence_ContributesNobody(
        MailFolderSpecialUse folderRole)
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var telemetry = new RecordingContactCollectionTelemetry();
        var collector = CollectorOver(
            book,
            SettingsCollecting(minimumMessages: 1),
            StubAuthoredMailTally.NobodyHasWritten,
            telemetry);

        // Act
        await collector.CollectFromAsync(
            MessageFrom("Anna Kowalska", "anna@example.test"),
            RunOver(collector, folderRole),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, book.ContactCount);
        Assert.Empty(telemetry.Outcomes);
    }

    /// <summary>A list posting carries the author's own real address, which is the address no rule about names could catch.</summary>
    [Fact]
    public async Task CollectFromAsync_AMessageAMailingListDistributed_ContributesNobody()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var telemetry = new RecordingContactCollectionTelemetry();
        var collector = CollectorOver(
            book,
            SettingsCollecting(minimumMessages: 1),
            StubAuthoredMailTally.NobodyHasWritten,
            telemetry);

        var distributed = MessageFrom("Anna Kowalska", "anna@example.test") with
        {
            Automation = EmailAutomation.MailingList,
        };

        // Act
        await collector.CollectFromAsync(
            distributed,
            RunOver(collector, folderRole: null),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, book.ContactCount);
        Assert.Equal([ContactCollectionOutcome.NotCorrespondence], telemetry.Outcomes);
    }

    /// <summary>The refusal to touch what an owner wrote down is the rule the whole origin distinction exists for.</summary>
    [Fact]
    public async Task CollectFromAsync_AnAddressAnAssertedContactAlreadyHolds_LeavesThatContactExactlyAsItWas()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var asserted = Contact.Create(
            ContactId.Create(Guid.CreateVersion7(Now)),
            ContactDisplayName.Create("Anna Kowalska"),
            [AddressOf("anna@example.test", displayName: null)],
            AddressOf("anna@example.test", displayName: null),
            ContactNote.Create("Met at the conference."),
            ContactOrigin.Asserted,
            Now,
            Now);

        book.Hold(asserted);

        var telemetry = new RecordingContactCollectionTelemetry();
        var collector = CollectorOver(
            book,
            SettingsCollecting(minimumMessages: 1),
            StubAuthoredMailTally.NobodyHasWritten,
            telemetry);

        // Act
        await collector.CollectFromAsync(
            MessageFrom("A. Kowalska (work)", "Anna@Example.test"),
            RunOver(collector, folderRole: null),
            TestContext.Current.CancellationToken);

        // Assert
        var held = Assert.Single(book.Contacts);
        Assert.Equal(asserted.Id, held.Id);
        Assert.Equal(ContactOrigin.Asserted, held.Origin);
        Assert.Equal("Anna Kowalska", held.DisplayName.Value);
        Assert.Equal("Met at the conference.", held.Note?.Value);
        Assert.Equal([ContactCollectionOutcome.AlreadyHeld], telemetry.Outcomes);
    }

    /// <summary>The owner's list and the structural rule are both held against the address before anything is read.</summary>
    [Theory]
    [InlineData("no-reply@example.test")]
    [InlineData("announce@lists.test")]
    [InlineData("owner@example.test")]
    public async Task CollectFromAsync_AnAddressThePolicyRefuses_IsNotRecordedAndCostsNoLookup(string address)
    {
        // Arrange
        Assert.True(ContactCollectionExclusion.TryCreateForDomain("lists.test", includeSubdomains: false, out var excluded));

        var book = new InMemoryContactBookStore();
        var tally = StubAuthoredMailTally.NobodyHasWritten;
        var telemetry = new RecordingContactCollectionTelemetry();
        var collector = CollectorOver(
            book,
            SettingsCollecting(minimumMessages: 1, ContactCollectionPolicy.Create(
                [excluded],
                [AddressOf("owner@example.test", displayName: null)])),
            tally,
            telemetry);

        // Act
        await collector.CollectFromAsync(
            MessageFrom("Somebody", address),
            RunOver(collector, folderRole: null),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, book.ContactCount);
        Assert.Equal(0, tally.QueryCount);
        Assert.Equal([ContactCollectionOutcome.Excluded], telemetry.Outcomes);
    }

    /// <summary>A first synchronization of years of mail must not gain a book thousands of people in one pass.</summary>
    [Fact]
    public async Task CollectFromAsync_TheRunsBoundReached_RecordsNoMoreAndSaysSo()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var telemetry = new RecordingContactCollectionTelemetry();
        var collector = CollectorOver(
            book,
            SettingsCollecting(minimumMessages: 1, maxContactsPerRun: 1),
            StubAuthoredMailTally.NobodyHasWritten,
            telemetry);

        var run = RunOver(collector, folderRole: null);

        // Act
        await collector.CollectFromAsync(
            MessageFrom("Anna Kowalska", "anna@example.test"),
            run,
            TestContext.Current.CancellationToken);
        await collector.CollectFromAsync(
            MessageFrom("Jan Nowak", "jan@example.test"),
            run,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, book.ContactCount);
        Assert.Equal(1, run.Budget.Recorded);
        Assert.Equal(
            [ContactCollectionOutcome.Recorded, ContactCollectionOutcome.RunBoundReached],
            telemetry.Outcomes);
    }

    /// <summary>A message addressed to a crowd is an announcement, and reading its first few recipients would record whoever was typed first.</summary>
    [Fact]
    public async Task CollectFromAsync_ASentMessageAddressedToACrowd_ContributesNobody()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var collector = CollectorOver(book, SettingsCollecting(minimumMessages: 1), StubAuthoredMailTally.NobodyHasWritten);

        var recipients = Enumerable
            .Range(0, MailContactCollector.MaximumRecipientsCollected + 1)
            .Select(index => new EmailParticipant(
                EmailAddressRole.To,
                AddressOf($"person{index}@example.test", $"Person {index}")))
            .ToArray();

        // Act
        await collector.CollectFromAsync(
            MessageWith(recipients),
            RunOver(collector, MailFolderSpecialUse.Sent),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, book.ContactCount);
    }

    /// <summary>A collected record is named by what the message wrote, and by its address where the message wrote nothing usable.</summary>
    [Fact]
    public async Task CollectFromAsync_AnAuthorWithNoDisplayName_IsNamedByTheirAddress()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var collector = CollectorOver(book, SettingsCollecting(minimumMessages: 1), StubAuthoredMailTally.NobodyHasWritten);

        // Act
        await collector.CollectFromAsync(
            MessageFrom(displayName: null, "anna@example.test"),
            RunOver(collector, folderRole: null),
            TestContext.Current.CancellationToken);

        // Assert
        var recorded = Assert.Single(book.Contacts);
        Assert.Equal("anna@example.test", recorded.DisplayName.Value);
    }

    /// <summary>Two spellings of one mailbox in one header are one candidate, which is the rule the book itself matches by.</summary>
    [Fact]
    public async Task CollectFromAsync_OneMailboxWrittenTwice_RecordsOnePerson()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var collector = CollectorOver(book, SettingsCollecting(minimumMessages: 1), StubAuthoredMailTally.NobodyHasWritten);

        // Act
        await collector.CollectFromAsync(
            MessageWith(
                new EmailParticipant(EmailAddressRole.To, AddressOf("anna@example.test", "Anna Kowalska")),
                new EmailParticipant(EmailAddressRole.To, AddressOf("Anna@EXAMPLE.test", "A. Kowalska"))),
            RunOver(collector, MailFolderSpecialUse.Sent),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, book.ContactCount);
    }

    private static MailContactCollector CollectorOver(
        InMemoryContactBookStore book,
        ContactCollectionSettings settings,
        IAuthoredMailTally tally,
        IContactCollectionTelemetry? telemetry = null)
    {
        var principals = Substitute.For<IAuthorizedPrincipalSource>();
        principals.Current.Returns(AuthorizedPrincipal.Process);

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        var timeProvider = new FakeTimeProvider(Now);

        return new MailContactCollector(
            new ContactBook(
                book,
                book,
                new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), timeProvider),
                timeProvider,
                new AccessAuthorization(principals)),
            new StubContactCollectionSettingsReader(settings),
            tally,
            telemetry ?? new RecordingContactCollectionTelemetry());
    }

    private static ContactCollectionRun RunOver(MailContactCollector collector, MailFolderSpecialUse? folderRole) =>
        collector.OpenRun(MailAccountId.Create("primary"), folderRole);

    private static ContactCollectionSettings SettingsCollecting(
        int minimumMessages,
        ContactCollectionPolicy? policy = null,
        int maxContactsPerRun = 50) => new()
        {
            IsEnabled = true,
            MinimumMessagesFromSender = minimumMessages,
            MaxContactsPerRun = maxContactsPerRun,
            Policy = policy ?? ContactCollectionPolicy.Create([], []),
        };

    private static ExtractedEmailMetadata MessageFrom(string? displayName, string address) =>
        MessageWith(new EmailParticipant(EmailAddressRole.From, AddressOf(address, displayName)));

    private static ExtractedEmailMetadata MessageWith(params EmailParticipant[] participants) => new(
        EmailOccurrenceId.Create(
            MailAccountId.Create("primary"),
            new MailFolderResolutionId(MailFolderAlias.Create("inbox"), MailFolderResolutionGeneration.First),
            ImapUidValidity.Create(5),
            ImapUid.Create(11)),
        Subject: "Subject",
        SentAt: Now,
        ReceivedAt: Now,
        participants,
        EmailThreadReferences.None,
        EmailAttachmentSummary.None,
        ExtractedEmailText.NoTextualBody,
        SenderAuthentication.NotEstablished());

    private static EmailAddress AddressOf(string address, string? displayName)
    {
        Assert.True(EmailAddress.TryCreate(displayName, address, out var parsed));

        return parsed;
    }

    /// <summary>A session that commits, because what is under test is what collection decides rather than a lost race.</summary>
    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
