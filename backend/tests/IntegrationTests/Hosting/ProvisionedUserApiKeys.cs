// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>One API key credential a shape's deployment holds, as the row a user was provisioned.</summary>
/// <param name="Key">What the client presents.</param>
/// <param name="User">Whose mail it reaches, which is also the rate-limit partition it spends.</param>
/// <param name="Permissions">What it may do, which is everything a mail surface publishes unless the shape narrows it.</param>
/// <param name="EndpointAccess">Which of the two mail-serving endpoints the user may be served on, which is where a surface's isolation from another now lives: the credentials are rows one store answers, so a key reaching only one surface is a switch on its user rather than a second key list.</param>
/// <remarks><see cref="ToString" /> is redacted, because the key is the whole of the credential and a test failure prints whatever a record's own formatting produces.</remarks>
internal sealed record ProvisionedUserApiKey(
    string Key,
    MailUserId User,
    IReadOnlyList<MailFathomPermission>? Permissions = null,
    MailUserEndpointAccess? EndpointAccess = null)
{
    /// <inheritdoc />
    public override string ToString() => $"{nameof(ProvisionedUserApiKey)} {{ {this.User.Value:D} }}";
}

/// <summary>Stands in for the credential rows a user-facing endpoint's API keys are, in a shape that reaches no database.</summary>
/// <remarks>
/// <para>
/// A key a user's client presents is a row rather than a configured value, so a shape that used to write its keys into
/// the endpoint's section now provisions them here instead. Everything about the pipeline stays the deployment's own:
/// the scheme registration, the handler, the limiter, and the grant are all composed as they would be, and what is
/// replaced is the store the handler reads and the minter that reduces a presented key to what resolves one.
/// </para>
/// <para>
/// The minter is hand-written rather than substituted, for the reason <c>ComposedPasswordAuthenticationTests</c> gives
/// about the hasher: <see cref="IUserApiKeyMinter.TryDigest" /> takes a <see cref="ReadOnlySpan{T}" /> and a dynamic
/// proxy cannot carry a by-ref-like argument through its invocation. It reduces a key to itself, which is all a shape
/// needs — what the real one computes is covered where it lives.
/// </para>
/// </remarks>
internal static class ProvisionedUserApiKeys
{
    /// <summary>Composes the registration a shape hands <c>InProcessComposedHost.StartAsync</c>, holding exactly these keys.</summary>
    /// <param name="provisioned">The credentials the deployment holds. A key no entry names resolves nothing, which is how an unrecognized credential is refused.</param>
    /// <returns>What to run over the builder once the composition is done.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="provisioned" /> is <see langword="null" />.</exception>
    internal static Action<WebApplicationBuilder> Holding(params ProvisionedUserApiKey[] provisioned)
    {
        ArgumentNullException.ThrowIfNull(provisioned);

        // Composed once rather than per scope, because a substitute is configured through NSubstitute's ambient call
        // context: building one while another request is building its own is what that context cannot carry, and the
        // shape's answer holds for every request whichever scope asks.
        var credentials = StoreHolding(provisioned);

        return builder =>
        {
            builder.Services.RemoveAll<IUserApiKeyMinter>();
            builder.Services.AddSingleton<IUserApiKeyMinter>(new KeyIsItsOwnLookup());
            builder.Services.RemoveAll<IUserCredentialStore>();
            builder.Services.AddSingleton(credentials);
        };
    }

    /// <summary>The store as a deployment holding exactly these credentials answers.</summary>
    private static IUserCredentialStore StoreHolding(IReadOnlyList<ProvisionedUserApiKey> provisioned)
    {
        var credentials = Substitute.For<IUserCredentialStore>();
        var byLookup = provisioned.ToDictionary(
            static key => key.Key,
            static key => new ResolvedUserCredential(
                Guid.NewGuid(),
                key.User,
                UserCredentialMethod.ApiKey,
                key.Permissions ?? MailFathomPermission.PublishedFor(ProtectedSurface.Mail),
                Enabled: true,
                Material: null,
                key.EndpointAccess ?? MailUserEndpointAccess.Everywhere),
            StringComparer.Ordinal);

        credentials.FindAsync(Arg.Any<UserCredentialMethod>(), Arg.Any<UserCredentialLookup>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<UserCredentialMethod>() == UserCredentialMethod.ApiKey
                && byLookup.TryGetValue(callInfo.Arg<UserCredentialLookup>().Value, out var credential)
                    ? credential
                    : null);

        return credentials;
    }

    /// <summary>Reduces a presented key to itself, so a shape names its credentials by the text a client sends.</summary>
    private sealed class KeyIsItsOwnLookup : IUserApiKeyMinter
    {
        public MintedUserApiKey Mint() =>
            throw new NotSupportedException("A shape provisions its keys by writing them down rather than by minting one.");

        public bool TryDigest(ReadOnlySpan<char> presentedKey, out UserCredentialLookup lookup)
        {
            if (presentedKey.IsWhiteSpace())
            {
                lookup = default;

                return false;
            }

            lookup = UserCredentialLookup.ForDigest(presentedKey.ToString());

            return true;
        }
    }
}
