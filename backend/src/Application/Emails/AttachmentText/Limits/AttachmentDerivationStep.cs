// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.AttachmentText.Limits;

/// <summary>Which step of reading an attachment a ceiling bounds, and therefore what unit it is counted in.</summary>
/// <remarks>
/// <para>
/// The two steps cost different things and neither figure predicts the other, which is why they are counted apart
/// rather than converted into one unit that would fit neither.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0029-what-an-embedding-is-derived-from-and-whether-attachment-text-joins-it.md">ADR 0029</see>
/// puts extraction's ceilings beside the embedding ones and counts them in what extraction reads;
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md">ADR 0030</see>
/// adds a step whose cost is a chat call rather than a parse, and a call is priced per request rather than per octet.
/// </para>
/// <para>
/// Embedding is deliberately not a member. ADR 0029 decision 2 states that an attachment-derived passage is the same
/// kind of passage under one profile with no second ceiling on what may be sent — a character out of a document costs
/// exactly what a character out of a body costs — so what bounds it is <c>Embeddings:MaxInputCharactersPerPeriod</c>
/// and its per-owner companion, which already do. Cutting passages is not a member either: it reaches no provider, and
/// what it grows is storage, which is reported beside the extraction figures rather than paced by a ceiling that prices
/// a provider call.
/// </para>
/// </remarks>
public enum AttachmentDerivationStep
{
    /// <summary>Reading a document attachment's own text, counted in the octets a parser was handed.</summary>
    Extraction = 0,

    /// <summary>Asking a chat provider what a picture shows, counted in the calls made.</summary>
    Description = 1,
}
