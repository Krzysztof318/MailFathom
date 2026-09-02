// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.AppHost;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Resolution;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Where the orchestrated mailbox's mail is submitted, as the delivery adapter's settings port sees it.</summary>
/// <remarks>
/// It is a type of its own rather than a second port on <see cref="SyntheticMailAccount" /> because the two ports name
/// their one method identically, which is the shape a deployment gets for free — a component resolving where mail is
/// read cannot thereby resolve where it is sent — and which a single class could only satisfy by implementing one of
/// them explicitly.
/// </remarks>
internal sealed class SyntheticSubmissionAccount(OrchestratedMailServerEndpoints endpoints) : ISmtpAccountSettingsProvider
{
    /// <inheritdoc />
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the resolved material passes to the connection attempt that requested it, which disposes it when the attempt ends.")]
    public Task<SmtpAccountSettings> GetSettingsAsync(string accountId, CancellationToken cancellationToken) =>
        Task.FromResult(new SmtpAccountSettings(
            SyntheticMailAccount.AccountId.Value,
            endpoints.SmtpHost,
            endpoints.SmtpPort,
            OrchestrationContract.MailServerAccountUserName,
            new MailAccountConnectionMaterial(
                ResolvedSecret.FromText(OrchestrationContract.MailServerAccountPassword),
                TrustedCertificateAuthority: null)));
}
