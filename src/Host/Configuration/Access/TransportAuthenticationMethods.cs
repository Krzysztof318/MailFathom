// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Access;

/// <summary>What a deployment accepts of a client before it serves an MCP request.</summary>
/// <remarks>
/// <para>
/// A set rather than a choice, because the two methods answer for different kinds of caller and a deployment commonly
/// has both. An API key belongs to a client the operator provisioned — a scheduled job, a workstation — while an OAuth
/// token belongs to a person an external authorization server signed in. Forcing one off so the other can be on would
/// mean either issuing a shared key to every user or standing up an authorization server to reach a cron job.
/// </para>
/// <para>
/// The members are the methods themselves, so <see cref="None" /> is the absence of all of them rather than a third
/// method. The unauthenticated posture is therefore what remains when nothing is turned on. That is deliberate: the
/// endpoint is off by default, so reaching this setting at all means an operator wrote <c>Enabled</c> down, and what
/// they get for writing nothing beside it is announced loudly at startup rather than quietly assumed to be intended.
/// The spellings that would otherwise slip past — a misspelled key, a value naming no method — are caught before that,
/// because the section binds strictly and unknown bits are refused.
/// </para>
/// <para>
/// This is a flags enumeration, which the repository's contiguous-value rule exempts. Every member still carries an
/// explicit value that is never reordered or reused; the values are powers of two so a set of them is one number, which
/// is what lets an operator write <c>ApiKey, OAuth</c> as a single configuration value rather than as a collection whose
/// elements a binder can drop one at a time.
/// </para>
/// </remarks>
[Flags]
internal enum TransportAuthenticationMethods
{
    /// <summary>No transport authentication at all, which is a posture for a local or network-isolated deployment and warns at startup.</summary>
    None = 0,

    /// <summary>A request presents one of the configured API keys as an HTTP <c>Bearer</c> credential.</summary>
    ApiKey = 1,

    /// <summary>A request presents an access token one of the configured external authorization servers issued, as an HTTP <c>Bearer</c> credential.</summary>
    OAuth = 2,
}
