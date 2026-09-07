// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text;
using System.Xml;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;

namespace MailFathom.AI.Retrieval;

/// <summary>Writes retrieved mail into the envelope a model reads it inside.</summary>
/// <remarks>
/// <para>
/// Mail is written by strangers, so an extract of it is quoted evidence rather than something the run said. The envelope
/// is what says so structurally: every extract sits inside an element of its own, and the model is told once — in the
/// system instruction rather than here — that everything within the envelope is data. A formatter that concatenated
/// extracts into prose would have given that up before the model read a character, because at that point a message
/// saying "ignore the previous instructions" is written in the same voice as the instruction it is imitating.
/// </para>
/// <para>
/// Nothing a message contains can end an element or open one, because every value is written through an
/// <see cref="XmlWriter" /> that escapes what would: a passage whose text closes the envelope and opens a fake
/// instruction arrives as that text, visible and inert. That is why the escaping is the writer's rather than a
/// replacement table of this file's own — a delimiter this formatter learns to write is escaped by the same mechanism on
/// the day it is added.
/// </para>
/// <para>
/// Each extract carries the identity it was retrieved under, unchanged: the stable local identifier an answer cites, and
/// the account and folder alias it was read from. An answer that cannot say which message a claim came from cannot be
/// checked, and the identity is what survives formatting to make that possible.
/// </para>
/// <para>
/// A file the message carried is written in an element of its own, carrying the file and the page it was read from, and
/// a model's account of a picture is written in a third element again. The three are separated because they were
/// written by three different parties — the person who typed the message, the person who wrote the file, and nobody at
/// all — and an envelope that flattened them would let a model report a machine's guess as something somebody said.
/// </para>
/// <para>
/// Formatting is a pure function of the passages, so the whole envelope is decidable without a provider, and the output
/// is somebody's mail: it is written into a request and never into a log, a span, or an exporter.
/// </para>
/// </remarks>
internal static class RetrievedMailContextFormatter
{
    /// <summary>Names the element every retrieved extract of one lookup is enclosed in.</summary>
    internal const string RetrievalElementName = "retrieved-mail";

    /// <summary>Names the attribute saying that the run may be handed no more mail than this envelope already carries.</summary>
    /// <remarks>
    /// Written on the envelope rather than left for the model to infer from a short result, because the two states it
    /// separates look identical from the inside: a lookup that found little and a lookup whose findings this run had no
    /// allowance left to send. Only the second one means asking again buys nothing.
    /// </remarks>
    internal const string RetrievalLimitReachedAttributeName = "retrieval-limit-reached";

    /// <summary>Names the attribute saying how the mail in this envelope was ranked.</summary>
    /// <remarks>
    /// Written per lookup rather than into the tool's description, because which ranking answered is a fact about the
    /// instance at the moment of the call: one whose embedding provider is refusing ranks lexically until it is not, and
    /// a description fixed at build time would tell the model the opposite for the whole of that stretch. It is what
    /// decides how a further query is worth wording, which is why the model is given it at all.
    /// </remarks>
    internal const string RetrievalModeAttributeName = "retrieval-mode";

    /// <summary>The value <see cref="RetrievalModeAttributeName" /> carries for a ranking over the words the mail is written in.</summary>
    internal const string LexicalRetrievalMode = "lexical";

    /// <summary>The value <see cref="RetrievalModeAttributeName" /> carries for a ranking that fuses words and meaning.</summary>
    internal const string HybridRetrievalMode = "hybrid";

    /// <summary>Names the element a lookup the search use case refused is reported in.</summary>
    /// <remarks>
    /// A document of its own rather than an empty envelope, because the two say opposite things: an empty envelope
    /// reports a mailbox that held no match, and this reports a lookup that never ran. A model told the first about the
    /// second concludes the mail does not exist and stops asking.
    /// </remarks>
    internal const string RefusalElementName = "search-refused";

    /// <summary>Names the attribute carrying which argument of the lookup was refused.</summary>
    internal const string RefusedFilterAttributeName = "argument";

    /// <summary>Names the element one retrieved message occupies.</summary>
    internal const string MessageElementName = "message";

    /// <summary>Names the attribute carrying the stable local identifier an answer cites a message by.</summary>
    internal const string MessageIdAttributeName = "id";

    /// <summary>Names the attribute carrying the account the message was read from.</summary>
    internal const string AccountAttributeName = "account";

    /// <summary>Names the attribute carrying the folder alias the message was read from.</summary>
    internal const string FolderAttributeName = "folder";

    /// <summary>Names the attribute carrying when the message was received.</summary>
    internal const string ReceivedAttributeName = "received";

    /// <summary>Names the element carrying the subject the message arrived with.</summary>
    internal const string SubjectElementName = "subject";

    /// <summary>Names the element carrying the extract itself.</summary>
    internal const string ExtractElementName = "extract";

    /// <summary>Names the element carrying an extract of a file the message carried.</summary>
    /// <remarks>
    /// Distinct from <see cref="ExtractElementName" /> because the two were written by different people. A model told a
    /// contract's clause was part of a message would attribute it to whoever wrote the covering note, and would cite the
    /// note for a promise the note never made.
    /// </remarks>
    internal const string AttachmentExtractElementName = "attachment-extract";

    /// <summary>Names the element carrying a model's account of a picture the message carried.</summary>
    /// <remarks>
    /// Its own element rather than an attachment extract with a flag on it, because nobody wrote it. A model reading it
    /// as quoted evidence would report that somebody said a whiteboard showed a roof plan when nobody did — and it is
    /// untrusted twice over, a hostile sender being able to compose an image whose description reads as an instruction.
    /// </remarks>
    internal const string ImageDescriptionElementName = "attached-picture-described";

    /// <summary>Names the attribute carrying the walk position of the attachment an extract came from.</summary>
    internal const string AttachmentPositionAttributeName = "attachment";

    /// <summary>Names the attribute carrying the file name the sender gave that attachment.</summary>
    internal const string AttachmentFileNameAttributeName = "file";

    /// <summary>Names the attribute carrying what the segment inside the file is, in the word its own format uses.</summary>
    internal const string AttachmentSegmentKindAttributeName = "segment";

    /// <summary>Names the attribute carrying that segment's one-based number in reading order.</summary>
    internal const string AttachmentSegmentNumberAttributeName = "segment-number";

    private static readonly XmlWriterSettings EnvelopeSettings = new()
    {
        OmitXmlDeclaration = true,
        Indent = true,
        IndentChars = "  ",
        NewLineChars = "\n",

        // The extract keeps the line breaks the message had, because what a model is shown must be what was received.
        NewLineHandling = NewLineHandling.None,

        // A control character somewhere in a message must not fail somebody's question. It cannot open or close an
        // element, so the reason the writer is here — that no content becomes a delimiter — is untouched by allowing it.
        CheckCharacters = false,
    };

    /// <summary>Writes one lookup's passages into the envelope the model reads them inside.</summary>
    /// <param name="passages">The extracts the lookup found, in the order it ranked them.</param>
    /// <param name="retrievalMode">How the lookup ranked the mail it drew them from.</param>
    /// <param name="retrievalLimitReached">Whether this run may be handed no more mail than the envelope carries.</param>
    /// <returns>The envelope, holding one element per passage.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="passages" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="retrievalMode" /> has no value this envelope publishes.</exception>
    /// <remarks>
    /// A lookup that found nothing is written as an empty envelope rather than as nothing at all, so the model reads
    /// that the mailbox was searched and held no answer instead of reading a blank result it has to guess at. An
    /// envelope emptied by the run's own ceiling says so on the element, which is the difference between a mailbox with
    /// no answer in it and a run with no allowance left to read one.
    /// </remarks>
    internal static string Format(
        IReadOnlyList<EmailKnowledgePassage> passages,
        EmailSearchRetrievalMode retrievalMode,
        bool retrievalLimitReached)
    {
        ArgumentNullException.ThrowIfNull(passages);

        var mode = Published(retrievalMode);
        var envelope = new StringBuilder();

        using (var writer = XmlWriter.Create(envelope, EnvelopeSettings))
        {
            writer.WriteStartElement(RetrievalElementName);
            writer.WriteAttributeString(RetrievalModeAttributeName, mode);

            if (retrievalLimitReached)
            {
                // Absent rather than written as false on an ordinary lookup, so the attribute's presence is the signal
                // and a model reading the common case is not asked to parse a negation on every result.
                writer.WriteAttributeString(RetrievalLimitReachedAttributeName, "true");
            }

            foreach (var passage in passages)
            {
                WriteMessage(writer, passage);
            }

            writer.WriteEndElement();
        }

        return envelope.ToString();
    }

    /// <summary>Writes the document a model reads when the search use case refused the lookup it wrote.</summary>
    /// <param name="filterName">Which argument was refused, named as the application names it.</param>
    /// <param name="reason">Why it was refused, in the words the failure itself states.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    /// <remarks>
    /// Both values come from MailFathom's own assemblies rather than from the lookup: the failure names the filter and
    /// states its limit, and it never repeats the value that was refused, which is what keeps a refusal from carrying an
    /// address or a subject fragment back into the conversation. They are written through the same writer the envelope
    /// uses regardless, because a document this system composes is escaped by the mechanism rather than by an argument
    /// about where its text came from.
    /// </remarks>
    internal static string FormatRefusal(string filterName, string reason)
    {
        ArgumentNullException.ThrowIfNull(filterName);
        ArgumentNullException.ThrowIfNull(reason);

        var document = new StringBuilder();

        using (var writer = XmlWriter.Create(document, EnvelopeSettings))
        {
            writer.WriteStartElement(RefusalElementName);
            writer.WriteAttributeString(RefusedFilterAttributeName, filterName);
            writer.WriteString(reason);
            writer.WriteEndElement();
        }

        return document.ToString();
    }

    /// <summary>Maps the ranking the application reports onto the word this envelope publishes.</summary>
    /// <remarks>
    /// A closed mapping rather than a lowercased name, for the reason the MCP boundary's own mapping is closed: the word
    /// is what the tool description tells the model to read, so a mode the application grows without one has to fail
    /// here rather than reach a model as an identifier nobody described.
    /// </remarks>
    private static string Published(EmailSearchRetrievalMode retrievalMode) => retrievalMode switch
    {
        EmailSearchRetrievalMode.Lexical => LexicalRetrievalMode,
        EmailSearchRetrievalMode.Hybrid => HybridRetrievalMode,
        _ => throw new ArgumentOutOfRangeException(
            nameof(retrievalMode),
            retrievalMode,
            "The retrieval mode has no value this envelope publishes."),
    };

    private static void WriteMessage(XmlWriter writer, EmailKnowledgePassage passage)
    {
        writer.WriteStartElement(MessageElementName);

        writer.WriteAttributeString(MessageIdAttributeName, passage.StoredEmailId.ToString());
        writer.WriteAttributeString(AccountAttributeName, passage.AccountId.Value);
        writer.WriteAttributeString(FolderAttributeName, passage.FolderAlias.Value);

        if (passage.ReceivedAt is { } receivedAt)
        {
            writer.WriteAttributeString(
                ReceivedAttributeName,
                receivedAt.ToString("O", CultureInfo.InvariantCulture));
        }

        // Absent rather than empty where the message carried none, so the model is not left reading a blank subject as
        // one somebody wrote.
        if (passage.Subject is { } subject)
        {
            writer.WriteElementString(SubjectElementName, subject);
        }

        // Absent rather than empty where nothing was cut from the body, so a message whose words live entirely in a file
        // is not presented as one that said nothing.
        if (passage.Text.Length is not 0)
        {
            writer.WriteElementString(ExtractElementName, passage.Text);
        }

        foreach (var attachmentExtract in passage.AttachmentExtracts)
        {
            WriteAttachmentExtract(writer, attachmentExtract);
        }

        writer.WriteEndElement();
    }

    /// <summary>Writes one file's contribution, in the element that says who wrote the words in it.</summary>
    /// <remarks>
    /// The coordinate is written as attributes rather than into the text, so nothing a sender named a file can be read
    /// as part of the extract — and so an answer citing page fourteen of a report has the number to cite.
    /// </remarks>
    private static void WriteAttachmentExtract(XmlWriter writer, EmailKnowledgeAttachmentExtract extract)
    {
        writer.WriteStartElement(extract.Kind is AttachmentTextKind.ImageDescription
            ? ImageDescriptionElementName
            : AttachmentExtractElementName);

        writer.WriteAttributeString(
            AttachmentPositionAttributeName,
            extract.AttachmentPosition.ToString(CultureInfo.InvariantCulture));

        if (extract.FileName is { } fileName)
        {
            writer.WriteAttributeString(AttachmentFileNameAttributeName, fileName);
        }

        if (extract.Segment is { } segment)
        {
            writer.WriteAttributeString(AttachmentSegmentKindAttributeName, segment.Kind.ToString());
            writer.WriteAttributeString(
                AttachmentSegmentNumberAttributeName,
                segment.Number.ToString(CultureInfo.InvariantCulture));
        }

        writer.WriteString(extract.Text);

        writer.WriteEndElement();
    }
}
