// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Host.Configuration;

/// <summary>Supplies the mail synchronization settings a new operation should run with.</summary>
/// <remarks>
/// <para>
/// Consumers read this rather than <c>IOptions</c> or <c>IOptionsMonitor</c> because a bound snapshot is not
/// automatically a usable one: a reload can carry a secret reference that resolves to nothing, and adopting it would
/// take a running deployment offline. Only a snapshot whose references have all been resolved is published here.
/// </para>
/// <para>
/// Read the settings once per operation — one synchronization run, one connection attempt — and use that instance for
/// its duration. Re-reading mid-operation is what would apply a rotation to work already in flight, which is the one
/// thing the reload contract promises never happens.
/// </para>
/// </remarks>
internal interface IMailSynchronizationSettingsReader
{
    /// <summary>Gets the most recent snapshot whose secret-bearing settings were all proven usable.</summary>
    MailSynchronizationOptions Current { get; }
}
