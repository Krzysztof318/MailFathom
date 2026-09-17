// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text.Encodings.Web;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Security.ApiKeys;
using MailFathom.Host.Security.Transport;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Security.ApiKeys;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.ApiKeys;

/// <summary>Covers what the administrative API key handler decides beside the authenticator: whose key it was, and where from it may be used.</summary>
/// <remarks>Which keys match is <see cref="ApiKeyAuthenticator" />'s and is covered where it lives.</remarks>
public sealed class ApiKeyAuthenticationHandlerTests
{
    private const string KeyName = "alice-laptop";

    private const string KeyMaterial = "an-administrative-key-long-enough-to-be-one";

    [Theory]
    [InlineData("10.20.30.40")]
    [InlineData("::ffff:10.20.30.40")]
    public async Task AuthenticateAsync_AValidKeyFromInsideTheAdministratorsNetworks_IsAdmittedAsTheAdministrator(string source)
    {
        // Arrange
        var handler = await InitializeAsync(source);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal("alice", TransportCallerIdentity.NameOf(result.Principal!));
    }

    /// <summary>A leaked key is still a valid key, so the network is what refuses it — with the answer a wrong key gets.</summary>
    [Theory]
    [InlineData("198.51.100.7")]
    [InlineData(null)]
    public async Task AuthenticateAsync_AValidKeyFromOutsideTheAdministratorsNetworks_IsRefused(string? source)
    {
        // Arrange
        var handler = await InitializeAsync(source);

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        Assert.False(result.Succeeded);
        Assert.Null(result.Principal);
    }

    private static async Task<IAuthenticationHandler> InitializeAsync(string? source)
    {
        var administrator = ConfiguredAuthentication.Administrator(
            "alice",
            new AdministratorCredentialOptions
            {
                ApiKey = new ConfiguredSecret { Name = KeyName, SecretReference = $"plaintext:{KeyMaterial}" },
            });
        administrator.AllowedSourceNetworks.Add("10.0.0.0/8");

        var schemeOptions = new ApiKeyAuthenticationSchemeOptions
        {
            Surface = TransportSurface.Admin,
            ApiKeys = [administrator.Credentials[0].ApiKey!],
            AdministratorsByKeyName = new Dictionary<string, AdministratorAdmission>(StringComparer.Ordinal)
            {
                [KeyName] = AdministratorAdmission.For(administrator),
            },
        };

        var handler = new ApiKeyAuthenticationHandler(
            new TestOptionsMonitor<ApiKeyAuthenticationSchemeOptions>(schemeOptions),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            new ApiKeyAuthenticator(
                new PlaintextOnlySecretReferenceResolver(),
                new FakeTimeProvider(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero)),
                NullLogger<ApiKeyAuthenticator>.Instance));

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {KeyMaterial}";
        context.Connection.RemoteIpAddress = source is null ? null : IPAddress.Parse(source);

        await handler.InitializeAsync(
            new AuthenticationScheme(TransportSurface.Admin.ApiKeySchemeName, displayName: null, typeof(ApiKeyAuthenticationHandler)),
            context);

        return handler;
    }
}
