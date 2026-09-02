// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Resilience;

namespace MailFathom.Infrastructure.Resilience;

/// <summary>Supplies the starting budget of each dependency class before configuration overrides it.</summary>
/// <remarks>
/// A shared default across all classes would be wrong for most of them: a database command that waits thirty seconds
/// for one attempt has already failed the request it serves, and an email delivery repeated as freely as a mailbox
/// read is visible in the recipient's inbox. The table therefore states one deliberate budget per class, and
/// configuration only has to name the values a deployment disagrees with.
/// </remarks>
internal static class OutboundDependencyResilienceDefaults
{
    /// <summary>Overwrites every setting of a freshly created options instance with the defaults of one dependency class.</summary>
    /// <param name="options">The options instance the configuration binder will bind onto afterwards.</param>
    /// <param name="dependency">The dependency class whose budget applies.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="dependency" /> is not a defined member.</exception>
    internal static void ApplyTo(OutboundDependencyResilienceOptions options, OutboundDependency dependency)
    {
        ArgumentNullException.ThrowIfNull(options);

        var defaults = CreateFor(dependency);

        options.MaxAttempts = defaults.MaxAttempts;
        options.BaseDelay = defaults.BaseDelay;
        options.MaxDelay = defaults.MaxDelay;
        options.AttemptTimeout = defaults.AttemptTimeout;
        options.TotalTimeout = defaults.TotalTimeout;
        options.CircuitBreakerFailureRatio = defaults.CircuitBreakerFailureRatio;
        options.CircuitBreakerMinimumThroughput = defaults.CircuitBreakerMinimumThroughput;
        options.CircuitBreakerSamplingDuration = defaults.CircuitBreakerSamplingDuration;
        options.CircuitBreakerBreakDuration = defaults.CircuitBreakerBreakDuration;
        options.ConcurrencyLimit = defaults.ConcurrencyLimit;
    }

    private static OutboundDependencyResilienceOptions CreateFor(OutboundDependency dependency) => dependency switch
    {
        // Connections are the scarce resource on an IMAP server and a rejected credential is terminal, so the class
        // reconnects patiently and keeps few establishments in flight.
        OutboundDependency.MailboxSessionEstablishment => new OutboundDependencyResilienceOptions
        {
            MaxAttempts = 3,
            BaseDelay = TimeSpan.FromSeconds(2),
            MaxDelay = TimeSpan.FromSeconds(30),
            AttemptTimeout = TimeSpan.FromSeconds(30),
            TotalTimeout = TimeSpan.FromMinutes(2),
            CircuitBreakerFailureRatio = 0.5,
            CircuitBreakerMinimumThroughput = 5,
            CircuitBreakerSamplingDuration = TimeSpan.FromSeconds(60),
            CircuitBreakerBreakDuration = TimeSpan.FromSeconds(30),
            ConcurrencyLimit = 4,
        },

        // A single fetch can stream a large message, so one attempt is allowed to take longer than a connect.
        OutboundDependency.MailboxDataRetrieval => new OutboundDependencyResilienceOptions
        {
            MaxAttempts = 3,
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(15),
            AttemptTimeout = TimeSpan.FromSeconds(60),
            TotalTimeout = TimeSpan.FromMinutes(3),
            CircuitBreakerFailureRatio = 0.5,
            CircuitBreakerMinimumThroughput = 10,
            CircuitBreakerSamplingDuration = TimeSpan.FromSeconds(30),
            CircuitBreakerBreakDuration = TimeSpan.FromSeconds(15),
            ConcurrencyLimit = 8,
        },

        // A repeated submission is visible to the recipient, so delivery gets the smallest attempt budget of any
        // class and waits long enough between attempts for a greylisting server to admit the message.
        OutboundDependency.EmailDelivery => new OutboundDependencyResilienceOptions
        {
            MaxAttempts = 2,
            BaseDelay = TimeSpan.FromSeconds(5),
            MaxDelay = TimeSpan.FromSeconds(60),
            AttemptTimeout = TimeSpan.FromSeconds(60),
            TotalTimeout = TimeSpan.FromMinutes(3),
            CircuitBreakerFailureRatio = 0.5,
            CircuitBreakerMinimumThroughput = 5,
            CircuitBreakerSamplingDuration = TimeSpan.FromSeconds(60),
            CircuitBreakerBreakDuration = TimeSpan.FromSeconds(60),
            ConcurrencyLimit = 4,
        },

        // The database is local and its transient failures clear in milliseconds; a long wait here only holds a
        // request open while the connection pool recovers behind it.
        OutboundDependency.DatabaseCommandExecution => new OutboundDependencyResilienceOptions
        {
            MaxAttempts = 3,
            BaseDelay = TimeSpan.FromMilliseconds(200),
            MaxDelay = TimeSpan.FromSeconds(2),
            AttemptTimeout = TimeSpan.FromSeconds(15),
            TotalTimeout = TimeSpan.FromSeconds(30),
            CircuitBreakerFailureRatio = 0.5,
            CircuitBreakerMinimumThroughput = 20,
            CircuitBreakerSamplingDuration = TimeSpan.FromSeconds(30),
            CircuitBreakerBreakDuration = TimeSpan.FromSeconds(5),
            ConcurrencyLimit = 32,
        },

        // Model inference is slow by nature and providers rate-limit aggressively, so the class tolerates a long
        // attempt and keeps few invocations in flight.
        OutboundDependency.AiProviderInvocation => new OutboundDependencyResilienceOptions
        {
            MaxAttempts = 3,
            BaseDelay = TimeSpan.FromSeconds(2),
            MaxDelay = TimeSpan.FromSeconds(30),
            AttemptTimeout = TimeSpan.FromSeconds(120),
            TotalTimeout = TimeSpan.FromMinutes(5),
            CircuitBreakerFailureRatio = 0.5,
            CircuitBreakerMinimumThroughput = 5,
            CircuitBreakerSamplingDuration = TimeSpan.FromSeconds(60),
            CircuitBreakerBreakDuration = TimeSpan.FromSeconds(30),
            ConcurrencyLimit = 4,
        },

        // A token endpoint is a fast, highly available HTTP service, and its answer gates a mailbox session that is
        // already waiting, so the budget is short on every axis. The attempt timeout is deliberately well inside the
        // session establishment timeout that encloses it: a token request that hangs must fail before the connection
        // attempt it serves does, or the operator sees a mailbox timeout whose cause was the authorization server.
        OutboundDependency.MailAuthorizationServerInvocation => new OutboundDependencyResilienceOptions
        {
            MaxAttempts = 3,
            BaseDelay = TimeSpan.FromMilliseconds(500),
            MaxDelay = TimeSpan.FromSeconds(5),
            AttemptTimeout = TimeSpan.FromSeconds(10),
            TotalTimeout = TimeSpan.FromSeconds(30),
            CircuitBreakerFailureRatio = 0.5,
            CircuitBreakerMinimumThroughput = 10,
            CircuitBreakerSamplingDuration = TimeSpan.FromSeconds(60),
            CircuitBreakerBreakDuration = TimeSpan.FromSeconds(30),
            ConcurrencyLimit = 8,
        },

        // An object-storage endpoint answers a single request per operation and rate-limits with a status rather than by
        // going quiet, so the class waits briefly and gives up quickly: the attempt timeout covers one request against a
        // service that is either reachable or not, and the total timeout leaves room for two backoffs inside it. The
        // in-flight limit is well below the database's, because a request to a remote endpoint costs latency the local
        // connection pool does not, and far below what a bucket would serve, because a deployment's own budget is the
        // point.
        OutboundDependency.ObjectStorageInvocation => new OutboundDependencyResilienceOptions
        {
            MaxAttempts = 3,
            BaseDelay = TimeSpan.FromMilliseconds(500),
            MaxDelay = TimeSpan.FromSeconds(10),
            AttemptTimeout = TimeSpan.FromSeconds(30),
            TotalTimeout = TimeSpan.FromMinutes(2),
            CircuitBreakerFailureRatio = 0.5,
            CircuitBreakerMinimumThroughput = 10,
            CircuitBreakerSamplingDuration = TimeSpan.FromSeconds(60),
            CircuitBreakerBreakDuration = TimeSpan.FromSeconds(15),
            ConcurrencyLimit = 16,
        },

        _ => throw new ArgumentOutOfRangeException(
            nameof(dependency),
            dependency,
            "No resilience budget is defined for this outbound dependency class."),
    };
}
