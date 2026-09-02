// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Domain.Transport;
using MailFathom.Infrastructure.Mail;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Delivery;

/// <summary>Proves against a real SMTP server that a delivery session opens and reports what that server will accept.</summary>
/// <remarks>
/// <para>
/// Only a server settles this. A substitute reports the capabilities a test told it to report, so what MailKit reads
/// out of a real greeting — which extensions were offered, which mechanisms the account's allow-list has to be narrowed
/// against — is established here and nowhere else, in both directions: an account the server can satisfy authenticates,
/// and one it cannot is refused before a credential is presented.
/// </para>
/// <para>
/// The orchestrated mail server speaks one transport mode. It offers plain SMTP on a container port with no
/// <c>STARTTLS</c> to negotiate and no certificate to validate, so implicit TLS and <c>STARTTLS</c> are not exercised
/// here and a pass says nothing about them; the mapping from a configured mode onto the client's socket option is
/// settled in the unit suite, where every mode can be scripted.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedDeliverySessionTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>
    /// GreenMail 2.1.11 answers <c>EHLO</c> with <c>AUTH PLAIN LOGIN XOAUTH2</c> and <c>SMTPUTF8</c>, and with neither
    /// <c>SIZE</c> nor <c>8BITMIME</c>. That is what the three facts are asserted against, and the assertion holds
    /// because the app model pins the image tag; a failure here is the pin having moved, and what it costs is stated
    /// rather than assumed — an unbounded message size and eight-bit content are the two facts this server cannot
    /// exercise, so both are proven against a scripted greeting in the unit suite instead. The two absences are read
    /// off the same answer that reports the internationalized-address extension present, so an observation channel
    /// that silently reported nothing would fail this test rather than pass it.
    /// </summary>
    [Fact]
    public async Task OpenForDeliveryAsync_TheOrchestratedSubmissionServer_OpensAndReportsWhatItAdvertised()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act
        var capabilities = await services.InScopeAsync(
            async (scope, token) =>
            {
                var policy = scope.GetRequiredService<IMailTransportSecurityPolicyReader>()
                    .GetDeliveryPolicy(SyntheticMailAccount.AccountId);

                await using var session = await scope.GetRequiredService<IMailDeliverySessionFactory>()
                    .OpenForDeliveryAsync(SyntheticMailAccount.AccountId, policy!, token);

                return session.Capabilities;
            },
            cancellationToken);

        // Assert
        TestContext.Current.TestOutputHelper?.WriteLine(
            "The orchestrated submission server advertised: maximum message bytes "
            + (capabilities.MaxMessageBytes?.ToString(CultureInfo.InvariantCulture) ?? "none")
            + $", eight-bit content {capabilities.AcceptsEightBitContent}"
            + $", internationalized addresses {capabilities.AcceptsInternationalizedAddresses}.");

        Assert.Null(capabilities.MaxMessageBytes);
        Assert.False(capabilities.AcceptsEightBitContent);
        Assert.True(capabilities.AcceptsInternationalizedAddresses);
        Assert.True(capabilities.PermitsMessageOfSize(long.MaxValue));
    }

    /// <summary>
    /// SMTP has no clear-text command to fall back to when no advertised mechanism is permitted, so the account is
    /// refused before a credential is presented rather than left to the mail library. Which mechanisms are really on
    /// offer is the part only a server supplies: this one advertises <c>XOAUTH2</c> and not its standards-track
    /// successor, which is exactly the disagreement an account restricted to the latter would meet in a deployment.
    /// </summary>
    [Fact]
    public async Task OpenForDeliveryAsync_AccountPermittingOnlyAMechanismTheServerDoesNotAdvertise_IsRefused()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        var oauthBearerOnlyPolicy = MailTransportSecurityPolicy.Create(
            MailConnectionSecurity.None,
            MailAuthenticationPolicy.Create(
                [MailAuthenticationMechanism.OAuthBearer],
                allowInsecureConnection: true,
                allowClearTextAuthenticationOverUnencryptedConnection: false),
            MailServerCertificateTrust.SystemTrustStore,
            trustedCertificateAuthorityReference: null);

        // Act, Assert
        await services.InScopeAsync(
            async (scope, token) =>
            {
                var factory = scope.GetRequiredService<IMailDeliverySessionFactory>();

                return await Assert.ThrowsAsync<MailAuthenticationMechanismUnavailableException>(() =>
                    factory.OpenForDeliveryAsync(SyntheticMailAccount.AccountId, oauthBearerOnlyPolicy, token));
            },
            cancellationToken);
    }
}
