// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.OwnerSettings.Administration;

/// <summary>What erasing an owner did, and whether this process was serving the person it removed.</summary>
/// <param name="OwnerErased">Whether an owner record was there to remove, so a repeat is reported as the no-op it is.</param>
/// <param name="WasServed">Whether the roster this process settled at start holds the owner that was erased.</param>
/// <remarks>
/// The second is reported because it is the one thing an operator has to act on afterwards and nothing else would tell
/// them. The roster is settled once, while the host starts, so a process that was serving the erased owner goes on
/// composing callers against a row that is no longer there and goes on scheduling synchronization for mail accounts
/// that no longer exist — until it is restarted. Erasing an owner the deployment held and did not serve, which is what
/// a file that stopped declaring somebody leaves behind, needs nothing of the sort.
/// </remarks>
internal readonly record struct OwnerErasureOutcome(bool OwnerErased, bool WasServed);
