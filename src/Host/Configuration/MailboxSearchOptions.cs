// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Emails;

namespace MailFathom.Host.Configuration;

/// <summary>Configures how much of a message one lexical search result may show.</summary>
/// <remarks>
/// These are the deployment's data-minimization bounds on search, not a display preference: together they decide how
/// much mail content one query can draw out of a mailbox. They are configuration rather than request input because a
/// caller who could raise them could lift the control, and the useful values follow from how a deployment's mail is
/// written rather than from what any single request wants.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailboxSearchOptions
{
    /// <summary>Gets or sets how many highlighted extracts one search result may carry.</summary>
    [Range(1, EmailSearchSnippetBounds.MaximumSnippetsPerEmail)]
    public int SnippetsPerEmail { get; set; } = EmailSearchSnippetBounds.Default.SnippetsPerEmail;

    /// <summary>Gets or sets how many words one highlighted extract may carry.</summary>
    /// <remarks>The floor exists because a shorter extract shows a matched word with nothing around it, which tells a reader nothing the relevance rank does not already.</remarks>
    [Range(EmailSearchSnippetBounds.MinimumWordsPerSnippet, EmailSearchSnippetBounds.MaximumWordsPerSnippet)]
    public int WordsPerSnippet { get; set; } = EmailSearchSnippetBounds.Default.WordsPerSnippet;
}
