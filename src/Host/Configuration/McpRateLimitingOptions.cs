// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Infrastructure.Security;

namespace MailFathom.Host.Configuration;

/// <summary>How much MCP traffic the endpoint accepts before it starts refusing.</summary>
/// <remarks>
/// <para>
/// Every setting here has a product default, so a deployment that writes none of them still runs bounded. That is the
/// opposite of how the endpoint itself is configured — <see cref="McpEndpointOptions.Enabled" /> and
/// <see cref="McpEndpointOptions.Authentication" /> have no safe default and refuse to be guessed — because the two
/// questions are different. There is no such thing as a correct posture for who may read a mailbox, but there is such a
/// thing as a sane bound on how fast one client may ask, and leaving the endpoint unbounded because nobody wrote a
/// number is not a decision an operator made.
/// </para>
/// <para>
/// Turning limiting off is therefore an explicit value rather than an omission, and it costs one startup warning. It is
/// the right setting where something in front of the process already bounds the traffic and a second limit would only
/// refuse requests the first one already shaped.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class McpRateLimitingOptions
{
    private const int MaximumConcurrentRequests = 1000;

    private const int MaximumQueueLimit = 1000;

    private const int MaximumTokenCapacity = 1_000_000;

    private static readonly TimeSpan ShortestReplenishmentPeriod = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan LongestReplenishmentPeriod = TimeSpan.FromHours(1);

    /// <summary>Gets or sets whether the endpoint refuses traffic beyond the limits below.</summary>
    /// <remarks>On unless a deployment states otherwise, so an endpoint someone enabled is bounded by the act of enabling it.</remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets how many MCP requests the process serves at once, across every client.</summary>
    public int MaxConcurrentRequests { get; set; } = McpRateLimits.Default.MaxConcurrentRequests;

    /// <summary>Gets or sets how many requests wait for a concurrency slot before the rest are refused.</summary>
    public int ConcurrencyQueueLimit { get; set; } = McpRateLimits.Default.ConcurrencyQueueLimit;

    /// <summary>Gets or sets the largest burst one client may spend at once.</summary>
    public int TokenCapacity { get; set; } = McpRateLimits.Default.TokenCapacity;

    /// <summary>Gets or sets how much of that burst one client gets back each <see cref="ReplenishmentPeriod" />.</summary>
    public int TokensPerReplenishmentPeriod { get; set; } = McpRateLimits.Default.TokensPerReplenishmentPeriod;

    /// <summary>Gets or sets how often a client's spent capacity is restored.</summary>
    public TimeSpan ReplenishmentPeriod { get; set; } = McpRateLimits.Default.ReplenishmentPeriod;

    /// <summary>Gets or sets how many of one client's requests wait for capacity before the rest are refused.</summary>
    /// <remarks>Has to stay below <see cref="MaxConcurrentRequests" />, because a request waiting here is already holding a concurrency permit.</remarks>
    public int RequestQueueLimit { get; set; } = McpRateLimits.Default.RequestQueueLimit;

    /// <summary>Finds everything an operator must fix before these limits can be applied.</summary>
    /// <returns>One message per faulty setting, relative to this section, empty when the limits are usable.</returns>
    /// <remarks>Every message is produced in one pass, so an operator who mistyped several numbers reads all of them rather than the first to be reached.</remarks>
    public IReadOnlyList<string> FindConfigurationErrors()
    {
        if (!this.Enabled)
        {
            return [];
        }

        var errors = new List<string>();

        errors.AddRange(this.FindRangeErrors());
        errors.AddRange(this.FindCombinationErrors());

        return errors;
    }

    /// <summary>Maps the configured settings onto the limits the endpoint runs under.</summary>
    /// <returns>The limits.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the settings have not passed <see cref="FindConfigurationErrors" />.</exception>
    public McpRateLimits ToRateLimits()
    {
        try
        {
            return McpRateLimits.Create(
                this.MaxConcurrentRequests,
                this.ConcurrencyQueueLimit,
                this.TokenCapacity,
                this.TokensPerReplenishmentPeriod,
                this.ReplenishmentPeriod,
                this.RequestQueueLimit);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidOperationException(
                "The configured rate limits were mapped before they were validated, so at least one of them is unusable.",
                exception);
        }
    }

    private IEnumerable<string> FindRangeErrors()
    {
        if (this.MaxConcurrentRequests is < 1 or > MaximumConcurrentRequests)
        {
            yield return $"{nameof(this.MaxConcurrentRequests)} — '{this.MaxConcurrentRequests}' is outside 1 to {MaximumConcurrentRequests}; the endpoint must serve at least one request at a time and cannot be told to serve an unbounded number.";
        }

        if (this.ConcurrencyQueueLimit is < 0 or > MaximumQueueLimit)
        {
            yield return $"{nameof(this.ConcurrencyQueueLimit)} — '{this.ConcurrencyQueueLimit}' is outside 0 to {MaximumQueueLimit}; write 0 to refuse a request the moment no slot is free, and any queue that waits instead has to be bounded.";
        }

        if (this.TokenCapacity is < 1 or > MaximumTokenCapacity)
        {
            yield return $"{nameof(this.TokenCapacity)} — '{this.TokenCapacity}' is outside 1 to {MaximumTokenCapacity}; a client that may spend nothing could never call a tool.";
        }

        if (this.TokensPerReplenishmentPeriod is < 1 or > MaximumTokenCapacity)
        {
            yield return $"{nameof(this.TokensPerReplenishmentPeriod)} — '{this.TokensPerReplenishmentPeriod}' is outside 1 to {MaximumTokenCapacity}; capacity that is never restored would refuse every client permanently once the first burst was spent.";
        }

        if (this.ReplenishmentPeriod < ShortestReplenishmentPeriod || this.ReplenishmentPeriod > LongestReplenishmentPeriod)
        {
            yield return $"{nameof(this.ReplenishmentPeriod)} — '{this.ReplenishmentPeriod}' is outside {ShortestReplenishmentPeriod} to {LongestReplenishmentPeriod}; a shorter period is below what the replenishment timer can resolve, and a longer one makes a spent burst look like an outage.";
        }

        if (this.RequestQueueLimit is < 0 or > MaximumQueueLimit)
        {
            yield return $"{nameof(this.RequestQueueLimit)} — '{this.RequestQueueLimit}' is outside 0 to {MaximumQueueLimit}; write 0 to refuse a request the moment a client is out of capacity, and any queue that waits instead has to be bounded.";
        }
    }

    /// <summary>Reports settings that are each in range and wrong together.</summary>
    /// <remarks>Reported only once both values are usable on their own, so a single mistyped number produces one message rather than two describing the same typo.</remarks>
    private IEnumerable<string> FindCombinationErrors()
    {
        var bothValuesAreUsable = this.TokenCapacity is >= 1 and <= MaximumTokenCapacity
            && this.TokensPerReplenishmentPeriod is >= 1 and <= MaximumTokenCapacity;

        if (bothValuesAreUsable && this.TokensPerReplenishmentPeriod > this.TokenCapacity)
        {
            yield return $"{nameof(this.TokensPerReplenishmentPeriod)} — '{this.TokensPerReplenishmentPeriod}' restores more capacity than {nameof(this.TokenCapacity)} of '{this.TokenCapacity}' can hold, so the surplus is discarded on every replenishment and the rate written here is never the rate that applies; raise {nameof(this.TokenCapacity)} or lower this.";
        }

        var bothLimitsAreUsable = this.RequestQueueLimit is >= 0 and <= MaximumQueueLimit
            && this.MaxConcurrentRequests is >= 1 and <= MaximumConcurrentRequests;

        if (bothLimitsAreUsable && this.RequestQueueLimit >= this.MaxConcurrentRequests)
        {
            yield return $"{nameof(this.RequestQueueLimit)} — '{this.RequestQueueLimit}' is not below {nameof(this.MaxConcurrentRequests)} of '{this.MaxConcurrentRequests}', and a queued request holds a concurrency permit while it waits for its client's capacity to return; one client out of capacity could therefore hold every permit the process has until its next replenishment; lower this below {nameof(this.MaxConcurrentRequests)} or write 0 to refuse instead of queueing.";
        }
    }
}
