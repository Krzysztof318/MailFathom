// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Client.Backend.Mail;

/// <summary>How a block places its content across the width it was given.</summary>
/// <remarks>
/// The only positional property the document carries, and it distributes content within a width the pane already
/// decided. Nothing a message says can offset, transform, float, or stack a node, which is what confines the sender's
/// styling to the pane it is drawn in.
/// </remarks>
[JsonConverter(typeof(MailBodyAlignmentJsonConverter))]
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
/// know arrives as a name rather than as a bit that would silently mean something else — and the reader drops that name
/// and keeps the rest, which draws the run without one decoration instead of losing the message over it.
/// </remarks>
[Flags]
[JsonConverter(typeof(MailBodyEmphasisJsonConverter))]
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
[JsonConverter(typeof(MailBodyLinkDeceptionJsonConverter))]
public enum MailBodyLinkDeception
{
    /// <summary>The link's text is not a place, so there is nothing for it to disagree with.</summary>
    NotApplicable = 0,

    /// <summary>The link's text names a place and it is the place the link goes.</summary>
    None = 1,

    /// <summary>The link's text names one host and the link goes to another.</summary>
    DisplayedHostDiffers = 2,

    /// <summary>The deployment reported a verdict this build does not know, so nothing about the link is vouched for.</summary>
    /// <remarks>
    /// It counts as worth warning about rather than as nothing found. A verdict this build cannot read is a verdict it
    /// cannot report as clean, and the cheaper of the two mistakes is a reader being shown where a link goes when they
    /// did not need to be.
    /// </remarks>
    Unrecognized = 3,
}

/// <summary>Why the pane reads a message as its plain text rather than as a document.</summary>
/// <remarks>
/// It is shown rather than swallowed. A pane that fell back silently would leave a reader unable to tell a message that
/// carried no HTML from one this build could not read, and the two are worth different reactions.
/// </remarks>
[JsonConverter(typeof(MailBodyRefusalJsonConverter))]
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

    /// <summary>The deployment gave a reason this build does not know, which is still a reason there is no document.</summary>
    /// <remarks>
    /// Separate from <see cref="None" /> because the two lead the pane to opposite places: nothing refused means the
    /// document is the message, and this means there is no document and the plain text beside it is what to read.
    /// Reading an unknown reason as nothing refused would leave a reader an empty pane and no explanation.
    /// </remarks>
    Unrecognized = 4,
}

/// <summary>Reads a word naming how a block is placed, or the pane's own reading direction where it names none.</summary>
/// <remarks>
/// Hand-written rather than the framework's enum converter, which throws on a name it does not know. That would make
/// one value this build has not met cost the whole body — the plain text beside it included — and this contract exists
/// so that a deployment ahead of the client is ordinary rather than fatal. The default is what the deployment itself
/// means by saying nothing, so an unreadable answer and an absent one place a block the same way.
/// </remarks>
public sealed class MailBodyAlignmentJsonConverter : JsonConverter<MailBodyAlignment>
{
    /// <inheritdoc />
    public override MailBodyAlignment Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        MailBodyWireWord.Read(ref reader) switch
        {
            "Start" => MailBodyAlignment.Start,
            "Center" => MailBodyAlignment.Center,
            "End" => MailBodyAlignment.End,
            "Justify" => MailBodyAlignment.Justify,
            _ => MailBodyAlignment.Inherited,
        };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, MailBodyAlignment value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>Reads the names of the decorations a run asked for, dropping any this build does not know.</summary>
/// <remarks>
/// The framework writes a flags value as its names separated by commas, and refuses the whole value over one it does
/// not recognize. What a reader loses by dropping the unknown name is one decoration on one run; what they would lose
/// by refusing is the message.
/// </remarks>
public sealed class MailBodyEmphasisJsonConverter : JsonConverter<MailBodyEmphasis>
{
    /// <inheritdoc />
    public override MailBodyEmphasis Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (MailBodyWireWord.Read(ref reader) is not { Length: > 0 } written)
        {
            return MailBodyEmphasis.None;
        }

        var asked = MailBodyEmphasis.None;

        foreach (var name in written.Split(','))
        {
            asked |= name.Trim() switch
            {
                "Bold" => MailBodyEmphasis.Bold,
                "Italic" => MailBodyEmphasis.Italic,
                "Underline" => MailBodyEmphasis.Underline,
                "Strikethrough" => MailBodyEmphasis.Strikethrough,
                "Monospace" => MailBodyEmphasis.Monospace,
                _ => MailBodyEmphasis.None,
            };
        }

        return asked;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, MailBodyEmphasis value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>Reads the verdict about a link's text, taking one this build does not know as unvouched for.</summary>
public sealed class MailBodyLinkDeceptionJsonConverter : JsonConverter<MailBodyLinkDeception>
{
    /// <inheritdoc />
    public override MailBodyLinkDeception Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        MailBodyWireWord.Read(ref reader) switch
        {
            "NotApplicable" => MailBodyLinkDeception.NotApplicable,
            "None" => MailBodyLinkDeception.None,
            "DisplayedHostDiffers" => MailBodyLinkDeception.DisplayedHostDiffers,
            _ => MailBodyLinkDeception.Unrecognized,
        };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, MailBodyLinkDeception value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>Reads why there is no document, taking a reason this build does not know as still being one.</summary>
public sealed class MailBodyRefusalJsonConverter : JsonConverter<MailBodyRefusal>
{
    /// <inheritdoc />
    public override MailBodyRefusal Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        MailBodyWireWord.Read(ref reader) switch
        {
            "None" => MailBodyRefusal.None,
            "NoHtmlPart" => MailBodyRefusal.NoHtmlPart,
            "ReductionFailed" => MailBodyRefusal.ReductionFailed,
            "NothingRenderable" => MailBodyRefusal.NothingRenderable,
            _ => MailBodyRefusal.Unrecognized,
        };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, MailBodyRefusal value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>Reads the word one of these values arrived as, whatever token the deployment wrote it in.</summary>
/// <remarks>
/// A token that is not a string names nothing here. The contract publishes these as names precisely so a position can
/// never change meaning, so a number is read as a value this build cannot name rather than as an ordinal into a set the
/// deployment may have reordered.
/// </remarks>
internal static class MailBodyWireWord
{
    internal static string? Read(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }

        reader.Skip();

        return null;
    }
}
