// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.Observability;
using MailFathom.Common.Observability;
using MailFathom.Domain.Accounts;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Makes a synchronization cycle readable as work rather than as a sequence of log lines.</summary>
/// <remarks>
/// <para>
/// No instrumentation package exists for the library this subsystem talks to, so the part of MailFathom that spends the
/// most wall-clock time would otherwise publish nothing at all. What is published here answers the two questions an
/// operator opens a dashboard with: is this account still synchronizing, and if it is slow, which part of it is. The
/// first is the account's schedule, which the gauges carry; the second is the cycle span with one child per folder,
/// which is what attributes a stalled cycle to the folder it stalled in instead of to the account as a whole.
/// </para>
/// <para>
/// A cycle's span opens once the account holds one of the slots that bound how many accounts run at once, so its
/// duration is the run rather than the run plus its wait. The wait is not lost: it is the queue depth beside it, which
/// is what separates a pipeline that is idle from one whose accounts are all queued behind a bound an operator could
/// raise.
/// </para>
/// <para>
/// The dimensions are MailFathom's own configured account and folder aliases and closed sets of its own words. Nothing
/// per email, per UID, per address, or per subject appears, and no remote folder path does — every one of those would
/// open a time series per message or per person quite apart from putting mail in a span store. Nothing MailKit reports
/// reaches any of this either: MailFathom attaches no protocol logger to any client it opens, so protocol traffic is
/// written nowhere for a level or a setting to expose.
/// </para>
/// </remarks>
public sealed class MailSynchronizationTelemetry : IMailSynchronizationPhaseTelemetry
{
    /// <summary>The name one account's synchronization cycle opens its span under.</summary>
    /// <remarks>
    /// Named after the operation rather than after the worker that drives it, so the span reads as the work that was
    /// done and stays right if the cycle is ever scheduled from somewhere else.
    /// </remarks>
    internal const string AccountRunSpanName = "synchronize_account";

    /// <summary>The name one folder's turn through a cycle opens its span under, always beneath the span above.</summary>
    internal const string FolderRunSpanName = "synchronize_folder";

    /// <summary>The name turning the configured alias into an advertised folder opens its span under.</summary>
    internal const string ResolveFolderSpanName = "resolve_mail_folder";

    /// <summary>The name opening the read-only session the run works over opens its span under.</summary>
    internal const string OpenSessionSpanName = "open_mailbox_session";

    /// <summary>The name the forward walk over the folder opens its span under.</summary>
    internal const string DiscoverEmailsSpanName = "discover_mailbox_emails";

    /// <summary>The name one batch of the forward walk's listing opens its span under.</summary>
    internal const string FetchEmailBatchSpanName = "fetch_email_batch";

    /// <summary>The name the backward pass over the window opens its span under.</summary>
    internal const string ReconcileFolderSpanName = "reconcile_mailbox_folder";

    /// <summary>The name retrieving the content an earlier run deferred opens its span under.</summary>
    internal const string RefillDeferredContentSpanName = "refill_deferred_content";

    internal const string AccountTagName = "mailfathom.mail.account";
    internal const string FolderTagName = "mailfathom.mail.folder";
    internal const string OutcomeTagName = "mailfathom.mail.sync.outcome";
    internal const string FailureTagName = "mailfathom.mail.sync.failure";
    internal const string ScheduledFolderCountTagName = "mailfathom.mail.sync.folders";
    internal const string FailedFolderCountTagName = "mailfathom.mail.sync.folders.failed";
    internal const string StoredEmailCountTagName = "mailfathom.mail.sync.stored";
    internal const string SkippedEmailCountTagName = "mailfathom.mail.sync.skipped";

    internal const string SucceededOutcomeName = "succeeded";
    internal const string FailedOutcomeName = "failed";

    /// <summary>Names a cycle or a folder that ended without reporting how, which is what shutdown produces.</summary>
    internal const string InterruptedOutcomeName = "interrupted";

    internal const string AliasUnresolvedOutcomeName = "alias_unresolved";
    internal const string AliasAmbiguousOutcomeName = "alias_ambiguous";

    internal const string ConcurrencyConflictFailureName = "concurrency_conflict";
    internal const string MailServerUnavailableFailureName = "mail_server_unavailable";
    internal const string CredentialRefusedFailureName = "credential_refused";
    internal const string UnexpectedFailureName = "unexpected";

    private readonly ConcurrentDictionary<string, AccountSchedule> scheduleByAccount = new(StringComparer.Ordinal);
    private readonly TimeProvider timeProvider;
    private readonly Histogram<double> runDuration;
    private readonly Counter<long> storedEmails;
    private readonly Counter<long> skippedEmails;
    private readonly Counter<long> failures;

    private long queuedRunCount;
    private long activeRunCount;

    /// <summary>Initializes the instruments a synchronization cycle is published through.</summary>
    /// <param name="timeProvider">Measures how long a cycle took.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public MailSynchronizationTelemetry(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;

        this.runDuration = Telemetry.Meter.CreateHistogram<double>(
            "mailfathom.mail.sync.run.duration",
            unit: "s",
            description: "How long one account's synchronization cycle took, by account and outcome.");
        this.storedEmails = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.sync.emails.stored",
            unit: "{email}",
            description: "Messages a folder run stored with their content, by account and folder alias.");
        this.skippedEmails = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.sync.emails.skipped",
            unit: "{email}",
            description: "Messages a folder run recorded from their envelope alone because they exceeded the size limit, by account and folder alias.");
        this.failures = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.sync.failures",
            unit: "{failure}",
            description: "Folder runs that did not complete, by account, folder alias, and what stopped them.");

        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.mail.sync.backoff",
            this.ObserveBackoff,
            unit: "s",
            description: "How long each account waits before its next synchronization run, which is its configured interval until a run fails.");
        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.mail.sync.consecutive_failures",
            this.ObserveConsecutiveFailures,
            unit: "{run}",
            description: "How many synchronization runs of each account failed in a row, which is what the wait beside it is derived from.");
        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.mail.sync.runs.queued",
            this.ObserveQueuedRuns,
            unit: "{run}",
            description: "Account synchronization runs waiting for one of the slots that bound how many accounts run at once.");
        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.mail.sync.runs.active",
            this.ObserveActiveRuns,
            unit: "{run}",
            description: "Account synchronization runs holding one of those slots.");
    }

    /// <summary>Counts one account run as waiting for a slot until the returned scope is disposed.</summary>
    /// <returns>The scope, which the caller disposes as soon as the wait ends however it ended.</returns>
    /// <remarks>
    /// The wait is counted rather than spanned, because a run that has not started is not work a trace can attribute
    /// anything to. What an operator needs from it is a level: how many accounts are queued behind the bound right now.
    /// </remarks>
    public IDisposable EnterRunQueue()
    {
        Interlocked.Increment(ref this.queuedRunCount);

        return new QueuedRun(this);
    }

    /// <summary>Opens the span one account's synchronization cycle is reported as, and returns the scope that ends it.</summary>
    /// <param name="accountId">The account whose cycle is beginning.</param>
    /// <returns>The scope, which the caller must dispose; a scope disposed without <see cref="AccountRunScope.Completed" /> reports an interrupted cycle.</returns>
    public AccountRunScope BeginAccountRun(MailAccountId accountId)
    {
        var activity = Telemetry.ActivitySource.StartActivity(AccountRunSpanName);
        activity?.SetTag(AccountTagName, accountId.Value);

        Interlocked.Increment(ref this.activeRunCount);

        return new AccountRunScope(this, accountId, activity, this.timeProvider.GetTimestamp());
    }

    /// <summary>Opens the span one folder's turn through a cycle is reported as, beneath the cycle's own span.</summary>
    /// <param name="accountId">The account the folder belongs to.</param>
    /// <returns>The scope, which the caller must dispose after recording how the folder ended.</returns>
    /// <remarks>
    /// The folder alias is carried by the outcome rather than by this call, because a mapping that reached the run
    /// unusable has only the configured alias to name until it has been turned into one.
    /// </remarks>
    public FolderRunScope BeginFolderRun(MailAccountId accountId)
    {
        var activity = Telemetry.ActivitySource.StartActivity(FolderRunSpanName, ActivityKind.Client);
        activity?.SetTag(AccountTagName, accountId.Value);

        return new FolderRunScope(this, accountId, activity);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The stage carries no account and no folder alias of its own. It is always started inside the folder run's span,
    /// which already names both, so repeating them here would put the same two dimensions on every stage of every run
    /// to say what the parent says once.
    /// </remarks>
    public IMailSynchronizationPhaseScope BeginPhase(
        MailSynchronizationPhase phase,
        CancellationToken cancellationToken) =>
        new PhaseScope(Telemetry.ActivitySource.StartActivity(SpanNameOf(phase)), cancellationToken);

    /// <summary>Reads the name one stage of a folder run is published under.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the stage is not one this adapter publishes.</exception>
    private static string SpanNameOf(MailSynchronizationPhase phase) => phase switch
    {
        MailSynchronizationPhase.ResolveFolder => ResolveFolderSpanName,
        MailSynchronizationPhase.OpenSession => OpenSessionSpanName,
        MailSynchronizationPhase.DiscoverEmails => DiscoverEmailsSpanName,
        MailSynchronizationPhase.FetchEmailBatch => FetchEmailBatchSpanName,
        MailSynchronizationPhase.ReconcileFolder => ReconcileFolderSpanName,
        MailSynchronizationPhase.RefillDeferredContent => RefillDeferredContentSpanName,
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "The synchronization stage has no published span name."),
    };

    /// <summary>Publishes the wait an account's next run is scheduled behind, and the failure count that produced it.</summary>
    /// <param name="accountId">The account the wait belongs to.</param>
    /// <param name="delayBeforeNextRun">What the run backoff decided, which is the configured interval while nothing is failing.</param>
    /// <param name="consecutiveFailureCount">How many of the account's runs have failed in a row.</param>
    /// <remarks>
    /// Both are published because neither is readable alone: a wait says nothing about health without the interval it
    /// is being compared against, and a failure count says nothing about when the server will next be approached.
    /// </remarks>
    public void RecordScheduledDelay(
        MailAccountId accountId,
        TimeSpan delayBeforeNextRun,
        int consecutiveFailureCount) =>
        this.scheduleByAccount[accountId.Value] =
            new AccountSchedule(delayBeforeNextRun, consecutiveFailureCount);

    /// <summary>Stops publishing an account's schedule, because nothing is scheduling it any more.</summary>
    /// <param name="accountId">The account whose supervision has ended.</param>
    /// <remarks>
    /// An account the operator removed, or one whose supervisor ended, would otherwise report the wait it was last
    /// scheduled behind for the life of the process — a flat line that reads as an account waiting rather than as one
    /// nobody is running.
    /// </remarks>
    public void RecordSupervisionEnded(MailAccountId accountId) =>
        this.scheduleByAccount.TryRemove(accountId.Value, out _);

    private void LeaveRunQueue() => Interlocked.Decrement(ref this.queuedRunCount);

    private void EndAccountRun(MailAccountId accountId, string outcome, TimeSpan elapsed)
    {
        Interlocked.Decrement(ref this.activeRunCount);

        this.runDuration.Record(
            elapsed.TotalSeconds,
            new TagList
            {
                { AccountTagName, accountId.Value },
                { OutcomeTagName, outcome },
            });
    }

    private void RecordFolderSynchronized(
        MailAccountId accountId,
        string folderAlias,
        int storedEmailCount,
        int skippedEmailCount)
    {
        var tags = new TagList
        {
            { AccountTagName, accountId.Value },
            { FolderTagName, folderAlias },
        };

        this.storedEmails.Add(storedEmailCount, tags);
        this.skippedEmails.Add(skippedEmailCount, tags);
    }

    private void RecordFolderFailure(MailAccountId accountId, string folderAlias, string failure) =>
        this.failures.Add(
            1,
            new TagList
            {
                { AccountTagName, accountId.Value },
                { FolderTagName, folderAlias },
                { FailureTagName, failure },
            });

    private TimeSpan ElapsedSince(long startingTimestamp) => this.timeProvider.GetElapsedTime(startingTimestamp);

    private IEnumerable<Measurement<double>> ObserveBackoff() =>
    [
        .. this.scheduleByAccount.Select(account => new Measurement<double>(
            account.Value.DelayBeforeNextRun.TotalSeconds,
            new TagList { { AccountTagName, account.Key } })),
    ];

    private IEnumerable<Measurement<long>> ObserveConsecutiveFailures() =>
    [
        .. this.scheduleByAccount.Select(account => new Measurement<long>(
            account.Value.ConsecutiveFailureCount,
            new TagList { { AccountTagName, account.Key } })),
    ];

    private Measurement<long> ObserveQueuedRuns() => new(Interlocked.Read(ref this.queuedRunCount));

    private Measurement<long> ObserveActiveRuns() => new(Interlocked.Read(ref this.activeRunCount));

    /// <summary>What an account's supervisor last decided about when it runs again.</summary>
    /// <param name="DelayBeforeNextRun">The wait the run backoff produced, which is the configured interval while nothing is failing.</param>
    /// <param name="ConsecutiveFailureCount">How many of the account's runs failed in a row.</param>
    private readonly record struct AccountSchedule(TimeSpan DelayBeforeNextRun, int ConsecutiveFailureCount);

    /// <summary>Carries one stage of a folder run from the span that opens it to the ending that closes it.</summary>
    /// <remarks>
    /// A stage that never reported completing is published as interrupted where the run's token was cancelled and as
    /// failed otherwise, which is the same distinction the folder run above it draws and for the same reason: shutdown
    /// is not something the work did, and a restart that marked every stage in flight as an error would fill a trace
    /// store with failures a rolling deployment produced.
    /// </remarks>
    private sealed class PhaseScope(Activity? activity, CancellationToken cancellationToken)
        : IMailSynchronizationPhaseScope
    {
        private bool completed;
        private bool reported;

        public void Completed() => this.completed = true;

        public void Dispose()
        {
            if (this.reported)
            {
                return;
            }

            this.reported = true;

            if (activity is null)
            {
                return;
            }

            var outcome = this.completed
                ? SucceededOutcomeName
                : cancellationToken.IsCancellationRequested ? InterruptedOutcomeName : FailedOutcomeName;

            activity.SetTag(OutcomeTagName, outcome);
            activity.SetStatus(
                outcome switch
                {
                    SucceededOutcomeName => ActivityStatusCode.Ok,
                    FailedOutcomeName => ActivityStatusCode.Error,
                    _ => ActivityStatusCode.Unset,
                });
            activity.Dispose();
        }
    }

    /// <summary>Holds one account run's place in the queue for a slot until the wait ends.</summary>
    private sealed class QueuedRun(MailSynchronizationTelemetry telemetry) : IDisposable
    {
        private bool left;

        public void Dispose()
        {
            if (this.left)
            {
                return;
            }

            this.left = true;
            telemetry.LeaveRunQueue();
        }
    }

    /// <summary>Carries one account's cycle from the moment it takes a slot to the outcome that ends it.</summary>
    /// <remarks>
    /// A cycle that never reported an outcome is published as interrupted rather than as failed. Shutdown is what
    /// produces one, and counting it as a failure would make every restart look like an account that stopped working.
    /// </remarks>
    public sealed class AccountRunScope : IDisposable
    {
        private readonly MailSynchronizationTelemetry telemetry;
        private readonly MailAccountId accountId;
        private readonly Activity? activity;
        private readonly long startingTimestamp;

        private string outcome = InterruptedOutcomeName;
        private bool reported;

        internal AccountRunScope(
            MailSynchronizationTelemetry telemetry,
            MailAccountId accountId,
            Activity? activity,
            long startingTimestamp)
        {
            this.telemetry = telemetry;
            this.accountId = accountId;
            this.activity = activity;
            this.startingTimestamp = startingTimestamp;
        }

        /// <summary>Gets how long the cycle has been running, which is its duration once the work is done.</summary>
        public TimeSpan Elapsed => this.telemetry.ElapsedSince(this.startingTimestamp);

        /// <summary>Records what the cycle turned out to have done.</summary>
        /// <param name="scheduledFolderCount">How many folders the cycle was scheduled to synchronize.</param>
        /// <param name="failedFolderCount">How many of them did not complete.</param>
        /// <param name="convergenceFailed">Whether the pass that carries outstanding mailbox changes failed.</param>
        public void Completed(int scheduledFolderCount, int failedFolderCount, bool convergenceFailed)
        {
            this.outcome = failedFolderCount > 0 || convergenceFailed ? FailedOutcomeName : SucceededOutcomeName;

            this.activity?.SetTag(ScheduledFolderCountTagName, scheduledFolderCount);
            this.activity?.SetTag(FailedFolderCountTagName, failedFolderCount);
            this.activity?.SetStatus(
                failedFolderCount > 0 || convergenceFailed ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (this.reported)
            {
                return;
            }

            this.reported = true;

            this.activity?.SetTag(OutcomeTagName, this.outcome);
            this.telemetry.EndAccountRun(this.accountId, this.outcome, this.Elapsed);
            this.activity?.Dispose();
        }
    }

    /// <summary>Carries one folder's turn through a cycle from the span that opens it to the outcome that ends it.</summary>
    /// <remarks>
    /// An alias the mail server advertises no single folder for is an outcome of its own rather than a failure, which
    /// is the same distinction the supervisor draws when it decides whether to back the account off: a configuration
    /// mistake is remedied by an edit, and counting it as a failure would put a working account into backoff.
    /// </remarks>
    public sealed class FolderRunScope : IDisposable
    {
        private readonly MailSynchronizationTelemetry telemetry;
        private readonly MailAccountId accountId;
        private readonly Activity? activity;

        private bool reported;

        internal FolderRunScope(
            MailSynchronizationTelemetry telemetry,
            MailAccountId accountId,
            Activity? activity)
        {
            this.telemetry = telemetry;
            this.accountId = accountId;
            this.activity = activity;
        }

        /// <summary>Records a folder that was synchronized, and what it brought in.</summary>
        /// <param name="folderAlias">MailFathom's own name for the folder.</param>
        /// <param name="storedEmailCount">How many messages were stored with their content.</param>
        /// <param name="skippedEmailCount">How many were recorded from their envelope alone.</param>
        public void Synchronized(string folderAlias, int storedEmailCount, int skippedEmailCount)
        {
            this.Report(folderAlias, SucceededOutcomeName, ActivityStatusCode.Ok);

            this.activity?.SetTag(StoredEmailCountTagName, storedEmailCount);
            this.activity?.SetTag(SkippedEmailCountTagName, skippedEmailCount);
            this.telemetry.RecordFolderSynchronized(
                this.accountId,
                folderAlias,
                storedEmailCount,
                skippedEmailCount);
        }

        /// <summary>Records an alias the mail server advertises no folder for.</summary>
        /// <param name="folderAlias">The configured alias that matched nothing.</param>
        public void AliasUnresolved(string folderAlias) =>
            this.Report(folderAlias, AliasUnresolvedOutcomeName, ActivityStatusCode.Unset);

        /// <summary>Records an alias several advertised folders matched.</summary>
        /// <param name="folderAlias">The configured alias that matched more than one folder.</param>
        public void AliasAmbiguous(string folderAlias) =>
            this.Report(folderAlias, AliasAmbiguousOutcomeName, ActivityStatusCode.Unset);

        /// <summary>Records a folder deferred by a competing writer this run could not resolve against.</summary>
        /// <param name="folderAlias">MailFathom's own name for the folder.</param>
        public void ConcurrencyConflict(string folderAlias) => this.Failed(folderAlias, ConcurrencyConflictFailureName);

        /// <summary>Records a folder deferred because the mail server did not serve it within its resilience budget.</summary>
        /// <param name="folderAlias">MailFathom's own name for the folder.</param>
        public void MailServerUnavailable(string folderAlias) =>
            this.Failed(folderAlias, MailServerUnavailableFailureName);

        /// <summary>Records a folder the mail server would not let this account reach, because it refused the credential.</summary>
        /// <param name="folderAlias">MailFathom's own name for the folder.</param>
        /// <remarks>
        /// Separate from every other failure name because an operator acts on it rather than waits it out: the count
        /// under this name is the one that means somebody has to replace a credential.
        /// </remarks>
        public void CredentialRefused(string folderAlias) => this.Failed(folderAlias, CredentialRefusedFailureName);

        /// <summary>Records a folder that ended in a way nothing above it anticipated.</summary>
        /// <param name="folderAlias">MailFathom's own name for the folder, or the configured alias where no mapping was built.</param>
        public void UnexpectedFailure(string folderAlias) => this.Failed(folderAlias, UnexpectedFailureName);

        /// <summary>Records a folder the host stopped before it finished, which is shutdown rather than a failure.</summary>
        /// <param name="folderAlias">MailFathom's own name for the folder.</param>
        public void Interrupted(string folderAlias) =>
            this.Report(folderAlias, InterruptedOutcomeName, ActivityStatusCode.Unset);

        /// <inheritdoc />
        public void Dispose()
        {
            // A folder that reported nothing is a path added above without an outcome beside it. The span says so
            // rather than the counter, because no alias is known here to count it under.
            if (!this.reported)
            {
                this.activity?.SetStatus(ActivityStatusCode.Error);
            }

            this.activity?.Dispose();
        }

        private void Failed(string folderAlias, string failure)
        {
            this.Report(folderAlias, FailedOutcomeName, ActivityStatusCode.Error);

            this.activity?.SetTag(FailureTagName, failure);
            this.telemetry.RecordFolderFailure(this.accountId, folderAlias, failure);
        }

        private void Report(string folderAlias, string outcome, ActivityStatusCode status)
        {
            this.reported = true;

            this.activity?.SetTag(FolderTagName, folderAlias);
            this.activity?.SetTag(OutcomeTagName, outcome);
            this.activity?.SetStatus(status);
        }
    }
}
