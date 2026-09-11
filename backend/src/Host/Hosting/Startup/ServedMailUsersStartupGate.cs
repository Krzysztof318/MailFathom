// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;
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
    private readonly IServiceScopeFactory scopeFactory;
    private readonly IConfiguration configuration;
    private readonly IMailRuleConditionCompiler ruleConditionCompiler;
    private readonly ServedMailUsers servedUsers;
    private readonly HostStartupGates startupGates;
    private readonly SeveralUserAdmission admission;
    private readonly ILogger<ServedMailUsersStartupGate> logger;

    /// <summary>Initializes a new served-user startup gate.</summary>
    /// <param name="scopeFactory">Creates the scope the user directory and the record reading are resolved from.</param>
    /// <param name="configuration">The configuration the synchronization switch and the declared rule set are read from.</param>
    /// <param name="ruleConditionCompiler">Reads each declared rule's condition, so a rule set is judged here by the rules composition judged it by.</param>
    /// <param name="servedUsers">The holder this gate publishes the roster into.</param>
    /// <param name="startupGates">The tracker this gate reports its completion to, which is what the startup probe reads.</param>
    /// <param name="admission">The reading that decides whether this deployment's endpoints could tell one user's caller from another's.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public ServedMailUsersStartupGate(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IMailRuleConditionCompiler ruleConditionCompiler,
        ServedMailUsers servedUsers,
        HostStartupGates startupGates,
        SeveralUserAdmission admission,
        ILogger<ServedMailUsersStartupGate> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(ruleConditionCompiler);
        ArgumentNullException.ThrowIfNull(servedUsers);
        ArgumentNullException.ThrowIfNull(startupGates);
        ArgumentNullException.ThrowIfNull(admission);

        this.scopeFactory = scopeFactory;
        this.configuration = configuration;
        this.ruleConditionCompiler = ruleConditionCompiler;
        this.servedUsers = servedUsers;
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

        var served = await this.ServeEveryHeldUserAsync(scope, held, cancellationToken);

        this.RefuseSeveralUsersOnAUserFacingSurface(served);

        await this.RefuseUnusableMailAccountSecretsAsync(scope, served, cancellationToken);

        this.servedUsers.Resolved(served);

        this.Report(served);

        this.startupGates.MarkCompleted(HostStartupGate.ServedMailUsers);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Serves every user this deployment holds, each from the document their own row carries.</summary>
    /// <remarks>
    /// Every held user is served and none is left out, because one source reaches all of them: a deployment that holds
    /// a row it did not serve would be one holding somebody's mail and synchronizing nobody's.
    /// </remarks>
    private async Task<IReadOnlyList<ServedMailUser>> ServeEveryHeldUserAsync(
        AsyncServiceScope scope,
        IReadOnlyList<MailUserRecord> held,
        CancellationToken cancellationToken)
    {
        var served = new List<ServedMailUser>(held.Count);

        foreach (var record in held)
        {
            served.Add(await this.ServeFromTheOwnDocumentAsync(scope, record, cancellationToken));
        }

        return served;
    }

    /// <summary>Serves one user from the document their row holds, which is the whole of what this deployment knows about them.</summary>
    /// <remarks>
    /// The document is put through the one binder both directions share, so what a user's record is judged by here is
    /// what a write to it would be judged by. A record that will not bind stops the start rather than leaving that
    /// user served with nothing: the alternative is a deployment quietly synchronizing none of the mailboxes their
    /// record names while reporting itself started.
    /// <para>
    /// It is read as a record already held, which drops exactly two rules — see <see cref="UserRecordArrival" />. The
    /// scanning block a stored record carries is composed against the deployment's section rather than refused against
    /// it, and a record naming no language reads as English rather than stopping the start, so neither an operator
    /// tightening what the deployment requires nor a release that began asking for a new property turns every record
    /// accepted before it into a start this host cannot complete.
    /// </para>
    /// </remarks>
    private async Task<ServedMailUser> ServeFromTheOwnDocumentAsync(
        AsyncServiceScope scope,
        MailUserRecord record,
        CancellationToken cancellationToken)
    {
        var document = await scope.ServiceProvider
            .GetRequiredService<IUserSettingsDocumentReader>()
            .ReadAsync(record.User, cancellationToken)
            ?? throw DeploymentMailUserUnresolvedException.UserRecordUnusable(
                record.DisplayName,
                ["The row it was read from is no longer there."]);

        var binding = scope.ServiceProvider
            .GetRequiredService<UserAccountDocumentBinder>()
            .Bind(document.Json, UserRecordArrival.AlreadyHeld);

        if (binding.User is not { } bound)
        {
            throw DeploymentMailUserUnresolvedException.UserRecordUnusable(record.DisplayName, binding.Refusals);
        }

        return new ServedMailUser(
            record.User,
            record.DisplayName,
            bound.MailAccounts,
            bound.ReadingLanguage ?? MailUserLanguage.English,
            bound.SpamClassification,
            bound.SensitiveContent);
    }

    /// <summary>Refuses a user whose own mail accounts carry a secret or a trust anchor this deployment cannot use.</summary>
    /// <remarks>
    /// A mailbox is a user's record rather than a configuration key, so no reading of the files walks one and without
    /// this a deployment would start clean and fail one connection at a time. It runs here because the roster is what
    /// says which accounts belong to whom, and the roster is what this gate establishes.
    /// </remarks>
    private async Task RefuseUnusableMailAccountSecretsAsync(
        AsyncServiceScope scope,
        IReadOnlyList<ServedMailUser> served,
        CancellationToken cancellationToken)
    {
        var validator = scope.ServiceProvider.GetRequiredService<SecretConfigurationValidator>();

        foreach (var user in served)
        {
            // A user read from their own record has no configuration path an operator could edit, so the refusal names
            // the document their mailboxes live in rather than a key nobody wrote.
            var errors = await validator.FindUserMailAccountErrorsAsync(
                "document",
                user.MailAccounts,
                cancellationToken);

            if (errors.Count > 0)
            {
                throw DeploymentMailUserUnresolvedException.UserMailAccountsUnusable(user.DisplayName, errors);
            }
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
    /// user-scoped routes names the user it is for. What that costs is the administrative routes which still resolve
    /// <see cref="IDeploymentMailUserSource.User" /> — the contact book above all — and those have no answer on a
    /// roster of several rather than a wrong one.
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
    /// mailbox. A mailbox identifier several users record is reported
    /// too, because nothing refuses it and it costs every one of them but the first that mailbox, and so is a rule
    /// set's claim about a mailbox nobody records, for the reason its own method gives.
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

        this.ReportMailAccountIdentifiersSharedAcrossUsers(served);

        this.ReportARuleSetTheRosterCannotAnswerFor(served);
    }

    /// <summary>Reports every mail-account identifier more than one served user records.</summary>
    /// <remarks>
    /// A report rather than a refusal, because an identifier names an account within its user and two people may each
    /// call theirs <c>work</c>. What still resolves an account by the identifier alone is the catalogue of served
    /// accounts and the settings lookup behind it, and both keep the user recorded first — so until
    /// <see href="https://github.com/Krzysztof318/MailFathom/issues/1325">issue 1325</see> keys them by the user as well,
    /// the others' mailbox under that identifier is not served, and this line is what tells the operator so.
    /// </remarks>
    private void ReportMailAccountIdentifiersSharedAcrossUsers(IReadOnlyList<ServedMailUser> served)
    {
        var shared = served
            .SelectMany(user => user.MailAccounts
                .Select(account => MailSynchronizationOptions.TryReadAccountId(account.AccountId))
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .Select(accountId => (AccountId: accountId, user.DisplayName)))
            .GroupBy(entry => entry.AccountId, StringComparer.Ordinal)
            .Where(holders => holders.Skip(1).Any());

        foreach (var holders in shared)
        {
            var labels = string.Join(", ", holders.Select(holder => $"'{holder.DisplayName}'"));

            this.LogMailAccountIdentifierShared(holders.Key, labels);
        }
    }

    /// <remarks>The record names no user. The identity is a generated identifier for a person this deployment serves, and what an operator needs from this line is how the roster came out rather than who is on it.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "This deployment serves {ServedUserCount} users, each read from their own record; no configuration source reaches anybody's mail accounts. Change them with mfctl.")]
    private partial void LogUsersResolved(int servedUserCount);

    /// <remarks>Reached on the first start of a fresh database, which holds no user, and on any start of a deployment whose every user was erased.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "This deployment holds no user and therefore serves nobody. Record one with 'mfctl user add', then give them a mailbox with 'mfctl user account add'.")]
    private partial void LogNoUserHeld();

    /// <remarks>A report rather than a refusal, because a deployment with the switch on and nothing recorded yet is the ordinary shape of a first run.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Mail synchronization is switched on and no user this deployment serves records a mail account, so there is nothing to synchronize. Record one with 'mfctl user account add'.")]
    private partial void LogNothingToSynchronize();

    /// <remarks>The labels rather than the identifiers, because they are the operator's own text and the identifiers are generated handles for people.</remarks>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The mail account '{MailAccountId}' is recorded by more than one user ({UserDisplayNames}). Only the one recorded first is served under that name, so the others' mailbox under it is not synchronized or read; give each of those mailboxes a name no other user records.")]
    private partial void LogMailAccountIdentifierShared(string mailAccountId, string userDisplayNames);

    /// <remarks>The claim is the sentence a configuration write would have been refused with, which names the rule by its position and the mailbox, folder, or action by what the operator wrote.</remarks>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A declared mail rule names something no record of a user this deployment serves provides, so the rule does nothing there until a record provides it or the rule is changed: {MailRuleClaim}")]
    private partial void LogMailRuleClaimUnanswered(string mailRuleClaim);
}
