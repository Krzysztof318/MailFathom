// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation;

/// <summary>Which MIME shape a generated message's body takes.</summary>
/// <remarks>
/// The three take three different paths through the MIME reader and the text extractor, so a corpus producing only one
/// of them would leave the other two unexercised. Which one a message gets is decided by the seed like everything else.
/// </remarks>
internal enum SyntheticBodyShape
{
    /// <summary>One <c>text/plain</c> part and nothing else.</summary>
    PlainTextOnly = 0,

    /// <summary>One <c>text/html</c> part and no text alternative, which is what a great deal of real mail is.</summary>
    HtmlOnly = 1,

    /// <summary>A <c>multipart/alternative</c> carrying both, which is what the extractor has to choose between.</summary>
    TextAndHtmlAlternative = 2,
}
