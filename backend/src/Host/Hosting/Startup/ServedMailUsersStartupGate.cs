// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Host.Hosting.Startup;

/// <summary>Reads the users this deployment holds, and settles who it serves and from where.</summary>
/// <remarks>
/// <para>
/// A user is a row rather than a declaration. What the row holds is the relational envelope — the identifier, the
/// label, the version, the instants — because <c>mailbox_accounts.UserId</c> is a foreign key and the integrity of the
/// mail graph is relational rather than a predicate over a document, and the content beside it is the user's own
/// record. The one user a deployment can still have without ever recording anything is the sole user
/// <c>MailSynchronization:Accounts</c> belongs to, which is the row this gate provisions where the deployment holds
/// none at all.
/// </para>
/// <para>
/// Which source reaches a user is decided per user, and nothing here moves anybody. A user whose row carries the
/// runtime-written marker is served from their own document, permanently and for that user alone, and the sole user of
/// a deployment that never recorded one goes on being read from its own mail section. What this gate does about it is
/// report which of the two each user is, because a section somebody goes on editing for a user that no longer reads it
/// is exactly the mistake nothing else would surface.
/// </para>
/// <para>
/// It runs behind the schema gate, because it reads and writes a table that migration creates, and ahead of the
/// workers, so nothing synchronizes mail before the user it belongs to is named. It is not ahead of the listener: the
/// web host registers its own hosted service while the builder runs and therefore starts it first, so the port is
/// already open while this gate runs, and the startup probe is what reports the deployment unstarted until it
/// completes.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class ServedMailUsersStartupGate : IHostedService
{
    /// <summary>The label the sole user of a deployment that recorded none is written under.</summary>
    /// <remarks>
    /// The same label the migration that provisions that user writes, so a deployment upgraded through it and one
    /// whose row this gate had to create read identically. It is only ever used where the deployment holds no user at
    /// all, so nothing can already be carrying it.
    /// </remarks>
    private const string SoleUserDisplayName = "user";

    private readonly IServiceScopeFactory scopeFactory;
    private readonly IConfiguration configuration;
    private readonly ServedMailUsers servedUsers;
    private readonly HostStartupGates startupGates;
    private readonly SeveralUserAdmission admission;
    private readonly ILogger<ServedMailUsersStartupGate> logger;

    /// <summary>Initializes a new served-user startup gate.</summary>
    /// <param name="scopeFactory">Creates the scope the user directory and the provisioning are resolved from.</param>
    /// <param name="configuration">The configuration the deployment's own mail section is read from.</param>
    /// <param name="servedUsers">The holder this gate publishes the roster into.</param>
    /// <param name="startupGates">The tracker this gate reports its completion to, which is what the startup probe reads.</param>
    /// <param name="admission">The reading that decides whether this deployment's endpoints could tell one user's caller from another's.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public ServedMailUsersStartupGate(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ServedMailUsers servedUsers,
        HostStartupGates startupGates,
        SeveralUserAdmission admission,
        ILogger<ServedMailUsersStartupGate> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(servedUsers);
        ArgumentNullException.ThrowIfNull(startupGates);
        ArgumentNullException.ThrowIfNull(admission);

        this.scopeFactory = scopeFactory;
        this.configuration = configuration;
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

        var sectionUsers = await this.ServeTheSoleUserAsync(scope, held, cancellationToken);

        IReadOnlyList<ServedMailUser> served =
        [
            .. sectionUsers,
            .. await this.ServeUsersOfTheirOwnRecordAsync(scope, sectionUsers, held, cancellationToken),
        ];

        this.RefuseSeveralUsersOnAUserFacingSurface(served);

        this.RefuseMailAccountNamesTwoUsersShare(served);

        this.RefuseADeploymentSectionNobodyReads(served);

        this.RefuseNothingToSynchronize(served);

        await this.RefuseUnusableMailAccountSecretsAsync(scope, served, cancellationToken);

        this.servedUsers.Resolved(served);

        this.Report(served);

        this.startupGates.MarkCompleted(HostStartupGate.ServedMailUsers);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Serves the user the deployment's own mail section belongs to, which is the sole user of a deployment that recorded none.</summary>
    /// <returns>That user, or nothing where every user this deployment holds reads their own record instead.</returns>
    /// <remarks>
    /// <para>
    /// The mail accounts stay in <c>MailSynchronization:Accounts</c> and no file has to change. The identifier is
    /// generated once and recorded, which the release's own migration ordinarily did; generating one here is what
    /// answers a database whose row is not there at all, and it is a version 4 value for the reason the column is —
    /// a user identifier reaches administrative APIs, audit records, and logs, and a time-ordered one would publish
    /// when each user was created and in what order.
    /// </para>
    /// <para>
    /// Only a user still reading that section contends for it. A user whose record was written at runtime reads
    /// none of it, so they neither make a deployment ambiguous about whose section it is nor keep one from being
    /// served, and they are served beside this from their own record. Where every user is of that kind the section
    /// belongs to nobody and this serves nobody: recording a user for it would mint a person the operator never
    /// asked for, on every start.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<ServedMailUser>> ServeTheSoleUserAsync(
        AsyncServiceScope scope,
        IReadOnlyList<MailUserRecord> held,
        CancellationToken cancellationToken)
    {
        var readingTheSection = held.Where(record => !record.DocumentWrittenAtRuntime).ToArray();

        if (readingTheSection.Length > 1)
        {
            throw DeploymentMailUserUnresolvedException.SeveralUsers();
        }

        if (readingTheSection is [var soleUser])
        {
            return [ServedFromTheSection(soleUser)];
        }

        if (held.Count > 0)
        {
            return [];
        }

        var generated = MailUserId.Create(Guid.NewGuid());

        var recorded = await scope.ServiceProvider
            .GetRequiredService<IMailUserProvisioning>()
            .ProvisionAsync(generated, SoleUserDisplayName, cancellationToken);

        if (recorded)
        {
            this.LogSoleUserRecorded();

            return
            [
                new ServedMailUser(
                    generated,
                    SoleUserDisplayName,
                    MailUserAccountSource.DeploymentSection,
                    MailAccounts: []),
            ];
        }

        // Another replica of this deployment recorded the sole user first, under an identifier it minted rather than
        // this one. Serving the identifier this process generated would hang every message on a row that is not there,
        // so the user the deployment actually holds is read back and served instead.
        var winners = await scope.ServiceProvider
            .GetRequiredService<IMailUserDirectory>()
            .ReadUsersAsync(2, cancellationToken);

        if (winners is not [var winner])
        {
            throw DeploymentMailUserUnresolvedException.SeveralUsers();
        }

        return
        [
            winner.DocumentWrittenAtRuntime
                ? await this.ServeFromTheOwnDocumentAsync(scope, winner, cancellationToken)
                : ServedFromTheSection(winner),
        ];
    }

    /// <summary>Serves every user whose record was written at runtime and whom nothing above has served already.</summary>
    /// <remarks>
    /// No configuration source reaches a user an administrator recorded, so nothing else on this path serves them and
    /// a deployment would hold a row it never served. They are served after the user the deployment's own mail section
    /// belongs to, because that section is the one part of the roster a file still decides and a user outside it has
    /// no place in that order to take.
    /// </remarks>
    private async Task<IReadOnlyList<ServedMailUser>> ServeUsersOfTheirOwnRecordAsync(
        AsyncServiceScope scope,
        IReadOnlyList<ServedMailUser> alreadyServed,
        IReadOnlyList<MailUserRecord> held,
        CancellationToken cancellationToken)
    {
        var records = held
            .Where(record => record.DocumentWrittenAtRuntime
                && alreadyServed.All(user => user.User != record.User))
            .ToArray();

        var served = new List<ServedMailUser>(records.Length);

        foreach (var record in records)
        {
            served.Add(await this.ServeFromTheOwnDocumentAsync(scope, record, cancellationToken));
        }

        return served;
    }

    /// <summary>Serves one user from the deployment's own mail section, which holds their accounts rather than their record.</summary>
    private static ServedMailUser ServedFromTheSection(MailUserRecord record) => new(
        record.User,
        record.DisplayName,
        MailUserAccountSource.DeploymentSection,
        MailAccounts: []);

    /// <summary>Serves one user from the document their row holds, which is what a committed record made the source.</summary>
    /// <remarks>
    /// The document is put through the one binder both directions share, so what a user's record is judged by here is
    /// what a write to it would be judged by. A record that will not bind stops the start rather than leaving that
    /// user served from a section they have stopped reading: the alternative is a deployment quietly synchronizing
    /// mailboxes the record was meant to have replaced.
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
            MailUserAccountSource.UserDocument,
            bound.MailAccounts,
            bound.SpamClassification,
            bound.SensitiveContent);
    }

    /// <summary>Refuses a user whose own mail accounts carry a secret or a trust anchor this deployment cannot use.</summary>
    /// <remarks>
    /// The deployment's own section is proven by the secret gate ahead of this one, which walks the bound mail
    /// snapshot; a user's mailboxes are not in that snapshot and are not bound when it runs, so without this they
    /// would start a host clean and fail one connection at a time, while the identical declaration in
    /// <c>MailSynchronization:Accounts</c> fails the start. It runs here rather than there because it is the roster
    /// that says which accounts belong to whom, and the roster is what this gate establishes.
    /// </remarks>
    private async Task RefuseUnusableMailAccountSecretsAsync(
        AsyncServiceScope scope,
        IReadOnlyList<ServedMailUser> served,
        CancellationToken cancellationToken)
    {
        var validator = scope.ServiceProvider.GetRequiredService<SecretConfigurationValidator>();

        foreach (var user in served.Where(user => user.Source != MailUserAccountSource.DeploymentSection))
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

    /// <summary>Refuses a roster in which one mail-account name would reach two users.</summary>
    /// <remarks>
    /// The deployment-wide bound, asked over the roster this start would actually serve. A write into one user's record
    /// is judged against the roster this process settled, so two writes into two record-served users in one process
    /// run — each judged against a snapshot the other had not moved — can name the same account, and the start that
    /// composes them is where the two are first in one place.
    /// <para>
    /// A user served from the deployment's own section carries no accounts on their roster entry, so this reads that
    /// section for them. Without it the one collision the write-time check cannot see — a record naming an account
    /// <c>MailSynchronization:Accounts</c> declares — would pass a start, which is the case this refusal exists for.
    /// </para>
    /// <para>
    /// It refuses the start rather than serving both, because serving them is a lookup by identifier reaching whichever
    /// user it met first: one person's mailbox settings resolved for another person's account.
    /// </para>
    /// </remarks>
    private void RefuseMailAccountNamesTwoUsersShare(IReadOnlyList<ServedMailUser> served)
    {
        var deploymentAccounts = MailSynchronizationOptions.AccountsDeclaredIn(this.configuration);

        var shared = served
            .SelectMany(user => NamesOf(AccountsOf(user, deploymentAccounts))
                .Select(name => (user.User, Name: name)))
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.DistinctBy(entry => entry.User).Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (shared.Length > 0)
        {
            throw DeploymentMailUserUnresolvedException.MailAccountNameSharedByUsers(shared);
        }
    }

    /// <summary>Refuses mail accounts left in the deployment's own section that no served user reads.</summary>
    /// <remarks>
    /// <para>
    /// A deployment whose sole user is read from their own record, while <c>MailSynchronization:Accounts</c> still
    /// declares accounts, has a section belonging to nobody — and it is still bound, still searched first by the
    /// per-account lookup, and still the answer every read gets. So the mailboxes that user's record declares would be
    /// synchronized under the file's settings instead, which is the opposite of what a record being their own means.
    /// </para>
    /// <para>
    /// No reading of the files can see this, because the users are records and only the roster holds them. So this is
    /// the reading that closes the invariant <c>MailSynchronizationOptions.FindConfiguredAccount</c> states about
    /// itself — the section and the roster never both answer — and it holds whether or not synchronization is switched
    /// on, because the lookup in front of every per-account read does not ask.
    /// </para>
    /// </remarks>
    private void RefuseADeploymentSectionNobodyReads(IReadOnlyList<ServedMailUser> served)
    {
        if (served.Any(user => user.Source == MailUserAccountSource.DeploymentSection))
        {
            return;
        }

        var deploymentAccounts = MailSynchronizationOptions.AccountsDeclaredIn(this.configuration);

        if (deploymentAccounts.Count > 0)
        {
            throw DeploymentMailUserUnresolvedException.DeploymentSectionServesNobody(deploymentAccounts.Count);
        }
    }

    /// <summary>Refuses a deployment that switched synchronization on with nothing at all to synchronize.</summary>
    /// <remarks>
    /// The deployment's own rule, held here rather than against the files because a user's mailboxes are a record as
    /// well as a section: a deployment whose only mailbox was declared through <c>mfctl user account add</c> or by the
    /// user themselves has an empty <c>MailSynchronization:Accounts</c>, and reading the file alone would refuse to
    /// start it. The roster is the first place both sources are in one place, which is what makes this the only
    /// reading that can tell a worker with no work from one whose work is in the database.
    /// </remarks>
    private void RefuseNothingToSynchronize(IReadOnlyList<ServedMailUser> served)
    {
        if (!MailSynchronizationOptions.IsEnabledIn(this.configuration))
        {
            return;
        }

        var deploymentAccounts = MailSynchronizationOptions.AccountsDeclaredIn(this.configuration);

        if (served.All(user => AccountsOf(user, deploymentAccounts).Count == 0))
        {
            throw DeploymentMailUserUnresolvedException.NothingToSynchronize();
        }
    }

    /// <summary>Reads the mail accounts one served user's mailboxes are declared in, whichever source holds them.</summary>
    private static IReadOnlyList<MailSynchronizationAccountOptions> AccountsOf(
        ServedMailUser user,
        IReadOnlyList<MailSynchronizationAccountOptions> deploymentAccounts) =>
        user.Source == MailUserAccountSource.DeploymentSection ? deploymentAccounts : user.MailAccounts;

    /// <summary>Names the strings one user's mail accounts answer to.</summary>
    /// <remarks>Both spellings, because a caller may name an account by either and the lookup resolves both.</remarks>
    private static IEnumerable<string> NamesOf(IReadOnlyList<MailSynchronizationAccountOptions> accounts) =>
        accounts
            .SelectMany(account => new[]
            {
                MailSynchronizationOptions.TryReadAccountId(account.AccountId),
                string.IsNullOrWhiteSpace(account.DisplayName) ? null : account.DisplayName.Trim(),
            })
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reports which users are served and where each of them is read from.</summary>
    /// <remarks>
    /// Every user the deployment holds is served: the one its own mail section belongs to, and every user whose record
    /// is their own. So this reports where each is read from rather than who was left out — nothing can be.
    /// </remarks>
    private void Report(IReadOnlyList<ServedMailUser> served)
    {
        var configuredUserCount = served.Count(user => user.ReadFromConfiguration);

        this.LogUsersResolved(served.Count, configuredUserCount, served.Count - configuredUserCount);

        foreach (var user in served.Where(user => user.Source == MailUserAccountSource.UserDocument))
        {
            this.LogUserReadFromTheirDocument(user.DisplayName);
        }
    }

    /// <remarks>The record names no user. The identity is a generated identifier for a person this deployment serves, and what an operator needs from this line is how the roster came out rather than who is on it.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "This deployment serves {ServedUserCount} users: {ConfiguredUserCount} read from configuration and {OwnDocumentUserCount} from their own document.")]
    private partial void LogUsersResolved(int servedUserCount, int configuredUserCount, int ownDocumentUserCount);

    /// <remarks>The label is the operator's own text for a person this deployment holds, which is what makes the line actionable: it names the user no configuration source reaches. Both halves of what a record supplies are named, because an operator who switched a scanner on in the deployment's section would otherwise have no sentence explaining why nothing changed for that user.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The user labelled {UserDisplayName} is read from their own document; no configuration source reaches their mail accounts or the scanning posture declared beside them. Change them with mfctl.")]
    private partial void LogUserReadFromTheirDocument(string userDisplayName);


    /// <remarks>Reached only where the deployment holds no user row at all, which the release's own migration ordinarily provisions.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "This deployment declared no user and held none, so one has been recorded for the mail accounts it is configured with.")]
    private partial void LogSoleUserRecorded();
}
