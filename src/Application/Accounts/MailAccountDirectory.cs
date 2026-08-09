// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Accounts;

/// <summary>What this deployment serves, as a caller deciding which mailbox to ask about reads it.</summary>
/// <param name="SynchronizationEnabled">Whether the deployment refreshes the local copy of these accounts at all.</param>
/// <param name="Accounts">The served accounts, ordered by identifier, empty when the deployment serves none.</param>
/// <remarks>
/// The two answer different halves of one question. The accounts say which mailboxes can be read and what to name them,
/// and the switch says whether what is read is being kept up to date — a deployment with synchronization off answers
/// every query from a copy that stops advancing, and nothing in the per-folder timestamps says why it stopped.
/// </remarks>
public sealed record MailAccountDirectory(
    bool SynchronizationEnabled,
    IReadOnlyList<DescribedMailAccount> Accounts);
