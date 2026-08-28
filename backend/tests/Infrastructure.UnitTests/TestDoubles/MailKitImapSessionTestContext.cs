// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Mail.MailKit;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailKit;
using MailKit.Net.Imap;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Builds the account, policy, folder, and session factory the MailKit adapter tests arrange around.</summary>
internal static class MailKitImapSessionTestContext
{
    /// <summary>The instant the adapter's clock reports, which every flag observation a test reads is stamped with.</summary>
    internal static DateTimeOffset ObservedAt { get; } = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    internal static MailAccountId PrimaryAccount { get; } = MailAccountId.Create("primary");

    internal static MailAccountId SecondaryAccount { get; } = MailAccountId.Create("secondary");

    /// <summary>Creates a generous isolated budget, so adapter tests wait only where the budget test asks them to.</summary>
    internal static MailServerConnectionBudget CreateConnectionBudget() => new(1000);

    internal static MailFolderResolution InboxFolder { get; } = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("inbox"),
        RemoteFolderPath.Create("INBOX", '/'));

    internal static MailTransportSecurityPolicy TlsOnConnectWithPlainPolicy { get; } =
        CreatePolicy(MailConnectionSecurity.TlsOnConnect, MailAuthenticationMechanism.Plain);

    /// <summary>Builds pipelines that make exactly one attempt, so a test about adapter behavior observes one call.</summary>
    internal static OutboundResilienceTestHost CreateSingleAttemptResilience() =>
        OutboundResilienceTestHost.WithConfiguredSettings(
            ("MailboxSessionEstablishment:MaxAttempts", "1"),
            ("MailboxDataRetrieval:MaxAttempts", "1"));

    internal static MailTransportSecurityPolicy CreatePolicy(
        MailConnectionSecurity connectionSecurity,
        MailAuthenticationMechanism permittedMechanism) => MailTransportSecurityPolicy.Create(
            connectionSecurity,
            MailAuthenticationPolicy.Create(
                [permittedMechanism],
                allowInsecureConnection: !MailTransportSecurityPolicy.GuaranteesEncryptedChannel(connectionSecurity),
                allowClearTextAuthenticationOverUnencryptedConnection: permittedMechanism.TransmitsCredentialsInClearText),
            MailServerCertificateTrust.SystemTrustStore,
            trustedCertificateAuthorityReference: null);

    /// <summary>Builds the folder a server answers a successful read-only selection with.</summary>
    /// <param name="uidValidity">The UIDVALIDITY the folder reports.</param>
    /// <param name="highestModSeq">The modification sequence the folder reports, where zero is the absence of one.</param>
    /// <returns>The selected folder.</returns>
    internal static IMailFolder CreateSelectedFolder(uint uidValidity = 7U, ulong highestModSeq = 0UL)
    {
        var folder = Substitute.For<IMailFolder>();
        folder.IsOpen.Returns(true);
        folder.UidValidity.Returns(uidValidity);
        folder.HighestModSeq.Returns(highestModSeq);

        return folder;
    }

    internal static EmailOccurrenceId CreateOccurrenceId(uint uid, uint uidValidity = 7U) => EmailOccurrenceId.Create(
        PrimaryAccount,
        InboxFolder.Id,
        ImapUidValidity.Create(uidValidity),
        ImapUid.Create(uid));

    /// <summary>Builds a catalog over one scripted connection, so a discovery test scripts the same server a session test does.</summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The returned test adapter owns the isolated process-lifetime budget through every connection it creates; no operating-system handle is allocated.")]
    internal static MailKitRemoteFolderCatalog CreateFolderCatalog(
        OutboundResilienceTestHost resilience,
        FakeImapClient client)
    {
        client.AuthenticationMechanisms.Add("PLAIN");

        return new MailKitRemoteFolderCatalog(
            () => client.Client,
            CreateSettingsProvider(),
            new UnusedMailAccessTokenSource(),
            resilience.Executor,
            resilience.TransientFailureClassifier,
            CreateConnectionBudget());
    }

    /// <summary>Opens a session over one scripted connection that authenticates with the default permitted mechanism.</summary>
    internal static Task<IMailboxSession> OpenSessionAsync(
        OutboundResilienceTestHost resilience,
        FakeImapClient client,
        IMailFolder folder)
    {
        var factory = CreateFactory(resilience, client, folder);
        client.AuthenticationMechanisms.Add("PLAIN");

        return factory.OpenReadOnlyAsync(
            PrimaryAccount,
            InboxFolder,
            TlsOnConnectWithPlainPolicy,
            CancellationToken.None);
    }

    internal static MailKitImapMailboxSessionFactory CreateFactory(
        OutboundResilienceTestHost resilience,
        FakeImapClient client,
        IMailFolder folder)
    {
        client.Folder = folder;

        return CreateFactory(resilience, () => client.Client, CreateSettingsProvider());
    }

    /// <summary>Opens a push session over one scripted connection, whichever way that server answered the capability.</summary>
    internal static Task<MailboxNotificationSessionResult> OpenNotificationSessionAsync(
        OutboundResilienceTestHost resilience,
        FakeImapClient client,
        IMailFolder folder,
        FakeTimeProvider clock)
    {
        client.Folder = folder;
        client.AuthenticationMechanisms.Add("PLAIN");

        return CreateNotificationSessionFactory(resilience, () => client.Client, clock).OpenAsync(
            PrimaryAccount,
            InboxFolder,
            TlsOnConnectWithPlainPolicy,
            CancellationToken.None);
    }

    /// <summary>Builds a push session factory over a scripted connection sequence, so a reconnection can be asserted.</summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The returned test factory owns the isolated process-lifetime budget through every connection it creates; no operating-system handle is allocated.")]
    internal static MailKitImapNotificationSessionFactory CreateNotificationSessionFactory(
        OutboundResilienceTestHost resilience,
        Func<IImapClient> clientFactory,
        FakeTimeProvider clock,
        ImapChangeSubscriptionCommand? requestFolderNotifications = null) =>
        new(
            clientFactory,
            CreateSettingsProvider(),
            new UnusedMailAccessTokenSource(),
            resilience.Executor,
            resilience.TransientFailureClassifier,
            CreateConnectionBudget(),
            requestFolderNotifications ?? ((_, _, _) => Task.CompletedTask),
            clock);

    /// <summary>Builds a factory over a scripted connection sequence and the real classifier the adapter consults.</summary>
    internal static MailKitImapMailboxSessionFactory CreateFactory(
        OutboundResilienceTestHost resilience,
        Func<IImapClient> clientFactory,
        IImapAccountSettingsProvider settingsProvider) =>
        CreateFactory(resilience, clientFactory, settingsProvider, new UnusedMailAccessTokenSource());

    /// <summary>Builds a factory whose connections authenticate with an access token from the supplied source.</summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The returned test factory owns the isolated process-lifetime budget through every connection it creates; no operating-system handle is allocated.")]
    internal static MailKitImapMailboxSessionFactory CreateFactory(
        OutboundResilienceTestHost resilience,
        Func<IImapClient> clientFactory,
        IImapAccountSettingsProvider settingsProvider,
        IMailAccessTokenSource accessTokenSource) =>
        new(
            clientFactory,
            settingsProvider,
            accessTokenSource,
            resilience.Executor,
            resilience.TransientFailureClassifier,
            CreateConnectionBudget(),
            new FakeTimeProvider(ObservedAt));

    /// <summary>A policy that permits only the registered token-bearing mechanism, so no password path is reachable.</summary>
    internal static MailTransportSecurityPolicy TlsOnConnectWithOAuthBearerPolicy { get; } =
        CreatePolicy(MailConnectionSecurity.TlsOnConnect, MailAuthenticationMechanism.OAuthBearer);

    /// <summary>Hands out one client per establishment attempt, in the order a test scripted the reconnections.</summary>
    /// <remarks>A request beyond the scripted sequence is a test asserting on a reconnection it did not intend, so it fails loudly.</remarks>
    internal static Func<IImapClient> ConnectionSequence(params FakeImapClient[] clients)
    {
        var pendingClients = new Queue<FakeImapClient>(clients);

        return () => pendingClients.Count > 0
            ? pendingClients.Dequeue().Client
            : throw new InvalidOperationException("The adapter established more connections than the test scripted.");
    }

    internal static IImapAccountSettingsProvider CreateSettingsProvider() => CreateSettingsProvider(out _);

    internal static IImapAccountSettingsProvider CreateSettingsProvider(out List<MailAccountConnectionMaterial> resolvedMaterial) =>
        CreateSettingsProvider(out resolvedMaterial, trustedCertificateAuthority: null);

    internal static IImapAccountSettingsProvider CreateSettingsProvider(
        out List<MailAccountConnectionMaterial> resolvedMaterial,
        X509Certificate2? trustedCertificateAuthority)
    {
        var issuedMaterial = new List<MailAccountConnectionMaterial>();
        var settingsProvider = Substitute.For<IImapAccountSettingsProvider>();
        settingsProvider.GetSettingsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(callInfo =>
        {
            var accountId = callInfo.Arg<string>() ?? PrimaryAccount.Value;
            var material = new MailAccountConnectionMaterial(
                ResolvedSecret.FromText("password"),
                trustedCertificateAuthority);
            issuedMaterial.Add(material);

            return Task.FromResult(new ImapAccountSettings(accountId, "imap.example.test", 993, "user", material));
        });

        resolvedMaterial = issuedMaterial;

        return settingsProvider;
    }
}
