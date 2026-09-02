// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.Primitives;

namespace MailFathom.Host.Configuration;

/// <summary>Supplies the settings a new operation should run with.</summary>
/// <typeparam name="TSettings">The bound settings group.</typeparam>
/// <remarks>
/// <para>
/// Consumers read this rather than <c>IOptions</c> or <c>IOptionsMonitor</c> because a bound snapshot is not
/// automatically a usable one: a reload can carry a secret reference that resolves to nothing, and adopting it would
/// take a running deployment offline. Only a snapshot whose references have all been proven usable is published here.
/// </para>
/// <para>
/// Read the settings once per operation — one synchronization run, one connection attempt — and use that instance for
/// its duration. Re-reading mid-operation is what would apply a rotation to work already in flight, which is the one
/// thing the reload contract promises never happens. Where an operation spans several services, the snapshot is
/// registered as a scoped dependency so the scope, not each service, decides which one they share.
/// </para>
/// <para>
/// That publication rule, together with the surface it withholds, is why this is declared rather than left to the
/// framework: <c>IOptionsMonitor</c> is deliberately narrowed — no lookup by name and no way to observe a candidate
/// that failed to resolve — and what it returns follows a rule no framework options type applies. Its token rises only
/// after a usable snapshot has been published.
/// </para>
/// </remarks>
internal interface ISettingsSnapshot<out TSettings>
    where TSettings : class
{
    /// <summary>Gets the most recent snapshot whose secret-bearing settings were all proven usable.</summary>
    TSettings Current { get; }

    /// <summary>Gets a token that changes when a newer usable snapshot is published.</summary>
    IChangeToken GetReloadToken();
}
