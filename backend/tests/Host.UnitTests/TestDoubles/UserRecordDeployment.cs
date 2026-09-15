// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.Records;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Configuration.UserSettings.Administration;
using MailFathom.Host.Signals;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Database;
using MailFathom.Infrastructure.Secrets.Resolution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>A deployment holding user records and mail accounts, composed as the services the user and account routes are published over.</summary>
/// <remarks>
/// <para>
/// Every administration is built rather than substituted, because they are concrete types the handlers take and
/// because what a route test is asking about is the boundary in front of real rules rather than in front of a scripted
/// answer. What is substituted is the row underneath — the reader, the writer, the directory, the provisioning, and the
/// erasure — so a test states what the deployment holds without a database. The mail accounts are held in memory
/// instead, because every account write moves the version of the user records it reaches and a test reads that
/// version back through the reader.
/// </para>
/// <para>
/// The binder is the real one for the same reason: a candidate a route accepted and the binder would refuse is exactly
/// the defect a substituted binder would hide.
/// </para>
/// </remarks>
internal sealed class UserRecordDeployment
{
    /// <summary>The instant the binder judges a date-bound rule at, so nothing here is drawn from the wall clock.</summary>
    private static readonly DateTimeOffset Today = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Composes the deployment for a caller granted the permissions a test's routes are published under.</summary>
    /// <param name="granted">The permissions the caller holds.</param>
    /// <param name="actingFor">The user the caller acts for, or the default for one acting for nobody's mail.</param>
    /// <param name="scanning">The deployment's own scanning section, which an account's block may only tighten; the default scans nothing.</param>
    internal UserRecordDeployment(
        IReadOnlyList<MailFathomPermission> granted,
        MailUserId actingFor = default,
        SensitiveContentOptions? scanning = null)
    {
        ArgumentNullException.ThrowIfNull(granted);

        var principals = Substitute.For<IAuthorizedPrincipalSource>();
        principals.Current.Returns(actingFor.IsSpecified
            ? AuthorizedPrincipal.CallerActingFor(actingFor, "operations", granted)
            : AuthorizedPrincipal.Caller("operations", granted));

        this.Documents = Substitute.For<IUserSettingsDocumentReader>();
        this.Documents.ReadVersionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => this.MailAccountRecords.Versions());

        this.Store = Substitute.For<IUserSettingsDocumentWriter>();
        this.Store
            .CommitAsync(
                Arg.Any<MailUserId>(),
                Arg.Any<string>(),
                Arg.Any<MailUserEndpointAccess>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(call => (long?)call.ArgAt<long>(3) + 1);

        this.Directory = Substitute.For<IMailUserDirectory>();
        this.Directory.ReadUsersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        this.Provisioning = Substitute.For<IMailUserProvisioning>();
        this.Provisioning
            .ProvisionAsync(Arg.Any<MailUserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        this.Provisioning
            .RelabelAsync(Arg.Any<MailUserId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        this.Erasure = Substitute.For<IMailUserErasure>();
        this.Erasure.EraseAsync(Arg.Any<MailUserId>(), Arg.Any<CancellationToken>()).Returns(false);

        // A roster naming somebody no test acts on, so every user a test writes for reads as one nothing declares —
        // which is the ordinary case — until the test says otherwise.
        this.ServedUsers.Resolved(
        [
            new(
                MailUserId.Create(new Guid("99999999-9999-9999-9999-999999999999")),
                "nobody-these-tests-name",
                []),
        ]);

        var authorization = new AccessAuthorization(principals);
        var settings = new ConfigurationBuilder().Build();
        var admission = new SeveralUserAdmission(
            Options.Create(new McpEndpointOptions()),
            Options.Create(new ClientEndpointOptions()));

        this.Roster = new UserRosterAdministration(
            authorization,
            this.Directory,
            this.Provisioning,
            this.Erasure,
            this.MailAccountRecords,
            this.Quiescing,
            this.Store,
            this.ServedUsers,
            admission,
            new ConfigurationChangeAnnouncements(connect: null, NullLogger<ConfigurationChangeAnnouncements>.Instance),
            NullLogger<UserRosterAdministration>.Instance);

        var binder = new UserAccountDocumentBinder(
            new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
            new FakeTimeProvider(Today),
            Options.Create(scanning ?? new SensitiveContentOptions()));

        this.Records = new UserRecordAdministration(
            authorization,
            this.Documents,
            this.Store,
            binder,
            SecretValidation.OverRegisteredSchemes(),
            this.ServedUsers,
            Substitute.For<IStoredFileStore>(),
            new ConfigurationChangeAnnouncements(connect: null, NullLogger<ConfigurationChangeAnnouncements>.Instance));

        this.MailAccounts = new MailAccountAdministration(
            authorization,
            this.Documents,
            this.MailAccountRecords,
            binder,
            SecretValidation.OverRegisteredSchemes(),
            new ServedMailUsersConvergence(
                UserRecordScopes.Resolving(this.Documents, binder),
                this.ServedUsers,
                new HeldBackRecords(),
                new RecordingLogger<ServedMailUsersConvergence>()),
            new ConfigurationChangeAnnouncements(
                () => Task.FromResult(this.Backplane.Connect()),
                new RecordingLogger<ConfigurationChangeAnnouncements>()));

        this.StoredSecrets = Substitute.For<IStoredSecretStore>();
        this.StoredSecrets.CanStore.Returns(true);
        this.StoredSecrets.StoreAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<DatabaseSecretReference>(),
                Arg.Any<MailUserId>(),
                Arg.Any<SecretName>(),
                Arg.Any<ResolvedSecret>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<DatabaseSecretReference>(1));
        var session = Substitute.For<IPersistenceSession>();
        session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
        var sessions = Substitute.For<IPersistenceSessionFactory>();
        sessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(session);
        this.Secrets = new StoredSecretAdministration(
            authorization,
            this.Documents,
            this.StoredSecrets,
            new OptimisticConcurrencyRetryPolicy(
                sessions,
                new PersistenceConcurrencyOptions { MaximumCommitAttempts = 1 },
                new FakeTimeProvider(Today)));
    }

    /// <summary>Gets the roster administration the deployment-wide routes are published over.</summary>
    internal UserRosterAdministration Roster { get; }

    /// <summary>Gets the record administration both user-record surfaces are published over.</summary>
    internal UserRecordAdministration Records { get; }

    /// <summary>Gets the mail-account administration the account routes and a user's own account routes are published over.</summary>
    internal MailAccountAdministration MailAccounts { get; }

    /// <summary>Gets the accounts the deployment holds and the user records they are assigned to.</summary>
    internal InMemoryMailAccountRecordStore MailAccountRecords { get; } = new();

    /// <summary>Gets the quiescing an erasure runs under, which lets the work through unless a test refuses it.</summary>
    internal RecordedMailAccountWorkQuiescing Quiescing { get; } = new();

    /// <summary>Gets the roster this process serves, which an account write converges before announcing.</summary>
    internal ServedMailUsers ServedUsers { get; } = new();

    /// <summary>Gets the backplane an account write is announced over, which nobody hears until a test listens.</summary>
    internal InMemoryBackplane Backplane { get; } = new();

    /// <summary>Gets the stored-secret administration exposed by the user routes.</summary>
    internal StoredSecretAdministration Secrets { get; }

    /// <summary>Gets the substituted sealed-material store.</summary>
    internal IStoredSecretStore StoredSecrets { get; }

    /// <summary>Gets the substituted reader one user's row is stated through.</summary>
    internal IUserSettingsDocumentReader Documents { get; }

    /// <summary>Gets the substituted writer every commit reaches.</summary>
    internal IUserSettingsDocumentWriter Store { get; }

    /// <summary>Gets the substituted roster read.</summary>
    internal IMailUserDirectory Directory { get; }

    /// <summary>Gets the substituted envelope write.</summary>
    internal IMailUserProvisioning Provisioning { get; }

    /// <summary>Gets the substituted erasure.</summary>
    internal IMailUserErasure Erasure { get; }

    /// <summary>States the record one user's row holds, and the mail accounts assigned to them.</summary>
    /// <param name="user">The user.</param>
    /// <param name="json">The record, as the row holds it.</param>
    /// <param name="version">The version the row stands at.</param>
    /// <param name="accounts">The accounts assigned to the user.</param>
    internal void Holding(MailUserId user, string json, long version, params MailAccountRecord[] accounts)
    {
        this.MailAccountRecords.HoldUser(user, json, version, accounts);
        this.Documents.ReadAsync(user, Arg.Any<CancellationToken>())
            .Returns(_ => this.MailAccountRecords.DocumentOf(user));
    }

    /// <summary>States the users this deployment holds, whether or not this process serves them.</summary>
    /// <param name="held">The users.</param>
    internal void Held(params MailUserRecord[] held) =>
        this.Directory.ReadUsersAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(held);

    /// <summary>States the roster this process settled at start.</summary>
    /// <param name="served">The users served, and where each one's mail accounts are read from.</param>
    internal void Serving(params ServedMailUser[] served) => this.ServedUsers.Resolved(served);
}
