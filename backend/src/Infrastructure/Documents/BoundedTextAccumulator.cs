// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Infrastructure.Documents;

/// <summary>Gathers extracted characters and abandons the read when they pass the output ceiling.</summary>
/// <remarks>
/// The ceiling is refused rather than truncated to. A document cut off at a limit and handed back as its text reads
/// exactly like a document that said only that much, and a search over the second half would answer that the words are
/// not there — which is the shape of silence this whole port exists to replace with a reason.
/// </remarks>
internal sealed class BoundedTextAccumulator(int maxCharacters)
{
    private readonly StringBuilder text = new();

    /// <summary>Gets how many characters have been gathered.</summary>
    public int Length => this.text.Length;

    /// <summary>Adds a run of extracted characters.</summary>
    /// <param name="value">The characters, which may be empty.</param>
    /// <exception cref="AttachmentTextExtractionStoppedException">Thrown when the addition would pass the output ceiling.</exception>
    public void Add(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        if (this.text.Length + value.Length > maxCharacters)
        {
            throw new AttachmentTextExtractionStoppedException(AttachmentTextExtractionOutcome.ExtractedTextTooLarge);
        }

        this.text.Append(value);
    }

    /// <summary>Ends the current line, unless nothing has been gathered or a line break already ends what has.</summary>
    /// <exception cref="AttachmentTextExtractionStoppedException">Thrown when the break would pass the output ceiling.</exception>
    public void EndLine()
    {
        if (this.text.Length == 0 || this.text[^1] == '\n')
        {
            return;
        }

        this.Add("\n");
    }

    /// <summary>Reads back everything gathered, less the trailing break this accumulator itself inserted.</summary>
    /// <returns>The gathered text, with no trailing line break.</returns>
    /// <remarks>
    /// For a reader that closes a paragraph or a page with <see cref="EndLine" />, the last of those breaks separates
    /// the document's text from nothing and is this accumulator's own rather than the document's. A reader that
    /// gathers characters straight out of a file inserts none, so every trailing break there is one somebody typed and
    /// <see cref="ToTextAsGathered" /> is what reads it back.
    /// </remarks>
    public string ToText() => this.ToTextAsGathered().TrimEnd('\n');

    /// <summary>Reads back everything gathered, exactly as it was added.</summary>
    /// <returns>The gathered text, including any trailing line break.</returns>
    public string ToTextAsGathered() => this.text.ToString();
}
