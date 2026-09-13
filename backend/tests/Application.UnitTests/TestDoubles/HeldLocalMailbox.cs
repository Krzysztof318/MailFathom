// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using NSubstitute;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Stands in for one account whose mailbox MailFathom holds alone, so a test can read what was filed locally.</summary>
/// <remarks>
/// The folders and their placements are the in-memory store; the stored message is a substitute that answers each filing
/// with a new identity and records it, because what a filing test asserts is which message was stored and where it went.
/// No role is mapped until <see cref="MapRole" /> says so, which is the arrangement of a filing with nowhere to go.
/// </remarks>
internal sealed class HeldLocalMailbox
{
    private readonly TimeProvider clock;
    private readonly InMemoryMailFolderResolutionStore folderResolutions = new();
    private readonly IEmailMimeReader mimeReader = Substitute.For<IEmailMimeReader>();
    private readonly IOutgoingMailFilingPolicyReader filingPolicies = Substitute.For<IOutgoingMailFilingPolicyReader>();
    private StubMailFolderMappings mappings = StubMailFolderMappings.Nothing;

    internal HeldLocalMailbox(MailAccountIdentity account, TimeProvider clock)
    {
        this.Account = account;
        this.clock = clock;
        this.Folders = new InMemoryLocalMailFolderStore(account, MailAccountCustodyPhase.Held);

        this.mimeReader
            .ReadMetadataAsync(Arg.Any<MailAccountIdentity>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(EmailMimeExtractionResult.Extracted(new ExtractedEmailMetadata(
                account.Id,
                Subject: "Filed",
                SentAt: null,
                ReceivedAt: null,
                Participants: [],
                EmailThreadReferences.None,
                EmailAttachmentSummary.None,
                ExtractedEmailText.NoTextualBody,
                SenderAuthentication.NotEstablished())));

        this.filingPolicies.FilesSentCopy(Arg.Any<MailAccountId>()).Returns(_ => this.FilesSentCopy);

        this.Emails
            .StoreFiledEmailAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<MailAccountIdentity>(),
                Arg.Any<MailFolderResolutionId>(),
                Arg.Any<ExtractedEmailMetadata?>(),
                Arg.Any<long>(),
                Arg.Any<AppendedMailFlags>(),
                Arg.Any<OutgoingEmailId?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var stored = StoredEmailId.Create(Guid.CreateVersion7(this.clock.GetUtcNow()));
                this.Stored.Add((stored, call.ArgAt<AppendedMailFlags>(5), call.ArgAt<OutgoingEmailId?>(6)));

                return stored;
            });
    }

    internal MailAccountIdentity Account { get; }

    /// <summary>Gets the account's local folders and what was placed in them.</summary>
    internal InMemoryLocalMailFolderStore Folders { get; }

    /// <summary>Gets the stored message port, for a test asserting it was never asked for something.</summary>
    internal IEmailMetadataRepository Emails { get; } = Substitute.For<IEmailMetadataRepository>();

    /// <summary>Gets where a failure to prepare a sent copy is recorded.</summary>
    internal IOutgoingMailFilingStore Filings { get; } = Substitute.For<IOutgoingMailFilingStore>();

    /// <summary>Gets every message stored, in the order it was filed, with the flags and the send it was filed with.</summary>
    internal List<(StoredEmailId Email, AppendedMailFlags Flags, OutgoingEmailId? FiledFrom)> Stored { get; } = [];

    /// <summary>Gets or sets whether the account files a copy of what it sends, which defaults to yes.</summary>
    internal bool FilesSentCopy { get; set; } = true;

    /// <summary>Maps and binds a source folder playing one role, which is what gives a filing somewhere to go.</summary>
    /// <param name="role">The role the folder plays.</param>
    /// <param name="alias">MailFathom's own name for it.</param>
    internal void MapRole(MailFolderSpecialUse role, string alias)
    {
        var folderAlias = MailFolderAlias.Create(alias);

        this.mappings = this.mappings.With(
            this.Account.Id,
            MailFolderMapping.ToRemotePath(
                folderAlias,
                RemoteFolderPath.Create(alias),
                MailFolderParticipation.Full,
                mayCreateMissingFolder: false,
                role));

        this.folderResolutions.Bind(this.Account.Id, folderAlias);
    }

    /// <summary>Withdraws every role mapping, which is the arrangement of a folder the operator stopped mapping.</summary>
    internal void UnmapRoles() => this.mappings = StubMailFolderMappings.Nothing;

    /// <summary>Builds the filer over this mailbox as it is mapped now.</summary>
    /// <param name="contents">The payload store, which is the one the calling harness reads back.</param>
    /// <returns>The filer.</returns>
    internal LocalMailFiler FilerOver(IEmailContentStore contents) => new(
        this.Folders,
        this.Emails,
        contents,
        this.mimeReader,
        this.mappings,
        this.folderResolutions,
        this.filingPolicies,
        this.Filings,
        ClientSignalPublishers.ReachingNobody,
        this.clock);
}
