// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;
using MailFathom.Application.Accounts;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Domain.Transport;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Secrets.References;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Configures periodic IMAP synchronization.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailSynchronizationOptions
    : IValidatableObject,
        IMailTransportSecurityPolicyReader,
        IMailSynchronizationWindowReader,
        IRemotelyDeletedEmailDispositionReader,
        IAuthoredDeleteEmailDispositionReader,
        IMailboxMutationAuditSettingsReader,
        IMailAccountCatalog
{
    /// <summary>The shutdown budget the .NET Generic Host applies when nothing configures one.</summary>
    private static readonly TimeSpan DefaultHostShutdownTimeout = TimeSpan.FromSeconds(30);

    /// <summary>What the shutdown budget keeps for the hosted services that stop beside a synchronization drain.</summary>
    private static readonly TimeSpan HostShutdownMargin = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets whether periodic synchronization is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the interval between reconciliation runs of one account.</summary>
    /// <remarks>The interval is measured from the end of one run to the start of the next, so a run that outlives it delays the account rather than overlapping itself.</remarks>
    [Range(typeof(TimeSpan), "00:00:10", "1.00:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets or sets the longest an account waits between runs while its runs keep failing.</summary>
    /// <remarks>
    /// It bounds the run-level backoff described on <see cref="SynchronizationRunBackoff" />, which grows from
    /// <see cref="Interval" /> and returns to it after a successful run. A value below the interval would ask backoff
    /// to run a failing account more often than a healthy one, so it fails startup.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:10", "1.00:00:00")]
    public TimeSpan MaxFailureBackoff { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Gets or sets how many accounts may be synchronizing at the same moment.</summary>
    /// <remarks>
    /// This bounds simultaneity, not throughput: every configured account is supervised and synchronized, and one
    /// beyond the bound waits for a slot rather than being skipped for that interval. It is what keeps the length of
    /// an operator's account list from deciding how much of the database and the network synchronization consumes at
    /// once. An account waiting for a slot is delayed by the runs holding them and never by a failing account's
    /// backoff.
    /// </remarks>
    [Range(1, 100)]
    public int MaxConcurrentAccounts { get; set; } = 4;

    /// <summary>Gets or sets how many folders of one account may be synchronizing at the same moment.</summary>
    /// <remarks>
    /// Like <see cref="MaxConcurrentAccounts" /> this bounds simultaneity rather than how many folders a run reaches:
    /// the account's remaining folders follow within the same run. The default of one is deliberate, because a single
    /// IMAP connection per account is the conservative, server-friendly choice and every folder of an account shares
    /// that account's session establishment budget and its circuit breaker. The two bounds multiply to give the number
    /// of folder work units that can be in flight across the process.
    /// </remarks>
    [Range(1, 20)]
    public int MaxConcurrentFoldersPerAccount { get; set; } = 1;

    /// <summary>Gets or sets how long an account's write connection is kept after the last change it carried.</summary>
    /// <remarks>
    /// <para>
    /// An account holds at most one connection able to change its mailbox, opened the first time something asks to and
    /// closed once this long has passed without anything asking again. It is the third kind of connection an account
    /// can hold, beside the <see cref="MaxConcurrentFoldersPerAccount" /> synchronization ones and the push session, so
    /// it counts against whatever limit the mail server applies per account even though nothing is being written.
    /// </para>
    /// <para>
    /// The bound is on the idle time rather than on the number of connections, because the number is already one and
    /// cannot become two. Lowering it gives the slot back sooner at the cost of a fresh handshake and authentication
    /// for the next change; raising it does the opposite. Zero is not accepted, because a connection closed the instant
    /// it is released is the per-mutation connection this exists to avoid.
    /// </para>
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:05", "00:30:00")]
    public TimeSpan WriteConnectionIdlePeriod { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Gets or sets how many attempts one recorded change to a mailbox may spend before it is given up on.</summary>
    /// <remarks>
    /// <para>
    /// A change MailFathom is asked to make is written down before it is issued and attempted again from that record
    /// until it succeeds. This is where that stops. A mail server that is refusing and a message another client already
    /// removed fail identically on the next run, and repeating them costs a login and a round trip each time while
    /// hiding the problem behind an operation that always looks busy.
    /// </para>
    /// <para>
    /// It bounds attempts of the whole change rather than retries inside one, which the account's resilience pipeline
    /// already bounds; separate attempts may be minutes or days apart. A change that spends them stops being attempted
    /// and stays visible as stuck rather than disappearing, so raising this buys patience with a failing server and
    /// lowering it surfaces a broken one sooner.
    /// </para>
    /// <para>
    /// Not every refusal waits for it. A server that advertises no way to carry the change safely, and one that answers
    /// that the destination folder is not there, have both already given the answer every later attempt would receive,
    /// so those are given up on at the first refusal whatever this is set to.
    /// </para>
    /// </remarks>
    [Range(1, 100)]
    public int MaxMutationAttempts { get; set; } = 5;

    /// <summary>Gets or sets how many outstanding changes one account converges per synchronization run.</summary>
    /// <remarks>
    /// Every run begins by taking the account's unfinished changes in hand — a filing interrupted by a restart, a change
    /// recorded while the server was unreachable — and finishing them or giving up on them. The bound is what keeps a
    /// backlog from turning one run into an unbounded sequence of mail-server round trips while the folders behind it
    /// wait; what it leaves is picked up by the next run, oldest first. Each one is a write to a mail server rather than
    /// a row to process, so the useful values are small.
    /// </remarks>
    [Range(1, 1000)]
    public int MaxMutationsPerConvergencePass { get; set; } = 50;

    /// <summary>Gets or sets how long a change whose outcome is unknown waits to be settled before it is given up on.</summary>
    /// <remarks>
    /// <para>
    /// A command that puts a message in another folder is never issued twice, because a second one is a second message
    /// rather than a repeat of the first. When such a command goes out and its answer never arrives, the only thing that
    /// can still settle it is the mailbox itself coming back through an ordinary run, which takes as long as this
    /// account's folders take to come round again — so this is a period rather than a number of attempts.
    /// </para>
    /// <para>
    /// When it elapses the change is given up on and stays visible as dead-lettered, because a change that looks busy
    /// forever is worse than one that says it stopped. Set it comfortably above the interval, so an ordinary run has
    /// several chances to settle the change before the deadline is reached.
    /// </para>
    /// </remarks>
    [Range(typeof(TimeSpan), "00:01:00", "7.00:00:00")]
    public TimeSpan UnknownMutationOutcomeGrace { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Gets or sets how long shutdown waits for the work units already under way before cancelling them.</summary>
    /// <remarks>
    /// Shutdown stops scheduling immediately and only then waits, so this bounds the drain rather than delaying every
    /// stop by its own length. Zero cancels in-flight work at once, which is safe but discards the run's remaining
    /// progress; the stored occurrences and the checkpoint a run already committed are durable either way. A value
    /// beyond the host's own shutdown budget would be accepted and never honored, which
    /// <see cref="ResolveHostShutdownBudget" /> is what prevents.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:00", "00:02:00")]
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets or sets how long a push-mode folder waits on one IDLE command before re-issuing it.</summary>
    /// <remarks>
    /// <para>
    /// RFC 2177 requires a client to leave and re-enter IDLE at least every 29 minutes, because a server is entitled to
    /// drop a connection that has been idle longer and several do. The ceiling is that mandate and the default keeps
    /// nine minutes below it, which is margin for a slow round trip rather than a tuned value.
    /// </para>
    /// <para>
    /// It bounds one command and not the wait: a folder waiting for the account's whole <see cref="Interval" /> simply
    /// re-issues IDLE as often as this says, and a change reported at any point during any of those commands starts a
    /// pass at once. Lowering it therefore buys nothing but chatter, and raising it past the mandate is refused.
    /// </para>
    /// </remarks>
    [Range(typeof(TimeSpan), "00:01:00", "00:29:00")]
    public TimeSpan PushRenewalInterval { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>Gets or sets how many of an account's folders one push subscription may name.</summary>
    /// <remarks>
    /// <para>
    /// A server that supports the <c>NOTIFY</c> extension reports changes to several folders over one connection, which
    /// is what keeps an account with many folders from holding one authenticated connection per folder. The
    /// subscription names those folders explicitly, and a server is entitled to refuse one that names more mailboxes
    /// than it will track — as a whole, not folder by folder — so the list is bounded here rather than left to grow
    /// with the account's configuration.
    /// </para>
    /// <para>
    /// Folders past the bound are synchronized on the account's interval, exactly as they are on a server that offers
    /// no subscription at all. The order is the order the run resolved its folders, which is the order they are
    /// configured in, so which folders get push is the operator's choice rather than an accident of discovery. Raise it
    /// for a server known to accept a longer list; lowering it moves folders onto the interval and nothing else.
    /// </para>
    /// </remarks>
    [Range(1, 100)]
    public int MaxSubscribedFolders { get; set; } = 20;

    /// <summary>Gets or sets how many times opening or holding a push session may fail in a row before the folder is degraded to polling.</summary>
    /// <remarks>
    /// A push session fails for the same reasons any mailbox session does, and the resilience pipelines have already
    /// spent their budget by the time a failure is counted here. What this bounds is how long MailFathom keeps asking a
    /// server for a mechanism it is not serving: past it the folder is polled, which always works, and push is retried
    /// once <see cref="PushDegradationPeriod" /> has passed.
    /// </remarks>
    [Range(1, 100)]
    public int MaxConsecutivePushFailures { get; set; } = 3;

    /// <summary>Gets or sets how long a degraded folder stays on polling before push is attempted again.</summary>
    /// <remarks>
    /// The degradation is deliberately temporary. A server that stopped serving IDLE has usually stopped for a reason
    /// that ends — a restart, a connection limit, a load balancer moving the mailbox — and a folder left on polling
    /// until the next process restart would keep an operator's configured mode wrong for as long as the process runs.
    /// The folder synchronizes on its ordinary interval throughout, so the only thing this delays is the retry.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:10", "1.00:00:00")]
    public TimeSpan PushDegradationPeriod { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Gets or sets the maximum number of messages requested from one IMAP metadata batch.</summary>
    [Range(1, 1000)]
    public int MaxMetadataBatchSize { get; set; } = 100;

    /// <summary>Gets or sets the maximum raw MIME content accepted for local storage.</summary>
    [Range(1024, 104857600)]
    public long MaxRawMimeBytes { get; set; } = 25L * 1024L * 1024L;

    /// <summary>Gets or sets the maximum number of bounded metadata batches processed by one synchronization run.</summary>
    [Range(1, 1000)]
    public int MaxMetadataBatchesPerRun { get; set; } = 10;

    /// <summary>Gets or sets how many bytes of raw MIME one folder run may fetch before it ends at its checkpoint.</summary>
    /// <remarks>
    /// <para>
    /// The batch settings above bound a run in messages and say nothing about its volume: a thousand occurrences of
    /// <see cref="MaxRawMimeBytes" /> each is a legal run under them and tens of gigabytes in practice. This is what
    /// bounds the volume, so a mailbox full of large attachments fills local storage gradually and observably instead
    /// of in one pass. A run that spends it commits what it stored, ends at the message it reached, reports that it
    /// stopped for the budget, and the next run resumes from the committed checkpoint.
    /// </para>
    /// <para>
    /// Raise it to backfill faster on a deployment with room; lower it to spread ingestion over more runs. It may not
    /// be lower than <see cref="MaxRawMimeBytes" />, which would leave a run unable to afford a single large message
    /// and stop the folder in front of it forever, and startup refuses that combination.
    /// </para>
    /// </remarks>
    [Range(1024, 1099511627776)]
    public long MaxContentBytesPerRun { get; set; } = 1024L * 1024L * 1024L;

    /// <summary>Gets or sets how much local storage stored mail content may occupy, or nothing for no ceiling.</summary>
    /// <remarks>
    /// <para>
    /// Reaching it degrades ingestion instead of failing it. Occurrences keep being discovered, recorded, and indexed
    /// from their envelopes, and their payloads are left unstored and marked as awaiting room; a later run with room
    /// fetches exactly those. Nothing is lost and nothing is duplicated, so raising the ceiling or freeing space is all
    /// it takes to fill the gap in.
    /// </para>
    /// <para>
    /// It is compared against what PostgreSQL reports the content table occupies — heap, indexes, and the out-of-line
    /// storage the payloads live in — rather than against the sum of the message sizes, because that is the quantity a
    /// disk fills with. Space a deletion freed is therefore still counted as occupied until the database reclaims it.
    /// </para>
    /// <para>
    /// There is deliberately no default. No value MailFathom could choose would describe an operator's disk, and one
    /// guessed too low would stop a healthy deployment from storing mail; leaving it unset means content storage is
    /// bounded only by the disk, which is what every deployment does until it says otherwise. It may not be lower than
    /// <see cref="MaxRawMimeBytes" />, which would leave no message storable at all.
    /// </para>
    /// <para>
    /// It bounds the whole process rather than one run, because every concurrent folder run writes into one content
    /// store and a per-run ceiling would let each of them claim the room the others were taking. It is therefore read
    /// once at startup, which is why the configuration reference marks it as needing a restart.
    /// </para>
    /// </remarks>
    [Range(1024, long.MaxValue)]
    public long? MaxStoredContentBytes { get; set; }

    /// <summary>Gets or sets how many bytes of raw MIME every folder work unit together may hold in memory at once.</summary>
    /// <remarks>
    /// <para>
    /// A payload is buffered whole between the fetch that reads it and the commit that stores it, so peak memory is one
    /// payload per work unit in flight. <see cref="MaxRawMimeBytes" /> bounds one of those and says nothing about their
    /// sum, which without this bound would make the peak a product of <see cref="MaxConcurrentAccounts" /> and
    /// <see cref="MaxConcurrentFoldersPerAccount" />: raising either would silently raise the memory ceiling with it.
    /// </para>
    /// <para>
    /// A work unit that cannot reserve its share waits for one that can, so this slows ingestion rather than refusing
    /// it. It may not be lower than <see cref="MaxRawMimeBytes" />, since a message larger than the whole budget could
    /// never be admitted, and startup refuses that combination. It is read once at startup, so changing it needs a
    /// restart.
    /// </para>
    /// </remarks>
    [Range(1024, 4294967296)]
    public long MaxInFlightRawMimeBytes { get; set; } = 128L * 1024L * 1024L;

    /// <summary>Gets or sets how many already-stored emails one folder run re-checks against the mail server.</summary>
    /// <remarks>
    /// It bounds the backward pass that notices a deletion or a flag change, in the same way the batch settings above
    /// bound the forward pass that notices new mail. A folder holding more emails than this is reconciled over several
    /// runs, oldest observation first, so raising it shortens the time a remote deletion stays unnoticed and lengthens
    /// each run; the whole window is one <c>UID FETCH</c> of flags, which is why the ceiling can be generous.
    /// </remarks>
    [Range(1, 10000)]
    public int MaxReconciledEmailsPerRun { get; set; } = 500;

    /// <summary>Gets or sets the maximum number of MIME entities one message may declare before extraction abandons it.</summary>
    [Range(1, 100000)]
    public int MaxMimePartCount { get; set; } = 1000;

    /// <summary>Gets or sets the maximum depth to which one message may nest multiparts before extraction abandons it.</summary>
    [Range(1, 1000)]
    public int MaxMimeNestingDepth { get; set; } = 30;

    /// <summary>Gets or sets the maximum number of characters one message's body contributes to its indexed text.</summary>
    /// <remarks>
    /// <para>
    /// The upper bound of the range is what keeps the generated search vector inside PostgreSQL's one-megabyte limit
    /// once the subject and the participant addresses sharing that document are counted too. It is a value the
    /// arithmetic supports rather than a round number: a <c>tsvector</c> spends four bytes of entry header, the lexeme
    /// itself, and four bytes of position data per distinct word, so text of single-character words separated by single
    /// spaces — the shape that maximizes entries — costs about 4.5 bytes of vector per character of input. The subject
    /// and participant copies take about 101,000 of the 1,048,575 available bytes at their own ceilings, which leaves
    /// roughly 210,000 characters of body; 200,000 keeps a margin.
    /// </para>
    /// <para>
    /// The bound matters because the vector is a generated column computed on every insert. Exceeding the limit would
    /// not degrade search: it would make the row unwritable, exhaust the retry budget, and stop the folder the message
    /// arrived in on every later run.
    /// </para>
    /// </remarks>
    [Range(1_000, 200_000)]
    public int MaxExtractedTextCharacters { get; set; } = 100_000;

    /// <summary>Gets or sets configured accounts and folders to synchronize.</summary>
    public List<MailSynchronizationAccountOptions> Accounts { get; set; } = [];

    /// <summary>Computes the host shutdown budget a configured drain needs to be honored.</summary>
    /// <param name="shutdownDrainTimeout">The configured drain the synchronization coordinator applies.</param>
    /// <returns>The budget to give <c>HostOptions.ShutdownTimeout</c>, never below the framework default.</returns>
    /// <remarks>
    /// <para>
    /// A drain is only real while the host is still waiting for it. Once its own shutdown timeout expires the host
    /// stops awaiting <c>StopAsync</c> and the process exits with the work still running, so a drain configured beyond
    /// that timeout would be accepted and silently not honored. The budget is therefore derived from the drain rather
    /// than left on the framework's 30-second default.
    /// </para>
    /// <para>
    /// The margin is what the other hosted services stop within, and the floor keeps a deployment that shortens the
    /// drain on the shutdown behavior the framework default gives it rather than tightening the whole host because one
    /// worker asked for less.
    /// </para>
    /// </remarks>
    internal static TimeSpan ResolveHostShutdownBudget(TimeSpan shutdownDrainTimeout) =>
        shutdownDrainTimeout + HostShutdownMargin > DefaultHostShutdownTimeout
            ? shutdownDrainTimeout + HostShutdownMargin
            : DefaultHostShutdownTimeout;

    /// <summary>Builds one account's connection settings, resolving its material for the caller to own.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <param name="resolver">The resolver that turns configured references into material.</param>
    /// <param name="trustAnchorLoader">The loader that turns configured material into a trust anchor.</param>
    /// <param name="cancellationToken">Cancels the secret resolution.</param>
    /// <returns>The settings, whose material the caller must dispose when its operation ends.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the resolved material passes to the caller, which erases it when its operation ends.")]
    internal async Task<ImapAccountSettings> ResolveSettingsAsync(
        string accountId,
        ISecretReferenceResolver resolver,
        TrustAnchorLoader trustAnchorLoader,
        CancellationToken cancellationToken)
    {
        var normalizedAccountId = MailAccountId.Create(accountId).Value;
        var account = this.FindAccount(normalizedAccountId);

        var material = await account.ResolveConnectionMaterialAsync(resolver, trustAnchorLoader, cancellationToken);

        return new ImapAccountSettings(
            normalizedAccountId,
            account.Host.Trim(),
            account.Port,
            account.UserName,
            material);
    }

    /// <inheritdoc />
    public MailTransportSecurityPolicy GetPolicy(MailAccountId accountId)
    {
        var account = this.FindAccount(accountId.Value);

        return account.CreateTransportSecurityPolicy();
    }

    /// <inheritdoc />
    public MailSynchronizationWindow GetWindow(MailAccountId accountId)
    {
        var account = this.FindAccount(accountId.Value);

        return account.CreateSynchronizationWindow();
    }

    /// <inheritdoc />
    public RemotelyDeletedEmailDisposition GetDisposition(MailAccountId accountId)
    {
        var account = this.FindAccount(accountId.Value);

        return account.RemotelyDeletedEmailDisposition;
    }

    /// <inheritdoc />
    public AuthoredDeleteEmailDisposition GetAuthoredDeleteDisposition(MailAccountId accountId)
    {
        var account = this.FindAccount(accountId.Value);

        return account.AuthoredDeleteEmailDisposition;
    }

    /// <inheritdoc />
    /// <remarks>
    /// An account this snapshot no longer names reports <see cref="MailboxMutationAuditSettings.Disabled" /> rather
    /// than failing, unlike every other per-account reader here. The two callers are why: a mutation is only recorded
    /// for a configured account, and the retention pass runs over accounts a reload may have removed between one run and
    /// the next — where the honest answer is that no operator decision applies, not that the deployment is broken.
    /// </remarks>
    public MailboxMutationAuditSettings GetAuditSettings(MailAccountId accountId) =>
        this.FindConfiguredAccount(accountId)?.CreateAuditSettings() ?? MailboxMutationAuditSettings.Disabled;

    /// <inheritdoc />
    /// <remarks>
    /// Configuration is what defines the set of accounts, so this answers from the same bound options every other
    /// per-account reader does. It deliberately ignores <see cref="Enabled" />: that switch stops runs from fetching
    /// mail, and an operator who turned it off has not asked for the copy already stored to become unreadable. An
    /// account they removed is a different matter, and its absence here is what makes its stored mail unreadable.
    /// </remarks>
    public IReadOnlyList<MailAccountId> ServedAccountIds =>
    [
        .. (this.Accounts ?? [])
            .Where(static candidate => !string.IsNullOrWhiteSpace(candidate.AccountId))
            .Select(static candidate => MailAccountId.Create(candidate.AccountId))
            .DistinctBy(static accountId => accountId.Value, StringComparer.Ordinal)
            .OrderBy(static accountId => accountId.Value, StringComparer.Ordinal),
    ];

    /// <summary>Finds every configured earliest received date that could not mean anything on the supplied date.</summary>
    /// <param name="today">The current date the configured bounds are read against.</param>
    /// <returns>One result per account whose bound lies in the future, empty when every bound is usable.</returns>
    /// <remarks>
    /// The rule lives here with the other configuration rules while its clock stays outside, because the current date
    /// is not something a bound options graph or a data annotation can reach. Nothing gates it on
    /// <see cref="Enabled" />: a date an operator wrote is a date they intend to synchronize from, and discovering that
    /// it excludes the whole mailbox at the moment synchronization is switched on is worse than discovering it now.
    /// </remarks>
    internal IEnumerable<ValidationResult> FindSynchronizationWindowErrors(DateOnly today) =>
        this.Accounts?.SelectMany(account => account.ValidateSynchronizationWindow(today)) ?? [];

    internal IEnumerable<ValidationResult> ValidateForSynchronization()
    {
        if (this.MaxFailureBackoff < this.Interval)
        {
            yield return new ValidationResult(
                $"The maximum failure backoff {this.MaxFailureBackoff} is shorter than the synchronization interval {this.Interval}, so a failing account would run more often than a healthy one.",
                [nameof(this.MaxFailureBackoff)]);
        }

        // Each of the three states the same thing about a different limit: a bound below the size of one message would
        // make that message unfetchable rather than merely rare, and the folder holding it would stop in front of it on
        // every run. The size limit is the floor of all three because it is what one message may cost.
        if (this.MaxContentBytesPerRun < this.MaxRawMimeBytes)
        {
            yield return new ValidationResult(
                $"The per-run content budget of {this.MaxContentBytesPerRun} bytes is below the {this.MaxRawMimeBytes} bytes one message may occupy, so no run could ever fetch a message of that size.",
                [nameof(this.MaxContentBytesPerRun)]);
        }

        if (this.MaxStoredContentBytes is { } storageCeiling && storageCeiling < this.MaxRawMimeBytes)
        {
            yield return new ValidationResult(
                $"The stored content ceiling of {storageCeiling} bytes is below the {this.MaxRawMimeBytes} bytes one message may occupy, so no message could ever be stored.",
                [nameof(this.MaxStoredContentBytes)]);
        }

        if (this.MaxInFlightRawMimeBytes < this.MaxRawMimeBytes)
        {
            yield return new ValidationResult(
                $"The in-flight content budget of {this.MaxInFlightRawMimeBytes} bytes is below the {this.MaxRawMimeBytes} bytes one message may occupy, so a work unit fetching a message of that size would wait for room that can never exist.",
                [nameof(this.MaxInFlightRawMimeBytes)]);
        }

        if (this.Accounts is null)
        {
            yield return new ValidationResult("Account configuration must be a list.", [nameof(this.Accounts)]);
            yield break;
        }

        if (this.Enabled && this.Accounts.Count == 0)
        {
            yield return new ValidationResult("At least one account is required when synchronization is enabled.", [nameof(this.Accounts)]);
        }

        if (this.Accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.AccountId))
            .GroupBy(account => MailAccountId.Create(account.AccountId).Value, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            yield return new ValidationResult("Account IDs must be unique after normalization.", [nameof(this.Accounts)]);
        }

        foreach (var result in this.Accounts.SelectMany(account => account.ValidateForSynchronization(this.Enabled)))
        {
            yield return result;
        }
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => this.ValidateForSynchronization();

    /// <summary>Finds the account a supervisor runs, reporting its absence rather than failing on it.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <returns>The configured account, or <see langword="null" /> when this snapshot no longer names it.</returns>
    /// <remarks>
    /// A reload can remove an account while its supervisor is between runs, which is an ordinary configuration change
    /// rather than a failure: the supervisor ends itself instead of connecting to a server the operator withdrew.
    /// Every other reader wants the account it was handed to exist, and keeps failing when it does not.
    /// </remarks>
    internal MailSynchronizationAccountOptions? FindConfiguredAccount(MailAccountId accountId) =>
        (this.Accounts ?? []).SingleOrDefault(
            candidate => !string.IsNullOrWhiteSpace(candidate.AccountId)
                && StringComparer.Ordinal.Equals(
                    MailAccountId.Create(candidate.AccountId).Value,
                    accountId.Value));

    private MailSynchronizationAccountOptions FindAccount(string normalizedAccountId) =>
        this.FindConfiguredAccount(MailAccountId.Create(normalizedAccountId))
        ?? throw new InvalidOperationException($"Account '{normalizedAccountId}' is not configured.");
}

/// <summary>Configures one account for periodic IMAP synchronization.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailSynchronizationAccountOptions : IValidatableObject
{
    /// <summary>Gets or sets the local account identifier.</summary>
    [Required]
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Gets or sets the IMAP server host name.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the IMAP server port.</summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 993;

    /// <summary>Gets or sets the account's transport security settings.</summary>
    public MailAccountTransportSecurityOptions TransportSecurity { get; set; } = new();

    /// <summary>Gets or sets the IMAP user name, which is an identifier rather than a credential and stays a plain configuration value.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the account's secret-bearing settings, which carry references rather than credentials.</summary>
    public MailAccountSecretOptions Secrets { get; set; } = new();

    /// <summary>Gets or sets the account's OAuth settings, read only when its policy permits a token-bearing mechanism.</summary>
    /// <remarks>An account authenticating with a password leaves the block empty, and validation then never reads it.</remarks>
    public MailAccountOAuthOptions OAuth { get; set; } = new();

    /// <summary>Gets or sets the earliest date the mail server may have received an email on for it to be synchronized.</summary>
    /// <remarks>
    /// Omitting it synchronizes every email the server still holds, which is the default. It binds as a plain date such
    /// as <c>2024-01-01</c>, and a value that is not one fails startup: the account is a collection item, and the binder
    /// would otherwise drop the whole account over a typo in this one setting, which is another reason the section is
    /// bound with <c>ErrorOnUnknownConfiguration</c>. Which date the bound compares against, and why, is recorded on
    /// <see cref="MailSynchronizationWindow" />.
    /// </remarks>
    public DateOnly? EarliestEmailReceivedDate { get; set; }

    /// <summary>Gets or sets what happens to the local copy of an email this account's mail server no longer holds.</summary>
    /// <remarks>
    /// <para>
    /// It binds as one of the two names <see cref="RemotelyDeletedEmailDisposition" /> declares, and a value that is
    /// neither fails startup rather than silently selecting a default: this setting decides whether stored mail is
    /// destroyed, and a typo in it must never be the reason mail survives or does not. The default keeps the local row
    /// as a tombstone that mailbox queries exclude.
    /// </para>
    /// <para>
    /// The setting is per account because the accounts of one deployment answer to different providers and serve
    /// different purposes; one mailbox may be followed exactly while another is the copy that has to outlive the
    /// server's own retention. Changing it governs the disappearances observed from then on and leaves everything
    /// already recorded exactly as it is.
    /// </para>
    /// </remarks>
    public RemotelyDeletedEmailDisposition RemotelyDeletedEmailDisposition { get; set; } =
        RemotelyDeletedEmailDisposition.RetainTombstone;

    /// <summary>Gets or sets what happens to the local copy of an email MailFathom itself deletes on this account's server.</summary>
    /// <remarks>
    /// <para>
    /// It is a separate setting from <see cref="RemotelyDeletedEmailDisposition" /> and takes precedence over it for
    /// every deletion MailFathom performed, because the two answer for different acts. That one governs a disappearance
    /// somebody else caused; this one governs one the mailbox owner authored, and an account that erases what its
    /// server loses must not thereby erase what it was just told to delete — freeing space on the server is not the
    /// same instruction as forgetting the mail.
    /// </para>
    /// <para>
    /// It binds as one of the names <see cref="AuthoredDeleteEmailDisposition" /> declares, and a value that is none of
    /// them fails startup for the same reason the setting above does. The default keeps the local copy readable, which
    /// is the value that destroys nothing.
    /// </para>
    /// </remarks>
    public AuthoredDeleteEmailDisposition AuthoredDeleteEmailDisposition { get; set; } =
        AuthoredDeleteEmailDisposition.RetainLocalCopy;

    /// <summary>Gets or sets whether and for how long this account keeps a record of the changes MailFathom made to it.</summary>
    /// <remarks>
    /// Omitting the block leaves the trail off, which is the default for the reason the privacy rules require: the trail
    /// is derived personal data — it says where a person's mail has been, when, and at whose instruction — so a
    /// deployment that never asked for it never accumulates it.
    /// </remarks>
    public MailboxMutationAuditTrailOptions AuditTrail { get; set; } = new();

    /// <summary>Gets or sets what starts this account's next synchronization pass.</summary>
    /// <remarks>
    /// <para>
    /// It binds as one of the two names <see cref="MailSynchronizationMode" /> declares and defaults to
    /// <see cref="MailSynchronizationMode.Polling" />, so an account that says nothing keeps the schedule it already
    /// had. Push is opt-in because it holds a connection open for the lifetime of the process — one for the whole
    /// account on a server that supports subscriptions, as <see cref="MailSynchronizationOptions.MaxSubscribedFolders" />
    /// describes, and otherwise one per folder — which is a cost against the server's connection limit that an operator
    /// should choose rather than inherit.
    /// </para>
    /// <para>
    /// The setting states what the operator asked for. What a folder actually gets is decided per folder against what
    /// the server advertises and how its recent push attempts went, and is reported separately for that reason.
    /// </para>
    /// </remarks>
    public MailSynchronizationMode Mode { get; set; } = MailSynchronizationMode.Polling;

    /// <summary>Gets or sets the configured folder aliases. When omitted, the worker synchronizes the inbox only.</summary>
    public List<MailFolderMappingOptions> Folders { get; set; } = [];

    /// <summary>Gets the configured folders or the post-binding default one.</summary>
    /// <remarks>
    /// The default names the inbox by its special-use role rather than by the path <c>INBOX</c>, so an account whose
    /// server presents the inbox under another name still synchronizes with no folder configuration at all.
    /// </remarks>
    public IReadOnlyList<MailFolderMappingOptions> EffectiveFolders =>
        this.Folders is not { Count: > 0 } ? [CreateDefaultInboxFolder()] : this.Folders;

    internal IEnumerable<ValidationResult> ValidateForSynchronization(bool synchronizationEnabled)
    {
        if (this.Port is < 1 or > 65535)
        {
            yield return new ValidationResult("IMAP port must be between 1 and 65535.", [nameof(this.Port)]);
        }

        // The binder converts a bare number onto an enum without asking whether any member carries it, and
        // ErrorOnUnknownConfiguration does not catch that: it rejects unknown keys and failed conversions, and this
        // conversion succeeds. Left unchecked, an undefined value would reach reconciliation, which treats anything
        // that is not EraseLocalCopy as the tombstone — a destructive setting silently doing the other thing.
        if (!Enum.IsDefined(this.RemotelyDeletedEmailDisposition))
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': the remotely deleted email disposition must be one of {string.Join(", ", Enum.GetNames<RemotelyDeletedEmailDisposition>())}.",
                [nameof(this.RemotelyDeletedEmailDisposition)]);
        }

        // Checked for the reason the disposition above is. An undefined value here names no decision about the local
        // copy, so every delete this account authored would be refused where its record is built — a failure an
        // operator would meet one deletion at a time rather than at the startup that accepted the typo.
        if (!Enum.IsDefined(this.AuthoredDeleteEmailDisposition))
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': the authored delete email disposition must be one of {string.Join(", ", Enum.GetNames<AuthoredDeleteEmailDisposition>())}.",
                [nameof(this.AuthoredDeleteEmailDisposition)]);
        }

        // Checked for the same reason as the disposition above: a bare number binds onto an enum whether or not a
        // member carries it, and an undefined value here would be read as "not Push" — an operator who asked for push
        // and mistyped it would silently get polling with nothing reporting the difference.
        if (!Enum.IsDefined(this.Mode))
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': the synchronization mode must be one of {string.Join(", ", Enum.GetNames<MailSynchronizationMode>())}.",
                [nameof(this.Mode)]);
        }

        // Checked here rather than through a data annotation because nothing binds the block as an options graph of its
        // own, so an annotation on it would be read by nothing. The window decides when personal data is destroyed, so
        // a typo in it fails startup instead of quietly selecting a period nobody wrote.
        if (this.AuditTrail is null)
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': the audit trail configuration must be a block.",
                [nameof(this.AuditTrail)]);
        }
        else if (this.AuditTrail.Retention < MailboxMutationAuditTrailOptions.MinimumRetention
            || this.AuditTrail.Retention > MailboxMutationAuditTrailOptions.MaximumRetention)
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': the audit trail retention must be between {MailboxMutationAuditTrailOptions.MinimumRetention} and {MailboxMutationAuditTrailOptions.MaximumRetention}.",
                [nameof(this.AuditTrail)]);
        }

        if (this.Folders is null)
        {
            yield return new ValidationResult("Folder configuration must be a list.", [nameof(this.Folders)]);
            yield break;
        }

        foreach (var result in this.Folders.SelectMany(folder => folder.ValidateForSynchronization()))
        {
            yield return result;
        }

        // Grouped the way MailFolderAlias normalizes rather than through the factory itself, because an alias this
        // method has already reported as unusable must not throw out of the rule that follows it.
        if (this.Folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder.Alias))
            .GroupBy(folder => folder.Alias.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            yield return new ValidationResult("Configured folder aliases must be unique after normalization.", [nameof(this.Folders)]);
        }

        if (synchronizationEnabled)
        {
            if (string.IsNullOrWhiteSpace(this.AccountId))
            {
                yield return new ValidationResult("Account ID is required when synchronization is enabled.", [nameof(this.AccountId)]);
            }

            if (string.IsNullOrWhiteSpace(this.Host))
            {
                yield return new ValidationResult("IMAP host is required when synchronization is enabled.", [nameof(this.Host)]);
            }

            if (string.IsNullOrWhiteSpace(this.UserName))
            {
                yield return new ValidationResult("IMAP user name is required when synchronization is enabled.", [nameof(this.UserName)]);
            }

            foreach (var result in this.ValidateTransportSecurity())
            {
                yield return result;
            }

            foreach (var result in this.ValidateAuthenticationCredentials())
            {
                yield return result;
            }
        }
    }

    /// <summary>Reports a credential shape that does not match the mechanisms the account's policy permits.</summary>
    /// <remarks>
    /// <para>
    /// This is what makes the optional password safe. An account permitting only token-bearing mechanisms needs a
    /// token endpoint and needs no password; one permitting any password mechanism needs the opposite. Settling it at
    /// startup means a connection attempt reads a decision instead of discovering halfway through authentication that
    /// the credential it needs was never configured.
    /// </para>
    /// <para>
    /// A configured OAuth block the policy can never use is refused rather than ignored. Silently unused credentials
    /// are the shape an operator misreads as working, and this one would leave a client secret provisioned for
    /// nothing.
    /// </para>
    /// </remarks>
    private IEnumerable<ValidationResult> ValidateAuthenticationCredentials()
    {
        MailAuthenticationPolicy authentication;
        try
        {
            authentication = this.CreateTransportSecurityPolicy().Authentication;
        }
        catch (MailTransportSecurityPolicyViolationException)
        {
            // ValidateTransportSecurity already reported this, and every rule below needs a policy to read.
            yield break;
        }

        if (authentication.PermitsAccessTokenAuthentication)
        {
            foreach (var result in this.ValidateOAuthBlock())
            {
                yield return result;
            }
        }
        else if (this.OAuth.IsConfigured)
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}' configures OAuth settings, but its permitted mechanisms include no token-bearing mechanism such as XOAUTH2 or OAUTHBEARER, so the settings could never be used.",
                [nameof(this.OAuth)]);
        }

        if (authentication.PermitsPasswordAuthentication && string.IsNullOrWhiteSpace(this.Secrets?.Password?.SecretReference))
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}' permits a password mechanism and configures no password secret reference.",
                [nameof(this.Secrets)]);
        }
    }

    private IEnumerable<ValidationResult> ValidateOAuthBlock()
    {
        if (!this.OAuth.ParsedGrant.IsSpecified)
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': the OAuth grant must be one of {string.Join(", ", MailOAuthGrant.All.Select(grant => grant.GrantTypeName))}.",
                [nameof(this.OAuth)]);
        }

        // The request body carries the client secret and the refresh token, so an unencrypted endpoint would publish
        // both to anyone on the path. This is refused rather than warned about, and there is no opt-in for it.
        if (!Uri.TryCreate(this.OAuth.TokenEndpoint, UriKind.Absolute, out var tokenEndpoint)
            || !string.Equals(tokenEndpoint.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': the OAuth token endpoint must be an absolute HTTPS address.",
                [nameof(this.OAuth)]);
        }

        if (string.IsNullOrWhiteSpace(this.OAuth.ClientId))
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': an OAuth client identifier is required.",
                [nameof(this.OAuth)]);
        }

        // A public client holds no secret by registration, which is what the device grant expects and what
        // 'mfctl mailbox authorize --public-client' produces. Requiring one regardless would refuse an account
        // the documented workflow just finished authorizing.
        var configuresClientSecret = !string.IsNullOrWhiteSpace(this.OAuth.ClientSecret?.SecretReference);

        if (!this.OAuth.PublicClient && !configuresClientSecret)
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': an OAuth client secret reference is required unless the application is registered as a public client, which is declared with 'PublicClient: true'.",
                [nameof(this.OAuth)]);
        }

        // Both together are a contradiction rather than a harmless extra: one of the two states the operator wrote is
        // not what the account will do, and silently ignoring the secret would leave a provisioned credential whose
        // disuse nobody can see.
        if (this.OAuth.PublicClient && configuresClientSecret)
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': a public client sends no client secret, so configuring one alongside 'PublicClient: true' is contradictory.",
                [nameof(this.OAuth)]);
        }

        if (this.OAuth.ParsedGrant.RequiresRefreshToken && string.IsNullOrWhiteSpace(this.OAuth.RefreshToken?.SecretReference))
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': the refresh-token grant requires a refresh token secret reference. Obtain one with 'mfctl mailbox authorize'.",
                [nameof(this.OAuth)]);
        }
    }

    /// <summary>Reports an earliest received date that lies ahead of the supplied date.</summary>
    /// <param name="today">The current date the configured bound is read against.</param>
    /// <returns>One result when the bound is in the future, none otherwise.</returns>
    /// <remarks>
    /// A future bound is refused rather than adopted because it excludes every email the mailbox holds, which is
    /// indistinguishable from synchronization silently doing nothing. The comparison is made in UTC, so a bound written
    /// as the operator's local date is refused while UTC has not reached it yet.
    /// </remarks>
    internal IEnumerable<ValidationResult> ValidateSynchronizationWindow(DateOnly today)
    {
        if (this.EarliestEmailReceivedDate is { } earliestReceivedDate && earliestReceivedDate > today)
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': the earliest email received date {earliestReceivedDate:yyyy-MM-dd} is later than the current UTC date {today:yyyy-MM-dd}, so it would exclude every email in the mailbox.",
                [nameof(this.EarliestEmailReceivedDate)]);
        }
    }

    /// <summary>Builds the account's configured audit trail settings.</summary>
    /// <returns>The settings the account's block names.</returns>
    internal MailboxMutationAuditSettings CreateAuditSettings() =>
        new(this.AuditTrail.Enabled, this.AuditTrail.Retention);

    /// <summary>Builds the account's configured synchronization window.</summary>
    /// <returns>The window the account's bound names, or an unbounded one when it configured none.</returns>
    internal MailSynchronizationWindow CreateSynchronizationWindow() =>
        this.EarliestEmailReceivedDate is { } earliestReceivedDate
            ? MailSynchronizationWindow.EmailsReceivedSince(earliestReceivedDate)
            : MailSynchronizationWindow.Unbounded;

    /// <summary>Builds the account's validated transport security policy.</summary>
    /// <returns>The policy the mailbox adapter must obey.</returns>
    /// <exception cref="MailTransportSecurityPolicyViolationException">Thrown when the configured combination is unsafe.</exception>
    internal MailTransportSecurityPolicy CreateTransportSecurityPolicy() => this.TransportSecurity.CreatePolicy();

    /// <summary>Resolves the password and trust anchor one connection attempt needs.</summary>
    /// <param name="resolver">The resolver that turns configured references into material.</param>
    /// <param name="trustAnchorLoader">The loader that turns configured material into a trust anchor.</param>
    /// <param name="cancellationToken">Cancels the retrieval.</param>
    /// <returns>The material, which the caller must dispose when its operation ends.</returns>
    /// <exception cref="InvalidOperationException">Thrown when configuration that passed startup validation no longer yields usable material.</exception>
    /// <remarks>
    /// <para>
    /// An anchor that fails to load fails the connection attempt rather than downgrading it to the system trust store,
    /// and the password resolved first is erased on that path so a failed attempt leaves nothing behind.
    /// </para>
    /// <para>
    /// No password is resolved for an account whose policy permits only token-bearing mechanisms, because such an
    /// account configures none. Validation settles that at startup, so the branch here reads a decision rather than
    /// guessing from whether a reference happens to be present.
    /// </para>
    /// </remarks>
    internal async Task<MailAccountConnectionMaterial> ResolveConnectionMaterialAsync(
        ISecretReferenceResolver resolver,
        TrustAnchorLoader trustAnchorLoader,
        CancellationToken cancellationToken)
    {
        var password = this.CreateTransportSecurityPolicy().Authentication.PermitsPasswordAuthentication
            ? await this.Secrets.ResolvePasswordAsync(resolver, cancellationToken)
            : null;

        try
        {
            var trustedCertificateAuthority = await this.LoadTrustedCertificateAuthorityAsync(
                trustAnchorLoader,
                cancellationToken);

            return new MailAccountConnectionMaterial(password, trustedCertificateAuthority);
        }
        catch
        {
            password?.Dispose();

            throw;
        }
    }

    /// <summary>Resolves the token endpoint settings and secrets one token request needs.</summary>
    /// <param name="resolver">The resolver that turns configured references into material.</param>
    /// <param name="cancellationToken">Cancels the retrieval.</param>
    /// <returns>The settings, whose material the caller must dispose when its operation ends.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the account configures no usable OAuth block, which startup validation already refuses.</exception>
    internal async Task<MailOAuthAccountSettings> ResolveOAuthSettingsAsync(
        ISecretReferenceResolver resolver,
        CancellationToken cancellationToken)
    {
        if (!this.OAuth.ParsedGrant.IsSpecified || !Uri.TryCreate(this.OAuth.TokenEndpoint, UriKind.Absolute, out var tokenEndpoint))
        {
            throw new InvalidOperationException(
                $"Account '{this.AccountId}' authenticates with an access token and configures no usable OAuth block.");
        }

        var material = await this.OAuth.ResolveClientMaterialAsync(resolver, cancellationToken);

        return new MailOAuthAccountSettings(
            MailAccountId.Create(this.AccountId).Value,
            tokenEndpoint,
            this.OAuth.ClientId.Trim(),
            this.OAuth.Scope.Trim(),
            this.OAuth.ParsedGrant,
            material);
    }

    private async Task<X509Certificate2?> LoadTrustedCertificateAuthorityAsync(
        TrustAnchorLoader trustAnchorLoader,
        CancellationToken cancellationToken)
    {
        var loadResult = await this.TransportSecurity.LoadTrustedCertificateAuthorityAsync(
            trustAnchorLoader,
            cancellationToken);

        if (loadResult is null)
        {
            return null;
        }

        // A failed result owns nothing, so nothing leaks by throwing past it.
        return loadResult.TrustAnchor ?? throw new InvalidOperationException(
            $"Account '{this.AccountId}': the configured trusted certificate authority material could not be loaded [{loadResult.Failure}].");
    }

    /// <summary>Re-checks the transport security rules so an unsafe account fails startup.</summary>
    /// <returns>One result per unsupported mechanism name and per violated rule, each naming the account.</returns>
    private IEnumerable<ValidationResult> ValidateTransportSecurity() => this.TransportSecurity
        .FindConfigurationErrors()
        .Select(error => new ValidationResult(
            DescribeConfigurationError(this.AccountId, error),
            [$"{nameof(this.TransportSecurity)}.{error.PropertyName}"]));

    /// <summary>Builds the startup message for one transport security configuration error.</summary>
    /// <remarks>
    /// The violation name is appended so the message carries a stable identity an operator or log query can match on,
    /// while the prose stays free to change. A mechanism-name parse failure has no violation and is reported without
    /// one. Neither part may name the user name, password, or trust anchor reference.
    /// </remarks>
    private static string DescribeConfigurationError(string accountId, MailAccountTransportSecurityConfigurationError error) =>
        error.Violation is { } violation
            ? $"Account '{accountId}': {error.Description} [{violation}]"
            : $"Account '{accountId}': {error.Description}";

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => this.ValidateForSynchronization(synchronizationEnabled: true);

    /// <summary>Builds the folder an account that configured none synchronizes.</summary>
    private static MailFolderMappingOptions CreateDefaultInboxFolder() => new()
    {
        Alias = nameof(MailFolderSpecialUse.Inbox),
        SpecialUse = nameof(MailFolderSpecialUse.Inbox),
    };
}
