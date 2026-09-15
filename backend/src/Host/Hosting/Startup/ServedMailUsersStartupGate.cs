// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Records;
using MailFathom.Host.Configuration.Rules;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Host.Hosting.Startup;

/// <summary>Reads the users this deployment holds, and composes each of them from their own record.</summary>
/// <remarks>
/// <para>
/// A user is a row rather than a declaration. What the row holds is the relational envelope — the identifier, the
/// label, the version, the instants — because <c>mailbox_accounts.UserId</c> is a foreign key and the integrity of the
/// mail graph is relational rather than a predicate over a document, and the content beside it is the user's own
/// record. No configuration source reaches any of it, so every user this gate serves is composed from the document
/// their row holds and from nothing else.
/// </para>
/// <para>
/// <b>A deployment holding nobody is a state a start admits.</b> A fresh database holds no user, so a first start —
/// like one of a deployment whose every user was erased — finds no row, serves nobody, and says so. A user or a mailbox recorded afterwards, through <c>mfctl</c> or the administrative routes behind it, is
/// carried into this process without a restart by the roster publication that provisioning and a record write raise.
/// </para>
/// <para>
/// It runs behind the schema gate, because it reads a table that migration creates, and ahead of the workers, so
/// nothing synchronizes mail before the user it belongs to is named. It is not ahead of the listener: the web host
/// registers its own hosted service while the builder runs and therefore starts it first, so the port is already open
/// while this gate runs, and the startup probe is what reports the deployment unstarted until it completes.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class ServedMailUsersStartupGate : IHostedService
{
    /// <summary>What a refusal about a user's own record names instead of a configuration key.</summary>
    /// <remarks>A user read from their own record has no path in the operator's files, so every sentence about one names the document rather than a key nobody wrote.</remarks>
    private const string RecordConfigurationPath = "document";

    private readonly IServiceScopeFactory scopeFactory;
    private readonly IConfiguration configuration;
    private readonly IMailRuleConditionCompiler ruleConditionCompiler;
    private readonly ServedMailUsers servedUsers;
    private readonly HeldBackRecords heldBackRecords;
    private readonly HostStartupGates startupGates;
    private readonly SeveralUserAdmission admission;
    private readonly ILogger<ServedMailUsersStartupGate> logger;

    /// <summary>Initializes a new served-user startup gate.</summary>
    /// <param name="scopeFactory">Creates the scope the user directory and the record reading are resolved from.</param>
    /// <param name="configuration">The configuration the synchronization switch and the declared rule set are read from.</param>
    /// <param name="ruleConditionCompiler">Reads each declared rule's condition, so a rule set is judged here by the rules composition judged it by.</param>
    /// <param name="servedUsers">The holder this gate publishes the roster into.</param>
    /// <param name="heldBackRecords">Where a record this start refused is reported, so an operator meets it on the administrative surface rather than in the log alone.</param>
    /// <param name="startupGates">The tracker this gate reports its completion to, which is what the startup probe reads.</param>
    /// <param name="admission">The reading that decides whether this deployment's endpoints could tell one user's caller from another's.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public ServedMailUsersStartupGate(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IMailRuleConditionCompiler ruleConditionCompiler,
        ServedMailUsers servedUsers,
        HeldBackRecords heldBackRecords,
        HostStartupGates startupGates,
        SeveralUserAdmission admission,
        ILogger<ServedMailUsersStartupGate> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(ruleConditionCompiler);
        ArgumentNullException.ThrowIfNull(servedUsers);
        ArgumentNullException.ThrowIfNull(heldBackRecords);
        ArgumentNullException.ThrowIfNull(startupGates);
        ArgumentNullException.ThrowIfNull(admission);

        this.scopeFactory = scopeFactory;
        this.configuration = configuration;
        this.ruleConditionCompiler = ruleConditionCompiler;
        this.servedUsers = servedUsers;
        this.heldBackRecords = heldBackRecords;
        this.startupGates = startupGates;
        this.admission = admission;
        this.logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="DeploymentMailUserUnresolvedException">Thrown when the roster this deployment holds is not a set of users it may serve.</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = this.scopeFactory.CreateAsyncScope();

        var directory = scope.ServiceProvider.GetRequiredService<IMailUserDirectory>();

        // One more than a deployment may hold, so that "more than the roster admits" is observable rather than
        // silently truncated into a roster this gate would then serve.
        var held = await directory.ReadUsersAsync(ServedMailUsers.MaximumUsers + 1, cancellationToken);

        if (held.Count > ServedMailUsers.MaximumUsers)
        {
            throw DeploymentMailUserUnresolvedException.TooManyUsers(ServedMailUsers.MaximumUsers);
        }

        var composed = await this.ServeEveryHeldUserAsync(scope, held, cancellationToken);
        IReadOnlyList<ServedMailUser> served = [.. composed.Select(entry => entry.User)];

        this.RefuseSeveralUsersOnAUserFacingSurface(served);

        this.servedUsers.Resolved(served, composed.ToDictionary(entry => entry.User.User, entry => entry.Version));

        this.Report(served);

        this.startupGates.MarkCompleted(HostStartupGate.ServedMailUsers);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Serves every user this deployment holds, each from the document their own row carries.</summary>
    /// <remarks>
    /// Every held user is read and none is left out, because one source reaches all of them: a deployment that holds
    /// a row it did not read would be one holding somebody's mail and synchronizing nobody's. A user whose own record
    /// is not one is left out of the roster rather than taking the start down with them, which is the whole of why
    /// this loop cannot raise: the alternative is one broken row costing every other user their mail.
    /// </remarks>
    private async Task<IReadOnlyList<(ServedMailUser User, long Version)>> ServeEveryHeldUserAsync(
        AsyncServiceScope scope,
        IReadOnlyList<MailUserRecord> held,
        CancellationToken cancellationToken)
    {
        var served = new List<(ServedMailUser User, long Version)>(held.Count);

        foreach (var record in held)
        {
            if (await this.ServeFromTheOwnDocumentAsync(scope, record, cancellationToken) is { } entry)
            {
                served.Add(entry);
            }
        }

        return served;
    }

    /// <summary>Serves one user from the document their row holds, which is the whole of what this deployment knows about them.</summary>
    /// <remarks>
    /// <para>
    /// The document is put through the one binder both directions share, so what a user's record is judged by here is
    /// what a write to it would be judged by. What a refusal costs is bounded by the composition rather than by this
    /// gate: a mail account that will not bind or names a secret this deployment cannot resolve is left out and
    /// reported, and only a user whose own document is not a record at all leaves that user unserved. Neither stops
    /// the start, because one operator's broken row must never be every other user's outage.
    /// </para>
    /// <para>
    /// It is read as a record already held, which drops exactly two rules — see <see cref="UserRecordArrival" />. The
    /// scanning block a stored record carries is composed against the deployment's section rather than refused against
    /// it, and a record naming no language reads as English rather than stopping the start, so neither an operator
    /// tightening what the deployment requires nor a release that began asking for a new property turns every record
    /// accepted before it into a start this host cannot complete.
    /// </para>
    /// </remarks>
    private async Task<(ServedMailUser User, long Version)?> ServeFromTheOwnDocumentAsync(
        AsyncServiceScope scope,
        MailUserRecord record,
        CancellationToken cancellationToken)
    {
        // Absent when the user was erased between the roster statement and this read, which holds nothing back: the
        // row the refusal would have named is gone, and the next start reads a roster that no longer lists them.
        if (await scope.ServiceProvider
            .GetRequiredService<IUserSettingsDocumentReader>()
            .ReadAsync(record.User, cancellationToken) is not { } document)
        {
            this.heldBackRecords.Cleared(record.User);
            this.LogUserRecordNoLongerHeld(record.DisplayName);

            return null;
        }

        var unaddressed = document.MailAccounts.Count(account => !MailAccountRecordComposition.IsServed(account));

        if (unaddressed > 0)
        {
            this.LogMailAccountsHeldWithoutAnAddress(record.DisplayName, unaddressed);
        }

        var composed = scope.ServiceProvider
            .GetRequiredService<ServedUserRecordComposition>()
            .Compose(document, UserRecordArrival.AlreadyHeld);

        var heldBack = new List<HeldBackRecord>(composed.HeldBack);

        if (composed.Record is not { } bound)
        {
            this.PublishHeldBack(record.User, heldBack);

            return null;
        }

        var usable = await this.MailAccountsWithUsableSecretsAsync(
            scope,
            document,
            bound.MailAccounts,
            heldBack,
            cancellationToken);

        this.PublishHeldBack(record.User, heldBack);

        var served = new ServedMailUser(record.User, record.DisplayName, usable);

        return (served, document.Version);
    }

    /// <summary>Leaves out the mail accounts carrying a secret or a trust anchor this deployment cannot use.</summary>
    /// <remarks>
    /// <para>
    /// A mailbox is a user's record rather than a configuration key, so no reading of the files walks one and without
    /// this a deployment would start clean and fail one connection at a time. It runs here because the roster is what
    /// says which accounts belong to whom, and the roster is what this gate establishes.
    /// </para>
    /// <para>
    /// The whole set is resolved in one pass, which is the only pass a deployment whose secrets are in place ever
    /// makes. What to do with what it reported is the method below.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<MailSynchronizationAccountOptions>> MailAccountsWithUsableSecretsAsync(
        AsyncServiceScope scope,
        UserSettingsDocument document,
        List<MailSynchronizationAccountOptions> accounts,
        List<HeldBackRecord> heldBack,
        CancellationToken cancellationToken)
    {
        // A user read from their own record has no configuration path an operator could edit, so the refusal names
        // the document their mailboxes live in rather than a key nobody wrote.
        var errors = await scope.ServiceProvider
            .GetRequiredService<SecretConfigurationValidator>()
            .FindUserMailAccountErrorsAsync(RecordConfigurationPath, accounts, cancellationToken);

        return errors.Count == 0
            ? accounts
            : MailAccountsTheErrorsLeaveUsable(
                errors,
                accounts,
                document.MailAccounts.ToDictionary(account => account.Id, account => account.Version),
                heldBack);
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
    /// mailboxes keep synchronizing.
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

    /// <summary>Publishes what one user's row left refused, and says each of it once in the log.</summary>
    private void PublishHeldBack(MailUserId user, IReadOnlyList<HeldBackRecord> heldBack)
    {
        this.heldBackRecords.Replace(user, heldBack);

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

    /// <summary>Refuses a deployment whose served surfaces could not say which user an act is for.</summary>
    /// <remarks>
    /// <para>
    /// A user-facing surface answers one person about their own mail, so it may be served on a roster of several only
    /// where every caller it admits says which user it is acting for. The reading that decides that is shared with the
    /// provisioning this refusal is the start-time half of, because a deployment refused a second user over a route
    /// and one refused it at its next start are the same operator correcting the same setting.
    /// </para>
    /// <para>
    /// A roster of one and a roster of none are both admissible, which is what lets a first run reach an
    /// unauthenticated surface with no credential and record the person it is for.
    /// </para>
    /// <para>
    /// The administrative surface is deliberately outside it, which is what makes a second user reachable at all. An
    /// administrator's acts are the deployment's rather than one person's, so they carry no user and each of their
    /// user-scoped routes names the user or the mail account it is for rather than resolving
    /// <see cref="IDeploymentMailUserSource.User" />.
    /// </para>
    /// </remarks>
    private void RefuseSeveralUsersOnAUserFacingSurface(IReadOnlyList<ServedMailUser> served)
    {
        if (served.Count > 1 && this.admission.AdmitsACallerNamingNoUser)
        {
            throw DeploymentMailUserUnresolvedException.SeveralUsersOnAUserFacingSurface(this.admission.Refusal);
        }
    }

    /// <summary>Reports every claim the declared rule set makes about a mailbox no user this deployment serves records.</summary>
    /// <remarks>
    /// A rule's scope and its per-account actions are claims about mailboxes, and every mailbox is somebody's record,
    /// so composition cannot judge them: it has the files and no roster. This is the first moment both exist. It
    /// reports rather than refuses, because the roster can lose a mailbox after the rule naming it was accepted — its
    /// user is erased, or their record stops declaring it — and neither of those writes consults the rule set. A start
    /// refusing then could be undone only through the running host it refused, the persisted layer outranking every
    /// file and being writable only while the deployment runs. A configuration write and a reload still refuse such a
    /// claim, because both are judged while the host is up and can be answered there. Everything a rule can be refused
    /// for without knowing the mailboxes was already refused during composition, so what this finds is only what the
    /// roster decides.
    /// </remarks>
    private void ReportARuleSetTheRosterCannotAnswerFor(IReadOnlyList<ServedMailUser> served)
    {
        var claims = ComposedSettings
            .FindMailRuleRefusals(
                this.configuration,
                this.ruleConditionCompiler,
                DeclaredMailAccounts.ReadFrom(served.SelectMany(user => user.MailAccounts)))
            .SelectMany(refusal => refusal.Errors);

        foreach (var claim in claims)
        {
            this.LogMailRuleClaimUnanswered(claim);
        }
    }

    /// <summary>Reports who is served, and says outright when that is nobody.</summary>
    /// <remarks>
    /// The empty deployment gets a line of its own rather than a count of zero, because the operator reading it needs
    /// the command that ends it. A synchronization switch left on with nothing to synchronize is reported beside it and
    /// refuses nothing: it is what every deployment looks like between its first start and its first recorded
    /// mailbox. A rule set's claim about a mailbox nobody records is reported too, for the reason its own method gives.
    /// </remarks>
    private void Report(IReadOnlyList<ServedMailUser> served)
    {
        if (served.Count == 0)
        {
            this.LogNoUserHeld();
        }
        else
        {
            this.LogUsersResolved(served.Count);
        }

        if (MailSynchronizationOptions.IsEnabledIn(this.configuration)
            && served.All(user => user.MailAccounts.Count == 0))
        {
            this.LogNothingToSynchronize();
        }

        this.ReportARuleSetTheRosterCannotAnswerFor(served);
    }

    /// <remarks>The record names no user. The identity is a generated identifier for a person this deployment serves, and what an operator needs from this line is how the roster came out rather than who is on it.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "This deployment serves {ServedUserCount} users, each read from their own record; no configuration source reaches anybody's mail accounts. Change them with mfctl.")]
    private partial void LogUsersResolved(int servedUserCount);

    /// <remarks>Reached on the first start of a fresh database, which holds no user, and on any start of a deployment whose every user was erased.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "This deployment holds no user and therefore serves nobody. Record one with 'mfctl user add', then give them a mailbox with 'mfctl account add'.")]
    private partial void LogNoUserHeld();

    /// <remarks>A report rather than a refusal, because a deployment with the switch on and nothing recorded yet is the ordinary shape of a first run.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Mail synchronization is switched on and no user this deployment serves is assigned a mail account, so there is nothing to synchronize. Record one with 'mfctl account add'.")]
    private partial void LogNothingToSynchronize();

    /// <remarks>The user's label and a count rather than the accounts, because what the operator needs is where to look and an address is exactly what these accounts lack. It is reached after an upgrade carried a declaration whose address could not be derived.</remarks>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The user labelled {UserDisplayName} is assigned {UnaddressedCount} mail accounts that hold no email address, so those mailboxes are not served. State each address with 'mfctl account edit'; 'mfctl account list' names the accounts.")]
    private partial void LogMailAccountsHeldWithoutAnAddress(string userDisplayName, int unaddressedCount);

    /// <remarks>The claim is the sentence a configuration write would have been refused with, which names the rule by its position and the mailbox, folder, or action by what the operator wrote.</remarks>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A declared mail rule names something no record of a user this deployment serves provides, so the rule does nothing there until a record provides it or the rule is changed: {MailRuleClaim}")]
    private partial void LogMailRuleClaimUnanswered(string mailRuleClaim);

    /// <remarks>Reached when a user is erased between the statement that listed the roster and the read of their record, which is an ordinary race rather than a record to correct.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The user labelled {UserDisplayName} was erased while this start was reading the roster, so they are not served and nothing about them is held back.")]
    private partial void LogUserRecordNoLongerHeld(string userDisplayName);

    /// <remarks>
    /// The corrections are carried rather than counted, because they are MailFathom's own sentences about settings: the
    /// binder restates what the framework raised instead of handing it on, quotes a property name only where it is free
    /// of control characters, and repeats no value, so a line here carries no secret, no mail content, and no address.
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
