// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.AI.ContactRelationships;

/// <summary>The shape a relationship agent answers in, before anything of it is believed.</summary>
/// <remarks>
/// It stays inside this boundary and never becomes a statement. Every field is optional and every value is untrusted:
/// what a model wrote is read into this and then validated into
/// <see cref="Application.Contacts.Relationship.ContactRelationship" />, which is the type the rest of the system works
/// with.
/// </remarks>
internal sealed record ContactRelationshipDocument
{
    /// <summary>Gets what the model said the correspondence amounts to.</summary>
    [JsonPropertyName("note")]
    public ContactRelationshipStatementDocument? Note { get; init; }

    /// <summary>Gets what the model said to do next, where it said anything.</summary>
    [JsonPropertyName("nextAction")]
    public ContactRelationshipStatementDocument? NextAction { get; init; }

    /// <summary>Gets what the model said about when this person is in the correspondence.</summary>
    [JsonPropertyName("activePeriod")]
    public ContactRelationshipStatementDocument? ActivePeriod { get; init; }

    /// <summary>Gets what the model said the correspondence leaves outstanding.</summary>
    [JsonPropertyName("openItem")]
    public ContactRelationshipStatementDocument? OpenItem { get; init; }

    /// <summary>Gets what the model said the exchanges are about.</summary>
    [JsonPropertyName("cases")]
    public ContactRelationshipStatementDocument? Cases { get; init; }
}

/// <summary>One statement as the model wrote it, with nothing about it yet established.</summary>
/// <remarks>
/// The sources are the numbers the turn gave the conversations and the documents rather than identifiers, which is why
/// they are integers here: a number outside the range the turn published names nothing and the statement falls away
/// with it.
/// </remarks>
internal sealed record ContactRelationshipStatementDocument
{
    /// <summary>Gets the statement itself.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>Gets the turn's own numbers for what the statement rests on.</summary>
    [JsonPropertyName("sources")]
    public IReadOnlyList<int>? Sources { get; init; }
}
