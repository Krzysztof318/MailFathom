// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Observability;

/// <summary>Publishes what one bounded pass of the content move carried, and what it refused to carry.</summary>
/// <remarks>
/// A port rather than a call into a metrics API, for the reason every other publisher here is one: creating instruments
/// is infrastructure, and what the work knows is that a pass began, that a payload moved, and why one did not. Nothing
/// above the adapter can attach a dimension, so no key, no identity, and no part of a message can reach a series.
/// </remarks>
public interface IStoredContentMoveTelemetry
{
    /// <summary>Opens the report of one bounded pass, which is published when the returned scope is disposed.</summary>
    /// <returns>The scope, which the caller must dispose exactly once and inside which the pass runs.</returns>
    IStoredContentMovePassScope BeginPass();
}
