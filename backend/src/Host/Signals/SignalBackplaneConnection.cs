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
/// <b>The connection string is read when a connection is first wanted, and the endpoint is then kept for the life of
/// the process.</b> <c>RedisHubLifetimeManager</c> holds the multiplexer the factory handed it and calls the factory
/// again only after an attempt that threw, so what a later reconnection uses is the
/// <see cref="ConfigurationOptions" /> parsed at first use rather than the reference read again. A credential rotated
/// behind an unchanged reference therefore takes effect at the next start — unlike the material behind a request-path
/// credential, and unlike a host that never reached the endpoint at all, whose next attempt does re-read it.
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
        ConnectionMultiplexer connection;

        try
        {
            var endpoint = await this.ReadEndpointAsync();
            connection = await ConnectionMultiplexer.ConnectAsync(endpoint, log);
        }
        catch
        {
            // A reference that resolves to nothing, a connection string the client refuses, and one that parses to no
            // endpoint at all are the same condition to every screen that stops being told things as an endpoint this
            // replica dialled and could not reach. None of the three is proved at startup — the gate there proves the
            // reference resolves, not that what it resolved to is a connection string — so without this the whole
            // symptom of a typo is a deployment that serves correctly and fans nothing out. The lifetime manager keeps
            // no multiplexer after an attempt that threw and asks the factory again on the next publish, so this runs
            // once per raised signal for as long as the fault lasts; what keeps that from becoming a line and a
            // measurement per publish is the telemetry reporting a transition rather than a state.
            this.telemetry.RecordLost();

            throw;
        }

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

        // The transitions no handler can see, both of them. With AbortOnConnectFail off the connect completes against
        // an endpoint that answered nothing, and whatever the library raised during that first attempt was raised
        // before anything above was subscribed — so a replica whose backplane was never reachable would otherwise
        // report nothing at all. The other direction is this attempt succeeding after one that threw: the lifetime
        // manager asks the factory again on the next publish, and a replica fanning signals again while its last
        // transition reads lost is the state an operator is told to alert on.
        if (connection.IsConnected)
        {
            this.telemetry.RecordRestored();
        }
        else
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

        return ComposeEndpoint(material.RevealAsString(), this.settings);
    }

    /// <summary>Reads the operator's connection string and applies the two decisions this deployment takes over it.</summary>
    /// <param name="connectionString">What the declared reference resolved to.</param>
    /// <param name="settings">The section the channel prefix is read from.</param>
    /// <returns>The endpoint a connection is opened against.</returns>
    /// <remarks>
    /// Separate from the resolution around it so both overrides are reachable without dialling anything. Each of them
    /// is a decision a connection string may contradict and neither is the operator's to take: the prefix is what
    /// keeps two deployments sharing one endpoint from receiving each other's statements, and the retry posture is
    /// what keeps an unreachable endpoint from being a failed start.
    /// </remarks>
    internal static ConfigurationOptions ComposeEndpoint(string connectionString, SignalBackplaneOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var endpoint = ConfigurationOptions.Parse(connectionString);

        // Set after the parse rather than before it, so a connection string carrying either is corrected rather than
        // obeyed.
        endpoint.ChannelPrefix = RedisChannel.Literal(settings.ChannelPrefix);
        endpoint.AbortOnConnectFail = false;

        return endpoint;
    }
}
