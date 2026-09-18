// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Contacts.Relationship;

/// <summary>One observation about a correspondence, under the aspect it answers.</summary>
/// <param name="Aspect">What the observation is about, which is the label a card draws it under.</param>
/// <param name="Statement">What the derivation observed, and the conversations or documents it rests on.</param>
/// <remarks>
/// An aspect appears at most once in an answer, because the answer is composed of one value per heading rather than of
/// a list a producer decides the shape of. What a reader is shown is therefore a fixed set of labels with a value
/// beside some of them, and never two competing values under one label.
/// </remarks>
public sealed record ContactRelationshipObservation(
    ContactRelationshipAspect Aspect,
    ContactRelationshipStatement Statement);
