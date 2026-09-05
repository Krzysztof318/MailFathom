// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.IO.Compression;
using System.Xml;
using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Infrastructure.Documents;

/// <summary>Opens and walks one XML part of a package archive, under every bound that applies to reading one.</summary>
/// <remarks>
/// Both package families are zip archives of XML, so how a part is opened and how it is walked is the same question for
/// both — and it is the question two of this adapter's guards live in: the depth ceiling and the cancellation observed
/// between elements. Keeping them here is what makes them one decision rather than two copies, so a correction to
/// either cannot land in one reader and miss the other with nothing failing.
/// </remarks>
internal sealed class BoundedArchivePartReader(AttachmentTextExtractionOptions options)
{
    /// <summary>Builds the settings every XML part in this adapter is read under.</summary>
    /// <returns>Settings that resolve no entity and fetch nothing.</returns>
    /// <remarks>
    /// The two properties are the whole of the external-entity answer and they are set explicitly rather than left to a
    /// framework default, because a default is a decision somebody else may revise. <c>Prohibit</c> refuses a document
    /// type declaration outright, which is where an entity would have to be declared, and a null resolver leaves
    /// nothing able to fetch a resource even if one were.
    /// </remarks>
    public static XmlReaderSettings PartReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        CloseInput = false,
    };

    /// <summary>Opens one archive part under the container's shared inflation budget.</summary>
    /// <param name="entry">The part to open.</param>
    /// <param name="budget">What the whole container has left to inflate to.</param>
    /// <returns>A reader over the part.</returns>
    public XmlReader OpenPart(ZipArchiveEntry entry, DecompressionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(budget);

        return XmlReader.Create(
            new BoundedInflationStream(
                entry.Open(),
                budget.HonestCompressedLength(entry.CompressedLength),
                budget,
                options.MaxDecompressionRatio),
            PartReaderSettings());
    }

    /// <summary>Advances a reader one node, refusing an element tree nested past the configured depth.</summary>
    /// <param name="reader">The part being walked.</param>
    /// <param name="cancellationToken">Cancels the walk between elements, which is where the timeout is observed.</param>
    /// <returns><see langword="true" /> while the part has more to read; otherwise <see langword="false" />.</returns>
    /// <exception cref="AttachmentTextExtractionStoppedException">Thrown when the element depth is passed.</exception>
    public bool ReadNode(XmlReader reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);

        cancellationToken.ThrowIfCancellationRequested();

        if (!reader.Read())
        {
            return false;
        }

        if (reader.Depth > options.MaxElementDepth)
        {
            throw new AttachmentTextExtractionStoppedException(AttachmentTextExtractionOutcome.ContainerBoundExceeded);
        }

        return true;
    }
}
