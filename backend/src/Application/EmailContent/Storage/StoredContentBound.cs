// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>Which of the two stored-content ceilings refused room for a payload.</summary>
/// <remarks>
/// A deployment bounds what its content storage may occupy in total and what any one owner may occupy of that, and the
/// two refusals need different actions: one is raised by giving the instance more disk or a higher ceiling, the other
/// by raising that owner's share or by leaving them to wait while everybody else's mail keeps arriving whole. Naming
/// which was reached is what lets an operator tell "this instance is full" from "this person is at their share".
/// </remarks>
public enum StoredContentBound
{
    /// <summary>Neither ceiling refused, so the payload may be fetched and stored.</summary>
    None = 0,

    /// <summary>The named owner's stored content is at what they are allowed, while the deployment still has room.</summary>
    Owner = 1,

    /// <summary>The deployment's content storage is at its ceiling, whatever any one owner is occupying of it.</summary>
    /// <remarks>
    /// Reported in preference to <see cref="Owner" /> when both are reached, because it is the wider fact: raising an
    /// owner's share would change nothing while the instance itself is full.
    /// </remarks>
    Deployment = 2,
}
