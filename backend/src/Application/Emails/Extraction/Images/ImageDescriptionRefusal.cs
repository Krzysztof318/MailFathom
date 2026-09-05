// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Extraction.Images;

/// <summary>Names why an image attachment produced no description, at the granularity a caller acts on.</summary>
/// <remarks>
/// <para>
/// One value per decision a caller could take differently. Four of these say the attachment will never be described,
/// however often it is offered — the deployment does not describe images, the octets are not a format the allow-list
/// admits, they are a document rather than a picture, or they are a picture too large to send or to decode — so a
/// caller records the reason against the attachment and never asks again. Two more say the provider may answer later,
/// and one says it will not.
/// </para>
/// <para>
/// A caller's own cancellation is absent, exactly as it is from <see cref="Chat.ChatGenerationFailure" />: it arrives
/// as an <see cref="OperationCanceledException" /> and is a decision this system took rather than an outcome the
/// attachment or the provider produced.
/// </para>
/// </remarks>
public enum ImageDescriptionRefusal
{
    /// <summary>The deployment has not turned image description on, so no attachment octets left it.</summary>
    /// <remarks>The default state of an instance, and the one refusal that is a configuration rather than a property of the image.</remarks>
    NotActivated = 0,

    /// <summary>The octets are not one of the formats <see cref="ImageAttachmentFormat" /> admits.</summary>
    FormatNotSupported = 1,

    /// <summary>The octets are a markup document — an SVG image among them — rather than a raster picture.</summary>
    /// <remarks>
    /// Told apart from an unrecognized format because the two are refused for different reasons and only one of them is
    /// a candidate for ever being admitted. An SVG is XML that a renderer executes as a document, with script and
    /// external references available to whoever composed it, and nothing here is a renderer with a security team behind
    /// it; an unrecognized format is merely one nothing has been written for.
    /// </remarks>
    FormatExcluded = 2,

    /// <summary>The attachment holds more octets than one request may send.</summary>
    ImageTooLarge = 3,

    /// <summary>The image's own header declares a pixel grid larger than this deployment allows a decoder to be asked for.</summary>
    /// <remarks>The decompression-bomb refusal, and the reason the header is read before anything else happens to the octets: a small file may declare an enormous grid, and the cost of finding out is the allocation the declaration asks for.</remarks>
    PixelGridTooLarge = 4,

    /// <summary>The octets name a supported format and then do not hold one: the header is truncated, malformed, or absent.</summary>
    ImageUnreadable = 5,

    /// <summary>The request outlived the time the deployment allows one chat call.</summary>
    ProviderTimedOut = 6,

    /// <summary>The provider did not answer, and asking again later may produce one.</summary>
    /// <remarks>An unreachable endpoint, a dropped connection, an unreadable response, or a rate the deployment is over.</remarks>
    ProviderUnavailable = 7,

    /// <summary>The provider answered by refusing, and repeating the request cannot change that.</summary>
    /// <remarks>A rejected credential, a request the model or its safety system would not take, and an answer that arrived carrying no text.</remarks>
    ProviderRefused = 8,
}
