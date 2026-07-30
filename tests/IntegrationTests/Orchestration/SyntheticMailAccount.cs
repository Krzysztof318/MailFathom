// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.AppHost;
using MailMcp.Application.Mail;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Synchronization;
using MailMcp.Domain.Transport;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Secrets;

namespace MailMcp.IntegrationTests.Orchestration;

/// <summary>The one throwaway mailbox the orchestrated mail server serves, as the adapter's two ports see it.</summary>
/// <remarks>
/// <para>
/// The suite supplies these ports itself rather than composing the host's configuration-bound ones, for the same reason
/// it does not start the host resource: what is under test is the mail adapter against a real server, not how a
/// composition root binds an options section. Both implementations are small enough that stating them here is clearer
/// than reaching into another assembly's internals for them.
/// </para>
/// <para>
/// The policy is the weakest one MailMcp will build, and deliberately: the server speaks plain IMAP on a container port
/// and offers no SASL mechanism, so reaching it requires the unencrypted-connection opt-in and the clear-text
/// authentication opt-in together. That combination is exactly what a test of the clear-text fallback needs to
/// exercise, and it is confined to a container that lives for one run.
/// </para>
/// </remarks>
internal sealed class SyntheticMailAccount(OrchestratedMailServerEndpoints endpoints)
    : IImapAccountSettingsProvider, IMailTransportSecurityPolicyReader, IMailSynchronizationWindowReader
{
    /// <summary>Gets the account identifier every occurrence this suite stores belongs to.</summary>
    public static MailAccountId AccountId { get; } = MailAccountId.Create("integration");

    /// <inheritdoc />
    /// <remarks>
    /// Unbounded, because every test seeds the mail it then expects a run to find. A bound would silently exclude a
    /// seeded email whenever the container's clock and the seeding date disagreed about the day, which would look like a
    /// synchronization defect rather than like the arrangement it was.
    /// </remarks>
    public MailSynchronizationWindow GetWindow(MailAccountId accountId) => MailSynchronizationWindow.Unbounded;

    /// <inheritdoc />
    public MailTransportSecurityPolicy GetPolicy(MailAccountId accountId) => MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.None,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain, MailAuthenticationMechanism.Login],
            allowInsecureConnection: true,
            allowClearTextAuthenticationOverUnencryptedConnection: true),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    /// <inheritdoc />
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the resolved material passes to the connection attempt that requested it, which disposes it when the attempt ends.")]
    public Task<ImapAccountSettings> GetSettingsAsync(string accountId, CancellationToken cancellationToken) =>
        Task.FromResult(new ImapAccountSettings(
            AccountId.Value,
            endpoints.ImapHost,
            endpoints.ImapPort,
            OrchestrationContract.MailServerAccountUserName,
            new MailAccountConnectionMaterial(
                ResolvedSecret.FromText(OrchestrationContract.MailServerAccountPassword),
                TrustedCertificateAuthority: null)));
}
