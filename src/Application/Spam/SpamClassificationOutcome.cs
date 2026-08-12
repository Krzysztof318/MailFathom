// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Spam;

/// <summary>How one attempt to classify an occurrence ended.</summary>
/// <remarks>
/// Every member but <see cref="Classified" /> is a reason nothing was recorded, and none of them is a failure: each is
/// an answer whichever caller asked can act on, and repeating the attempt is safe in all of them. What a job built on
/// this needs to distinguish is whether a retry could ever change the answer, which is why an absent occurrence and
/// absent content are separate members.
/// </remarks>
public enum SpamClassificationOutcome
{
    /// <summary>A verdict was reached and recorded.</summary>
    Classified = 0,

    /// <summary>Classification is switched off, so nothing was read and nothing was recorded.</summary>
    Disabled = 1,

    /// <summary>The occurrence is in a folder the configured scope does not cover.</summary>
    OutsideConfiguredScope = 2,

    /// <summary>The occurrence already carried a classification and this attempt was not asked to replace it.</summary>
    AlreadyClassified = 3,

    /// <summary>Nothing is stored under that identifier, which is what an expunged message reaches.</summary>
    OccurrenceMissing = 4,

    /// <summary>The occurrence has no local content to read, so no header could be observed.</summary>
    /// <remarks>
    /// A message whose size exceeded the configured fetch limit is stored with its metadata and no content at all, and a
    /// message can be classified before synchronization has fetched its body. Both are this, and both become
    /// classifiable later without the message being re-fetched for classification's sake.
    /// </remarks>
    ContentUnavailable = 5,
}
