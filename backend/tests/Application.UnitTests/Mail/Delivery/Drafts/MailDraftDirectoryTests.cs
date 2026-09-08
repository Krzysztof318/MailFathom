// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Screening;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Drafts;

/// <summary>Covers the reading that turns an identifier into a draft the caller's own user holds.</summary>
/// <remarks>
/// Every test here is about the user axis, because that is the whole of what this reading adds to the store beneath
/// it: the store answers about any draft it holds, and this answers about the caller's.
/// </remarks>
public sealed class MailDraftDirectoryTests
{
    private static readonly DateTimeOffset Moment = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Work = MailAccountId.Create("work");
    private static readonly MailAccountId Personal = MailAccountId.Create("personal");

    /// <summary>A listing answers with this user's drafts and with none of anybody else's.</summary>
    [Fact]
    public async Task ReadAsync_DraftsOfSeveralUsers_AnswersOnlyTheOnesTheCallersUserHolds()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var mine = await OpenAsync(drafts, SyntheticMailUser.Deployment, Work, "mine");
        await OpenAsync(drafts, SyntheticMailUser.Another, Work, "somebody else's");

        var directory = DirectoryOver(drafts);

        // Act
        var listed = await directory.ReadAsync(account: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([mine.Id], listed.Select(draft => draft.Id));
    }

    /// <summary>A listing narrowed to an account this user owns answers with that account's drafts alone.</summary>
    [Fact]
    public async Task ReadAsync_NarrowedToOneAccount_AnswersWithThatAccountsDraftsAlone()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var atWork = await OpenAsync(drafts, SyntheticMailUser.Deployment, Work, "at work");
        await OpenAsync(drafts, SyntheticMailUser.Deployment, Personal, "at home");

        var directory = DirectoryOver(drafts);

        // Act
        var listed = await directory.ReadAsync(
            MailAccountSelector.For(Work),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([atWork.Id], listed.Select(draft => draft.Id));
    }

    /// <summary>An account another user owns is refused exactly as one this deployment does not serve.</summary>
    [Fact]
    public async Task ReadAsync_AnAccountAnotherUserOwns_IsRefusedAsOneThisUserDoesNotOwn()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var directory = DirectoryOver(drafts);

        // Act
        var refusal = () => directory.ReadAsync(
            MailAccountSelector.For(MailAccountId.Create("theirs")),
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<MailAccountNotAccessibleException>(refusal);
    }

    /// <summary>Reading somebody's drafts asks for the drafting grant rather than for the grant that reads mail.</summary>
    [Fact]
    public async Task ReadAsync_CallerHoldingOnlyTheReadingGrant_IsRefused()
    {
        // Arrange
        var directory = DirectoryOver(
            new InMemoryMailDraftStore(),
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

        // Act
        var refusal = () => directory.ReadAsync(account: null, TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(refusal);
    }

    /// <summary>A draft another user holds answers exactly as one nobody holds.</summary>
    [Fact]
    public async Task FindAsync_ADraftAnotherUserHolds_AnswersAsOneNobodyHolds()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var theirs = await OpenAsync(drafts, SyntheticMailUser.Another, Work, "somebody else's");

        var directory = DirectoryOver(drafts);

        // Act
        var found = await directory.FindAsync(theirs.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(found);
    }

    /// <summary>Opening a draft answers with the words its stored message carries rather than a second copy of them.</summary>
    /// <remarks>
    /// It asserts which of the port's two reads was asked as well as what came back: the reader here throws from
    /// <c>ReadForScreeningAsync</c>, so a read-back that reached for it would fail this test rather than pass while
    /// running a document parser per attachment on a request that discards the result.
    /// </remarks>
    [Fact]
    public async Task ReadComposedAsync_ADraftThisUserHolds_AnswersWithWhatTheStoredMessageSays()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var contents = new InMemoryMailDraftContentStore();
        var draft = await OpenAsync(drafts, SyntheticMailUser.Deployment, Work, "a draft");

        await contents.SaveMailDraftContentAsync(
            Substitute.For<IPersistenceSession>(),
            draft.Id,
            PlacedEmailContent.InDatabase(Encoding.ASCII.GetBytes("Subject: a draft\r\n\r\nHello.").AsMemory()),
            TestContext.Current.CancellationToken);

        var directory = DirectoryOver(drafts, contents);

        // Act
        var reading = await directory.ReadComposedAsync(draft.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(reading);
        Assert.Equal(draft.Id, reading.Draft.Id);
        Assert.Equal("a draft", reading.Text.Subject);
        Assert.Equal("Hello.", reading.Text.PlainTextBody);
    }

    /// <summary>A draft whose stored message has gone answers as one this user does not hold.</summary>
    [Fact]
    public async Task ReadComposedAsync_ADraftWhoseStoredMessageIsGone_AnswersAsOneNobodyHolds()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var draft = await OpenAsync(drafts, SyntheticMailUser.Deployment, Work, "a draft");

        var directory = DirectoryOver(drafts);

        // Act
        var reading = await directory.ReadComposedAsync(draft.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(reading);
    }

    /// <summary>Builds the reading over the store a test arranged, for a caller acting for the deployment's user.</summary>
    private static MailDraftDirectory DirectoryOver(
        InMemoryMailDraftStore drafts,
        IEmailContentStore? contents = null,
        AccessAuthorization? authorization = null)
    {
        var callerAuthorization =
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailDraftsWrite);

        return new MailDraftDirectory(
            OwnedMailAccountCatalogs.For(
                callerAuthorization,
                SyntheticServedAccount.Of(Work),
                SyntheticServedAccount.Of(Personal)),
            drafts,
            contents ?? new InMemoryMailDraftContentStore(),
            new HeaderReadingOutgoingMailText(),
            callerAuthorization);
    }

    /// <summary>Writes one draft down for one user, which is the arrangement every test here starts from.</summary>
    private static Task<MailDraftRecord> OpenAsync(
        InMemoryMailDraftStore drafts,
        MailUserId user,
        MailAccountId accountId,
        string subject) =>
        drafts.OpenAsync(
            Substitute.For<IPersistenceSession>(),
            MailAccountIdentity.Create(user, accountId),
            OutgoingEmailRequester.Command($"mfctl-{subject}"),
            [],
            subject,
            mimeByteLength: 64,
            Moment,
            TestContext.Current.CancellationToken);

    /// <summary>Reads a composed message the way the MIME adapter does, over the trivial messages these tests write.</summary>
    /// <remarks>
    /// A hand-written reader rather than a substitute, because what the tests assert is that the words come out of the
    /// stored bytes: a substituted reader would answer whatever the arrangement said and prove nothing about that.
    /// </remarks>
    private sealed class HeaderReadingOutgoingMailText : IOutgoingMailTextReader
    {
        /// <summary>
        /// It throws rather than answering, so a read-back reaching for it fails instead of passing quietly. Reading a
        /// draft back for its author discards every attachment field, so asking the screening read for one would run a
        /// document parser per attachment on a request nothing screens — and both reads answering the same thing is
        /// exactly what would hide that.
        /// </summary>
        public Task<OutgoingMailText> ReadForScreeningAsync(
            ReadOnlyMemory<byte> rawMime,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Reading a draft back for its author asks for the message's words, never for a screening read.");

        public Task<OutgoingMailText> ReadWordsAsync(ReadOnlyMemory<byte> rawMime, CancellationToken cancellationToken)
        {
            var message = Encoding.ASCII.GetString(rawMime.Span).Split("\r\n\r\n", 2);
            var subject = message[0].StartsWith("Subject: ", StringComparison.Ordinal)
                ? message[0]["Subject: ".Length..]
                : string.Empty;

            return Task.FromResult(new OutgoingMailText(subject, message[1], HtmlBody: null));
        }
    }
}
