// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using MailFathom.SyntheticMail.Generation.AiContent;
using MailFathom.SyntheticMail.Generation.SensitiveDecoys;

namespace MailFathom.SyntheticMail.Generation;

/// <summary>Turns a seed into a corpus, reaching nothing and calling nobody for what the seed decides.</summary>
/// <remarks>
/// <para>
/// Every draw comes from one <see cref="Random" /> constructed from the plan's seed, which the base class library
/// documents as producing identical sequences for identical seeds. That is the whole reproducibility mechanism: the
/// same plan yields the same messages, so a page boundary, a ranking, or a retrieval result is something to assert
/// against rather than something to look at.
/// </para>
/// <para>
/// The one input the seed does not decide is the content a plan with named languages is written with. The generator
/// decides the envelope around it — author, thread, date, language, topic, attachment — and hands one question to the
/// source it is given at a time, so the same plan asks the same questions in the same order on every run and what the
/// source answers is the only thing that differs between them.
/// </para>
/// <para>
/// The corpus is built as a loop rather than as a query because each message may answer one already produced. A
/// threaded reply reads the ancestry, the subject, and the date of a message earlier in the same batch, which is a
/// dependency on the sequence being built and not something a projection over the indices can express.
/// </para>
/// </remarks>
[SuppressMessage(
    "Security",
    "CA5394:Do not use insecure randomness",
    Justification = "Being predictable from a seed is this type's whole contract, and a cryptographic generator is by construction incapable of it. Nothing drawn here is a token, an identifier a security decision reads, or a value anybody must be unable to guess: it selects a noun, a date, and a participant for mail that is invented on purpose.")]
internal sealed class SyntheticEmailGenerator
{
    /// <summary>How many invented people one corpus draws its participants from.</summary>
    /// <remarks>
    /// Small enough that the same names recur across a batch, which is what makes a participant filter worth testing;
    /// large enough that a few hundred messages are not all between the same two people.
    /// </remarks>
    private const int ParticipantPoolSize = 14;

    /// <summary>How far back a reply may reach for the message it answers.</summary>
    private const int ThreadWindow = 20;

    /// <summary>How often a message answers an earlier one, in messages per hundred.</summary>
    private const int ReplyPercentage = 40;

    /// <summary>How often a message carries an attachment, in messages per hundred.</summary>
    private const int AttachmentPercentage = 35;

    /// <summary>The encoder an HTML body is written through, which escapes markup and passes every character.</summary>
    private static readonly HtmlEncoder BodyEncoder = HtmlEncoder.Create(new TextEncoderSettings(UnicodeRanges.All));

    private readonly SyntheticCorpusPlan plan;
    private readonly Random source;
    private readonly IReadOnlyList<SyntheticParticipant> participants;
    private readonly List<SyntheticEmail> produced;
    private readonly int firstDecoyOrdinal;
    private int plantedDecoys;

    private SyntheticEmailGenerator(SyntheticCorpusPlan plan)
    {
        this.plan = plan;
        this.source = new Random(plan.Seed);
        this.participants = BuildParticipantPool(this.source);
        this.produced = new List<SyntheticEmail>(plan.Count);

        // Nothing is drawn when the corpus carries no decoys, which is what keeps a run asking for none identical to
        // one from before this generator could plant any: a draw made and discarded would still move the sequence
        // every other value comes out of.
        this.firstDecoyOrdinal = plan.SensitivePercentage > 0
            ? this.source.Next(SensitiveDecoyCatalog.Kinds.Count)
            : 0;
    }

    /// <summary>Produces the corpus a plan describes.</summary>
    /// <param name="plan">What the corpus is, seed included.</param>
    /// <returns>The messages, oldest first.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plan" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the plan names languages, which is a corpus only <see cref="GenerateAsync" /> can build.</exception>
    internal static IReadOnlyList<SyntheticEmail> Generate(SyntheticCorpusPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Languages.Count > 0)
        {
            throw new ArgumentException(
                "A plan that names languages is one a source writes, and GenerateAsync is the one that names it.",
                nameof(plan));
        }

        return new SyntheticEmailGenerator(plan).Produce();
    }

    /// <summary>Produces the corpus a plan describes, reaching the named source for what the seed does not decide.</summary>
    /// <param name="plan">What the corpus is, seed included.</param>
    /// <param name="contentSource">The source the message content comes from, required when the plan names languages.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The messages, oldest first.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plan" /> is <see langword="null" />, or <paramref name="contentSource" /> is when the plan needs one.</exception>
    /// <exception cref="ArgumentException">Thrown when the plan's distribution names no topic or one that is not a topic.</exception>
    internal static async Task<IReadOnlyList<SyntheticEmail>> GenerateAsync(
        SyntheticCorpusPlan plan,
        IAiEmailContentSource? contentSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Languages.Count == 0)
        {
            return Generate(plan);
        }

        ArgumentNullException.ThrowIfNull(contentSource);
        RequireCompleteDistribution(plan);

        return await new SyntheticEmailGenerator(plan).ProduceAsync(contentSource, cancellationToken);
    }

    /// <summary>Refuses a distribution the seed could not draw from, before a message is built against it.</summary>
    private static void RequireCompleteDistribution(SyntheticCorpusPlan plan)
    {
        if (plan.Topics.Count == 0)
        {
            throw new ArgumentException(
                "A plan that names languages names the topics it distributes across.",
                nameof(plan));
        }

        if (plan.Topics.Any(topic => !topic.IsSpecified))
        {
            throw new ArgumentException(
                "The plan's topic distribution names a value that is not a topic.",
                nameof(plan));
        }
    }

    private static IReadOnlyList<SyntheticParticipant> BuildParticipantPool(Random source) =>
    [
        .. Enumerable.Range(0, ParticipantPoolSize).Select(_ => BuildParticipant(source)),
    ];

    private static SyntheticParticipant BuildParticipant(Random source)
    {
        var givenName = Pick(source, SyntheticVocabulary.GivenNames);
        var familyName = Pick(source, SyntheticVocabulary.FamilyNames);
        var domain = Pick(source, SyntheticVocabulary.Domains);

        return new SyntheticParticipant(
            $"{givenName} {familyName}",
            $"{ToAddressPart(givenName)}.{ToAddressPart(familyName)}@{domain}");
    }

    /// <summary>Reduces a name to the ASCII letters an address local part may hold.</summary>
    /// <remarks>
    /// An explicit fold rather than Unicode normalization, because the project runs with globalization turned off and
    /// the alphabet is one this repository chose: every accented character the name lists use appears below, and
    /// anything else is dropped rather than guessed at.
    /// </remarks>
    private static string ToAddressPart(string name) =>
        string.Concat(name.ToLowerInvariant().Select(FoldToAscii).Where(char.IsAsciiLetter));

    private static char FoldToAscii(char character) => character switch
    {
        'á' => 'a',
        'ä' => 'a',
        'é' => 'e',
        'ë' => 'e',
        'í' => 'i',
        'ó' => 'o',
        'ö' => 'o',
        'ø' => 'o',
        'ř' => 'r',
        _ => character,
    };

    private static string Pick(Random source, IReadOnlyList<string> values) => values[source.Next(values.Count)];

    private static string StripReplyPrefix(string subject) =>
        subject.StartsWith("Re: ", StringComparison.Ordinal) ? subject[4..] : subject;

    private static (string MediaType, string MediaSubtype) ResolveMediaType(string fileName) =>
        Path.GetExtension(fileName) switch
        {
            ".csv" => ("text", "csv"),
            ".txt" => ("text", "plain"),
            _ => ("application", "octet-stream"),
        };

    private List<SyntheticEmail> Produce()
    {
        for (var index = 0; index < this.plan.Count; index++)
        {
            this.produced.Add(this.BuildEmail(index));
        }

        return this.produced;
    }

    private async Task<List<SyntheticEmail>> ProduceAsync(
        IAiEmailContentSource contentSource,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < this.plan.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.produced.Add(await this.BuildEmailAsync(index, contentSource, cancellationToken));
        }

        return this.produced;
    }

    private SyntheticEmail BuildEmail(int index)
    {
        var author = this.participants[this.source.Next(this.participants.Count)];
        var carbonCopies = this.BuildCarbonCopies(author);
        var parent = this.PickParent();
        var sentAt = this.BuildSentAt(index, parent);
        var subject = parent is null ? this.BuildSubject() : $"Re: {StripReplyPrefix(parent.Subject)}";

        return new SyntheticEmail(
            this.BuildMessageId(index, author),
            parent?.MessageId,
            parent is null ? [] : [.. parent.References, parent.MessageId],
            author,
            carbonCopies,
            subject,
            sentAt,
            this.BuildBody(),
            this.BuildAttachment(),
            null);
    }

    private async Task<SyntheticEmail> BuildEmailAsync(
        int index,
        IAiEmailContentSource contentSource,
        CancellationToken cancellationToken)
    {
        var author = this.participants[this.source.Next(this.participants.Count)];
        var carbonCopies = this.BuildCarbonCopies(author);
        var parent = this.PickParent();
        var sentAt = this.BuildSentAt(index, parent);
        var origin = new SyntheticEmailAiOrigin(this.DrawLanguage(), this.DrawTopic());
        var messageId = this.BuildMessageId(index, author);

        // The envelope is decided before the call, for the reason the question is: the same plan asks the source the
        // same questions in the same order on every run, so what differs between runs is the answer and nothing else.
        var content = await contentSource.GenerateAsync(
            new AiEmailContentRequest(origin.Language, origin.Topic, author.DisplayName, parent?.Subject),
            cancellationToken);

        // A reply keeps the thread's subject, which the deterministic layer owns: the source answers the body, and
        // a subject it invented would break the In-Reply-To chain a corpus exists to exercise.
        var subject = parent is null ? content.Subject : $"Re: {StripReplyPrefix(parent.Subject)}";

        return new SyntheticEmail(
            messageId,
            parent?.MessageId,
            parent is null ? [] : [.. parent.References, parent.MessageId],
            author,
            carbonCopies,
            subject,
            sentAt,
            this.BuildAiBody(content),
            this.BuildAttachment(),
            origin);
    }

    private string DrawLanguage() => this.plan.Languages[this.source.Next(this.plan.Languages.Count)];

    private SyntheticMailTopic DrawTopic() => this.plan.Topics[this.source.Next(this.plan.Topics.Count)];

    private List<SyntheticParticipant> BuildCarbonCopies(SyntheticParticipant author)
    {
        var copyCount = this.source.Next(0, 4);
        var candidates = new List<SyntheticParticipant>(this.participants.Where(participant => participant != author));
        var chosen = new List<SyntheticParticipant>(copyCount);

        // Drawn without replacement, so a message never copies the same person twice. Removing the drawn candidate is
        // what a query cannot express, which is why this is a loop rather than a projection.
        for (var drawn = 0; drawn < copyCount && candidates.Count > 0; drawn++)
        {
            var position = this.source.Next(candidates.Count);

            chosen.Add(candidates[position]);
            candidates.RemoveAt(position);
        }

        return chosen;
    }

    private SyntheticEmail? PickParent()
    {
        if (this.produced.Count == 0 || this.source.Next(100) >= ReplyPercentage)
        {
            return null;
        }

        var window = Math.Min(ThreadWindow, this.produced.Count);

        return this.produced[this.produced.Count - 1 - this.source.Next(window)];
    }

    private DateTimeOffset BuildSentAt(int index, SyntheticEmail? parent)
    {
        var earliest = this.plan.LatestSentAt.AddDays(-this.plan.SpanDays);
        var spanSeconds = (this.plan.LatestSentAt - earliest).TotalSeconds;

        // Dates advance with the index rather than being drawn independently, so the batch reads as a mailbox that
        // filled up over the range instead of as a shuffle, and a reply is later than what it answers by construction.
        var offsetSeconds = Math.Floor(spanSeconds * (index + this.source.NextDouble()) / this.plan.Count);
        var sentAt = earliest.AddSeconds(offsetSeconds);

        // One slot's worth of jitter can still put a message a little before the one it answers, which no mail client
        // would ever show. Pushing it past the parent costs one branch and keeps every thread readable.
        return parent is not null && sentAt <= parent.SentAt
            ? parent.SentAt.AddMinutes(this.source.Next(5, 240))
            : sentAt;
    }

    private string BuildMessageId(int index, SyntheticParticipant author) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{index:x8}.{this.source.Next():x8}@{author.Address[(author.Address.IndexOf('@', StringComparison.Ordinal) + 1)..]}");

    private string BuildSubject() => this.Fill(Pick(this.source, SyntheticVocabulary.SubjectTemplates));

    private string Fill(string template) => template
        .Replace("{0}", Pick(this.source, SyntheticVocabulary.Adjectives), StringComparison.Ordinal)
        .Replace("{1}", Pick(this.source, SyntheticVocabulary.Nouns), StringComparison.Ordinal)
        .Replace("{2}", Pick(this.source, SyntheticVocabulary.Verbs), StringComparison.Ordinal)
        .Replace("{3}", Pick(this.source, SyntheticVocabulary.Nouns), StringComparison.Ordinal);

    private SyntheticEmailBody BuildBody()
    {
        var shape = (SyntheticBodyShape)this.source.Next(3);
        var characterSet = (SyntheticCharacterSet)this.source.Next(3);
        var paragraphCount = this.source.Next(1, 7);
        var paragraphs = Enumerable
            .Range(0, paragraphCount)
            .Select(_ => this.BuildParagraph())
            .ToArray();

        var closing = characterSet switch
        {
            SyntheticCharacterSet.Latin1 => Pick(this.source, SyntheticVocabulary.Latin1ClosingLines),
            SyntheticCharacterSet.Utf8 => Pick(this.source, SyntheticVocabulary.UnicodeClosingLines),
            _ => string.Empty,
        };

        var decoy = this.PlantDecoy();

        // The decoy stands after what the message was about and before whatever closes it, which is where somebody
        // pasting a credential into a thread puts it. Its own sentence is a paragraph rather than a clause inside one,
        // so a redacted body still reads as a message with one line replaced.
        List<string> blocks = [.. paragraphs];

        if (decoy is not null)
        {
            blocks.Add(decoy.Sentence);
        }

        if (closing.Length > 0)
        {
            blocks.Add(closing);
        }

        return new SyntheticEmailBody(
            shape,
            string.Join("\n\n", blocks),
            BuildHtml(blocks),
            characterSet,
            decoy);
    }

    private SyntheticEmailBody BuildAiBody(AiEmailContent content)
    {
        var shape = (SyntheticBodyShape)this.source.Next(3);

        // The source answers with paragraphs separated by blank lines, and the MIME shape is still drawn from the
        // seed for the reason the deterministic body varies it: both alternatives are built from the same paragraphs,
        // which is what the extractor's choice between them is meant to read.
        var blocks = content.Body
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split(["\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // The charset is the one axis the seed does not draw here: a body written in the language the invocation names
        // is one the vocabulary's three charsets cannot be promised to hold, and utf-8 is the one that holds any of
        // them.
        var decoy = this.PlantDecoy();

        if (decoy is not null)
        {
            blocks.Add(decoy.Sentence);
        }

        return new SyntheticEmailBody(
            shape,
            string.Join("\n\n", blocks),
            BuildHtml(blocks),
            SyntheticCharacterSet.Utf8,
            decoy);
    }

    private SensitiveDecoy? PlantDecoy()
    {
        if (this.plan.SensitivePercentage <= 0 || this.source.Next(100) >= this.plan.SensitivePercentage)
        {
            return null;
        }

        var decoy = SensitiveDecoyCatalog.Plant(this.source, this.firstDecoyOrdinal + this.plantedDecoys);

        this.plantedDecoys++;

        return decoy;
    }

    /// <summary>Wraps the paragraphs in the smallest HTML document that is still one.</summary>
    /// <remarks>
    /// Encoded through <see cref="BodyEncoder" /> rather than <c>WebUtility.HtmlEncode</c>, whose output is ASCII by
    /// construction: it rewrites every code point outside Basic Latin as a numeric character reference. That would
    /// make the <c>text/html</c> part's bytes pure ASCII while the part still declared <c>iso-8859-1</c> or
    /// <c>utf-8</c>, so the charset axis this generator exists to vary would be varied in the header alone and
    /// nothing reading the corpus would ever decode a non-ASCII byte out of an HTML body.
    /// </remarks>
    private static string BuildHtml(IReadOnlyList<string> blocks) =>
        $"<html><body>{string.Concat(blocks.Select(block => $"<p>{BodyEncoder.Encode(block)}</p>"))}</body></html>";

    private string BuildParagraph() => string.Join(
        ' ',
        Enumerable
            .Range(0, this.source.Next(1, 5))
            .Select(_ => this.Fill(Pick(this.source, SyntheticVocabulary.SentenceTemplates)))
            .ToArray());

    private SyntheticEmailAttachment? BuildAttachment()
    {
        if (this.plan.MaximumAttachmentBytes <= 0 || this.source.Next(100) >= AttachmentPercentage)
        {
            return null;
        }

        var fileName = Pick(this.source, SyntheticVocabulary.AttachmentNames);
        var (mediaType, mediaSubtype) = ResolveMediaType(fileName);

        return new SyntheticEmailAttachment(
            fileName,
            mediaType,
            mediaSubtype,
            this.source.Next(1, this.plan.MaximumAttachmentBytes + 1),
            this.source.Next());
    }
}
