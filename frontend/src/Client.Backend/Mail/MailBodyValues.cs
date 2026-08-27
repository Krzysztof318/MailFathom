// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Client.Backend.Mail;

/// <summary>How a block places its content across the width it was given.</summary>
/// <remarks>
/// The only positional property the document carries, and it distributes content within a width the pane already
/// decided. Nothing a message says can offset, transform, float, or stack a node, which is what confines the sender's
/// styling to the pane it is drawn in.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<MailBodyAlignment>))]
public enum MailBodyAlignment
{
    /// <summary>The message said nothing, so the pane's own reading direction decides.</summary>
    Inherited = 0,

    /// <summary>Against the start of the line.</summary>
    Start = 1,

    /// <summary>Centred within the width.</summary>
    Center = 2,

    /// <summary>Against the end of the line.</summary>
    End = 3,

    /// <summary>Spread to both edges.</summary>
    Justify = 4,
}

/// <summary>What the message asked for about how one run of its text is drawn.</summary>
/// <remarks>
/// A flags value because the five compose. The deployment writes them as their names, so a member this build does not
/// know arrives as a name the reader refuses rather than as a bit that would silently mean something else.
/// </remarks>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<MailBodyEmphasis>))]
public enum MailBodyEmphasis
{
    /// <summary>Drawn as the pane's ordinary body text.</summary>
    None = 0,

    /// <summary>Drawn heavier than the text around it.</summary>
    Bold = 1,

    /// <summary>Drawn slanted.</summary>
    Italic = 2,

    /// <summary>Drawn with a line under it.</summary>
    Underline = 4,

    /// <summary>Drawn with a line through it.</summary>
    Strikethrough = 8,

    /// <summary>Drawn in the pane's fixed-width face.</summary>
    Monospace = 16,
}

/// <summary>What the deployment established about a link's text against where the link actually goes.</summary>
/// <remarks>
/// The determination is the deployment's rather than this client's, deliberately. A client deriving it for itself could
/// be quieter about a deceptive link than another client reading the same message, which is exactly what a reader
/// cannot check for themselves.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<MailBodyLinkDeception>))]
public enum MailBodyLinkDeception
{
    /// <summary>The link's text is not a place, so there is nothing for it to disagree with.</summary>
    NotApplicable = 0,

    /// <summary>The link's text names a place and it is the place the link goes.</summary>
    None = 1,

    /// <summary>The link's text names one host and the link goes to another.</summary>
    DisplayedHostDiffers = 2,
}

/// <summary>Why the pane reads a message as its plain text rather than as a document.</summary>
/// <remarks>
/// It is shown rather than swallowed. A pane that fell back silently would leave a reader unable to tell a message that
/// carried no HTML from one this build could not read, and the two are worth different reactions.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<MailBodyRefusal>))]
public enum MailBodyRefusal
{
    /// <summary>Nothing was refused; the document is the message.</summary>
    None = 0,

    /// <summary>The message carried no HTML part, so its plain text is the whole of what it wrote.</summary>
    NoHtmlPart = 1,

    /// <summary>The body could not be reduced, so nothing was built from it.</summary>
    ReductionFailed = 2,

    /// <summary>The body reduced to no block at all, so there is nothing to draw.</summary>
    NothingRenderable = 3,
}
