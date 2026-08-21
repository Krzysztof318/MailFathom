// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Maintenance;

/// <summary>Summarizes one bounded pass of the re-derivation.</summary>
/// <param name="RederivedEmailCount">How many stored emails were re-read and had their metadata written.</param>
/// <param name="UnreadableEmailCount">How many stored emails carried MIME no reader could parse, which the pass stepped over.</param>
/// <param name="MissingContentEmailCount">How many stored emails no longer had raw MIME to re-read.</param>
/// <param name="EmailsRemain">Whether the scope still holds mail a further pass would reach.</param>
/// <remarks>
/// Every field is a count. Nothing derived from a message — no subject, address, or fragment of body text — belongs in
/// a result an operator's terminal prints and a deployment logs.
/// </remarks>
public sealed record StoredMailRederivationPass(
    int RederivedEmailCount,
    int UnreadableEmailCount,
    int MissingContentEmailCount,
    bool EmailsRemain);
