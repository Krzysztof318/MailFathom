// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using MailFathom.Host.Configuration.RootSettings;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Signals;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Keeps this replica's persisted settings and roster at what PostgreSQL holds, whichever replica committed a change.</summary>
/// <remarks>
/// <para>
/// A committed write republishes only in the process that committed it, and this is how every other replica catches up.
/// It compares what it bound against the stored versions when another replica announces a change over the signal
/// backplane, and every <see cref="Interval" /> whether or not anything was announced. The interval is the guarantee and
/// the announcement is the optimization: an announcement is lost across any gap in the backplane and is never sent where
/// a deployment declares none, so a change reaches every replica within one interval of committing, and within a round
/// trip wherever its announcement arrives.
/// </para>
/// <para>
/// A reading that fails is reported and made again on the next interval, and never ends the worker. A database briefly
/// out of reach says nothing about whether a change is waiting, and every failure beneath this leaves the version in
/// force where it was.
/// </para>
/// </remarks>
internal sealed partial class ConfigurationConvergenceWorker : BackgroundService
{
    /// <summary>The longest a replica serves settings or a roster without comparing them against what PostgreSQL holds.</summary>
    /// <remarks>A constant rather than a setting, because it is the bound the documentation promises an operator about how long a change takes to reach every replica. What it costs is one statement over the users' versions and one read of the deployment's document per replica per interval.</remarks>
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly Channel<bool> announced = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    private readonly ConfigurationChangeAnnouncements announcements;
    private readonly ServedUsersConvergence users;
    private readonly Func<RootSettingsReloader?> rootSettings;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<ConfigurationConvergenceWorker> logger;

    /// <summary>Initializes the worker over the readings it runs.</summary>
    /// <param name="announcements">What another replica's change is heard through.</param>
    /// <param name="users">Brings the roster up to the users' records.</param>
    /// <param name="rootSettings">Resolves what brings the persisted layer up to the deployment's document, answering <see langword="null" /> where the host composed no persisted layer.</param>
    /// <param name="timeProvider">What the interval is measured by.</param>
    /// <param name="logger">Records a reading that failed.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public ConfigurationConvergenceWorker(
        ConfigurationChangeAnnouncements announcements,
        ServedUsersConvergence users,
        Func<RootSettingsReloader?> rootSettings,
        TimeProvider timeProvider,
        ILogger<ConfigurationConvergenceWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(announcements);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(rootSettings);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.announcements = announcements;
        this.users = users;
        this.rootSettings = rootSettings;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Resolved here rather than taken at construction, because the reader beneath it holds the connection pool and
        // the pool cannot be built until startup composed the connection string. Every hosted service is constructed
        // before any of them is started, so asking for this one at construction builds that pool a phase too early and
        // ends the process on a connection string that does not exist yet.
        var persistedSettings = this.rootSettings();

        var listening = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            listening = listening || await this.announcements.ListenAsync(() => this.announced.Writer.TryWrite(true));

            await this.WaitForAnnouncementOrIntervalAsync(stoppingToken);

            if (persistedSettings is not null)
            {
                await this.ReadInIsolationAsync(persistedSettings.ReloadAsync, stoppingToken);
            }

            await this.ReadInIsolationAsync(this.users.ConvergeAsync, stoppingToken);
        }
    }

    private async Task WaitForAnnouncementOrIntervalAsync(CancellationToken stoppingToken)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        await Task.WhenAny(
            Task.Delay(Interval, this.timeProvider, waiting.Token),
            this.announced.Reader.ReadAsync(waiting.Token).AsTask());

        await waiting.CancelAsync();

        stoppingToken.ThrowIfCancellationRequested();
    }

    /// <summary>Runs one reading, so that one failing leaves the other to run and the worker to go on.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A reading that failed leaves the version in force where it was and is made again on the next interval; ending the worker over it would leave the replica never catching up again.")]
    private async Task ReadInIsolationAsync(Func<CancellationToken, Task> reading, CancellationToken stoppingToken)
    {
        try
        {
            await reading(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.LogReadingFailed(Interval, exception);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "This replica could not compare what it serves against the persisted configuration and the users' records, and reads them again in {Interval}; what it bound stays in force.")]
    private partial void LogReadingFailed(TimeSpan interval, Exception exception);
}
