// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Connections;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Reports whatever a test needs the database adapter to say about a candidate.</summary>
/// <remarks>Whether a connection string parses is the adapter's own behaviour and is covered by <c>Infrastructure.UnitTests</c>; host tests only need to know that the verdict reaches the published snapshot.</remarks>
internal sealed class StubDatabaseConnectionSettingsValidator : IDatabaseConnectionSettingsValidator
{
    public IReadOnlyList<DatabaseConnectionConfigurationFailure> Failures { get; set; } = [];

    public PostgresConnectionSettings? LastCandidate { get; private set; }

    public Task<IReadOnlyList<DatabaseConnectionConfigurationFailure>> FindConfigurationFailuresAsync(
        PostgresConnectionSettings candidate,
        CancellationToken cancellationToken)
    {
        this.LastCandidate = candidate;

        return Task.FromResult(this.Failures);
    }
}
