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

/// <summary>Reconciles the users this deployment declares against the rows it holds, and settles who it serves.</summary>
/// <remarks>
/// <para>
/// A deployment may keep its whole configuration outside the database, users included. What the database still holds
/// per user is the relational envelope — the identifier, the label, the version, the instants — because
/// <c>mailbox_accounts.UserId</c> is a foreign key and the integrity of the mail graph is relational rather than a
/// predicate over a document. So this gate gives every declared user that row and nothing inside it: their mail
/// accounts go on being read from the effective configuration until an adoption writes their document.
/// </para>
/// <para>
/// The handover is per user and never happens here. A user whose row carries the runtime-written marker is served
/// from their own document, permanently and for that user alone, and every other user goes on being read from the
/// file beside them. What this gate does about it is report which of the two each user is, because a section somebody
/// goes on editing for a user that no longer reads it is exactly the mistake nothing else would surface.
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
    /// <summary>The label the user of a deployment that declares none is recorded under.</summary>
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
    /// <param name="configuration">The configuration the user declarations are read from.</param>
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
    /// <exception cref="DeploymentMailUserUnresolvedException">Thrown when the roster and the declarations cannot be reconciled into a set of users this deployment may serve.</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = this.scopeFactory.CreateAsyncScope();

        var declared = DeclaredUsers.ReadFrom(this.configuration);
        var directory = scope.ServiceProvider.GetRequiredService<IMailUserDirectory>();

        // One more than a deployment may hold, so that "more than the roster admits" is observable rather than
        // silently truncated into a roster this gate would then serve.
        var held = await directory.ReadUsersAsync(DeclaredUsers.MaximumDeclaredUsers + 1, cancellationToken);

        if (held.Count > DeclaredUsers.MaximumDeclaredUsers)
        {
            throw DeploymentMailUserUnresolvedException.TooManyUsers(DeclaredUsers.MaximumDeclaredUsers);
        }

        // The bound is judged again against the roster this start would leave, because provisioning is what grows the
        // table: a deployment holding users the file no longer declares keeps every one of them, so a file within the
        // bound and a table within the bound can still sum past it. Refusing here rather than after the writes is what
        // keeps this start from producing a roster every later start refuses over rows this one wrote.
        var newUsers = declared.Count(declaration =>
            held.All(record => record.User != IdentifierOf(declaration)));

        if (held.Count + newUsers > DeclaredUsers.MaximumDeclaredUsers)
        {
            throw DeploymentMailUserUnresolvedException.RosterWouldExceedTheBound(
                DeclaredUsers.MaximumDeclaredUsers,
                held.Count,
                newUsers);
        }

        var declaredUsers = declared.Count == 0
            ? await this.ServeTheSoleUserAsync(scope, held, cancellationToken)
            : await this.ServeDeclaredUsersAsync(scope, declared, held, cancellationToken);

        IReadOnlyList<ServedMailUser> served =
        [
            .. declaredUsers,
            .. await this.ServeUsersOfTheirOwnRecordAsync(scope, declaredUsers, held, cancellationToken),
        ];

        this.RefuseSeveralUsersOnAUserFacingSurface(served);

        this.RefuseMailAccountNamesTwoUsersShare(served);

        this.RefuseADeploymentSectionNobodyReads(served);

        this.RefuseNothingToSynchronize(served);

        await this.RefuseUnusableMailAccountSecretsAsync(scope, served, cancellationToken);

        this.servedUsers.Resolved(served);

        this.Report(served, held);

        this.startupGates.MarkCompleted(HostStartupGate.ServedMailUsers);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Serves the user of the deployment's own mail section, which a deployment declaring none keeps.</summary>
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
    /// A user an administrator provisioned is declared in no file, so nothing else on this path reaches them and a
    /// deployment would hold a row it never served. They are served last, after the users a file names, because the
    /// roster's order is the operator's own reading of their configuration and a user outside it has no place in
    /// that order to take.
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

    /// <summary>Gives every declared user their row, and serves each of them from the source their own row names.</summary>
    private async Task<IReadOnlyList<ServedMailUser>> ServeDeclaredUsersAsync(
        AsyncServiceScope scope,
        IReadOnlyList<DeclaredUserOptions> declared,
        IReadOnlyList<MailUserRecord> held,
        CancellationToken cancellationToken)
    {
        var provisioning = scope.ServiceProvider.GetRequiredService<IMailUserProvisioning>();
        var served = new List<(int Index, ServedMailUser User)>(declared.Count);

        // The roster this start has actually reached rather than the snapshot it opened with. Every write below is
        // applied to it, because the label check reads it: a file that renames one user and gives their old label to
        // another would otherwise be refused for a label nobody carries any more, and the refusal would clear itself on
        // the next start — which is the proof that the file was legal all along.
        var roster = held.ToList();

        // The users the deployment already holds are reconciled first, so every relabel this start commits is in the
        // roster before a new user's label is judged against it. Otherwise a file that declares the new user above
        // the rename — one user taking the label another is being renamed out of — would be refused on the order its
        // entries happen to be written in, while the same two entries the other way round start cleanly. Ordering is
        // stable, so within each of the two groups the file's own order is what is walked, and a swap between two held
        // users stays refused because both are in the first group and the loser still carries the label when it is
        // checked. The entry's own index travels with it, because the roster is published in the order the file
        // declares rather than in the order it was reconciled in.
        var reconciliationOrder = declared.Index()
            .OrderBy(entry => held.Any(record => record.User == IdentifierOf(entry.Item)) ? 0 : 1);

        foreach (var (index, declaration) in reconciliationOrder)
        {
            // Every declaration has already been proven to carry one, by the composed rules a start refuses before its
            // container exists, so the identifier is read rather than judged again here.
            var user = IdentifierOf(declaration);
            var label = declaration.DisplayName.Trim();
            var record = roster.FirstOrDefault(candidate => candidate.User == user);

            // A label another user is already recorded under is what the unique index refuses, whichever of the two
            // writes below would meet it. Refusing here is what turns a constraint violation into a sentence, and which
            // sentence it is depends on whether this user has a row at all: without one the declaration is the same
            // person written down twice under a new identifier, and their mail would stay on the row nothing serves;
            // with one it is a label moving onto a user while its holder still carries it.
            if (roster.Any(candidate =>
                candidate.User != user && StringComparer.Ordinal.Equals(candidate.DisplayName, label)))
            {
                throw record is null
                    ? DeploymentMailUserUnresolvedException.UserIdentifierChanged(label)
                    : DeploymentMailUserUnresolvedException.UserLabelHeldByAnother(label);
            }

            if (record is null)
            {
                // False is the label having been taken between the roster being read and this insert reaching the
                // table, which no reading of a snapshot could have refused earlier.
                if (!await provisioning.ProvisionAsync(user, label, cancellationToken))
                {
                    throw DeploymentMailUserUnresolvedException.UserLabelHeldByAnother(label);
                }

                roster.Add(new MailUserRecord(user, label, DocumentWrittenAtRuntime: false));
                served.Add((index, new ServedMailUser(
                    user,
                    label,
                    MailUserAccountSource.UserDeclaration,
                    declaration.MailAccounts,
                    SensitiveContent: declaration.SensitiveContent)));

                continue;
            }

            if (!StringComparer.Ordinal.Equals(record.DisplayName, label))
            {
                // False is the label having been taken between the roster being read and this statement reaching the
                // table, which no reading of a snapshot could have refused earlier — the same race the insert above
                // answers, and the same refusal, because a start whose file renames a user onto a label somebody
                // else now holds is a start that cannot say who is who.
                if (!await provisioning.RelabelAsync(user, label, cancellationToken))
                {
                    throw DeploymentMailUserUnresolvedException.UserLabelHeldByAnother(label);
                }

                roster[roster.IndexOf(record)] = record with { DisplayName = label };
            }

            served.Add((index, record.DocumentWrittenAtRuntime
                ? await this.ServeFromTheOwnDocumentAsync(scope, record with { DisplayName = label }, cancellationToken)
                : new ServedMailUser(
                    user,
                    label,
                    MailUserAccountSource.UserDeclaration,
                    declaration.MailAccounts,
                    SensitiveContent: declaration.SensitiveContent)));
        }

        return [.. served.OrderBy(entry => entry.Index).Select(entry => entry.User)];
    }

    /// <summary>Reads the identifier a declaration carries, which the composed rules have already proven it has.</summary>
    private static MailUserId IdentifierOf(DeclaredUserOptions declaration) =>
        MailUserId.Create(DeclaredUsers.TryReadIdentifier(declaration.Id)!.Value);

    /// <summary>Serves one user from the document their row holds, which is what an adoption made the source.</summary>
    /// <remarks>
    /// The document is put through the one binder both directions share, so what a user's record is judged by here is
    /// what a write to it would be judged by. A record that will not bind stops the start rather than leaving that
    /// user served from a section they have stopped reading: the alternative is a deployment quietly synchronizing the
    /// mailboxes an adoption was meant to replace.
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

        foreach (var (index, user) in served.Index())
        {
            if (user.Source == MailUserAccountSource.DeploymentSection)
            {
                continue;
            }

            // The path an operator would edit, which is not the same place for the two sources: a declared user is a
            // numbered entry of the file's own collection, and an adopted one has no configuration path at all.
            var path = user.Source == MailUserAccountSource.UserDeclaration
                ? $"{DeclaredUserOptions.SectionName}:{index}"
                : "document";

            var errors = await validator.FindUserMailAccountErrorsAsync(path, user.MailAccounts, cancellationToken);

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
    /// The deployment-wide rule <c>DeclaredUsers</c> states over a file, asked here over the roster this start would
    /// actually serve. It has to be asked in both places and neither is redundant: the file's own reading is what names
    /// the entry an operator corrects, and this one is what sees a record. A write into one user's record is judged
    /// against the roster this process settled, so two writes into two record-served users in one process run — each
    /// judged against a snapshot the other had not moved — can name the same account, and the start that composes them
    /// is where the two are first in one place.
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
        var deploymentAccounts = DeclaredUsers.DeploymentMailAccountsIn(this.configuration);

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
    /// Adopting the sole user of a deployment that declares none copies <c>MailSynchronization:Accounts</c> into their
    /// record and leaves the section where it was. Their entry then reads that record rather than the section, so the
    /// section belongs to nobody — and it is still bound, still searched first by the per-account lookup, and still the
    /// answer every read gets. What the operator was told is the opposite: that editing the configuration those
    /// accounts came from no longer changes what the deployment reads for them.
    /// </para>
    /// <para>
    /// The file's own reading cannot see this. <c>DeclaredUsers</c> refuses that section beside declared users, and
    /// here there are none: the users are records, which only the roster holds. So this is the reading that closes
    /// the invariant <c>MailSynchronizationOptions.FindConfiguredAccount</c> states about itself — the section and the
    /// roster never both answer — and it holds whether or not synchronization is switched on, because the lookup in
    /// front of every per-account read does not ask.
    /// </para>
    /// </remarks>
    private void RefuseADeploymentSectionNobodyReads(IReadOnlyList<ServedMailUser> served)
    {
        if (served.Any(user => user.Source == MailUserAccountSource.DeploymentSection))
        {
            return;
        }

        var deploymentAccounts = DeclaredUsers.DeploymentMailAccountsIn(this.configuration);

        if (deploymentAccounts.Count > 0)
        {
            throw DeploymentMailUserUnresolvedException.DeploymentSectionServesNobody(deploymentAccounts.Count);
        }
    }

    /// <summary>Refuses a deployment that switched synchronization on with nothing at all to synchronize.</summary>
    /// <remarks>
    /// The deployment's own rule, held here rather than against the files because a user's mailboxes are a record as
    /// well as a section: a deployment whose only mailbox was declared through <c>mfctl user account add</c> or by the
    /// user themselves has an empty <c>MailSynchronization:Accounts</c> and an empty top-level collection, and reading
    /// the files alone would refuse to start it. The roster is the first place every source is in one place, which is
    /// what makes this the only reading that can tell a worker with no work from one whose work is in the database.
    /// </remarks>
    private void RefuseNothingToSynchronize(IReadOnlyList<ServedMailUser> served)
    {
        if (!DeclaredUsers.SynchronizationIsOn(this.configuration))
        {
            return;
        }

        var deploymentAccounts = DeclaredUsers.DeploymentMailAccountsIn(this.configuration);

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

    /// <summary>Reports which users are served, where each is read from, and which held users are not served at all.</summary>
    private void Report(IReadOnlyList<ServedMailUser> served, IReadOnlyList<MailUserRecord> held)
    {
        var configuredUserCount = served.Count(user => user.ReadFromConfiguration);

        this.LogUsersResolved(served.Count, configuredUserCount, served.Count - configuredUserCount);

        foreach (var user in served.Where(user => user.Source == MailUserAccountSource.UserDocument))
        {
            this.LogUserReadFromTheirDocument(user.DisplayName);
        }

        foreach (var record in held.Where(record => served.All(user => user.User != record.User)))
        {
            this.LogUserNotServed(record.DisplayName);
        }
    }

    /// <remarks>The record names no user. The identity is a generated identifier for a person this deployment serves, and what an operator needs from this line is how the roster came out rather than who is on it.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "This deployment serves {ServedUserCount} users: {ConfiguredUserCount} read from configuration and {AdoptedUserCount} from their own document.")]
    private partial void LogUsersResolved(int servedUserCount, int configuredUserCount, int adoptedUserCount);

    /// <remarks>The label is the operator's own text for a row of their own file, which is what makes the line actionable: it is the user whose declared section has stopped being applied. Every part of that section is named, because the scanning block still binds and is still judged for an adopted user and then decides nothing, so a line naming only the mail accounts would leave an operator who switched a scanner on there with no sentence explaining why nothing changed.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The user labelled {UserDisplayName} is read from their own document; no configuration source reaches their mail accounts or the scanning posture declared beside them. Change them with mfctl.")]
    private partial void LogUserReadFromTheirDocument(string userDisplayName);

    /// <remarks>A warning rather than information, because a user the deployment holds and no longer serves keeps every message of theirs and synchronizes none of it, which is a state an operator meant either to reach or to notice.</remarks>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The user labelled {UserDisplayName} is held by this deployment and declared nowhere, so they are not served. Their mail is kept and neither read nor refreshed; removing them is an explicit act through mfctl.")]
    private partial void LogUserNotServed(string userDisplayName);

    /// <remarks>Reached only where the deployment holds no user row at all, which the release's own migration ordinarily provisions.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "This deployment declared no user and held none, so one has been recorded for the mail accounts it is configured with.")]
    private partial void LogSoleUserRecorded();
}
