// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access;

/// <summary>Names the one owner every mail account this deployment was configured with belongs to.</summary>
/// <remarks>
/// <para>
/// A configured mail account names no owner, so nothing about a configuration file could say which of several owners a
/// declared account belongs to. While accounts are declared there, a deployment therefore holds exactly one owner
/// record, and this is that owner. The invariant is established before the first request rather than assumed: a host
/// that cannot establish it does not finish starting, so a request is never answered against a deployment where the
/// question has no answer.
/// </para>
/// <para>
/// It is what makes an admitted caller a caller acting for somebody. A credential is configured today and carries no
/// owner of its own, so every caller a mail-reading surface admits is acting for this one; when credentials become
/// records of their own, the owner comes off the credential and this port stops being what answers for a caller.
/// </para>
/// <para>
/// The answer is a value rather than a read, because it is settled once and consulted per request. Nothing here reaches
/// a database while a request is being served, and nothing about a request can change the answer.
/// </para>
/// </remarks>
public interface IDeploymentMailOwnerSource
{
    /// <summary>Gets the owner this deployment's configured mail accounts belong to.</summary>
    MailOwnerId Owner { get; }
}
