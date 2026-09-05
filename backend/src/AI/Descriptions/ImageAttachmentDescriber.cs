// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers;
using MailFathom.AI.Chat;
using MailFathom.Application.Chat;
using MailFathom.Application.Emails.Extraction.Images;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.Descriptions;

/// <summary>Writes down what one image attachment shows, by showing it to the declared chat endpoint.</summary>
/// <remarks>
/// <para>
/// A single judgement carrying its own instruction, offering no tool and running no loop, so it composes no agent and
/// reaches the chat client directly — the shape <see cref="Orchestration.AgentComposition" /> names as belonging
/// outside it. What it adds over that client is the order the refusals are taken in, which is the whole of the safety
/// argument for this file.
/// </para>
/// <para>
/// **Nothing decodes the image, here or anywhere on this path.** The octets are read, their header is inspected, and
/// they are forwarded; no codec is asked for a pixel buffer, so the decompression bomb that a small file declaring an
/// enormous grid would be has nothing in this process to detonate against. The pixel ceiling is applied anyway, because
/// the provider does decode and a grid this deployment would not have decoded is not one to make somebody else decode
/// either.
/// </para>
/// <para>
/// The refusals are ordered cheapest-first and every one of them is taken before an octet leaves: the declared media
/// type, then the size, then the format, then the grid. A picture reaches the provider only after all four have passed,
/// which is what makes the activation the last remaining question about egress rather than one check among several.
/// </para>
/// <para>
/// The octets are read into a pooled buffer and the buffer is returned once the call has completed. It is held across
/// the whole provider call rather than copied, because the resilience pipeline may send it more than once and a
/// per-attempt copy of a photograph is the one allocation on this path worth avoiding.
/// </para>
/// </remarks>
internal sealed class ImageAttachmentDescriber : IEmailAttachmentImageDescriber
{
    /// <summary>The media type an SVG declares, refused by name so the exclusion is visible rather than inferred.</summary>
    private const string ScalableVectorGraphicsMediaType = "image/svg+xml";

    private readonly IChatModelClient chatModelClient;
    private readonly IChatGenerationPlanSource planSource;
    private readonly long maximumPixelCount;
    private readonly ILogger<ImageAttachmentDescriber> logger;

    /// <summary>Initializes a describer over the declared chat endpoint and this deployment's own decoding ceiling.</summary>
    /// <param name="chatModelClient">Sends the picture and returns what the model said about it.</param>
    /// <param name="planSource">Publishes the declaration in force, which is where the octet ceiling one request may carry comes from.</param>
    /// <param name="maximumPixelCount">The largest pixel grid an image may declare and still be sent.</param>
    /// <param name="logger">Records the outcome without recording the picture or the description.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumPixelCount" /> is not positive.</exception>
    public ImageAttachmentDescriber(
        IChatModelClient chatModelClient,
        IChatGenerationPlanSource planSource,
        long maximumPixelCount,
        ILogger<ImageAttachmentDescriber> logger)
    {
        ArgumentNullException.ThrowIfNull(chatModelClient);
        ArgumentNullException.ThrowIfNull(planSource);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPixelCount);
        ArgumentNullException.ThrowIfNull(logger);

        this.chatModelClient = chatModelClient;
        this.planSource = planSource;
        this.maximumPixelCount = maximumPixelCount;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<ImageAttachmentDescription> DescribeAsync(
        string declaredMediaType,
        Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declaredMediaType);
        ArgumentNullException.ThrowIfNull(content);

        if (IsExcludedByDeclaration(declaredMediaType))
        {
            return this.Refused(ImageDescriptionRefusal.FormatExcluded);
        }

        // One octet past the ceiling, so a file that reaches the ceiling exactly is admitted and the first one over it
        // is known to be over without the rest of it ever being read.
        var ceiling = this.planSource.Current.MaximumRequestImageOctets;
        var buffer = ArrayPool<byte>.Shared.Rent(ceiling + 1);

        try
        {
            var window = buffer.AsMemory(0, ceiling + 1);
            var octetCount = await content.ReadAtLeastAsync(
                window,
                window.Length,
                throwOnEndOfStream: false,
                cancellationToken);

            if (octetCount == 0)
            {
                return this.Refused(ImageDescriptionRefusal.ImageUnreadable);
            }

            if (octetCount > ceiling)
            {
                return this.Refused(ImageDescriptionRefusal.ImageTooLarge);
            }

            var octets = window[..octetCount];

            if (!ImageAttachmentHeader.TryRead(octets.Span, out var header, out var refusal))
            {
                return this.Refused(refusal);
            }

            if (header.PixelCount > this.maximumPixelCount)
            {
                ImageDescriptionEvents.LogPixelGridRefused(this.logger, header.PixelCount, this.maximumPixelCount);

                return ImageAttachmentDescription.Refused(ImageDescriptionRefusal.PixelGridTooLarge);
            }

            return await this.AskAsync(header, octets, cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Reports whether the part declared a format this deployment will not send whatever its octets turn out to be.</summary>
    /// <remarks>
    /// Read before the stream is touched, so an SVG costs nothing to refuse. It is not the exclusion's only line of
    /// defence and is not meant to be — the declaration belongs to the sender — which is why the octets are checked for
    /// a markup document as well; this exists so that the common, honest case is named rather than falling through as
    /// an unrecognized format.
    /// </remarks>
    private static bool IsExcludedByDeclaration(string declaredMediaType)
    {
        var separator = declaredMediaType.IndexOf(';', StringComparison.Ordinal);
        var mediaType = separator < 0 ? declaredMediaType : declaredMediaType[..separator];

        return mediaType.Trim().Equals(ScalableVectorGraphicsMediaType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Shows the picture to the model and reads back what it said.</summary>
    /// <remarks>
    /// Two turns and nothing else, and the media type sent is the one read from the octets rather than the one the part
    /// declared. Cancellation is deliberately not caught: the caller stopping the work is not a property of the image
    /// or of the provider, and recording it against the attachment would refuse a picture that nothing was wrong with.
    /// </remarks>
    private async Task<ImageAttachmentDescription> AskAsync(
        ImageAttachmentHeader header,
        ReadOnlyMemory<byte> octets,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ChatMessage> conversation =
        [
            new ChatMessage(ChatRole.System, ImageDescriptionInstructions.Text),
            new ChatMessage(
                ChatRole.User,
                ImageDescriptionInstructions.DescriptionRequest,
                new ChatImage(header.MediaType, octets)),
        ];

        try
        {
            var answer = await this.chatModelClient.AnswerAsync(conversation, cancellationToken);

            // A withheld generation is a refusal rather than a short answer: what survives it is a fragment the
            // provider stopped mid-way, which ChatGenerationStop itself says is presented as a refusal.
            if (answer.Stop == ChatGenerationStop.ContentFiltered)
            {
                return this.Refused(ImageDescriptionRefusal.ProviderRefused);
            }

            var description = answer.Text.Trim();

            if (description.Length == 0)
            {
                return this.Refused(ImageDescriptionRefusal.ProviderRefused);
            }

            // A transcription is as long as the words on the page, so the output budget is the bound a picture of a
            // dense document meets first. The prefix is kept — it is real text somebody can search — and the truncation
            // is recorded, because the remedy is the operator's output budget rather than anything about the picture.
            if (answer.Stop == ChatGenerationStop.OutputLimitReached)
            {
                ImageDescriptionEvents.LogDescriptionTruncated(this.logger, header.Format, description.Length);
            }
            else
            {
                ImageDescriptionEvents.LogDescribed(this.logger, header.Format, description.Length);
            }

            return ImageAttachmentDescription.Described(description);
        }
        catch (ChatGenerationFailedException failure)
        {
            return this.Refused(ToRefusal(failure.Failure));
        }
    }

    /// <summary>Reads a chat failure into the reason recorded against the attachment.</summary>
    /// <remarks>
    /// Three outcomes rather than six, because that is how many different things a caller does with one: wait for the
    /// deadline to be worth another try, wait for the provider to come back, or stop offering this attachment. A
    /// provider that answered without text is grouped with the refusals rather than with the outages, because the call
    /// reached the model and the model produced nothing — repeating it buys the same silence.
    /// </remarks>
    private static ImageDescriptionRefusal ToRefusal(ChatGenerationFailure failure) => failure switch
    {
        ChatGenerationFailure.RequestTimedOut => ImageDescriptionRefusal.ProviderTimedOut,
        ChatGenerationFailure.RateLimited or ChatGenerationFailure.TransportFaulted =>
            ImageDescriptionRefusal.ProviderUnavailable,
        _ => ImageDescriptionRefusal.ProviderRefused,
    };

    private ImageAttachmentDescription Refused(ImageDescriptionRefusal refusal)
    {
        ImageDescriptionEvents.LogRefused(this.logger, refusal);

        return ImageAttachmentDescription.Refused(refusal);
    }
}
