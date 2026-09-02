// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Spam;

/// <summary>Says where one signal came from, so the fact it states can be checked rather than only believed.</summary>
/// <remarks>
/// Provenance travels with every signal rather than with the classification as a whole, because one classification
/// mixes facts of different standing: a header the receiving server wrote, the folder the mailbox filed the message in,
/// and a rule corpus that re-read the message afterwards. A record that named only the deciding stage would leave a
/// reader unable to tell which of them the verdict actually rested on.
/// </remarks>
public sealed record SpamSignalProvenance
{
    /// <summary>The greatest length an origin may carry, which every one MailFathom writes is far inside.</summary>
    /// <remarks>
    /// Origins are header field names, folder aliases, and corpus revisions — all short, and all MailFathom's own or
    /// the mail standard's rather than free text from a message body. The bound exists so a scanner that answered with
    /// something unexpected cannot widen a stored column, and it refuses rather than truncates for that reason.
    /// </remarks>
    public const int MaximumOriginLength = 128;

    private SpamSignalProvenance(SpamSignalSource source, string origin)
    {
        this.Source = source;
        this.Origin = origin;
    }

    /// <summary>Gets where the signal was read from.</summary>
    public SpamSignalSource Source { get; }

    /// <summary>Gets what within that source named the signal: a header field name, a folder alias, or a corpus revision.</summary>
    public string Origin { get; }

    /// <summary>Records that a signal was read out of one of the message's own headers.</summary>
    /// <param name="headerFieldName">The header field the signal was read from.</param>
    /// <returns>The provenance.</returns>
    /// <exception cref="ArgumentException">Thrown when the field name is blank, over-long, or carries a control character.</exception>
    public static SpamSignalProvenance FromMessageHeader(string headerFieldName) =>
        new(SpamSignalSource.MessageHeader, Checked(headerFieldName, nameof(headerFieldName)));

    /// <summary>Records that a signal is the folder the occurrence was stored from.</summary>
    /// <param name="folderAlias">MailFathom's own name for that folder.</param>
    /// <returns>The provenance.</returns>
    /// <exception cref="ArgumentException">Thrown when the alias is blank, over-long, or carries a control character.</exception>
    public static SpamSignalProvenance FromFolderPlacement(string folderAlias) =>
        new(SpamSignalSource.FolderPlacement, Checked(folderAlias, nameof(folderAlias)));

    /// <summary>Records that a signal came from a scanner's rule corpus.</summary>
    /// <param name="corpusRevision">The revision the scanner ran the message under.</param>
    /// <returns>The provenance.</returns>
    /// <exception cref="ArgumentException">Thrown when the revision is blank, over-long, or carries a control character.</exception>
    public static SpamSignalProvenance FromScannerCorpus(string corpusRevision) =>
        new(SpamSignalSource.ScannerCorpus, Checked(corpusRevision, nameof(corpusRevision)));

    /// <summary>Reads back a provenance this system recorded earlier.</summary>
    /// <param name="source">Where the signal was read from.</param>
    /// <param name="origin">What within that source named it.</param>
    /// <returns>The provenance.</returns>
    /// <exception cref="ArgumentException">Thrown when the origin is blank, over-long, or carries a control character.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="source" /> is not a defined member.</exception>
    public static SpamSignalProvenance Restore(SpamSignalSource source, string origin)
    {
        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(
                nameof(source),
                source,
                "A recorded signal names one of the sources this system reads signals from.");
        }

        return new SpamSignalProvenance(source, Checked(origin, nameof(origin)));
    }

    private static string Checked(string origin, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(origin, parameterName);

        var trimmed = origin.Trim();

        if (trimmed.Length > MaximumOriginLength)
        {
            throw new ArgumentException(
                $"A signal origin carries at most {MaximumOriginLength} characters.",
                parameterName);
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("A signal origin cannot contain control characters.", parameterName);
        }

        return trimmed;
    }
}
