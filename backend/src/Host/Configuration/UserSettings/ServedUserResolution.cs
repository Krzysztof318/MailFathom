// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Records;
using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Composes one user this deployment serves from the document their own row holds, which is the whole of what it knows about them.</summary>
/// <remarks>
/// <para>
/// The document is put through the one binder both directions share, so what a user's record is judged by here is what
/// a write to it would be judged by. What a refusal costs is bounded by the composition rather than by this type: a mail
/// account that will not bind or names a secret this deployment cannot resolve is left out and reported, and only a user
/// whose own document is not a record at all is left unserved. Neither fails the caller, because one operator's broken
/// row must never be every other user's outage.
/// </para>
/// <para>
/// It is read as a record already held, which drops exactly two rules — see <see cref="UserRecordArrival" />. The
/// scanning block a stored record carries is composed against the deployment's section rather than refused against it,
/// and a record naming no language reads as English, so neither an operator tightening what the deployment requires nor
/// a release that began asking for a new property turns every record accepted before it into one nobody is served from.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this service.")]
internal sealed partial class ServedUserResolution(
    IUserSettingsDocumentReader documents,
    ServedUserRecordComposition composition,
    SecretConfigurationValidator secrets,
    HeldBackRecords heldBackRecords,
    ILogger<ServedUserResolution> logger)
{
    /// <summary>What a refusal about a user's own record names instead of a configuration key.</summary>
    /// <remarks>A user read from their own record has no path in the operator's files, so every sentence about one names the document rather than a key nobody wrote.</remarks>
    private const string RecordConfigurationPath = "document";

    /// <summary>Reads one user's record and composes the user it serves.</summary>
    /// <param name="user">The user to compose.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The record's version and the user it composes, which is <see langword="null" /> where the record is not one this
    /// build binds; or <see langword="null" /> as a whole where this deployment holds no such user.
    /// </returns>
    /// <exception cref="UserSettingsUnreadableException">Thrown when the record could not be read.</exception>
    internal async Task<(ServedUser? User, long Version)?> ResolveAsync(UserId user, CancellationToken cancellationToken)
    {
        if (await documents.ReadAsync(user, cancellationToken) is not { } document)
        {
            heldBackRecords.Cleared(user);

            return null;
        }

        var unaddressed = document.MailAccounts.Count(account => !MailAccountRecordComposition.IsServed(account));

        if (unaddressed > 0)
        {
            this.LogMailAccountsHeldWithoutAnAddress(document.DisplayName, unaddressed);
        }

        var composed = composition.Compose(document, UserRecordArrival.AlreadyHeld);
        var heldBack = new List<HeldBackRecord>(composed.HeldBack);

        if (composed.Record is not { } bound)
        {
            this.PublishHeldBack(user, heldBack);

            return (null, document.Version);
        }

        var usable = await this.MailAccountsWithUsableSecretsAsync(document, bound.MailAccounts, heldBack, cancellationToken);

        this.PublishHeldBack(user, heldBack);

        var served = new ServedUser(
            user,
            document.DisplayName,
            usable,
            bound.ReadingLanguage ?? UserLanguage.English)
        {
            TimeZone = bound.ReadingTimeZone,
            ClientTelemetryLevel = bound.ReadingClientTelemetryLevel,
        };

        return (served, document.Version);
    }

    /// <summary>Sorts one user's mail accounts into those the secret errors name and those they leave alone.</summary>
    /// <param name="errors">What the validator said about this user's mailboxes, which is never empty here.</param>
    /// <param name="accounts">The declarations the record bound, in the order the paths in <paramref name="errors" /> index them by.</param>
    /// <param name="versions">The version each declaration was recorded at, read by the identifier the composition put on it.</param>
    /// <param name="heldBack">Collects a record for every account this refuses, in the order the accounts were declared.</param>
    /// <returns>The accounts to serve, which is empty where an error named none of them.</returns>
    /// <remarks>
    /// <para>
    /// Each error is attributed by the position its own path carries: every path the validator composes for a user's
    /// mailboxes hangs under the account's index within the set, so the prefix names exactly one account and no message
    /// has to be parsed further. An account with something against it is left out and reported; the rest of that user's
    /// mailboxes keep being served.
    /// </para>
    /// <para>
    /// Separated from the resolution above so both endings can be judged without a validator, a secret scheme, or a
    /// deployment. What this decides is which mailboxes a person's mail keeps flowing through, and the ending that
    /// matters most is the one no deployment reaches while the validator and this method agree about paths.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<MailSynchronizationAccountOptions> MailAccountsTheErrorsLeaveUsable(
        IReadOnlyList<string> errors,
        IReadOnlyList<MailSynchronizationAccountOptions> accounts,
        IReadOnlyDictionary<Guid, long> versions,
        List<HeldBackRecord> heldBack)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(heldBack);

        var usable = new List<MailSynchronizationAccountOptions>(accounts.Count);
        var attributed = 0;

        foreach (var (position, account) in accounts.Index())
        {
            var declared = $"{RecordConfigurationPath}:{nameof(UserAccountOptions.MailAccounts)}:{position}:";
            var own = errors.Where(error => error.StartsWith(declared, StringComparison.Ordinal)).ToArray();

            if (own.Length == 0)
            {
                usable.Add(account);

                continue;
            }

            attributed += own.Length;

            var identity = IdentityOf(account);

            heldBack.Add(new HeldBackRecord(
                HeldBackRecordKind.MailAccount,
                identity,
                account.DisplayName ?? identity.ToString("D"),
                versions.TryGetValue(identity, out var version) ? version : null,
                own));
        }

        if (attributed == errors.Count)
        {
            return usable;
        }

        // The prefixes are mutually exclusive, so a shortfall means the validator reported something about this user's
        // mailboxes under a path that names none of them — a validator this method no longer understands. Serving a
        // mailbox whose secrets nothing proved is exactly what that resolution exists to prevent, so none of the
        // remaining ones is served either and each is reported with everything that was said.
        heldBack.AddRange(usable.Select(account => new HeldBackRecord(
            HeldBackRecordKind.MailAccount,
            IdentityOf(account),
            account.DisplayName ?? IdentityOf(account).ToString("D"),
            versions.TryGetValue(IdentityOf(account), out var version) ? version : null,
            errors)));

        return [];
    }

    /// <summary>Reads the identifier the composition put on a declaration, which is the generated one the row is keyed by.</summary>
    private static Guid IdentityOf(MailSynchronizationAccountOptions account) =>
        Guid.TryParse(account.AccountId, out var accountId) ? accountId : Guid.Empty;

    /// <summary>Leaves out the mail accounts carrying a secret or a trust anchor this deployment cannot use.</summary>
    /// <remarks>
    /// A mailbox is a user's record rather than a configuration key, so no reading of the files walks one and without
    /// this a user would be served cleanly and fail one connection at a time. The whole set is resolved in one pass,
    /// which is the only pass a user whose secrets are in place ever costs.
    /// </remarks>
    private async Task<IReadOnlyList<MailSynchronizationAccountOptions>> MailAccountsWithUsableSecretsAsync(
        UserSettingsDocument document,
        List<MailSynchronizationAccountOptions> accounts,
        List<HeldBackRecord> heldBack,
        CancellationToken cancellationToken)
    {
        var errors = await secrets.FindUserMailAccountErrorsAsync(RecordConfigurationPath, accounts, cancellationToken);

        return errors.Count == 0
            ? accounts
            : MailAccountsTheErrorsLeaveUsable(
                errors,
                accounts,
                document.MailAccounts.ToDictionary(account => account.Id, account => account.Version),
                heldBack);
    }

    /// <summary>Publishes what one user's row left refused, and says each of it once in the log.</summary>
    private void PublishHeldBack(UserId user, IReadOnlyList<HeldBackRecord> heldBack)
    {
        heldBackRecords.Replace(user, heldBack);

        foreach (var record in heldBack)
        {
            this.LogRecordHeldBack(
                record.Kind,
                record.Identity,
                record.Label,
                record.RejectedVersion,
                string.Join(" ", record.Corrections));
        }
    }

    /// <remarks>The user's label and a count rather than the accounts, because what the operator needs is where to look and an address is exactly what these accounts lack. It is reached after an upgrade carried a declaration whose address could not be derived.</remarks>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The user labelled {UserDisplayName} is assigned {UnaddressedCount} mail accounts that hold no email address, so those mailboxes are not served. State each address with 'mfctl account edit'; 'mfctl account list' names the accounts.")]
    private partial void LogMailAccountsHeldWithoutAnAddress(string userDisplayName, int unaddressedCount);

    /// <remarks>
    /// The identifier and the label together, because the label is what an operator recognizes a record by and the
    /// identifier is what the command repairing it names. The corrections are carried rather than counted, because they
    /// are MailFathom's own sentences about settings: the binder restates what the framework raised instead of handing it
    /// on, quotes a property name only where it is free of control characters, and repeats no value, so a line here
    /// carries no secret, no mail content, and no address. It is said once per version a replica composes, because a
    /// composed version is held until the record moves again. The documentation quotes the sentence, so an operator greps
    /// for it as written.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "A {HeldBackRecordKind} record is held back by a document this build will not bind: {HeldBackRecordIdentity} labelled {HeldBackRecordLabel}, at version {RejectedVersion}. It is served from the last version that bound, where there is one, and every other record is unaffected. Correct it: {Corrections}")]
    private partial void LogRecordHeldBack(
        HeldBackRecordKind heldBackRecordKind,
        Guid heldBackRecordIdentity,
        string heldBackRecordLabel,
        long? rejectedVersion,
        string corrections);
}
