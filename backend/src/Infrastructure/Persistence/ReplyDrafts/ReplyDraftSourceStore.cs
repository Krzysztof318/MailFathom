// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.ReplyDrafts;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.ReplyDrafts;

/// <summary>Reads what one drafting is written from out of PostgreSQL: the conversation, its people, and the account's own manner.</summary>
/// <remarks>
/// <para>
/// Projections rather than entity loads, which is the same privacy control every other mail-derived read applies: each
/// query names the columns a drafting is shown, and no row enters the change tracker on a path that only reads.
/// </para>
/// <para>
/// The answered message decides everything. Its conversation is what the reply is grounded in, and its account is the
/// only one whose sent mail is read for a manner — asking the caller for either would let a reply be written in one
/// person's voice out of another's correspondence.
/// </para>
/// <para>
/// The style read is inside the caller's scope like every other read here, so a deployment that withholds the sent
/// folder from tools derives no manner from it rather than reaching around the withholding. A conversation whose sent
/// folder is unmapped is the same case and answers the same way: no samples, and a reply written from the
/// correspondence alone.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ReplyDraftSourceStore(
    MailFathomDbContext dbContext,
    IMailFolderMappingReader folderMappings,
    IOutgoingSenderIdentityReader senderIdentities)
    : IReplyDraftSourceReader
{
    /// <inheritdoc />
    public async Task<ReplyDraftSources?> ReadSourcesAsync(
        StoredEmailId answeredEmailId,
        MailboxScope scope,
        ReplyDraftBounds bounds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(bounds);

        var answered = await this.Readable(scope)
            .Where(email => email.Id == answeredEmailId.Value)
            .Select(email => new AnsweredEmailRow(email.MailboxAccountId, email.EmailThreadId))
            .SingleOrDefaultAsync(cancellationToken);

        if (answered is null)
        {
            return null;
        }

        // The recent end of the conversation rather than its opening, because a reply answers where an exchange got to.
        // Ordering descending and reversing afterwards is what makes the bound cut the beginning rather than the end.
        var recent = await this.ReadableWithText(scope, answered, answeredEmailId)
            .OrderByDescending(email => email.SentAt)
            .ThenByDescending(email => email.Id)
            .Take(bounds.MaximumMessages)
            .Select(email => new ConversationMessageRow(
                email.Id,
                email.Subject,
                email.SenderDisplayName,
                email.SenderAddress,
                email.SentAt,
                email.SearchDocument!.BodyText!.Substring(0, bounds.MaximumCharactersPerMessage)))
            .ToArrayAsync(cancellationToken);

        var conversation = recent.Reverse().ToArray();

        return new ReplyDraftSources(
            conversation.Length > 0 ? conversation[^1].Subject : null,
            [
                .. conversation.Select(static (row, position) => new ReplyDraftMessage(
                    StoredEmailId.Create(row.StoredEmailId),
                    position,
                    row.SenderDisplayName,
                    row.SentAt,
                    row.Text)),
            ],
            Participants(conversation, this.SendingAddressOf(answered.MailboxAccountId)),
            await this.StyleSamplesAsync(scope, answered, bounds, cancellationToken));
    }

    /// <summary>Reads the people the conversation names, leaving out the account whose reply this is.</summary>
    /// <remarks>
    /// Distinct by the normalized address rather than by the display name, because one correspondent writes under
    /// several spellings of their own name and a list holding each of them would offer the same person three times. The
    /// account's own sending address is left out for the reason nobody drafts a reply to themselves: it is the one
    /// address in the exchange that is never a recipient, and proposing it is a mistake a person then has to undo.
    /// </remarks>
    private static IReadOnlyList<ReplyDraftParticipant> Participants(
        IReadOnlyList<ConversationMessageRow> conversation,
        string? sendingNormalizedAddress) =>
    [
        .. conversation
            .Select(static row => row.SenderAddress)
            .OfType<string>()
            .Where(address => EmailAddress.TryCreate(displayName: null, address, out var parsed)
                && parsed.NormalizedAddress != sendingNormalizedAddress)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(
                (address, position) => new ReplyDraftParticipant(
                    position,
                    NamedIn(conversation, address))),
    ];

    /// <summary>Puts one address back together with the name the conversation most recently wrote it under.</summary>
    private static EmailAddress NamedIn(IReadOnlyList<ConversationMessageRow> conversation, string address)
    {
        var named = conversation
            .LastOrDefault(row => string.Equals(row.SenderAddress, address, StringComparison.OrdinalIgnoreCase));

        EmailAddress.TryCreate(named?.SenderDisplayName, address, out var parsed);

        return parsed;
    }

    /// <summary>Narrows stored mail to what this caller may read.</summary>
    /// <remarks>
    /// It composes the narrowing every mail-returning read composes and states none of its own, which is what stops a
    /// caller's entitlement being read twice and read differently. It is a member of this class rather than an
    /// expression inside an asynchronous method for the reason every other one is: a call made only inside an async
    /// method body belongs to the compiler-generated state machine, and the architecture rule holding every
    /// mail-derived read to this narrowing reads the class.
    /// </remarks>
    private IQueryable<StoredEmailEntity> Readable(MailboxScope scope) =>
        StoredEmailSelectionPredicate.WithinScope(dbContext.StoredEmails.AsNoTracking(), scope);

    /// <summary>Narrows that mail to the conversation being answered, and to the messages a body was extracted from.</summary>
    /// <remarks>
    /// A message whose body extraction has not reached it is in no turn, because a drafting shown a subject and nothing
    /// else would write a reply to a message it never read. A message belonging to no conversation is answered on its
    /// own, which is the ordinary shape of a first message somebody replies to.
    /// </remarks>
    private IQueryable<StoredEmailEntity> ReadableWithText(
        MailboxScope scope,
        AnsweredEmailRow answered,
        StoredEmailId answeredEmailId)
    {
        var readable = this.Readable(scope)
            .Where(email => email.SearchDocument != null && email.SearchDocument.BodyText != null);

        return answered.EmailThreadId is { } threadId
            ? readable.Where(email => email.EmailThreadId == threadId)
            : readable.Where(email => email.Id == answeredEmailId.Value);
    }

    /// <summary>Reads the openings of the account's own recent sent mail, or nothing where this deployment derives no manner.</summary>
    /// <remarks>
    /// The conversation being answered is left out of the sample. Its messages are already in the turn, and sending
    /// them twice would spend the request on text the drafting has, while making the person's own last reply the
    /// loudest example of how they write.
    /// </remarks>
    private async Task<IReadOnlyList<string>> StyleSamplesAsync(
        MailboxScope scope,
        AnsweredEmailRow answered,
        ReplyDraftBounds bounds,
        CancellationToken cancellationToken)
    {
        if (bounds.MaximumStyleMessages <= 0)
        {
            return [];
        }

        var accountId = MailAccountId.Create(answered.MailboxAccountId);

        if (folderMappings.FindFolderPlayingRole(accountId, MailFolderSpecialUse.Sent) is not { } sentFolder)
        {
            return [];
        }

        var alias = sentFolder.Alias.Value;
        var answeredThreadId = answered.EmailThreadId;

        return await this.Readable(scope)
            .Where(email => email.MailboxAccountId == answered.MailboxAccountId
                && email.MailFolder.Alias == alias
                && email.SearchDocument != null
                && email.SearchDocument.BodyText != null
                && (answeredThreadId == null || email.EmailThreadId != answeredThreadId))
            .OrderByDescending(email => email.SentAt)
            .ThenByDescending(email => email.Id)
            .Take(bounds.MaximumStyleMessages)
            .Select(email => email.SearchDocument!.BodyText!.Substring(0, bounds.MaximumStyleCharactersPerMessage))
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>Reads the address the answering account sends from, which is the one participant a reply never proposes.</summary>
    private string? SendingAddressOf(string mailboxAccountId) =>
        senderIdentities.FindSenderIdentity(MailAccountId.Create(mailboxAccountId))?.Address.NormalizedAddress;

    private sealed record AnsweredEmailRow(string MailboxAccountId, Guid? EmailThreadId);

    private sealed record ConversationMessageRow(
        Guid StoredEmailId,
        string? Subject,
        string? SenderDisplayName,
        string? SenderAddress,
        DateTimeOffset? SentAt,
        string Text);
}
