// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Mail;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Extraction;

/// <summary>Covers the seam at which an authenticated author is held against what the receiving account recognizes.</summary>
public sealed class SenderTrustEvaluatingEmailMimeReaderTests
{
    /// <summary>The account's own list is what judges the message, and the verdict says which half named the author.</summary>
    [Fact]
    public async Task ReadMetadataAsync_AnAuthorTheReceivingAccountRecognizes_IsRecordedAsTrusted()
    {
        // Arrange
        var policy = PolicyRecognizing("partner.example");
        var reader = new SenderTrustEvaluatingEmailMimeReader(
            ReaderYielding(WrittenBy("partner.example"), "alice@partner.example"),
            PolicyReaderFor("primary", policy));

        // Act
        var extraction = await reader.ReadMetadataAsync(Content(), SyntheticMailOwner.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, extraction.Metadata?.SenderTrust.Level);
        Assert.Equal(
            SenderTrustSource.ConfiguredTrustedSender,
            extraction.Metadata?.SenderTrust.GrantedBy);
        Assert.Equal(policy.Revision, extraction.Metadata?.SenderTrust.PolicyRevision);
    }

    /// <summary>Most legitimate mail comes from somebody nobody named, and that answer is recorded rather than left blank.</summary>
    [Fact]
    public async Task ReadMetadataAsync_AnAuthorNobodyNamed_IsRecordedAsUnknownUnderThePolicyThatJudgedIt()
    {
        // Arrange
        var policy = PolicyRecognizing("partner.example");
        var reader = new SenderTrustEvaluatingEmailMimeReader(
            ReaderYielding(WrittenBy("stranger.example"), "bob@stranger.example"),
            PolicyReaderFor("primary", policy));

        // Act
        var extraction = await reader.ReadMetadataAsync(Content(), SyntheticMailOwner.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, extraction.Metadata?.SenderTrust.Level);
        Assert.Equal(policy.Revision, extraction.Metadata?.SenderTrust.PolicyRevision);
    }

    /// <summary>An address entry needs the mailbox a client displays, which is read from <c>From</c> and not from <c>Sender</c>.</summary>
    [Fact]
    public async Task ReadMetadataAsync_AnAddressEntryAndAFromHeaderNamingIt_IsRecordedAsTrusted()
    {
        // Arrange
        Assert.True(TrustedSenderEntry.TryCreateForAddress("alice@partner.example", out var entry));
        Assert.NotNull(entry);
        var policy = SenderTrustPolicy.Create([], [entry], []);
        var reader = new SenderTrustEvaluatingEmailMimeReader(
            ReaderYielding(WrittenBy("partner.example"), from: "alice@partner.example", sender: "relay@partner.example"),
            PolicyReaderFor("primary", policy));

        // Act
        var extraction = await reader.ReadMetadataAsync(Content(), SyntheticMailOwner.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, extraction.Metadata?.SenderTrust.Level);
    }

    /// <summary>Reading the submitting mailbox instead of the displayed one would recognize an author nobody named.</summary>
    [Fact]
    public async Task ReadMetadataAsync_AnAddressEntryNamingOnlyTheSenderHeader_IsNotRecognized()
    {
        // Arrange
        Assert.True(TrustedSenderEntry.TryCreateForAddress("relay@partner.example", out var entry));
        Assert.NotNull(entry);
        var policy = SenderTrustPolicy.Create([], [entry], []);
        var reader = new SenderTrustEvaluatingEmailMimeReader(
            ReaderYielding(WrittenBy("partner.example"), from: "alice@partner.example", sender: "relay@partner.example"),
            PolicyReaderFor("primary", policy));

        // Act
        var extraction = await reader.ReadMetadataAsync(Content(), SyntheticMailOwner.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, extraction.Metadata?.SenderTrust.Level);
    }

    /// <summary>A message can display only one author, so the first mailbox the header carried is the one judged.</summary>
    [Fact]
    public async Task ReadMetadataAsync_SeveralFromParticipants_JudgesTheFirstOne()
    {
        // Arrange
        Assert.True(TrustedSenderEntry.TryCreateForAddress("alice@partner.example", out var entry));
        Assert.NotNull(entry);
        var reader = new SenderTrustEvaluatingEmailMimeReader(
            ReaderYielding(
                WrittenBy("partner.example"),
                [
                    new EmailParticipant(EmailAddressRole.From, AddressOf("alice@partner.example")),
                    new EmailParticipant(EmailAddressRole.From, AddressOf("mallory@partner.example")),
                ]),
            PolicyReaderFor("primary", SenderTrustPolicy.Create([], [entry], [])));

        // Act
        var extraction = await reader.ReadMetadataAsync(Content(), SyntheticMailOwner.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, extraction.Metadata?.SenderTrust.Level);
    }

    /// <summary>A message displaying no author at all is judged rather than refused, and recognizes nobody.</summary>
    [Fact]
    public async Task ReadMetadataAsync_NoFromParticipant_IsRecordedAsUnknown()
    {
        // Arrange
        Assert.True(TrustedSenderEntry.TryCreateForAddress("alice@partner.example", out var entry));
        Assert.NotNull(entry);
        var policy = SenderTrustPolicy.Create([], [entry], []);
        var reader = new SenderTrustEvaluatingEmailMimeReader(
            ReaderYielding(
                WrittenBy("partner.example"),
                [new EmailParticipant(EmailAddressRole.To, AddressOf("owner@work.example"))]),
            PolicyReaderFor("primary", policy));

        // Act
        var extraction = await reader.ReadMetadataAsync(Content(), SyntheticMailOwner.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, extraction.Metadata?.SenderTrust.Level);
        Assert.Equal(policy.Revision, extraction.Metadata?.SenderTrust.PolicyRevision);
    }

    /// <summary>Content nobody could parse carries no author to judge, and the failure has to reach the caller unchanged.</summary>
    [Fact]
    public async Task ReadMetadataAsync_ContentNoReaderCouldParse_IsCarriedThroughAsTheFailureItIs()
    {
        // Arrange
        var policies = Substitute.For<ISenderTrustPolicyReader>();
        var inner = Substitute.For<IEmailMimeReader>();
        inner.ReadMetadataAsync(Arg.Any<RemoteEmailContent>(), Arg.Any<MailOwnerId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailMimeExtractionResult.MalformedContent()));
        var reader = new SenderTrustEvaluatingEmailMimeReader(inner, policies);

        // Act
        var extraction = await reader.ReadMetadataAsync(Content(), SyntheticMailOwner.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailMimeExtractionOutcome.MalformedContent, extraction.Outcome);
        policies.DidNotReceive().GetTrustPolicy(Arg.Any<MailAccountId>());
    }

    /// <summary>The verdict the parsing reader established is what the decision reads, and it is left exactly as it was.</summary>
    [Fact]
    public async Task ReadMetadataAsync_AnyMessage_LeavesWhatTheServerEstablishedUntouched()
    {
        // Arrange
        var authentication = WrittenBy("partner.example");
        var reader = new SenderTrustEvaluatingEmailMimeReader(
            ReaderYielding(authentication, "alice@partner.example"),
            PolicyReaderFor("primary", PolicyRecognizing("partner.example")));

        // Act
        var extraction = await reader.ReadMetadataAsync(Content(), SyntheticMailOwner.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(authentication, extraction.Metadata?.SenderAuthentication);
        Assert.Equal("Subject", extraction.Metadata?.Subject);
    }

    private static ISenderTrustPolicyReader PolicyReaderFor(string accountId, SenderTrustPolicy policy)
    {
        var policies = Substitute.For<ISenderTrustPolicyReader>();

        policies.GetTrustPolicy(MailAccountId.Create(accountId)).Returns(policy);

        return policies;
    }

    private static SenderTrustPolicy PolicyRecognizing(string domain)
    {
        Assert.True(TrustedSenderEntry.TryCreateForDomain(domain, includeSubdomains: false, out var entry));
        Assert.NotNull(entry);

        return SenderTrustPolicy.Create([], [entry], []);
    }

    /// <summary>Builds the verdict of a message whose displayed author the receiving server established.</summary>
    private static SenderAuthentication WrittenBy(string domain)
    {
        Assert.True(SenderDomain.TryCreate(domain, out var author));

        return SenderAuthentication.Authenticated([author], spfDomains: [], author, DmarcOutcome.Pass);
    }

    private static IEmailMimeReader ReaderYielding(
        SenderAuthentication authentication,
        string from,
        string? sender = null) =>
        ReaderYielding(authentication, ParticipantsOf(from, sender));

    private static IEmailMimeReader ReaderYielding(
        SenderAuthentication authentication,
        IReadOnlyList<EmailParticipant> participants)
    {
        var reader = Substitute.For<IEmailMimeReader>();

        reader.ReadMetadataAsync(Arg.Any<RemoteEmailContent>(), Arg.Any<MailOwnerId>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(EmailMimeExtractionResult.Extracted(new ExtractedEmailMetadata(
                call.Arg<RemoteEmailContent>()!.OccurrenceId,
                Subject: "Subject",
                SentAt: null,
                ReceivedAt: null,
                participants,
                EmailThreadReferences.None,
                EmailAttachmentSummary.None,
                ExtractedEmailText.FromPlainTextBody("body", "body"),
                authentication))));

        return reader;
    }

    private static IReadOnlyList<EmailParticipant> ParticipantsOf(string from, string? sender) => sender is null
        ? [new EmailParticipant(EmailAddressRole.From, AddressOf(from))]
        :
        [
            new EmailParticipant(EmailAddressRole.Sender, AddressOf(sender)),
            new EmailParticipant(EmailAddressRole.From, AddressOf(from)),
        ];

    private static EmailAddress AddressOf(string written)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, written, out var address));

        return address;
    }

    private static RemoteEmailContent Content() => new(
        EmailOccurrenceId.Create(
            MailAccountId.Create("primary"),
            new MailFolderResolutionId(MailFolderAlias.Create("inbox"), MailFolderResolutionGeneration.First),
            ImapUidValidity.Create(5),
            ImapUid.Create(11)),
        new byte[] { 1, 2, 3 });
}
