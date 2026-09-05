// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction.Images;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.Descriptions;

/// <summary>Records what describing an image attachment decided, and nothing about the picture or the words it produced.</summary>
/// <remarks>
/// Every parameter is a classification, a count, or a measurement this system made. The octets, the description, and
/// the media type the sender declared are all somebody's mail or derived from it, and none of them reaches these
/// events — which is what lets them stay on in a deployment holding real mail. The declared pixel grid is a number the
/// image states about itself and is logged only where it is what caused a refusal, because a photograph's dimensions
/// are not what identifies it.
/// </remarks>
internal static partial class ImageDescriptionEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "An image attachment of {Format} was described in {DescriptionLength} characters.")]
    internal static partial void LogDescribed(ILogger logger, ImageAttachmentFormat format, int descriptionLength);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "An image attachment produced no description, because {Refusal}.")]
    internal static partial void LogRefused(ILogger logger, ImageDescriptionRefusal refusal);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "An image attachment declaring a grid of {DeclaredPixelCount} pixels was refused, because this deployment describes an image of at most {MaximumPixelCount}.")]
    internal static partial void LogPixelGridRefused(
        ILogger logger,
        long declaredPixelCount,
        long maximumPixelCount);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "An image attachment of {Format} was described in {DescriptionLength} characters and the model was cut off before it finished, so the description ends mid-sentence. Raising the chat endpoint's output budget is what completes it.")]
    internal static partial void LogDescriptionTruncated(
        ILogger logger,
        ImageAttachmentFormat format,
        int descriptionLength);
}
