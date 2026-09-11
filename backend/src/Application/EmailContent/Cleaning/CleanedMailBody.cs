// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering.Document;

namespace MailFathom.Application.EmailContent.Cleaning;

/// <summary>What came of asking for one body to be cleaned.</summary>
public enum MailBodyCleaningOutcome
{
    /// <summary>A cleaning was proposed, checked against the document, and applied.</summary>
    Cleaned = 0,

    /// <summary>There was nothing to clean: the body carried no reduced document, or the document carried no block.</summary>
    NothingToClean = 1,

    /// <summary>This deployment declared no chat endpoint, or its operator left the pass off.</summary>
    NotActivated = 2,

    /// <summary>The deployment has spent what it allows a provider for the current period.</summary>
    AllowanceExhausted = 3,

    /// <summary>The endpoint did not answer, twice where it was asked twice.</summary>
    ProviderUnavailable = 4,

    /// <summary>What came back did not describe the document, and the second attempt did not either.</summary>
    AnswerRejected = 5,
}

/// <summary>One message's body as the third rendering, or the ordinary reduced one and why it is what arrived.</summary>
/// <param name="Outcome">What came of the cleaning, which is what a reader is told where it did not happen.</param>
/// <param name="Document">
/// The document to draw: the cleaned one where the outcome is <see cref="MailBodyCleaningOutcome.Cleaned" />, the
/// ordinary reduced one in every other case, and <see langword="null" /> only where the body carried no document at all —
/// which is the same answer the body route gives for that message.
/// </param>
/// <remarks>
/// <para>
/// <b>The fallback travels with the reason rather than instead of it.</b> A reader whose cleaning did not happen is shown
/// the reduced body and told so, because a view that silently became a different view reads as a setting that stopped
/// working — and a pane with nothing in it is the one outcome this type exists to rule out.
/// </para>
/// <para>
/// The cleaned document is the reduced one with blocks absent and nothing else changed. Every block it keeps is the block
/// the reduced view would have drawn, and the counts it carries are still the whole body's: what the message asked to
/// fetch from somebody else's server does not become untrue because a footer was dropped.
/// </para>
/// </remarks>
public sealed record CleanedMailBody(MailBodyCleaningOutcome Outcome, MailDocument? Document);
