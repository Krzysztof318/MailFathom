// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.Providers;

/// <summary>One header a request carries beside whatever the credential writes, already resolved for that request.</summary>
/// <param name="name">The field name, which an operator declared and startup proved to be one.</param>
/// <param name="value">The resolved value.</param>
/// <remarks>
/// <para>
/// The shape a gateway in front of several models usually asks for: a tenant, a project, or a routing key that decides
/// which model answers, sent beside the bearer credential rather than instead of it. The value is declared as a secret
/// reference like every other credential here, so it is resolved per request and released with the credential that
/// carries it.
/// </para>
/// <para>
/// Deliberately not a place to write an authorization scheme. What proves the deployment's identity is
/// <see cref="ProviderEndpointCredential" />, and startup refuses a header naming what the client construction already
/// writes, so a second credential cannot arrive here and silently win or lose against the declared one.
/// </para>
/// <para>
/// A class rather than a record, for the reason <see cref="ProviderEndpointCredential" /> is one: a record synthesizes
/// a <see cref="object.ToString" /> printing every positional member, so an instance interpolated into a log line or an
/// exception message would put resolved secret material in clear text. Nothing here needs value equality.
/// </para>
/// </remarks>
public sealed class ProviderEndpointHeader(string name, string value)
{
    /// <summary>The field name the request writes.</summary>
    public string Name { get; } = name;

    /// <summary>The resolved value, which is secret material and never reaches a log.</summary>
    public string Value { get; } = value;
}
