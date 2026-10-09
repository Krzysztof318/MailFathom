// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using MailFathom.Application.Access.Grants;
using MailFathom.Host.Configuration.RootSettings;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Signals;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Keeps this replica's persisted settings and the users it holds at what PostgreSQL holds, whichever replica committed a change.</summary>
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
/// The same interval reads again the settings columns of an account a build older than them wrote the document of alone.
/// That is a write to the shared rows rather than to this replica, and every replica makes it, which is safe because it
/// is conditional on the version each one read.
/// </para>
/// <para>
/// The grants this replica computed are forgotten on the same occasions, before anything is read, so a change to a
/// role, an assignment, a group, or an organization reaches a request here within the same bound as a change to a
/// setting. Forgetting cannot fail, which is why it is not one of the readings below.
/// </para>
/// <para>
/// A reading that fails is reported and made again on the next interval, and never ends the worker. A database briefly
/// out of reach says nothing about whether a change is waiting, and every failure beneath this leaves the version in
/// force where it was.
/// </para>
/// </remarks>
internal sealed partial class ConfigurationConvergenceWorker : BackgroundService
{
    /// <summary>The longest a replica serves settings or a held user without comparing them against what PostgreSQL holds.</summary>
    /// <remarks>A constant rather than a setting, because it is the bound the documentation promises an operator about how long a change takes to reach every replica. What it costs is one statement over the versions of the users this replica holds, one read of the deployment's document, and one read of the accounts whose settings columns trail their document per replica per interval — plus a short write transaction for each of up to <see cref="MailAccountSettingsReconciliation.MaximumAccountsPerReading" /> such accounts, which is nothing once a rolling upgrade has finished.</remarks>
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly Channel<bool> announced = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    private readonly ConfigurationChangeAnnouncements announcements;
    private readonly ServedUsers users;
    private readonly MailAccountSettingsReconciliation accountSettings;
    private readonly UserGrantCache grants;
    private readonly Func<RootSettingsReloader?> rootSettings;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<ConfigurationConvergenceWorker> logger;

    /// <summary>Initializes the worker over the readings it runs.</summary>
    /// <param name="announcements">What another replica's change is heard through.</param>
    /// <param name="users">Brings the users this replica holds up to their records.</param>
    /// <param name="accountSettings">Brings the settings columns of an account an older build wrote up to its document.</param>
    /// <param name="grants">The grants this replica computed, forgotten whenever the worker wakes.</param>
    /// <param name="rootSettings">Resolves what brings the persisted layer up to the deployment's document, answering <see langword="null" /> where the host composed no persisted layer.</param>
    /// <param name="timeProvider">What the interval is measured by.</param>
    /// <param name="logger">Records a reading that failed.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public ConfigurationConvergenceWorker(
        ConfigurationChangeAnnouncements announcements,
        ServedUsers users,
        MailAccountSettingsReconciliation accountSettings,
        UserGrantCache grants,
        Func<RootSettingsReloader?> rootSettings,
        TimeProvider timeProvider,
        ILogger<ConfigurationConvergenceWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(announcements);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(accountSettings);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(rootSettings);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.announcements = announcements;
        this.users = users;
        this.accountSettings = accountSettings;
        this.grants = grants;
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

            this.grants.Forget();

            if (persistedSettings is not null)
            {
                await this.RunInIsolationAsync(persistedSettings.ReloadAsync, this.LogReadingFailed, stoppingToken);
            }

            await this.RunInIsolationAsync(this.users.ConvergeAsync, this.LogReadingFailed, stoppingToken);
            await this.RunInIsolationAsync(this.accountSettings.ReconcileAsync, this.LogReconciliationFailed, stoppingToken);
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

    /// <summary>Runs one reading, so that one failing leaves the others to run and the worker to go on.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A reading that failed leaves the version in force where it was and is made again on the next interval; ending the worker over it would leave the replica never catching up again.")]
    private async Task RunInIsolationAsync(
        Func<CancellationToken, Task> reading,
        Action<TimeSpan, Exception> report,
        CancellationToken stoppingToken)
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
            report(Interval, exception);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "This replica could not compare what it serves against the persisted configuration and the users' records, and reads them again in {Interval}; what it bound stays in force.")]
    private partial void LogReadingFailed(TimeSpan interval, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "This replica could not read the accounts whose settings columns trail their document, and reads them again in {Interval}; every question asked of all accounts at once goes on answering from what those columns hold.")]
    private partial void LogReconciliationFailed(TimeSpan interval, Exception exception);
}
