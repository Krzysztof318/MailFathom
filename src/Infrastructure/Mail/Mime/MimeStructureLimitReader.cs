// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Application.Emails;
using MimeKit;

namespace MailMcp.Infrastructure.Mail.Mime;

/// <summary>Names the structural limit a message crossed, if it crossed one.</summary>
internal enum ExceededMimeStructureLimit
{
    /// <summary>The message stays within both configured limits.</summary>
    None = 0,

    /// <summary>The message declares more entities than the configured maximum.</summary>
    PartCount = 1,

    /// <summary>The message nests multiparts or embedded messages deeper than the configured maximum.</summary>
    NestingDepth = 2,
}

/// <summary>Checks a message's declared structure against the configured limits while the bytes are being read.</summary>
/// <remarks>
/// <para>
/// The check has to happen during the read rather than over a parsed message. Counting the parts of a materialized
/// <see cref="MimeMessage" /> would concede exactly the allocations the limit exists to prevent: by the time a
/// traversal could observe forty thousand parts, forty thousand objects exist. <see cref="MimeReader" /> is
/// forward-only and reports structure through callbacks without building an object tree, so the message is abandoned
/// as soon as a limit is crossed.
/// </para>
/// <para>
/// Only multiparts and embedded messages contribute to depth, because only they can contain further entities. Every
/// entity, leaf or container, counts towards the part total.
/// </para>
/// </remarks>
internal sealed class MimeStructureLimitReader : MimeReader
{
    private readonly int maxPartCount;
    private readonly int maxNestingDepth;
    private int declaredPartCount;
    private int currentNestingDepth;

    private MimeStructureLimitReader(Stream stream, EmailMimeExtractionOptions options)
        : base(stream, MimeFormat.Entity)
    {
        this.maxPartCount = options.MaxPartCount;
        this.maxNestingDepth = options.MaxNestingDepth;
    }

    /// <summary>Reads the message's structure and reports the first limit it crosses.</summary>
    /// <param name="rawMime">The raw MIME, positioned at its first byte.</param>
    /// <param name="options">The configured structural limits.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The limit that was crossed, or <see cref="ExceededMimeStructureLimit.None" />.</returns>
    /// <remarks>
    /// Content that does not parse is reported as within limits rather than as a failure of its own, so that whether a
    /// message is malformed is decided in one place — the load that follows — instead of by two parsers that could
    /// disagree.
    /// </remarks>
    public static async Task<ExceededMimeStructureLimit> FindExceededLimitAsync(
        Stream rawMime,
        EmailMimeExtractionOptions options,
        CancellationToken cancellationToken)
    {
        var reader = new MimeStructureLimitReader(rawMime, options);

        try
        {
            await reader.ReadMessageAsync(cancellationToken);

            return ExceededMimeStructureLimit.None;
        }
        catch (MimeStructureLimitReachedException exception)
        {
            return exception.ExceededLimit;
        }
        catch (FormatException)
        {
            return ExceededMimeStructureLimit.None;
        }
    }

    /// <inheritdoc />
    protected override Task OnMimePartBeginAsync(ContentType contentType, long beginOffset, int lineNumber, CancellationToken cancellationToken)
    {
        this.CountEntity();

        return base.OnMimePartBeginAsync(contentType, beginOffset, lineNumber, cancellationToken);
    }

    /// <inheritdoc />
    protected override Task OnMultipartBeginAsync(ContentType contentType, long beginOffset, int lineNumber, CancellationToken cancellationToken)
    {
        this.CountEntity();
        this.EnterContainer();

        return base.OnMultipartBeginAsync(contentType, beginOffset, lineNumber, cancellationToken);
    }

    /// <inheritdoc />
    protected override Task OnMultipartEndAsync(
        ContentType contentType,
        long beginOffset,
        int lineNumber,
        long headersEndOffset,
        long endOffset,
        int lines,
        CancellationToken cancellationToken)
    {
        this.currentNestingDepth--;

        return base.OnMultipartEndAsync(contentType, beginOffset, lineNumber, headersEndOffset, endOffset, lines, cancellationToken);
    }

    /// <inheritdoc />
    protected override Task OnMessagePartBeginAsync(ContentType contentType, long beginOffset, int lineNumber, CancellationToken cancellationToken)
    {
        this.CountEntity();
        this.EnterContainer();

        return base.OnMessagePartBeginAsync(contentType, beginOffset, lineNumber, cancellationToken);
    }

    /// <inheritdoc />
    protected override Task OnMessagePartEndAsync(
        ContentType contentType,
        long beginOffset,
        int lineNumber,
        long headersEndOffset,
        long endOffset,
        int lines,
        CancellationToken cancellationToken)
    {
        this.currentNestingDepth--;

        return base.OnMessagePartEndAsync(contentType, beginOffset, lineNumber, headersEndOffset, endOffset, lines, cancellationToken);
    }

    private void CountEntity()
    {
        this.declaredPartCount++;

        if (this.declaredPartCount > this.maxPartCount)
        {
            throw new MimeStructureLimitReachedException(ExceededMimeStructureLimit.PartCount);
        }
    }

    private void EnterContainer()
    {
        this.currentNestingDepth++;

        if (this.currentNestingDepth > this.maxNestingDepth)
        {
            throw new MimeStructureLimitReachedException(ExceededMimeStructureLimit.NestingDepth);
        }
    }
}

/// <summary>Abandons a structural read the moment a limit is crossed.</summary>
/// <remarks>
/// <see cref="MimeReader" /> exposes no way for a callback to stop the read, and returning a flag would let the reader
/// finish parsing the message the limit exists to refuse. The type is confined to this file: it is thrown by a callback
/// and caught by the method that started the read, and never leaves the adapter.
/// </remarks>
[SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "The type is a control-flow signal between one private callback and its own caller, and never crosses the adapter boundary.")]
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "The exception carries the crossed limit and is never constructed from a message or an inner exception.")]
[SuppressMessage("Usage", "RCS1194:Implement exception constructors", Justification = "The exception carries the crossed limit and is never constructed from a message or an inner exception.")]
internal sealed class MimeStructureLimitReachedException(ExceededMimeStructureLimit exceededLimit)
    : Exception($"The message crossed the configured MIME structure limit {exceededLimit}.")
{
    /// <summary>Gets the limit the message crossed.</summary>
    public ExceededMimeStructureLimit ExceededLimit { get; } = exceededLimit;
}
