// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Outbox;

/// <summary>Bounds one delivery pass: how much it takes, how long it holds it, how long one send may run, and how often a transient failure is retried.</summary>
/// <remarks>
/// <para>
/// The ordering between the two durations is the whole reason this is validated rather than passed as loose values. An
/// attempt has to be cancelled before its lease can expire underneath it, because a lease that ran out while its holder
/// was still transmitting is a second attempt taking a message the first may already have sent — so a timeout at or
/// above the lease duration is refused rather than warned about.
/// </para>
/// <para>
/// The attempt bound and the two retry delays are the outbox's own budget for offering a message again, and there is
/// exactly one of them. What an attempt calls is already retried inside itself by the delivery dependency's resilience
/// pipeline, which repeats a submission server's explicit temporary rejection and nothing else; a second bound at this
/// level would multiply against that one instead of bounding anything.
/// </para>
/// </remarks>
public sealed record MailOutboxSettings
{
    private MailOutboxSettings(
        int maxDeliveriesPerPass,
        TimeSpan leaseDuration,
        TimeSpan attemptTimeout,
        int maxAttempts,
        TimeSpan retryBaseDelay,
        TimeSpan retryMaxDelay,
        TimeSpan allowedLateness)
    {
        this.MaxDeliveriesPerPass = maxDeliveriesPerPass;
        this.LeaseDuration = leaseDuration;
        this.AttemptTimeout = attemptTimeout;
        this.MaxAttempts = maxAttempts;
        this.RetryBaseDelay = retryBaseDelay;
        this.RetryMaxDelay = retryMaxDelay;
        this.AllowedLateness = allowedLateness;
    }

    /// <summary>Gets the greatest number of queued sends one pass claims.</summary>
    /// <remarks>
    /// A send is a conversation with a submission server rather than a row to process, so the useful values are small.
    /// What the bound leaves is claimed by the next pass, oldest first, so nothing is dropped by it.
    /// </remarks>
    public int MaxDeliveriesPerPass { get; }

    /// <summary>Gets how long a claim holds each record it takes.</summary>
    public TimeSpan LeaseDuration { get; }

    /// <summary>Gets how long one send may be attempted for before it is cancelled.</summary>
    public TimeSpan AttemptTimeout { get; }

    /// <summary>Gets how many attempts one send may be handed out for before a transient failure ends it.</summary>
    /// <remarks>A value of <c>1</c> leaves no retry at all, so the first transient refusal is terminal.</remarks>
    public int MaxAttempts { get; }

    /// <summary>Gets the delay the first retry is drawn around, from which the doubling grows.</summary>
    public TimeSpan RetryBaseDelay { get; }

    /// <summary>Gets the ceiling a grown retry delay never exceeds.</summary>
    public TimeSpan RetryMaxDelay { get; }

    /// <summary>Gets how long after the time a send was written for this deployment will still deliver it.</summary>
    /// <remarks>
    /// It bounds nothing an ordinary send meets, because a send that named no time is never late. What it decides is
    /// the one case a held message reaches after the moment it was written for — a process that was down, a queue that
    /// was full, a provider that was unreachable for the whole window — where delivering and dropping are both wrong
    /// answers and the deployment says which side of them a message falls on.
    /// </remarks>
    public TimeSpan AllowedLateness { get; }

    /// <summary>States the bounds one pass runs under.</summary>
    /// <param name="maxDeliveriesPerPass">The greatest number of queued sends one pass claims.</param>
    /// <param name="leaseDuration">How long a claim holds each record it takes.</param>
    /// <param name="attemptTimeout">How long one send may be attempted for.</param>
    /// <param name="maxAttempts">How many attempts one send may be handed out for.</param>
    /// <param name="retryBaseDelay">The delay the first retry is drawn around.</param>
    /// <param name="retryMaxDelay">The ceiling a grown retry delay never exceeds.</param>
    /// <param name="allowedLateness">How long after the time a send was written for this deployment will still deliver it.</param>
    /// <returns>The validated bounds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a count is not positive, when a duration is not positive, when <paramref name="attemptTimeout" /> is not shorter than <paramref name="leaseDuration" />, or when <paramref name="retryMaxDelay" /> is below <paramref name="retryBaseDelay" />.</exception>
    public static MailOutboxSettings Create(
        int maxDeliveriesPerPass,
        TimeSpan leaseDuration,
        TimeSpan attemptTimeout,
        int maxAttempts,
        TimeSpan retryBaseDelay,
        TimeSpan retryMaxDelay,
        TimeSpan allowedLateness)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDeliveriesPerPass);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(attemptTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(attemptTimeout, leaseDuration);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retryBaseDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryMaxDelay, retryBaseDelay);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(allowedLateness, TimeSpan.Zero);

        return new MailOutboxSettings(
            maxDeliveriesPerPass,
            leaseDuration,
            attemptTimeout,
            maxAttempts,
            retryBaseDelay,
            retryMaxDelay,
            allowedLateness);
    }
}
