// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Api;

/// <summary>What a deployment is asked when stored mail is to be brought up to a newer release's properties.</summary>
/// <param name="Account">The account to act on, as the deployment's configuration names it.</param>
/// <param name="Folder">MailFathom's own alias for the one folder to act on, or nothing for every folder the account holds mail in.</param>
/// <remarks>
/// One request shape for both operations, because an operator names the same two things for either and a second record
/// differing in nothing would be two contracts to keep in agreement. Which operation is meant is the route, never a
/// field: a mistyped value must not be the difference between re-reading local bytes and pulling a mailbox over IMAP
/// again.
/// </remarks>
internal sealed record MailboxMaintenanceRequest(string? Account, string? Folder);

/// <summary>What a rewind of one scope would have the next synchronization runs read again.</summary>
/// <param name="Account">The account the assessment is about.</param>
/// <param name="Folder">The normalized alias it was narrowed to, or nothing when it covers the whole account.</param>
/// <param name="StoredEmailCount">How many stored emails the scope holds.</param>
/// <remarks>
/// A count and MailFathom's own names for things. It is what the scope holds rather than what a run would fetch,
/// because the difference between the two is only knowable from a mailbox session and this reads none.
/// </remarks>
internal sealed record MailboxRewindAssessmentResponse(string Account, string? Folder, int StoredEmailCount);

/// <summary>Which of a scope's folders held durable synchronization progress that a rewind discarded.</summary>
/// <param name="Account">The account the rewind ran against.</param>
/// <param name="Folder">The normalized alias it was narrowed to, or nothing when it covered the whole account.</param>
/// <param name="Folders">The aliases whose bindings held progress, ordered and without repeats.</param>
/// <remarks>
/// Aliases rather than remote paths, and no UID, timestamp, or modification sequence. What was discarded is named by
/// MailFathom's own configured names for folders, so an answer says which folders will be read afresh without
/// describing where the mail server keeps them.
/// </remarks>
internal sealed record MailboxRewindResponse(string Account, string? Folder, IReadOnlyList<string> Folders);

/// <summary>What one bounded pass of a re-derivation re-read, and whether the scope still holds mail it has not reached.</summary>
/// <param name="Account">The account the pass ran against.</param>
/// <param name="Folder">The normalized alias it was narrowed to, or nothing when it covered the whole account.</param>
/// <param name="RederivedEmailCount">How many stored emails this pass re-read and wrote metadata for.</param>
/// <param name="UnreadableEmailCount">How many carried MIME no reader could parse, which the pass stepped over.</param>
/// <param name="MissingContentEmailCount">How many no longer had raw MIME to re-read.</param>
/// <param name="EmailsRemain">Whether the scope still holds mail a further pass would reach.</param>
/// <remarks>
/// Counts and nothing a message supplied. What was re-read is a number rather than a list, because a list of what a
/// deployment has just re-read would be a copy of the part of the mailbox the operator asked it to refresh.
/// </remarks>
internal sealed record MailboxRederivationResponse(
    string Account,
    string? Folder,
    int RederivedEmailCount,
    int UnreadableEmailCount,
    int MissingContentEmailCount,
    bool EmailsRemain);
