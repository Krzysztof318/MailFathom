// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using StackExchange.Redis;

namespace MailFathom.Host.Signals;

/// <summary>Tells every replica that a persisted configuration change committed, so each one reads what it composes from again.</summary>
/// <remarks>
/// <para>
/// A committed write republishes the deployment's persisted document, or a user's record, only in the process that
/// committed it. The other replicas hear of it here where the deployment runs the signal backplane, and on their own
/// interval where it runs none or where the announcement was lost. The announcement is an optimization over that
/// interval rather than the guarantee, exactly as a client signal is an optimization over the client's own re-read.
/// </para>
/// <para>
/// <b>It carries nothing.</b> The message says that something committed and never what: a replica that hears it reads
/// the versions PostgreSQL holds and republishes whatever is newer than what it bound. No setting, no user identifier,
/// and no version number crosses the backplane, and a message published by anything else able to reach the endpoint
/// costs one round of reads rather than a change.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0032-reaching-a-client-from-any-replica-over-websockets-and-a-resp-backplane.md#amendment-2-a-configuration-change-is-announced-over-the-backplane">ADR 0032, Amendment 2</see>
/// records that exception to what the backplane carries.
/// </para>
/// <para>
/// Neither half fails anything. Where no backplane is declared both do nothing, and where one is declared and cannot be
/// reached an announcement is dropped and listening is attempted again on the next interval.
/// </para>
/// </remarks>
internal sealed partial class ConfigurationChangeAnnouncements
{
    /// <summary>The channel an announcement travels on, beneath the deployment's own channel prefix.</summary>
    internal static readonly RedisChannel Channel = RedisChannel.Literal(":configuration-changed");

    private readonly Func<Task<ISubscriber>>? connect;
    private readonly ILogger<ConfigurationChangeAnnouncements> logger;
    private readonly Lock mutex = new();
    private Task<ISubscriber>? subscriber;

    /// <summary>Initializes the announcements over the backplane they travel on, or over none.</summary>
    /// <param name="connect">Opens the subscriber announcements travel through, or <see langword="null" /> where this deployment declared no backplane.</param>
    /// <param name="logger">Records an announcement that could not be published or listened for.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger" /> is <see langword="null" />.</exception>
    public ConfigurationChangeAnnouncements(
        Func<Task<ISubscriber>>? connect,
        ILogger<ConfigurationChangeAnnouncements> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        this.connect = connect;
        this.logger = logger;
    }

    /// <summary>Tells the other replicas that a change committed.</summary>
    /// <returns>A task that completes once the announcement was handed to the connection, or dropped.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "An announcement that could not be published must not fail the committed write that raised it; every replica still converges on its own interval.")]
    public async Task AnnounceAsync()
    {
        if (this.connect is null)
        {
            return;
        }

        try
        {
            var channel = await this.SubscriberAsync();

            await channel.PublishAsync(Channel, RedisValue.EmptyString, CommandFlags.FireAndForget);
        }
        catch (Exception exception)
        {
            this.LogAnnouncementDropped(exception);
        }
    }

    /// <summary>Starts hearing what the other replicas announce.</summary>
    /// <param name="announced">What runs when a replica announces a change. It runs on the connection's own thread, so it returns at once.</param>
    /// <returns><see langword="true" /> when there is nothing more to do — this replica listens, or no backplane is declared — and <see langword="false" /> when listening failed and is worth attempting again.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="announced" /> is <see langword="null" />.</exception>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A backplane that cannot be listened to must not stop the replica converging on its own interval; the caller attempts again.")]
    public async Task<bool> ListenAsync(Action announced)
    {
        ArgumentNullException.ThrowIfNull(announced);

        if (this.connect is null)
        {
            return true;
        }

        try
        {
            var channel = await this.SubscriberAsync();

            await channel.SubscribeAsync(Channel, (_, _) => announced());

            return true;
        }
        catch (Exception exception)
        {
            this.LogListeningFailed(exception);

            return false;
        }
    }

    private Task<ISubscriber> SubscriberAsync()
    {
        lock (this.mutex)
        {
            // An attempt that threw is made again rather than kept, for the reason the hub's lifetime manager asks its
            // factory again: the reference behind the endpoint may resolve, and the endpoint answer, by the next one.
            if (this.subscriber is not { IsFaulted: false, IsCanceled: false })
            {
                this.subscriber = this.connect!();
            }

            return this.subscriber;
        }
    }

    /// <remarks>Debug rather than warning, because losing the endpoint is already reported once as a transition by the backplane's own telemetry, and an announcement is one of as many as there are writes.</remarks>
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "A configuration change could not be announced over the signal backplane; every replica still reads it on its own interval.")]
    private partial void LogAnnouncementDropped(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "This replica could not listen for configuration changes over the signal backplane and attempts again on its next interval; until it does, a change another replica commits reaches it on that interval.")]
    private partial void LogListeningFailed(Exception exception);
}
