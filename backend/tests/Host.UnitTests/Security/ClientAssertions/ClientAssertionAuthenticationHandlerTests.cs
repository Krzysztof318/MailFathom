// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using MailFathom.Common.ClientAssertions;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Security.ClientAssertions;
using MailFathom.Host.Security.Transport;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Secrets.Discovery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.ClientAssertions;

/// <summary>Covers what the administrative assertion handler decides beside the authenticator: whose key signed, and where from it may be used.</summary>
/// <remarks>What makes an assertion valid is <see cref="ClientAssertionAuthenticator" />'s and is covered where it lives.</remarks>
public sealed class ClientAssertionAuthenticationHandlerTests
{
    private const string KeyName = "alice-cron";

    private static readonly DateTimeOffset VerifiedAt = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("10.20.30.40")]
    [InlineData("::ffff:10.20.30.40")]
    public async Task AuthenticateAsync_AValidAssertionFromInsideTheAdministratorsNetworks_IsAdmittedAsTheAdministrator(string source)
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var handler = await InitializeAsync(clientKey, source);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal("alice", TransportCallerIdentity.NameOf(result.Principal!));
    }

    /// <summary>A copied private key signs valid assertions, so the network is what refuses it — with the answer a bad signature gets.</summary>
    [Theory]
    [InlineData("198.51.100.7")]
    [InlineData(null)]
    public async Task AuthenticateAsync_AValidAssertionFromOutsideTheAdministratorsNetworks_IsRefused(string? source)
    {
        // Arrange
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var handler = await InitializeAsync(clientKey, source);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        Assert.False(result.Succeeded);
        Assert.Null(result.Principal);
    }

    private static async Task<IAuthenticationHandler> InitializeAsync(ECDsa clientKey, string? source)
    {
        var administrator = ConfiguredAuthentication.Administrator(
            "alice",
            new AdministratorCredentialOptions
            {
                PublicKey = new ConfiguredSecret
                {
                    Name = KeyName,
                    SecretReference = $"plaintext:{clientKey.ExportSubjectPublicKeyInfoPem()}",
                },
            });
        administrator.AllowedSourceNetworks.Add("10.0.0.0/8");

        var schemeOptions = new ClientAssertionAuthenticationSchemeOptions
        {
            Surface = TransportSurface.Admin,
            PublicKeys = [administrator.Credentials[0].PublicKey!],
            AdministratorsByKeyName = new Dictionary<string, AdministratorAdmission>(StringComparer.Ordinal)
            {
                [KeyName] = AdministratorAdmission.For(administrator),
            },
        };

        var clock = new FakeTimeProvider(VerifiedAt);
        var handler = new ClientAssertionAuthenticationHandler(
            new TestOptionsMonitor<ClientAssertionAuthenticationSchemeOptions>(schemeOptions),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            new ClientAssertionAuthenticator(
                new PlaintextOnlySecretReferenceResolver(),
                new ClientAssertionReplayStore(new InMemoryClientAssertionSpendStore(), clock),
                clock,
                NullLogger<ClientAssertionAuthenticator>.Instance));

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization =
            $"Bearer {ClientAssertionMinter.Mint(clientKey, ClientAssertion.AdminAudience, VerifiedAt)}";
        context.Connection.RemoteIpAddress = source is null ? null : IPAddress.Parse(source);

        await handler.InitializeAsync(
            new AuthenticationScheme(
                TransportSurface.Admin.ClientAssertionSchemeName,
                displayName: null,
                typeof(ClientAssertionAuthenticationHandler)),
            context);

        return handler;
    }
}
