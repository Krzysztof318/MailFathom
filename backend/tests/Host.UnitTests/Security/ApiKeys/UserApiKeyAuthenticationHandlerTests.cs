// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Encodings.Web;
using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.ApiKeys;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Security.ApiKeys;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.ApiKeys;

/// <summary>Covers what the user API key handler decides beside the authenticator: whether the surface serves the user the key resolved.</summary>
/// <remarks>Which keys resolve a credential is <see cref="UserApiKeyAuthenticator" />'s and is covered where it lives.</remarks>
public sealed class UserApiKeyAuthenticationHandlerTests
{
    private static readonly Guid CredentialId = new("0197c0de-0000-7000-8000-000000000002");

    private static readonly MailUserId CredentialUser =
        MailUserId.Create(new Guid("0197c0de-0000-7000-8000-00000000ffff"));

    /// <summary>A key whose user is kept off the endpoint authenticates nobody there, which is the challenge an unknown key meets.</summary>
    [Fact]
    public async Task AuthenticateAsync_AKeyWhoseUserIsKeptOffTheMcpEndpoint_IsRefusedThere()
    {
        // Arrange
        var handler = await InitializeAsync(
            TransportSurface.Mcp,
            new MailUserEndpointAccess(McpEndpoint: false, ClientEndpoint: true));

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        Assert.False(result.Succeeded);
        Assert.Null(result.Principal);
    }

    /// <summary>The switch is the user's and names one endpoint, so the same key still opens the other.</summary>
    [Fact]
    public async Task AuthenticateAsync_AKeyWhoseUserIsKeptOffTheMcpEndpoint_IsStillAdmittedOnTheClient()
    {
        // Arrange
        var handler = await InitializeAsync(
            TransportSurface.Client,
            new MailUserEndpointAccess(McpEndpoint: false, ClientEndpoint: true));

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(CredentialUser, TransportCallerUser.CarriedBy(result.Principal!));
    }

    private static async Task<IAuthenticationHandler> InitializeAsync(
        TransportSurface surface,
        MailUserEndpointAccess endpointAccess)
    {
        var credentials = Substitute.For<IUserCredentialStore>();
        credentials.FindAsync(UserCredentialMethod.ApiKey, StatedApiKeyMinter.Lookup, Arg.Any<CancellationToken>())
            .Returns(new ResolvedUserCredential(
                CredentialId,
                CredentialUser,
                UserCredentialMethod.ApiKey,
                [MailFathomPermission.MailRead],
                Enabled: true,
                Material: null,
                endpointAccess));

        var handler = new UserApiKeyAuthenticationHandler(
            new StaticOptionsMonitor(new UserApiKeyAuthenticationSchemeOptions { Surface = surface }),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            new UserApiKeyAuthenticator(credentials, new StatedApiKeyMinter()));

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {StatedApiKeyMinter.Key}";

        await handler.InitializeAsync(
            new AuthenticationScheme(surface.ApiKeySchemeName, displayName: null, typeof(UserApiKeyAuthenticationHandler)),
            context);

        return handler;
    }

    /// <summary>Reduces one stated key, because the real minter is internal to the infrastructure boundary.</summary>
    private sealed class StatedApiKeyMinter : IUserApiKeyMinter
    {
        internal const string Key = "mfk_stated-key";

        internal static readonly UserCredentialLookup Lookup = UserCredentialLookup.ForDigest("stated-digest");

        public MintedUserApiKey Mint() => new(Key, Lookup);

        public bool TryDigest(ReadOnlySpan<char> presentedKey, out UserCredentialLookup lookup)
        {
            lookup = presentedKey.SequenceEqual(Key) ? Lookup : default;

            return lookup.IsSpecified;
        }
    }

    /// <summary>Hands the handler the one options instance it is built with.</summary>
    private sealed class StaticOptionsMonitor(UserApiKeyAuthenticationSchemeOptions schemeOptions)
        : IOptionsMonitor<UserApiKeyAuthenticationSchemeOptions>
    {
        public UserApiKeyAuthenticationSchemeOptions CurrentValue { get; } = schemeOptions;

        public UserApiKeyAuthenticationSchemeOptions Get(string? name) => this.CurrentValue;

        public IDisposable? OnChange(Action<UserApiKeyAuthenticationSchemeOptions, string?> listener) => null;
    }
}
