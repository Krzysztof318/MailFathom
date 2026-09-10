// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Notifications;

namespace MailFathom.Application.Signals;

/// <summary>One statement that something changed for one user, carrying no mail.</summary>
/// <remarks>
/// <para>
/// A signal is an instruction to look again rather than a payload to keep. It names what changed and for whom, and the
/// client re-reads over the authenticated routes it already has — the one exception being
/// <see cref="ClientSignalKind.MailFlagsChanged" />, which states where two flags stand because they are the only
/// change a client can apply without deciding whether a row still belongs where it is drawn. That keeps
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0028-no-mail-on-the-device-and-an-honest-client-with-no-route-to-its-deployment.md">ADR 0028</see>
/// intact: nothing new is stored on a device, and a client whose channel is down behaves exactly as one that never had
/// it.
/// </para>
/// <para>
/// <b>No mail crosses.</b> A count, an account alias, a folder alias, a stored identity, a server flag, and a state are
/// the whole vocabulary; no subject, address, body fragment, filename, attachment name, or snippet reaches a signal at
/// any size.
/// The one exception is <see cref="Headline" /> and <see cref="SecondLine" /> on
/// <see cref="ClientSignalKind.NotificationRaised" />, which are the notification record's own already-derived text and
/// reach a client that is entitled to read that record over its own route.
/// </para>
/// <para>
/// The factories are the only way to compose one, so which fields a kind carries is decided here rather than by each
/// caller, and a kind that carries nothing about mail cannot be handed something about mail by accident.
/// </para>
/// </remarks>
public sealed class ClientSignal
{
    /// <summary>The most stored identities one signal names before it stops naming them individually.</summary>
    /// <remarks>
    /// A bound at a boundary rather than a tuning value: a reconciliation window can attribute changes to a few hundred
    /// occurrences at once, and a client acting on such a signal re-reads the scope rather than each row anyway. Beyond
    /// this many the signal still names the account and the folder, which is enough for the client to re-read what it
    /// is showing.
    /// </remarks>
    public const int MostNamedEmails = 100;

    private ClientSignal(
        ClientSignalKind kind,
        MailUserId user,
        MailAccountId? account,
        MailFolderAlias? folder,
        int count,
        IReadOnlyList<StoredEmailId> emails,
        IReadOnlyList<SignalledEmailFlags> flags,
        NotificationKind? notificationKind,
        string? headline,
        string? secondLine)
    {
        this.Kind = kind;
        this.User = user;
        this.Account = account;
        this.Folder = folder;
        this.Count = count;
        this.Emails = emails;
        this.Flags = flags;
        this.NotificationKind = notificationKind;
        this.Headline = headline;
        this.SecondLine = secondLine;
    }

    /// <summary>Gets which of the six kinds this is.</summary>
    public ClientSignalKind Kind { get; }

    /// <summary>Gets the user whose connections this reaches, and no other's.</summary>
    public MailUserId User { get; }

    /// <summary>Gets the account the change is in, where the kind names one.</summary>
    public MailAccountId? Account { get; }

    /// <summary>Gets the folder the change is in, where the kind names one.</summary>
    public MailFolderAlias? Folder { get; }

    /// <summary>Gets how many things the change covers, which is the run's arrival count or the unread notification count.</summary>
    public int Count { get; }

    /// <summary>Gets the stored identities the change names, bounded by <see cref="MostNamedEmails" /> and empty for every other kind.</summary>
    public IReadOnlyList<StoredEmailId> Emails { get; }

    /// <summary>Gets where the two server flags now stand for each email a flag change names, bounded by <see cref="MostNamedEmails" /> and empty for every other kind.</summary>
    public IReadOnlyList<SignalledEmailFlags> Flags { get; }

    /// <summary>Gets which kind of notification was written, where the kind reports one.</summary>
    public NotificationKind? NotificationKind { get; }

    /// <summary>Gets the notification's own headline, and nothing for every other kind.</summary>
    public string? Headline { get; }

    /// <summary>Gets the notification's own second line, and nothing for every other kind.</summary>
    public string? SecondLine { get; }

    /// <summary>Gets the scope two signals must share before one folds into the other.</summary>
    internal ClientSignalScope Scope => new(this.User, this.Kind, this.Account, this.Folder);

    /// <summary>States that a synchronization run committed mail into one folder.</summary>
    /// <param name="account">The account the run was over.</param>
    /// <param name="folder">The folder the mail was committed into.</param>
    /// <param name="newEmailCount">How many occurrences the run committed there.</param>
    /// <returns>The signal.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="newEmailCount" /> is not positive, an arrival of nothing being no arrival.</exception>
    public static ClientSignal MailArrived(MailAccountIdentity account, MailFolderAlias folder, int newEmailCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newEmailCount);

        return new ClientSignal(
            ClientSignalKind.MailArrived,
            account.User,
            account.Id,
            folder,
            newEmailCount,
            emails: [],
            flags: [],
            notificationKind: null,
            headline: null,
            secondLine: null);
    }

    /// <summary>States that stored mail in one folder is no longer what a client last read.</summary>
    /// <param name="account">The account the change is in.</param>
    /// <param name="folder">The folder the change is in.</param>
    /// <param name="emails">The occurrences affected, taken up to <see cref="MostNamedEmails" />.</param>
    /// <returns>The signal.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="emails" /> is <see langword="null" />.</exception>
    public static ClientSignal MailChanged(
        MailAccountIdentity account,
        MailFolderAlias folder,
        IEnumerable<StoredEmailId> emails)
    {
        ArgumentNullException.ThrowIfNull(emails);

        return new ClientSignal(
            ClientSignalKind.MailChanged,
            account.User,
            account.Id,
            folder,
            count: 0,
            [.. emails.Distinct().Take(MostNamedEmails)],
            flags: [],
            notificationKind: null,
            headline: null,
            secondLine: null);
    }

    /// <summary>States where the <c>\Seen</c> and <c>\Flagged</c> flags of one folder's mail now stand, nothing else about it having moved.</summary>
    /// <param name="account">The account the change is in.</param>
    /// <param name="folder">The folder the change is in.</param>
    /// <param name="flags">Where each affected email's flags now stand, taken up to <see cref="MostNamedEmails" />.</param>
    /// <returns>The signal.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="flags" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when nothing is stated: an empty list, or one whose every entry is silent about both flags.</exception>
    /// <remarks>
    /// It is composed only where nothing but those two flags moved. A message that also changed folder, was deleted, or
    /// whose keywords moved is a <see cref="MailChanged" />, because a client applying a flag in place has decided that
    /// the row still belongs where it is drawn — and only a flag makes that true.
    /// </remarks>
    public static ClientSignal MailFlagsChanged(
        MailAccountIdentity account,
        MailFolderAlias folder,
        IEnumerable<SignalledEmailFlags> flags)
    {
        ArgumentNullException.ThrowIfNull(flags);

        IReadOnlyList<SignalledEmailFlags> stated =
        [
            .. flags
                .Where(static flag => flag.SaysAnything)
                .DistinctBy(static flag => flag.Email)
                .Take(MostNamedEmails),
        ];

        if (stated.Count == 0)
        {
            throw new ArgumentException(
                "A flag change states where a flag now stands, so one stating neither flag of any email says nothing a client could apply.",
                nameof(flags));
        }

        return new ClientSignal(
            ClientSignalKind.MailFlagsChanged,
            account.User,
            account.Id,
            folder,
            count: 0,
            emails: [],
            stated,
            notificationKind: null,
            headline: null,
            secondLine: null);
    }

    /// <summary>States that the set of folders an account mirrors has moved.</summary>
    /// <param name="account">The account whose folder set moved.</param>
    /// <returns>The signal.</returns>
    public static ClientSignal FoldersChanged(MailAccountIdentity account) =>
        new(
            ClientSignalKind.FoldersChanged,
            account.User,
            account.Id,
            folder: null,
            count: 0,
            emails: [],
            flags: [],
            notificationKind: null,
            headline: null,
            secondLine: null);

    /// <summary>States that a notification record was written for one person.</summary>
    /// <param name="notification">The row that was written, whose user and already-derived text this carries.</param>
    /// <param name="unreadCount">How many of that person's notifications now stand unread.</param>
    /// <returns>The signal.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="notification" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="unreadCount" /> is negative.</exception>
    /// <remarks>The headline and the second line are the record's own, composed when it was raised and bounded by the same columns the notification routes already serve them from, so nothing here reads mail to compose them.</remarks>
    public static ClientSignal NotificationRaised(Notification notification, int unreadCount)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentOutOfRangeException.ThrowIfNegative(unreadCount);

        return new ClientSignal(
            ClientSignalKind.NotificationRaised,
            notification.User,
            account: null,
            folder: null,
            unreadCount,
            emails: [],
            flags: [],
            notification.Kind,
            notification.Title,
            notification.Body);
    }

    /// <summary>States that an account's synchronization run finished, so what the client says about it is out of date.</summary>
    /// <param name="account">The account the run was over.</param>
    /// <returns>The signal.</returns>
    /// <remarks>
    /// It names the account and says nothing about the state it is in, deliberately. What state an account is in — the
    /// four-way reading, when it last committed progress, and whether it is behind — is derived in exactly one place,
    /// and a run deriving it a second way here would be two reductions of one question waiting to disagree. The client
    /// re-reads the accounts behind its freshness line, which is what it does with this signal anyway.
    /// </remarks>
    public static ClientSignal AccountState(MailAccountIdentity account) =>
        new(
            ClientSignalKind.AccountState,
            account.User,
            account.Id,
            folder: null,
            count: 0,
            emails: [],
            flags: [],
            notificationKind: null,
            headline: null,
            secondLine: null);

    /// <summary>Folds a later signal of the same scope into this one, so a window produces one statement rather than many.</summary>
    /// <param name="later">The signal raised after this one, in the same scope.</param>
    /// <returns>The one signal that says what both said.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="later" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the two are not in one scope, which is a caller folding two statements that are not about the same thing.</exception>
    /// <remarks>
    /// Counts add, because two runs that committed mail into one folder committed the sum of them. Named identities
    /// join, up to the same bound one signal carries. Stated flags join by email, each value taking the later
    /// statement about it and keeping what that statement was silent about, so a window that reported both flags is not
    /// erased by an authored change that reported one. Everything else takes the later value, because a state, a
    /// headline, and an unread count each describe a moment rather than an accumulation — and the later moment is the
    /// true one.
    /// </remarks>
    internal ClientSignal FoldedWith(ClientSignal later)
    {
        ArgumentNullException.ThrowIfNull(later);

        if (later.Scope != this.Scope)
        {
            throw new ArgumentException(
                "Two signals fold only within one scope; folding across scopes would lose which place a client was told to look at.",
                nameof(later));
        }

        return new ClientSignal(
            this.Kind,
            this.User,
            this.Account,
            this.Folder,
            this.Kind == ClientSignalKind.MailArrived ? this.Count + later.Count : later.Count,
            [.. this.Emails.Concat(later.Emails).Distinct().Take(MostNamedEmails)],
            FoldedFlags(this.Flags, later.Flags),
            later.NotificationKind,
            later.Headline,
            later.SecondLine);
    }

    /// <summary>Joins two windows' flag statements, one entry per email, bounded like every other list a signal carries.</summary>
    /// <remarks>The order the earlier statement established is kept, so an email folded into is not moved to the end of the list and pushed past the bound by one a client had already been told about.</remarks>
    private static IReadOnlyList<SignalledEmailFlags> FoldedFlags(
        IReadOnlyList<SignalledEmailFlags> earlier,
        IReadOnlyList<SignalledEmailFlags> later)
    {
        if (later.Count == 0)
        {
            return earlier;
        }

        return
        [
            .. earlier
                .Concat(later)
                .GroupBy(static flag => flag.Email)
                .Select(static statements => statements.Aggregate(static (held, flag) => held.FoldedWith(flag)))
                .Take(MostNamedEmails),
        ];
    }
}

/// <summary>What two signals must share before one folds into the other: whose it is, what kind it is, and where it happened.</summary>
/// <param name="User">Whose mail the statement is about.</param>
/// <param name="Kind">Which of the six kinds it is.</param>
/// <param name="Account">The account it names, where the kind names one.</param>
/// <param name="Folder">The folder it names, where the kind names one.</param>
/// <remarks>Declared once and read from both sides of the fold — the buffer keys on it and <see cref="ClientSignal.FoldedWith" /> refuses a pair that does not share it — so the two can never come to disagree about what one scope is. The place is part of it deliberately: folding two folders' arrivals into one would leave a client told that mail arrived without being told where to look.</remarks>
internal readonly record struct ClientSignalScope(
    MailUserId User,
    ClientSignalKind Kind,
    MailAccountId? Account,
    MailFolderAlias? Folder);
