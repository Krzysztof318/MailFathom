// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.Synchronization;
using MailFathom.Common.Observability;
using MailFathom.Domain.Accounts;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Makes the byte volume of synchronization visible, so storage is sized from what MailFathom ingests.</summary>
/// <remarks>
/// <para>
/// Counting messages answers none of the questions an operator sizing a disk asks, because one message is anywhere
/// between a kilobyte and the configured size limit. The counters here are what a rate is read from — how much a
/// mailbox is costing per interval — and the gauge beside them is the level that rate is filling.
/// </para>
/// <para>
/// Reaching a limit is counted rather than only logged, because both are conditions that persist: a run that stops for
/// its byte budget will stop again next interval, and a deployment at its storage ceiling stays there until somebody
/// acts. A rising count is what says the deployment has been running that way rather than that it did once.
/// </para>
/// <para>
/// The dimensions are the account and folder aliases MailFathom itself was configured with, and the name of the limit
/// that was reached. All three are bounded by configuration, and none is derived from a message: no subject, no
/// address, no remote folder path, and no UID appears here.
/// </para>
/// </remarks>
public sealed partial class MailboxContentVolumeTelemetry
{
    private const string AccountTagName = "mailfathom.mail.account";
    private const string FolderTagName = "mailfathom.mail.folder";
    private const string LimitTagName = "mailfathom.mail.content.limit";

    /// <summary>Names the run budget in the limit dimension, matching the setting an operator would raise.</summary>
    private const string RunBudgetLimitName = "run_budget";

    /// <summary>Names the deployment-wide storage ceiling in the limit dimension.</summary>
    private const string StorageCeilingLimitName = "storage_ceiling";

    /// <summary>Names the per-owner storage ceiling in the limit dimension.</summary>
    /// <remarks>
    /// A value of its own rather than the same one, because the two describe different states of a deployment: one says
    /// the instance is full and the other that one person is at their share while everybody else's mail keeps arriving
    /// whole. An alert written against either would be wrong if they shared a name.
    /// </remarks>
    private const string OwnerStorageCeilingLimitName = "owner_storage_ceiling";

    private readonly Counter<long> fetchedBytes;
    private readonly Counter<long> storedBytes;
    private readonly Counter<long> limitsReached;
    private readonly ILogger<MailboxContentVolumeTelemetry> logger;
    private long storedContentBytes;

    /// <summary>Initializes the instruments a run's content volume is published through.</summary>
    /// <param name="logger">Records the limits a run reached, in MailFathom's own names and byte counts only.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger" /> is <see langword="null" />.</exception>
    public MailboxContentVolumeTelemetry(ILogger<MailboxContentVolumeTelemetry> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;

        this.fetchedBytes = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.content.fetched",
            unit: "By",
            description: "Raw MIME bytes synchronization read from mail servers.");
        this.storedBytes = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.content.stored",
            unit: "By",
            description: "Raw MIME bytes synchronization wrote to local content storage.");
        this.limitsReached = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.content.limits_reached",
            unit: "{run}",
            description: "Folder runs that ended against a content byte limit, by which limit they reached.");
        Telemetry.Meter.CreateObservableGauge(
            "mailfathom.mail.content.stored_total",
            () => this.ObserveStoredContentBytes(),
            unit: "By",
            description: "How much local storage the stored mail content occupies, as the most recent run measured it.");
    }

    /// <summary>Publishes what one folder run moved, and records the limits it reached.</summary>
    /// <param name="accountId">The account the run belonged to.</param>
    /// <param name="folderAlias">MailFathom's own name for the folder the run worked.</param>
    /// <param name="volume">The byte volume and limits the run reported.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folderAlias" /> or <paramref name="volume" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A run that moved nothing still publishes its measured total, because a deployment whose ingestion has stopped is
    /// exactly the one whose level somebody needs to be able to read. The log lines are emitted only for a limit that
    /// was actually reached, so an ordinary run adds nothing an operator has to skip past.
    /// </remarks>
    public void Report(MailAccountId accountId, string folderAlias, MailboxContentVolume volume)
    {
        ArgumentNullException.ThrowIfNull(folderAlias);
        ArgumentNullException.ThrowIfNull(volume);

        var tags = new TagList
        {
            { AccountTagName, accountId.Value },
            { FolderTagName, folderAlias },
        };

        this.fetchedBytes.Add(volume.FetchedBytes, tags);
        this.storedBytes.Add(volume.StoredBytes, tags);
        Interlocked.Exchange(ref this.storedContentBytes, volume.StoredContentBytes);

        if (volume.StoppedForContentBudget)
        {
            this.limitsReached.Add(1, [.. tags, new KeyValuePair<string, object?>(LimitTagName, RunBudgetLimitName)]);
            this.LogRunBudgetSpent(accountId.Value, folderAlias, volume.FetchedBytes);
        }

        if (volume.DeferredForStorageEmailCount > 0)
        {
            this.limitsReached.Add(1, [.. tags, new KeyValuePair<string, object?>(LimitTagName, StorageCeilingLimitName)]);
            this.LogStorageCeilingReached(
                accountId.Value,
                folderAlias,
                volume.DeferredForStorageEmailCount,
                volume.StoredContentBytes);
        }

        if (volume.DeferredForOwnerStorageEmailCount > 0)
        {
            this.limitsReached.Add(1, [.. tags, new KeyValuePair<string, object?>(LimitTagName, OwnerStorageCeilingLimitName)]);
            this.LogOwnerStorageCeilingReached(
                accountId.Value,
                folderAlias,
                volume.DeferredForOwnerStorageEmailCount);
        }

        if (volume.RefilledEmailCount > 0)
        {
            this.LogDeferredContentRefilled(accountId.Value, folderAlias, volume.RefilledEmailCount);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Folder {AccountId}/{FolderAlias} ended its run after fetching {FetchedBytes} bytes of mail content, which is the budget one run may spend; the next run resumes at the committed checkpoint.")]
    private partial void LogRunBudgetSpent(string accountId, string folderAlias, long fetchedBytes);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Local content storage holds {StoredContentBytes} bytes and has reached its configured ceiling, so {DeferredEmailCount} messages of {AccountId}/{FolderAlias} were recorded without their content and are fetched once there is room.")]
    private partial void LogStorageCeilingReached(
        string accountId,
        string folderAlias,
        int deferredEmailCount,
        long storedContentBytes);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The owner of {AccountId} holds what MailSynchronization:MaxStoredContentBytesPerOwner allows one owner, so {DeferredEmailCount} messages of {AccountId}/{FolderAlias} were recorded without their content and are fetched once that owner has room. Every other owner's mail is stored as usual.")]
    private partial void LogOwnerStorageCeilingReached(
        string accountId,
        string folderAlias,
        int deferredEmailCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Fetched the content of {RefilledEmailCount} messages of {AccountId}/{FolderAlias} that an earlier run had left without it.")]
    private partial void LogDeferredContentRefilled(string accountId, string folderAlias, int refilledEmailCount);

    /// <summary>Reports the level the most recent run measured, which is one number for the whole deployment.</summary>
    /// <remarks>
    /// Unlike the counters beside it the level carries no account or folder dimension, because local content storage is
    /// one store that every account writes into. Publishing it per account would report the same number several times
    /// and invite a dashboard to sum them.
    /// </remarks>
    private Measurement<long> ObserveStoredContentBytes() => new(Interlocked.Read(ref this.storedContentBytes));
}
