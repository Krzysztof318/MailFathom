// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Domain.Transport;
using MailFathom.Host.Configuration.Mail.Readers;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Secrets.References;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Configures periodic IMAP synchronization.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailSynchronizationOptions : IValidatableObject
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string SectionName = "MailSynchronization";

    /// <summary>The shutdown budget the .NET Generic Host applies when nothing configures one.</summary>
    private static readonly TimeSpan DefaultHostShutdownTimeout = TimeSpan.FromSeconds(30);

    /// <summary>What the shutdown budget keeps for the hosted services that stop beside a synchronization drain.</summary>
    private static readonly TimeSpan HostShutdownMargin = TimeSpan.FromSeconds(5);

    private Lazy<MailSynchronizationSettingsReaders> readers;

    /// <summary>Initializes the options the binder is about to write into.</summary>
    /// <remarks>
    /// The readers are deferred rather than built here, because nothing is bound yet when this runs. What forces them
    /// is the first port resolved against this snapshot, which happens long after binding and after validation, and a
    /// reload binds a new instance rather than rewriting this one — so a value they memoize can never describe
    /// superseded configuration.
    /// </remarks>
    public MailSynchronizationOptions() => this.readers = ReadersFor(this);

    /// <summary>Gets the port readers this snapshot is read through.</summary>
    /// <remarks>
    /// One set belongs to this instance and is shared by every scope that runs against it, which is what keeps the
    /// per-account maps three of the readers memoize built once per snapshot rather than once per work unit.
    /// </remarks>
    internal MailSynchronizationSettingsReaders Readers => this.readers.Value;

    /// <summary>Gets or sets the owners this deployment serves, which is where a declaration outside this section lives.</summary>
    /// <remarks>
    /// <para>
    /// Not bound from anything: the roster is established against the database while the host starts, so it is put onto
    /// each materialized snapshot afterwards rather than read out of a section. Absent on a snapshot nobody serves from
    /// — the container a configuration write judges its candidate in — where the only declarations that exist are the
    /// candidate's own.
    /// </para>
    /// <para>
    /// It is the immutable roster this snapshot was published with. A later owner-document commit produces another
    /// settings snapshot, so a run already under way never sees its account declaration change beneath it.
    /// </para>
    /// </remarks>
    internal IReadOnlyList<ServedMailOwner>? ServedOwners { get; set; }

    /// <summary>Copies these bound settings onto one immutable owner roster, with readers of its own.</summary>
    internal MailSynchronizationOptions WithServedOwners(IReadOnlyList<ServedMailOwner> servedOwners)
    {
        ArgumentNullException.ThrowIfNull(servedOwners);

        var snapshot = (MailSynchronizationOptions)this.MemberwiseClone();
        snapshot.ServedOwners = servedOwners;
        snapshot.readers = ReadersFor(snapshot);

        return snapshot;
    }

    private static Lazy<MailSynchronizationSettingsReaders> ReadersFor(MailSynchronizationOptions settings) =>
        new(
            () => new MailSynchronizationSettingsReaders(settings),
            LazyThreadSafetyMode.ExecutionAndPublication);

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

    /// <summary>Gets or sets how many open or establishing IMAP connections one server host may hold across all owners.</summary>
    /// <remarks>
    /// Push sessions, folder synchronization, discovery, and the account's write connection all consume this one
    /// process-wide budget. Push may consume at most one less than the bound, so a long-lived watch cannot prevent
    /// ordinary synchronization from reaching the host. The value is read once because every existing lease belongs
    /// to the budget that admitted it.
    /// </remarks>
    [Range(2, 1000)]
    public int MaxConcurrentConnectionsPerHost { get; set; } =
        MailServerConnectionBudget.DefaultMaximumConnectionsPerHost;

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

    /// <summary>Gets or sets how much stored content any one owner's mail may occupy, or nothing for no such ceiling.</summary>
    /// <remarks>
    /// <para>
    /// The same bound asked of one person rather than of the instance. It exists because
    /// <see cref="MaxStoredContentBytes" /> is the only thing bounding storage otherwise, so one large mailbox can fill
    /// it and leave every other owner's mail recorded without content. Reaching this one defers that owner's messages
    /// and nobody else's, and a later run with room for them fetches exactly what was left.
    /// </para>
    /// <para>
    /// It is compared against what that owner's stored payloads hold, which is not the quantity
    /// <see cref="MaxStoredContentBytes" /> is compared against: a catalogue reports what a table occupies on disk and
    /// can never report a share of one. The two are therefore different measures of the same storage and are not
    /// expected to agree — an owner's figure excludes the indexes, the row overhead, and the space a deletion freed
    /// that PostgreSQL has not reclaimed.
    /// </para>
    /// <para>
    /// There is deliberately no default, and leaving it unset is what a deployment serving one owner wants: the
    /// instance ceiling already bounds that person. What leaving it unset exposes on a deployment serving several is
    /// exactly the fault above, and the configuration reference says so. It may not be lower than
    /// <see cref="MaxRawMimeBytes" />, which would leave no message storable for anybody.
    /// </para>
    /// </remarks>
    [Range(1024, long.MaxValue)]
    public long? MaxStoredContentBytesPerOwner { get; set; }

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

    /// <summary>Gets or sets whether an author writing from a domain one of this deployment's accounts uses is recognized.</summary>
    /// <remarks>
    /// <para>
    /// The set of domains is derived from the configured accounts rather than restated here, so adding an account
    /// extends it and removing one narrows it without a second edit. It is deployment-wide rather than per account
    /// because an instance synchronizing a work mailbox and a personal one is synchronizing one person's
    /// correspondence, and mail sent from the first to the second is the least suspicious mail in the mailbox.
    /// </para>
    /// <para>
    /// It defaults to on because that mail is either the owner's own or somebody who has taken their mailbox, and the
    /// first is far more common. The case for turning it off is an account on a large shared provider, where every
    /// user of that provider writes from the same domain and the set would recognize all of them; a deployment that
    /// turns it off names the domains it does mean on the accounts' own trusted-sender lists instead.
    /// </para>
    /// </remarks>
    public bool TrustOwnAccountDomains { get; set; } = true;

    /// <summary>Gets or sets whether extraction verifies a message's own DKIM signatures where no trusted server did.</summary>
    /// <remarks>
    /// <para>
    /// It defaults to on because the deployment it was built for is the one whose receiving server writes no
    /// <c>Authentication-Results</c> header. Every message there records that nothing was established, no author ever
    /// authenticates, and the trusted-sender list has no identity to be held against — so shipping this off would
    /// switch it off for exactly the mailboxes it exists for, while leaving them looking correctly configured.
    /// </para>
    /// <para>
    /// It runs only as a fallback. An account naming a trusted authority whose header was found goes on believing that
    /// server and verifies nothing itself, because that server observed the connection and this process did not.
    /// </para>
    /// <para>
    /// What it costs is a bounded, cached DNS lookup of <c>&lt;selector&gt;._domainkey.&lt;domain&gt;</c> when a message
    /// is stored. That is a low-cardinality name the signing domain published in order to be asked for, and it discloses
    /// to that domain only that somebody here received mail they sent — which is why this may default on while the spam
    /// scanner's DNS checks stay off, where what would be sent is the sending address and the URI hosts out of the body.
    /// An operator who wants no egress from this path at all sets it to <see langword="false" /> and gets exactly the
    /// behaviour of a deployment that never had it.
    /// </para>
    /// </remarks>
    public bool VerifyDkimLocally { get; set; } = true;

    /// <summary>Gets or sets whether extraction assesses how much each message's own text reads as machine written.</summary>
    /// <remarks>
    /// <para>
    /// It defaults to on because the assessment costs one pass over text the extraction has already produced — no
    /// network, no model, no DNS, and nothing an operator has to configure for it to mean something — and because the
    /// strongest thing it reports is a message carrying characters no mail client renders, which is worth knowing on a
    /// first run rather than after somebody has thought to look for it.
    /// </para>
    /// <para>
    /// The case for turning it off is a deployment that does not want the reading published at all: it is an
    /// observation about how a message was written, and an operator may reasonably decide their readers should not be
    /// handed one. A deployment that turns it off records the not-assessed state, which is what a message with no
    /// readable body carries and what mail stored before this deployment assessed anything carries — so nothing about
    /// the column says the operator turned it off.
    /// </para>
    /// </remarks>
    public bool AssessMachineAuthorship { get; set; } = true;

    /// <summary>Gets the weighting extraction assesses by, which is the one that reads nothing where the setting is off.</summary>
    /// <remarks>
    /// The weighting itself is the project's and is deliberately not configurable, so what the setting decides is which
    /// of two profiles extraction is handed rather than what either of them contains. Resolving it here rather than in
    /// the composition root keeps the decision beside the setting it follows from and testable without a container.
    /// </remarks>
    public MachineAuthorshipProfile MachineAuthorshipProfile => this.AssessMachineAuthorship
        ? MachineAuthorshipProfile.Standard
        : MachineAuthorshipProfile.Disabled;

    /// <summary>Gets or sets configured accounts and folders to synchronize.</summary>
    public List<MailSynchronizationAccountOptions> Accounts { get; set; } = [];

    /// <summary>Reads the two keys a convergence pass is bounded by.</summary>
    /// <returns>The bounds the pass runs under.</returns>
    internal MailboxConvergenceOptions ToConvergenceOptions() => new()
    {
        MaxMutationsPerPass = this.MaxMutationsPerConvergencePass,
        UnknownOutcomeGrace = this.UnknownMutationOutcomeGrace,
    };

    /// <summary>Reads the five keys one synchronization run is bounded by.</summary>
    /// <returns>The bounds the run stops at.</returns>
    internal MailboxSynchronizationOptions ToSynchronizationOptions() => new()
    {
        MaxMetadataBatchSize = this.MaxMetadataBatchSize,
        MaxRawMimeBytes = this.MaxRawMimeBytes,
        MaxMetadataBatchesPerRun = this.MaxMetadataBatchesPerRun,
        MaxReconciledEmailsPerRun = this.MaxReconciledEmailsPerRun,
        MaxContentBytesPerRun = this.MaxContentBytesPerRun,
    };

    /// <summary>Reads the four keys a MIME walk is bounded by, and whether it verifies a signature for itself.</summary>
    /// <returns>The limits the parse is performed under.</returns>
    internal EmailMimeExtractionOptions ToMimeExtractionOptions() => new()
    {
        MaxPartCount = this.MaxMimePartCount,
        MaxNestingDepth = this.MaxMimeNestingDepth,
        MaxExtractedTextCharacters = this.MaxExtractedTextCharacters,
        VerifyDkimLocally = this.VerifyDkimLocally,
    };

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
        var accountIdentity = MailAccountId.Create(accountId);
        var account = this.RequireAccount(accountIdentity);

        var material = await account.ResolveConnectionMaterialAsync(resolver, trustAnchorLoader, cancellationToken);

        return new ImapAccountSettings(
            accountIdentity.Value,
            account.Host.Trim(),
            account.Port,
            account.UserName,
            material);
    }

    /// <summary>Builds one account's submission settings, resolving its material for the caller to own.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <param name="resolver">The resolver that turns configured references into material.</param>
    /// <param name="trustAnchorLoader">The loader that turns configured material into a trust anchor.</param>
    /// <param name="cancellationToken">Cancels the secret resolution.</param>
    /// <returns>The settings, whose material the caller must dispose when its operation ends.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the account is not configured, or configures no submission endpoint.</exception>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the resolved material passes to the caller, which erases it when its operation ends.")]
    internal async Task<SmtpAccountSettings> ResolveDeliverySettingsAsync(
        string accountId,
        ISecretReferenceResolver resolver,
        TrustAnchorLoader trustAnchorLoader,
        CancellationToken cancellationToken)
    {
        var accountIdentity = MailAccountId.Create(accountId);
        var account = this.RequireAccount(accountIdentity);

        if (!account.Delivery.IsConfigured)
        {
            throw new InvalidOperationException($"Account '{accountIdentity.Value}' configures no submission endpoint.");
        }

        var material = await account.ResolveDeliveryConnectionMaterialAsync(resolver, trustAnchorLoader, cancellationToken);

        return new SmtpAccountSettings(
            accountIdentity.Value,
            account.Delivery.Host.Trim(),
            account.Delivery.Port,
            account.Delivery.ResolveUserName(account.UserName),
            material);
    }

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

        if (this.MaxStoredContentBytesPerOwner is { } ownerStorageCeiling
            && ownerStorageCeiling < this.MaxRawMimeBytes)
        {
            yield return new ValidationResult(
                $"The per-owner stored content ceiling of {ownerStorageCeiling} bytes is below the {this.MaxRawMimeBytes} bytes one message may occupy, so no owner could ever have a message stored.",
                [nameof(this.MaxStoredContentBytesPerOwner)]);
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

        // Whether anything is declared at all is deliberately not asked here. This section is one of two places a
        // mailbox is declared — the other being each owner's own section of the top-level Accounts collection — and a
        // deployment that moved its mailboxes under their owners has emptied this one on purpose. The rule is stated
        // once, over the effective set, in DeclaredOwners.
        //
        // Every account this section declares belongs to the one owner such a deployment serves, which is why the whole
        // section is one naming space here. A second owner declaring an account of the same name is not a collision
        // and never reaches this, because their accounts are in their own section rather than in this list.
        foreach (var result in MailAccountNamingSpace.FindCollisions(this.Accounts, nameof(this.Accounts)))
        {
            yield return result;
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
    /// <para>
    /// A reload can remove an account while its supervisor is between runs, which is an ordinary configuration change
    /// rather than a failure: the supervisor ends itself instead of connecting to a server the operator withdrew.
    /// Every other reader wants the account it was handed to exist, and keeps failing when it does not.
    /// </para>
    /// <para>
    /// The roster is searched first because an adoption publishes the owner's document without rewriting the file it
    /// superseded. A later start refuses that stale deployment section, but the running process must follow the commit
    /// now. What makes the identifier enough to search either source is the deployment-wide bound on mail-account names
    /// that <c>DeclaredOwners</c> states.
    /// </para>
    /// </remarks>
    internal MailSynchronizationAccountOptions? FindConfiguredAccount(MailAccountId accountId) =>
        this.ServedOwners?
            .SelectMany(static owner => owner.MailAccounts)
            .SingleOrDefault(
                candidate => !string.IsNullOrWhiteSpace(candidate.AccountId)
                    && StringComparer.Ordinal.Equals(
                        MailAccountId.Create(candidate.AccountId).Value,
                        accountId.Value))
        ?? (this.Accounts ?? []).SingleOrDefault(
            candidate => !string.IsNullOrWhiteSpace(candidate.AccountId)
                && StringComparer.Ordinal.Equals(
                    MailAccountId.Create(candidate.AccountId).Value,
                    accountId.Value));

    /// <summary>Finds the account a reader was handed, failing when this snapshot does not name it.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <returns>The configured account.</returns>
    /// <exception cref="InvalidOperationException">Thrown when this snapshot does not name the account.</exception>
    internal MailSynchronizationAccountOptions RequireAccount(MailAccountId accountId) =>
        this.FindConfiguredAccount(accountId)
        ?? throw new InvalidOperationException($"Account '{accountId.Value}' is not configured.");

    /// <summary>Normalizes a configured account identifier the way every lookup arrives already normalized, or reports it unusable.</summary>
    /// <param name="configuredAccountId">The identifier as the operator wrote it.</param>
    /// <returns>The normalized identifier, or <see langword="null" /> when it is not a value this system issues.</returns>
    internal static string? TryReadAccountId(string? configuredAccountId)
    {
        try
        {
            return string.IsNullOrWhiteSpace(configuredAccountId)
                ? null
                : MailAccountId.Create(configuredAccountId).Value;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>Configures one account for periodic IMAP synchronization.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailSynchronizationAccountOptions : IValidatableObject
{
    /// <summary>Gets or sets the local account identifier.</summary>
    [Required]
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Gets or sets the name this account is published under.</summary>
    /// <remarks>
    /// <para>
    /// Required, and with no fallback to <see cref="AccountId" />. The identifier is a key an operator invented for
    /// configuration, and a reader meeting it in an answer has no way to tell which mailbox it means; this is the text
    /// that answers that, so a deployment states it rather than having MailFathom guess a name and publish it as though
    /// somebody had chosen it.
    /// </para>
    /// <para>
    /// It shares a naming space with the identifiers, because a caller may name an account by either and one name must
    /// never select two accounts. Startup therefore refuses a display name that another account's identifier or display
    /// name already carries, compared without regard to case; an account's own identifier is not a collision, since both
    /// spellings then reach the same mailbox.
    /// </para>
    /// </remarks>
    [Required]
    public string DisplayName { get; set; } = string.Empty;

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

    /// <summary>Gets or sets where this account's mail is submitted, which is a second server from the one it is read on.</summary>
    /// <remarks>
    /// An account that names no submission host in the block configures no submission endpoint, and no delivery session
    /// can be opened for it. That is the default and an ordinary shape: reading a mailbox needs nothing here.
    /// </remarks>
    public MailAccountDeliveryOptions Delivery { get; set; } = new();

    /// <summary>Gets or sets the authserv-id of the one server whose sender-authentication results this account believes.</summary>
    /// <remarks>
    /// <para>
    /// RFC 8601 has every server that checks SPF, DKIM, or DMARC write its findings into an <c>Authentication-Results</c>
    /// header stamped with its own identifier, and it has a consumer read only the headers carrying the identifier it
    /// trusts. That is the whole defence: the header is ordinary, so anything upstream of the receiving server can write
    /// one claiming that everything passed, and a reading that took the topmost header whatever it said could be
    /// defeated by the thing it is checking.
    /// </para>
    /// <para>
    /// Which identifier is right is a property of who receives this account's mail rather than of MailFathom, so there
    /// is nothing to default it to. Omitting it is an ordinary choice and not a misconfiguration: the account then
    /// believes no header at all and every message it holds carries the not-established verdict, which is also what a
    /// deployment whose provider publishes no results sees. Configuring one afterwards changes what a later extraction
    /// derives; the backfill is what re-derives the mail already stored.
    /// </para>
    /// <para>
    /// It is compared without regard to case. A value that is present but unusable — blank, longer than a domain name,
    /// or carrying whitespace — fails startup, because the alternative is discovering it as mail that never
    /// authenticates.
    /// </para>
    /// </remarks>
    public string? TrustedAuthenticationServiceIdentifier { get; set; }

    /// <summary>Gets or sets the senders this account recognizes on top of the domains this deployment's own accounts use.</summary>
    /// <remarks>
    /// <para>
    /// Per account rather than deployment-wide, because the accounts an instance synchronizes are different
    /// correspondence: a work account's counterparties have nothing to do with a personal one's, and one list would
    /// either recognize too much on one account or make an owner maintain the union of both.
    /// </para>
    /// <para>
    /// This is the declared half of the list and the store holds the half somebody adds while the deployment is
    /// running; an entry in either recognizes a sender, and a reload can no more remove a stored entry than a stored
    /// entry can shadow one written here. An entry that names neither a domain nor an address, names both, or writes
    /// one nothing can compare fails startup naming this account and the entry's position.
    /// </para>
    /// </remarks>
    public List<TrustedSenderOptions> TrustedSenders { get; set; } = [];

    /// <summary>Gets the configured entries as the values the matcher holds a sender against, dropping the unusable ones.</summary>
    /// <remarks>
    /// An unusable entry is skipped here rather than raised over, because startup validation refuses that configuration
    /// and a reload being rejected must not make an arriving message throw. What that costs is one entry of one
    /// account's list, and the cost is in the safe direction: an entry nobody could read recognizes nobody.
    /// </remarks>
    internal IReadOnlyList<TrustedSenderEntry> ConfiguredTrustedSenders =>
    [
        .. (this.TrustedSenders ?? [])
            .Select(static configured => configured.TryCreateEntry(out var entry) ? entry : null)
            .OfType<TrustedSenderEntry>(),
    ];

    /// <summary>Gets or sets whether this account records the people it corresponds with, and under what bounds.</summary>
    /// <remarks>
    /// Per account rather than deployment-wide, because what it produces is a record about third parties and the
    /// decision to build one belongs to whoever owns the correspondence. An account that omits the block collects
    /// nothing, which is what every account does until somebody writes <c>Enabled</c> into it.
    /// </remarks>
    public ContactCollectionOptions ContactCollection { get; set; } = new();

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

    /// <summary>Gets or sets which changes a mail rule may make to this account's mailbox.</summary>
    /// <remarks>
    /// Omitting the block permits the three reversible actions and refuses deletion, which is what every account gets
    /// until it says otherwise. What a rule declares is judged against this when the rule set is read, so an account
    /// that refuses an action never has a rule silently skipped over it.
    /// </remarks>
    public MailRuleActionPermissionOptions RuleActions { get; set; } = new();

    /// <summary>Gets or sets whether and for how long this account keeps a record of the changes MailFathom made to it.</summary>
    /// <remarks>
    /// Omitting the block leaves the trail off, which is the default for the reason the privacy rules require: the trail
    /// is derived personal data — it says where a person's mail has been, when, and at whose instruction — so a
    /// deployment that never asked for it never accumulates it.
    /// </remarks>
    public MailboxMutationAuditTrailOptions AuditTrail { get; set; } = new();

    /// <summary>Gets or sets whether and for how long this account keeps a record of the questions answered from it.</summary>
    /// <remarks>
    /// A block of its own beside <see cref="AuditTrail" /> rather than the same switch, because the two records answer
    /// different questions: one says where a person's mail has been, this says what it was read for. Omitting it leaves
    /// the record off, for the reason the privacy rules require — it is derived personal data, so a deployment that
    /// never asked for it never accumulates it.
    /// </remarks>
    public MailAnsweringAuditTrailOptions AnsweringAuditTrail { get; set; } = new();

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

        // Required whether or not synchronization is enabled, unlike the connection settings below. The name is what
        // every result publishing this account carries, and the stored copy stays readable after an operator switches
        // synchronization off, so an account without one would reach a caller nameless rather than merely unrefreshed.
        foreach (var result in this.ValidateDisplayName())
        {
            yield return result;
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

        // A present but unusable authserv-id fails startup rather than degrading to trusting nothing, because the two
        // are indistinguishable afterwards: an account that believes no header and an account whose configured
        // identifier matches none both see every message as unauthenticated. The message names the account and never
        // the value, which the failure rules refuse as a host name.
        if (!TrustedAuthenticationAuthority.TryCreate(this.TrustedAuthenticationServiceIdentifier, out _))
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': the trusted authentication service identifier must be a domain-shaped token of at most {TrustedAuthenticationAuthority.MaximumLength} characters, or be omitted so that the account believes no header.",
                [nameof(this.TrustedAuthenticationServiceIdentifier)]);
        }

        foreach (var result in this.ValidateTrustedSenders())
        {
            yield return result;
        }

        foreach (var result in this.ValidateContactCollection())
        {
            yield return result;
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

        // Checked here for the reason the block above is, and separately from it because the two are separate operator
        // decisions: an account may keep one record and not the other, and a typo in either window decides when
        // personal data is destroyed.
        if (this.AnsweringAuditTrail is null)
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': the answering audit trail configuration must be a block.",
                [nameof(this.AnsweringAuditTrail)]);
        }
        else if (this.AnsweringAuditTrail.Retention < MailAnsweringAuditTrailOptions.MinimumRetention
            || this.AnsweringAuditTrail.Retention > MailAnsweringAuditTrailOptions.MaximumRetention)
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': the answering audit trail retention must be between {MailAnsweringAuditTrailOptions.MinimumRetention} and {MailAnsweringAuditTrailOptions.MaximumRetention}.",
                [nameof(this.AnsweringAuditTrail)]);
        }

        // Checked here for the reason the blocks above are. A missing block would read as an account that permits
        // nothing, so every rule declaring an action over it would be refused with a message naming the rule rather
        // than the configuration that actually went missing.
        if (this.RuleActions is null)
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': the rule action permissions must be a block.",
                [nameof(this.RuleActions)]);
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

        foreach (var result in this.FindRoleCollisions())
        {
            yield return result;
        }

        foreach (var result in this.ValidateDelivery(synchronizationEnabled))
        {
            yield return result;
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

    /// <summary>Reports a contact collection block this account could not run under.</summary>
    /// <returns>One result per unusable value or entry, empty when the whole block is one this account can run under.</returns>
    /// <remarks>
    /// <para>
    /// Checked here rather than through data annotations because nothing binds the block as an options graph of its own,
    /// so an annotation on it would be read by nothing. It is checked whether or not collection is switched on: a typo
    /// in a block somebody has not enabled yet is a typo they meet on the day they enable it.
    /// </para>
    /// <para>
    /// An unusable exclusion fails startup rather than being dropped, because the two are not indistinguishable in the
    /// safe direction — an entry nobody could read excludes nobody, so a deployment would go on recording exactly the
    /// people the entry was written to keep out. The message names the account and the entry's position and never the
    /// value it holds, because a domain and a pattern over an address are both personal data and a validation failure is
    /// written to a log.
    /// </para>
    /// </remarks>
    private IEnumerable<ValidationResult> ValidateContactCollection()
    {
        if (this.ContactCollection is null)
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': the contact collection configuration must be a block.",
                [nameof(this.ContactCollection)]);

            yield break;
        }

        if (this.ContactCollection.MinimumMessagesFromSender < ContactCollectionOptions.MinimumMessageThreshold
            || this.ContactCollection.MinimumMessagesFromSender > ContactCollectionOptions.MaximumMessageThreshold)
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': contact collection must ask for between {ContactCollectionOptions.MinimumMessageThreshold} and {ContactCollectionOptions.MaximumMessageThreshold} messages from a sender.",
                [nameof(this.ContactCollection)]);
        }

        if (this.ContactCollection.MaxContactsPerRun < 0
            || this.ContactCollection.MaxContactsPerRun > ContactCollectionOptions.MaximumContactsPerRun)
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': contact collection must record between 0 and {ContactCollectionOptions.MaximumContactsPerRun} contacts per run.",
                [nameof(this.ContactCollection)]);
        }

        if (this.ContactCollection.Exclusions is null)
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': the contact collection exclusions must be a list.",
                [nameof(this.ContactCollection)]);

            yield break;
        }

        foreach (var (entry, position) in this.ContactCollection.Exclusions.Select(static (entry, index) => (Entry: entry, Position: index)))
        {
            if (entry is null || !entry.TryCreateExclusion(out _))
            {
                yield return new ValidationResult(
                    $"Account '{this.AccountId}': contact collection exclusion {position} must name exactly one of a usable domain or a usable address pattern, may ask to include subdomains only where it names a domain, and may not write a pattern whose only characters are the two wildcards and the at-sign.",
                    [nameof(this.ContactCollection)]);
            }
        }
    }

    /// <summary>Reports every trusted-sender entry that names no sender this system can compare against.</summary>
    /// <returns>One result per unusable entry, empty when every entry names exactly one usable sender.</returns>
    /// <remarks>
    /// <para>
    /// A typo here fails startup rather than degrading to an entry that recognizes nobody, because the two are
    /// indistinguishable afterwards: a list nobody wrote and a list whose entries match nothing both leave every
    /// sender unrecognized, and an operator would meet the difference as mail that never stops carrying a warning.
    /// </para>
    /// <para>
    /// The message names the account and the entry's position and never the value it holds, because a domain and an
    /// address are both personal data and a validation failure is written to a log.
    /// </para>
    /// </remarks>
    private IEnumerable<ValidationResult> ValidateTrustedSenders()
    {
        if (this.TrustedSenders is null)
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': the trusted sender configuration must be a list.",
                [nameof(this.TrustedSenders)]);

            yield break;
        }

        foreach (var (entry, position) in this.TrustedSenders.Select(static (entry, index) => (Entry: entry, Position: index)))
        {
            if (entry is null || !entry.TryCreateEntry(out _))
            {
                yield return new ValidationResult(
                    $"Account '{this.AccountId}': trusted sender {position} must name exactly one of a usable domain or a usable address, and may ask to include subdomains only where it names a domain.",
                    [nameof(this.TrustedSenders)]);
            }
        }
    }

    /// <summary>Reports every special-use role this account gives to more than one folder.</summary>
    /// <returns>One result per shared role, naming every alias that claims it, empty when each role names one folder.</returns>
    /// <remarks>
    /// A role is how something asks for <em>this account's junk folder</em>, so two folders carrying one role would give
    /// that question two answers and let whichever mapping happened to be read first decide where mail is filed.
    /// Refusing it when the configuration binds is what keeps a copy-paste mistake an error an operator reads once. An
    /// account naming no role at all is untouched by this, and so is one naming several different roles.
    /// </remarks>
    private IEnumerable<ValidationResult> FindRoleCollisions() => this.Folders
        .Where(folder => !string.IsNullOrWhiteSpace(folder.Alias) && folder.DeclaredRole is not null)
        .GroupBy(folder => folder.DeclaredRole!.Value)
        .Where(group => group.Count() > 1)
        .Select(group => new ValidationResult(
            $"Account '{this.AccountId}': the folder aliases {string.Join(", ", group.Select(folder => $"'{folder.Alias.Trim()}'"))} all name the special-use role '{group.Key}', and an account has at most one folder per role.",
            [nameof(this.Folders)]));

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

    /// <summary>Reports every reason this account's submission endpoint could not be reached as configured.</summary>
    /// <param name="synchronizationEnabled">Whether the account's reading endpoint is validated by the rules above.</param>
    /// <returns>One result per unusable delivery setting, empty when the account configures none or configures a usable one.</returns>
    /// <remarks>
    /// The block is validated whether or not synchronization is enabled, because submitting and reading are separate
    /// capabilities against separate servers and an account may be configured for one without the other. What the flag
    /// decides is only which rules have already been reported elsewhere: the credential and the user name are the
    /// account's and are checked above when synchronization is on, so repeating them here would report one missing
    /// setting twice.
    /// </remarks>
    private IEnumerable<ValidationResult> ValidateDelivery(bool synchronizationEnabled)
    {
        if (this.Delivery is null)
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': the delivery configuration must be a block.",
                [nameof(this.Delivery)]);

            yield break;
        }

        if (!this.Delivery.IsConfigured)
        {
            // Refused rather than left to the first send, because an operator who turned sending on has decided this
            // account may write to people and would read a configuration that binds as one that did so. The remedy is
            // an endpoint or a switch turned back off, and both are edits to this block.
            if (this.Delivery.Enabled)
            {
                yield return new ValidationResult(
                    $"Account '{this.AccountId}': sending is enabled and no submission host is configured, so nothing could ever carry a message.",
                    [$"{nameof(this.Delivery)}.{nameof(MailAccountDeliveryOptions.Enabled)}"]);
            }

            // A credential or a login provisioned for an endpoint that does not exist is the shape an operator reads
            // as working, so it is refused rather than left silently unused.
            if (this.Delivery.Secrets is not null || !string.IsNullOrWhiteSpace(this.Delivery.UserName))
            {
                yield return new ValidationResult(
                    $"Account '{this.AccountId}': the delivery block names a user name or a credential and no submission host, so neither could ever be used.",
                    [nameof(this.Delivery)]);
            }

            yield break;
        }

        if (this.Delivery.Port is < 1 or > 65535)
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': the submission port must be between 1 and 65535.",
                [$"{nameof(this.Delivery)}.{nameof(MailAccountDeliveryOptions.Port)}"]);
        }

        foreach (var result in this.ValidateDeliveryTransportSecurity())
        {
            yield return result;
        }

        // Refused at startup rather than at the first send, because an endpoint configured without a sending identity
        // is an endpoint that will compose nothing — and an operator reads a submission block that binds and validates
        // as one that works.
        if (!EmailAddress.TryCreate(
            this.Delivery.FromDisplayName,
            this.Delivery.ResolveFromAddress(this.UserName),
            out _))
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': the submission endpoint names no address to send from, so set Delivery.FromAddress or give the account a user name that is a mailbox address.",
                [$"{nameof(this.Delivery)}.{nameof(MailAccountDeliveryOptions.FromAddress)}"]);
        }

        if (!synchronizationEnabled)
        {
            foreach (var result in this.ValidateDeliveryCredentials())
            {
                yield return result;
            }
        }
    }

    /// <summary>Reports every transport security rule the submission endpoint's own connection mode breaks.</summary>
    /// <returns>One result per violated rule, empty when the mode is safe under the account's policy.</returns>
    /// <remarks>
    /// Only the rules the mode itself decides are reported here. The permitted mechanisms and the certificate
    /// authority belong to the account rather than to either endpoint, so they are reported once against the account's
    /// own block instead of a second time against this one.
    /// </remarks>
    private IEnumerable<ValidationResult> ValidateDeliveryTransportSecurity() => this.TransportSecurity
        .FindConfigurationErrors(this.Delivery.ConnectionSecurity)
        .Where(error => error.Violation is { } violation && DecidedByConnectionSecurity(violation))
        .Select(error => new ValidationResult(
            $"Account '{this.AccountId}' submission endpoint: {error.Description} [{error.Violation}]",
            [$"{nameof(this.Delivery)}.{nameof(MailAccountDeliveryOptions.ConnectionSecurity)}"]));

    /// <summary>Reports a submission credential the account's policy needs and nothing supplies.</summary>
    /// <returns>One result when a password mechanism is permitted and no reference is configured, empty otherwise.</returns>
    /// <remarks>
    /// The delivery block's own credential is what answers this where it names one, and the account's is what answers
    /// it otherwise, so an account that submits with the same login it reads with configures nothing extra.
    /// </remarks>
    private IEnumerable<ValidationResult> ValidateDeliveryCredentials()
    {
        MailAuthenticationPolicy authentication;
        try
        {
            authentication = this.CreateDeliveryTransportSecurityPolicy().Authentication;
        }
        catch (MailTransportSecurityPolicyViolationException)
        {
            // ValidateDeliveryTransportSecurity and ValidateTransportSecurity already reported this between them, and
            // the rules below need a policy to read.
            yield break;
        }

        if (string.IsNullOrWhiteSpace(this.Delivery.ResolveUserName(this.UserName)))
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': a submission user name is required, on the account or on its delivery block.",
                [nameof(this.Delivery)]);
        }

        if (authentication.PermitsPasswordAuthentication
            && string.IsNullOrWhiteSpace(this.Delivery.ResolveSecrets(this.Secrets).Password?.SecretReference))
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}' submits with a password mechanism and configures no password secret reference, on the account or on its delivery block.",
                [nameof(this.Delivery)]);
        }
    }

    /// <summary>Reports whether a violated rule is one the endpoint's own connection mode decides.</summary>
    private static bool DecidedByConnectionSecurity(MailTransportSecurityViolation violation) => violation is
        MailTransportSecurityViolation.ConnectionSecurityNotSupported
        or MailTransportSecurityViolation.UnencryptedConnectionRequiresExplicitOptIn
        or MailTransportSecurityViolation.OpportunisticEncryptionRequiresExplicitOptIn
        or MailTransportSecurityViolation.ClearTextAuthenticationRequiresEncryptedConnection;

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

    /// <summary>Reports a display name this account could not be published under.</summary>
    /// <returns>One result naming the rule the configured text breaks, empty when the name is usable.</returns>
    /// <remarks>
    /// The domain type owns the rules and this translates its refusal into a startup message, so the bound on length and
    /// the refusal of control characters are stated once. The account is named in the message and the offending text is
    /// not, because a control character written into a startup log is the thing being refused.
    /// </remarks>
    private IEnumerable<ValidationResult> ValidateDisplayName()
    {
        if (string.IsNullOrWhiteSpace(this.DisplayName))
        {
            return
            [
                new ValidationResult(
                    $"Account '{this.AccountId}': a display name is required. It is the name the account is published under, and there is no default.",
                    [nameof(this.DisplayName)]),
            ];
        }

        try
        {
            MailAccountDisplayName.Create(this.DisplayName);

            return [];
        }
        catch (ArgumentException exception)
        {
            return
            [
                new ValidationResult(
                    $"Account '{this.AccountId}': the display name is not usable [{exception.Message}]",
                    [nameof(this.DisplayName)]),
            ];
        }
    }

    /// <summary>Builds what this account is published as, or nothing when its configuration cannot name it.</summary>
    /// <param name="owner">The owner a configured account belongs to, which configuration itself cannot name.</param>
    /// <returns>The served account, or <see langword="null" /> when the identifier or the display name is unusable.</returns>
    /// <remarks>
    /// The absence is the reload case rather than an ordinary one: startup refuses configuration this returns nothing
    /// for, so the only way to reach it is a reload being rejected while the previous snapshot is still serving.
    /// The owner is a parameter rather than a configured key because no account block names one: an account declared in
    /// a file belongs to the one owner such a deployment holds, and the caller is what knows which that is.
    /// </remarks>
    internal ServedMailAccount? CreateServedAccount(MailOwnerId owner)
    {
        try
        {
            return new ServedMailAccount(
                owner,
                MailAccountId.Create(this.AccountId),
                MailAccountDisplayName.Create(this.DisplayName),
                this.Mode);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Builds the account's configured audit trail settings.</summary>
    /// <returns>The settings the account's block names.</returns>
    internal MailboxMutationAuditSettings CreateAuditSettings() =>
        new(this.AuditTrail.Enabled, this.AuditTrail.Retention);

    /// <summary>Builds the account's configured answering record settings.</summary>
    /// <returns>The settings the account's block names.</returns>
    internal MailAnsweringAuditSettings CreateAnsweringAuditSettings() =>
        new(this.AnsweringAuditTrail.Enabled, this.AnsweringAuditTrail.Retention);

    /// <summary>Builds the account's configured sender-authentication authority.</summary>
    /// <returns>The authority the account named, or none when it named none.</returns>
    /// <remarks>
    /// An unusable value answers with none rather than raising, because startup validation already refuses that
    /// configuration and a reload being rejected must not make an extraction throw. Believing no header is the safe
    /// direction to fall in: it withholds a verdict rather than inventing one.
    /// </remarks>
    internal TrustedAuthenticationAuthority CreateTrustedAuthority() =>
        TrustedAuthenticationAuthority.TryCreate(this.TrustedAuthenticationServiceIdentifier, out var authority)
            ? authority
            : TrustedAuthenticationAuthority.None;

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

    /// <summary>Builds the validated policy this account's submission endpoint is reached under.</summary>
    /// <returns>The policy the delivery adapter must obey.</returns>
    /// <exception cref="MailTransportSecurityPolicyViolationException">Thrown when the configured combination is unsafe.</exception>
    /// <remarks>
    /// It is the account's own policy with the submission endpoint's connection mode in it, so the permitted
    /// mechanisms, the accepted weakenings, and the certificate authority are one decision and only the encryption of
    /// the channel differs between the two servers.
    /// </remarks>
    internal MailTransportSecurityPolicy CreateDeliveryTransportSecurityPolicy() =>
        this.TransportSecurity.CreatePolicy(this.Delivery.ConnectionSecurity);

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
    internal Task<MailAccountConnectionMaterial> ResolveConnectionMaterialAsync(
        ISecretReferenceResolver resolver,
        TrustAnchorLoader trustAnchorLoader,
        CancellationToken cancellationToken) =>
        this.ResolveConnectionMaterialAsync(
            this.CreateTransportSecurityPolicy(),
            this.Secrets,
            resolver,
            trustAnchorLoader,
            cancellationToken);

    /// <summary>Resolves the password and trust anchor one submission connection attempt needs.</summary>
    /// <param name="resolver">The resolver that turns configured references into material.</param>
    /// <param name="trustAnchorLoader">The loader that turns configured material into a trust anchor.</param>
    /// <param name="cancellationToken">Cancels the retrieval.</param>
    /// <returns>The material, which the caller must dispose when its operation ends.</returns>
    /// <exception cref="InvalidOperationException">Thrown when configuration that passed startup validation no longer yields usable material.</exception>
    /// <remarks>
    /// The credential is the delivery block's where it names one and the account's otherwise, because a provider that
    /// authenticates one login for both protocols is the ordinary case. The trust anchor is the account's either way:
    /// it is the authority this deployment added to the system trust store rather than a property of one endpoint.
    /// </remarks>
    internal Task<MailAccountConnectionMaterial> ResolveDeliveryConnectionMaterialAsync(
        ISecretReferenceResolver resolver,
        TrustAnchorLoader trustAnchorLoader,
        CancellationToken cancellationToken) =>
        this.ResolveConnectionMaterialAsync(
            this.CreateDeliveryTransportSecurityPolicy(),
            this.Delivery.ResolveSecrets(this.Secrets),
            resolver,
            trustAnchorLoader,
            cancellationToken);

    /// <summary>Resolves one endpoint's credential beside the account's trust anchor, owning neither afterwards.</summary>
    private async Task<MailAccountConnectionMaterial> ResolveConnectionMaterialAsync(
        MailTransportSecurityPolicy transportSecurityPolicy,
        MailAccountSecretOptions secrets,
        ISecretReferenceResolver resolver,
        TrustAnchorLoader trustAnchorLoader,
        CancellationToken cancellationToken)
    {
        var password = transportSecurityPolicy.Authentication.PermitsPasswordAuthentication
            ? await secrets.ResolvePasswordAsync(resolver, cancellationToken)
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
