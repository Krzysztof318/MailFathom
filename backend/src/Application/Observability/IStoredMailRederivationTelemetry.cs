// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Observability;

/// <summary>Publishes what one segment of a re-derivation did, and what each of its bounded passes did beneath it.</summary>
/// <remarks>
/// <para>
/// A run over a large mailbox is carried by a chain of job attempts, each running as many bounded passes as it is
/// given. One duration for the whole attempt answers nothing an operator can act on, because a segment that took twice
/// as long may have read twice as much mail or read the same mail against a database twice as slow. The nesting is what
/// separates them: the segment says what it covered, and the passes beneath it say what each cost.
/// </para>
/// <para>
/// A port rather than a call into a tracing API, for the reason the folder run's phases are one: starting a span is
/// infrastructure, and the work states that a segment began, that a pass began, and what each of them found. Nothing
/// above the adapter can attach a tag, so a pass reports counts and the scope it walked and nothing about the mail it
/// passed over.
/// </para>
/// </remarks>
public interface IStoredMailRederivationTelemetry
{
    /// <summary>Opens the report of one segment of a run, and publishes it when the returned scope is disposed.</summary>
    /// <param name="accountId">The account whose stored mail the segment walks.</param>
    /// <param name="folderAlias">The one folder of it, or <see langword="null" /> when the segment walks every folder.</param>
    /// <returns>The scope, which the caller must dispose exactly once and inside which the segment's passes run.</returns>
    IStoredMailRederivationRunScope BeginRun(MailAccountId accountId, MailFolderAlias? folderAlias);
}
