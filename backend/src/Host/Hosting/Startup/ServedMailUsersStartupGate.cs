// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Rules;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Infrastructure.Persistence.Users;
using Microsoft.Extensions.Options;

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
/// <b>A deployment holding nobody is an ordinary state.</b> Its first start finds no row, serves nobody, and says so.
/// The first user arrives afterwards, through <c>mfctl user add</c> or the administrative route behind it, and the
/// roster publication that provisioning and a record write already raise is what carries them and their mailboxes into
/// this process without a restart.
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
    /// <exception cref="OptionsValidationException">Thrown when the declared rule set makes a claim about a mailbox no user this deployment serves records.</exception>
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

        this.RefuseARuleSetTheRosterCannotAnswerFor(served);

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
    /// It is read as a record already held, which drops exactly one rule — see <see cref="UserRecordArrival" />. The
    /// scanning block a stored record carries is composed against the deployment's section rather than refused against
    /// it, so an operator tightening what the deployment requires does not turn every record accepted before that into
    /// a start this host cannot complete.
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

    /// <summary>Refuses a declared rule set whose claims about mailboxes no user this deployment serves can answer.</summary>
    /// <remarks>
    /// A rule's scope and its per-account actions are claims about mailboxes, and every mailbox is somebody's record,
    /// so composition cannot judge them: it has the files and no roster. This is the first moment both exist, which is
    /// what makes it the place the claim is judged — and judging it against the roster rather than against
    /// configuration keys is what keeps a rule set a start accepts one a reload accepts, since a reload reads the same
    /// roster off the published snapshot. Everything a rule can be refused for without knowing the mailboxes was
    /// already refused during composition; asking the whole reading again here costs one compilation of a handful of
    /// conditions and keeps one entry point rather than two.
    /// </remarks>
    private void RefuseARuleSetTheRosterCannotAnswerFor(IReadOnlyList<ServedMailUser> served)
    {
        ComposedSettings.RefuseFirstOf(ComposedSettings.FindMailRuleRefusals(
            this.configuration,
            this.ruleConditionCompiler,
            DeclaredMailAccounts.ReadFrom(served.SelectMany(user => user.MailAccounts))));
    }

    /// <summary>Reports who is served, and says outright when that is nobody.</summary>
    /// <remarks>
    /// The empty deployment gets a line of its own rather than a count of zero, because it is the state a first run is
    /// in and the operator reading that line needs the command that ends it. A synchronization switch left on with
    /// nothing to synchronize is reported beside it and refuses nothing: it is what every deployment looks like
    /// between its first start and its first recorded mailbox.
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
    }

    /// <remarks>The record names no user. The identity is a generated identifier for a person this deployment serves, and what an operator needs from this line is how the roster came out rather than who is on it.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "This deployment serves {ServedUserCount} users, each read from their own record; no configuration source reaches anybody's mail accounts. Change them with mfctl.")]
    private partial void LogUsersResolved(int servedUserCount);

    /// <remarks>Reached on every start of a deployment nobody has been recorded on yet, which is what a first run is.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "This deployment holds no user and therefore serves nobody. Record the first with 'mfctl user add', then give them a mailbox with 'mfctl user account add'.")]
    private partial void LogNoUserHeld();

    /// <remarks>A report rather than a refusal, because a deployment with the switch on and nothing recorded yet is the ordinary shape of a first run.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Mail synchronization is switched on and no user this deployment serves records a mail account, so there is nothing to synchronize. Record one with 'mfctl user account add'.")]
    private partial void LogNothingToSynchronize();
}
