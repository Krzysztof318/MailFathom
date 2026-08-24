// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Host.Configuration.SensitiveContent;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Warnings;

/// <summary>Says how much of this mailbox was derived before the sensitive-content configuration it now runs.</summary>
/// <remarks>
/// <para>
/// The report this feature exists for. Switching a scanner on protects what is derived from now on and reaches nothing
/// already in the chunk store or among the stored vectors, and an operator reading their own configuration file has no way to
/// see that: the switch is on, the file says so, and the passages a retrieval hit returns were still built from
/// unredacted text. Widening a category set leaves the same gap one category wide. Neither is visible in the product
/// without this, which is why the gap is stated at every start rather than left in a column.
/// </para>
/// <para>
/// It reports and never refuses. A deployment that decides the re-derivation is not worth its cost is running a
/// supported configuration — the mail already stored is no less protected than it was yesterday — so refusing to start
/// over it would be refusing over a decision that is the operator's. What it must not do is stay quiet.
/// </para>
/// <para>
/// Registered only where a scanner is switched on. With both off there is no configuration for anything to be stale
/// against: a document derived under an older scanner holds redacted text, which is not a protection gap, and telling an
/// operator who deliberately switched a scanner off that their mailbox is out of date would be reporting their own
/// decision back to them as a problem.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class StaleDerivedDataStartupReport : IHostedService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly SensitiveContentDerivationGuard derivationGuard;
    private readonly SensitiveContentOptions settings;
    private readonly ILogger<StaleDerivedDataStartupReport> logger;

    /// <summary>Initializes a new stale derived-data report.</summary>
    /// <param name="scopeFactory">Opens the scope the count is read in, because the store that answers it is scoped.</param>
    /// <param name="derivationGuard">Names the configuration a derived row written now would carry.</param>
    /// <param name="settings">Read for whether the operator has already asked for the rebuild.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public StaleDerivedDataStartupReport(
        IServiceScopeFactory scopeFactory,
        SensitiveContentDerivationGuard derivationGuard,
        IOptions<SensitiveContentOptions> settings,
        ILogger<StaleDerivedDataStartupReport> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(derivationGuard);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        this.scopeFactory = scopeFactory;
        this.derivationGuard = derivationGuard;
        this.settings = settings.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A report that cannot read the count says so and lets the host start; it decides nothing.")]
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (this.derivationGuard.Stamp is not { } current)
        {
            return;
        }

        int staleEmailCount;

        try
        {
            using var scope = this.scopeFactory.CreateScope();

            staleEmailCount = await scope.ServiceProvider
                .GetRequiredService<IStoredEmailExtractionBackfillStore>()
                .CountEmailsWithStaleDerivedDataAsync(current, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            this.LogCountUnavailable(failure);

            return;
        }

        this.Report(staleEmailCount);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>States which of the three positions this deployment is in.</summary>
    /// <remarks>
    /// The rebuild being switched on is not the same fact as it doing anything, so the middle case names the walk that
    /// performs it: a deployment that asked for the rebuild and switched the extraction backfill off has asked for work
    /// nothing will do, and the two keys read together are what says so.
    /// </remarks>
    private void Report(int staleEmailCount)
    {
        if (staleEmailCount == 0)
        {
            this.LogNothingStale();

            return;
        }

        if (this.settings.RebuildStaleDerivedData)
        {
            this.LogRebuildRequested(staleEmailCount);

            return;
        }

        this.LogStaleDerivedData(staleEmailCount);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Every stored message with derived text was derived under the sensitive-content configuration this deployment runs.")]
    private partial void LogNothingStale();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{StaleEmailCount} stored messages have derived text — extracted text, passages, and any vectors built from them — written under an older sensitive-content configuration, or before any scanner was switched on. Those copies are not redacted to what this deployment now scans for, and switching a scanner on does not change them. Set 'SensitiveContent:RebuildStaleDerivedData' to true to have the extraction backfill re-derive them from the stored raw MIME; that costs one pass over every stored message and, where an embedding profile is active, a re-embedding of every passage whose text changed.")]
    private partial void LogStaleDerivedData(int staleEmailCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "{StaleEmailCount} stored messages carry derived text written under an older sensitive-content configuration, and 'SensitiveContent:RebuildStaleDerivedData' is set, so the extraction backfill re-derives them. It performs none while 'MailExtractionBackfill:Enabled' is off.")]
    private partial void LogRebuildRequested(int staleEmailCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not read how much derived text predates the sensitive-content configuration this deployment runs; the figure is unavailable until the next start.")]
    private partial void LogCountUnavailable(Exception exception);
}
