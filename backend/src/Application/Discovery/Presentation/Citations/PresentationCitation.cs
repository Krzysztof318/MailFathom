// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Discovery.Presentation.Citations;

/// <summary>One source a plan declares, under the name its blocks refer to it by.</summary>
/// <remarks>
/// <para>
/// A citation is declared once in the plan and referred to by <see cref="Id" /> wherever it is used, so a reader can see
/// that two facts rest on the same message and a client can draw one source list for a run.
/// </para>
/// <para>
/// The label is what a client prints where the source is named — a subject, a sender, a file name — and it exists so a
/// citation reads as something before it is followed. It is descriptive text and never the identity: two citations may
/// carry the same label and still resolve to different places.
/// </para>
/// </remarks>
/// <param name="Id">The name blocks refer to this citation by.</param>
/// <param name="Target">What the citation resolves to.</param>
/// <param name="Label">What a client prints where the source is named.</param>
public sealed record PresentationCitation(
    PresentationCitationId Id,
    PresentationCitationTarget Target,
    PresentationText Label);
