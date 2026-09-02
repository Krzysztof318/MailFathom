// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.EmailContent.Rendering.Document;

/// <summary>Why a message is read as its plain text rather than as a reduced document.</summary>
/// <remarks>
/// <para>
/// Three things reach the plain-text rendering and they are named apart because a reader is owed the reason rather than
/// a blank pane: a message that never carried HTML is ordinary mail, a body whose reduction failed is a defect worth
/// knowing about, and a body that reduced to nothing renderable is a message whose markup carried no content at all.
/// </para>
/// <para>
/// A node whose revision this build does not implement is not among them. That one costs the reader the node rather
/// than the message, so it never reaches this enumeration.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<MailDocumentRefusal>))]
public enum MailDocumentRefusal
{
    /// <summary>Nothing was refused; the document below is the message.</summary>
    None = 0,

    /// <summary>The message carried no HTML part, so its plain text is the whole of what it wrote.</summary>
    NoHtmlPart = 1,

    /// <summary>The body could not be reduced, so nothing was built from it.</summary>
    ReductionFailed = 2,

    /// <summary>The body reduced to no block at all, so there is nothing to draw.</summary>
    NothingRenderable = 3,
}
