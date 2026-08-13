// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Observability;

/// <summary>A measurement of staged work that is only true once the session holding it reaches an ending.</summary>
/// <remarks>
/// Work staged in a local write transaction is not work the deployment kept. An optimistic-concurrency retry runs the
/// whole staging body again in a fresh session, so a measurement published where the work happens counts the attempt
/// that lost the race as though it had landed — and it inflates exactly the deployment under contention, which is the
/// one an operator is reading these instruments about. Holding the measurement until the session says which ending it
/// reached is what keeps the durable count durable and still leaves the discarded attempt visible as its own outcome.
/// </remarks>
internal interface ISessionScopedMeasurement
{
    /// <summary>Publishes the held measurement now that the session's ending is known.</summary>
    /// <param name="sessionCommitted">Whether the session that staged the work durably committed.</param>
    void PublishAfterSession(bool sessionCommitted);
}
