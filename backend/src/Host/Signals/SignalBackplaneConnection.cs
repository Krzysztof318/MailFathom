// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Signals;
using MailFathom.Infrastructure.Secrets.References;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace MailFathom.Host.Signals;

/// <summary>Opens the connection the signal backplane runs over, from the endpoint this deployment declared.</summary>
/// <remarks>
/// <para>
/// A configured options type rather than a closure passed to the registration, because what the connection needs is
/// resolved from the container: the secret reference behind the connection string, and the telemetry both transitions
/// are reported through. The factory it installs is called by the hub lifetime manager rather than by composition,
/// which is what lets the credential be resolved asynchronously and lets a host whose backplane is unreachable finish
/// starting.
/// </para>
/// <para>
/// <b>The connection string is resolved per connection rather than once.</b> The factory runs again whenever the
/// lifetime manager has no usable connection, so a password rotated behind an unchanged reference is picked up by the
/// next attempt with no restart to schedule — the same promise every other credential in this deployment carries.
/// </para>
/// <para>
/// <b>The endpoint never being reachable is not a startup failure.</b> <c>AbortOnConnectFail</c> is forced off over
/// whatever the operator's connection string says, so the multiplexer returns a connection that is not yet connected
/// and reconnects on its own instead of throwing — which is the posture a signal deserves, being an optimization over
/// a client that already re-reads on its own interval. Nothing here gates readiness on it.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this options configurator.")]
internal sealed class SignalBackplaneConnection : IConfigureOptions<RedisOptions>
{
    private readonly SignalBackplaneOptions settings;
    private readonly ISecretReferenceResolver secretReferenceResolver;
    private readonly SignalBackplaneTelemetry telemetry;

    /// <summary>Initializes the configurator over what it needs to open one connection.</summary>
    /// <param name="settings">The declared endpoint and channel prefix.</param>
    /// <param name="secretReferenceResolver">Turns the declared reference into the connection string.</param>
    /// <param name="telemetry">Reports losing the endpoint and having it back.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public SignalBackplaneConnection(
        IOptions<SignalBackplaneOptions> settings,
        ISecretReferenceResolver secretReferenceResolver,
        SignalBackplaneTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(secretReferenceResolver);
        ArgumentNullException.ThrowIfNull(telemetry);

        this.settings = settings.Value;
        this.secretReferenceResolver = secretReferenceResolver;
        this.telemetry = telemetry;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options" /> is <see langword="null" />.</exception>
    public void Configure(RedisOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ConnectionFactory = this.ConnectAsync;
    }

    /// <summary>Opens one connection to the declared endpoint.</summary>
    /// <param name="log">Where the client writes its own connection diagnostics, which the lifetime manager binds to its logger.</param>
    /// <returns>The connection, which may not have reached the endpoint yet.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the declared reference resolves to no material, which the caller retries on its next attempt.</exception>
    /// <remarks>
    /// The material reaches a string here and nowhere else. <see cref="ConfigurationOptions" /> parses one and holds
    /// the password inside it for the life of the connection, so there is no erasable form to keep it in — the buffer
    /// it was resolved into is still erased as soon as it has been read, which is what bounds the copies to the one the
    /// client itself holds.
    /// </remarks>
    private async Task<IConnectionMultiplexer> ConnectAsync(TextWriter log)
    {
        var connection = await ConnectionMultiplexer.ConnectAsync(await this.ReadEndpointAsync(), log);

        // Both events are raised once per connection the multiplexer holds, and the interactive one is left out for
        // the reason the library leaves it out of its own logging: a drop raises the same condition twice, and it is
        // the subscription connection that carries a signal.
        connection.ConnectionFailed += (_, failure) =>
        {
            if (failure.ConnectionType is not ConnectionType.Interactive)
            {
                this.telemetry.RecordLost();
            }
        };

        connection.ConnectionRestored += (_, restoration) =>
        {
            if (restoration.ConnectionType is not ConnectionType.Interactive)
            {
                this.telemetry.RecordRestored();
            }
        };

        // The one transition no handler can see. With AbortOnConnectFail off the connect completes against an endpoint
        // that answered nothing, and whatever the library raised during that first attempt was raised before anything
        // above was subscribed — so a replica whose backplane was never reachable would otherwise be the one case that
        // reports nothing at all, which is exactly the case an operator has to be told about. The library's own
        // reconnection then raises the restoration, so the pair still reads as a transition rather than as a state.
        if (!connection.IsConnected)
        {
            this.telemetry.RecordLost();
        }

        return connection;
    }

    private async Task<ConfigurationOptions> ReadEndpointAsync()
    {
        // No cancellation token is reachable here: the factory is a framework contract that takes none, and the
        // attempt it belongs to is abandoned by the lifetime manager rather than cancelled.
        var resolution = await this.secretReferenceResolver.ResolveAsync(
            this.settings.ConnectionString?.SecretReference,
            CancellationToken.None);

        // The failure names the setting and nothing else, for the reason every other resolution failure does: the
        // target of a reference is a path or a store identifier, which resolution keeps out of its own result.
        using var material = resolution.Secret ?? throw new InvalidOperationException(
            $"{SignalBackplaneOptions.SectionName}:{nameof(SignalBackplaneOptions.ConnectionString)} could not be resolved [{resolution.Failure}].");

        var endpoint = ConfigurationOptions.Parse(material.RevealAsString());

        // Both are this deployment's decision rather than the operator's, and both are set after the parse so a
        // connection string carrying either is corrected rather than obeyed. The prefix is what keeps two applications
        // sharing one endpoint from receiving each other's statements; the retry posture is what keeps an unreachable
        // endpoint from being a failed start.
        endpoint.ChannelPrefix = RedisChannel.Literal(this.settings.ChannelPrefix);
        endpoint.AbortOnConnectFail = false;

        return endpoint;
    }
}
