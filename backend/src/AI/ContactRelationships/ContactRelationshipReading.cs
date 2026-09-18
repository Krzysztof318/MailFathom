// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Contacts.Correspondence;
using MailFathom.Application.Contacts.Relationship;

namespace MailFathom.AI.ContactRelationships;

/// <summary>Turns what a relationship agent wrote into a card, keeping only what the correspondence itself can back.</summary>
/// <remarks>
/// <para>
/// Every reading below is a pure function of the answer and the correlation the turn published, which is what makes the
/// cases a provider produces once in a thousand runs ordinary examples in a test rather than something only a live
/// endpoint reaches.
/// </para>
/// <para>
/// It drops rather than repairs. A statement with no text, or none the correlation can be checked against, is one
/// nothing could be followed back from, and there is no honest way to invent the missing half — so it falls away and
/// the card keeps what survived. A card whose note falls away is no card at all, because the note is what the rest is
/// drawn around.
/// </para>
/// <para>
/// A citation is a position in the one list the turn numbered — the conversations from zero, then the documents — so a
/// number outside it names nothing and takes its statement with it. That is the whole of why the model is shown
/// numbers rather than identifiers: an out-of-range number is unusable, while an identifier a model wrote would be a
/// message of some other correspondence and would look valid.
/// </para>
/// </remarks>
internal static class ContactRelationshipReading
{
    private const string JsonFence = "```";

    /// <summary>Reads the card out of an agent's answer.</summary>
    /// <param name="answerText">What the agent wrote, which may be empty, fenced, or surrounded by prose.</param>
    /// <param name="correspondence">The correlation the turn published, in the order it numbered it.</param>
    /// <returns>The card, or <see cref="ContactRelationship.Nothing" /> where nothing survived.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="correspondence" /> is <see langword="null" />.</exception>
    internal static ContactRelationship Read(string? answerText, ContactCorrespondence correspondence)
    {
        ArgumentNullException.ThrowIfNull(correspondence);

        if (ReadDocument(answerText) is not { } document)
        {
            return ContactRelationship.Nothing;
        }

        var note = ToStatement(document.Note, correspondence);

        if (note is null)
        {
            return ContactRelationship.Nothing;
        }

        List<ContactRelationshipObservation> observations = [];

        Observe(observations, ContactRelationshipAspect.ActivePeriod, document.ActivePeriod, correspondence);
        Observe(observations, ContactRelationshipAspect.OpenItem, document.OpenItem, correspondence);
        Observe(observations, ContactRelationshipAspect.Case, document.Cases, correspondence);

        return ContactRelationship.Derived(
            note,
            ToStatement(document.NextAction, correspondence),
            observations);
    }

    /// <summary>Records one observation, where the correspondence backs what the model wrote under that heading.</summary>
    private static void Observe(
        List<ContactRelationshipObservation> observations,
        ContactRelationshipAspect aspect,
        ContactRelationshipStatementDocument? written,
        ContactCorrespondence correspondence)
    {
        if (ToStatement(written, correspondence) is { } statement)
        {
            observations.Add(new ContactRelationshipObservation(aspect, statement));
        }
    }

    /// <summary>Turns one written statement into a statement, or into nothing where the correspondence cannot back it.</summary>
    private static ContactRelationshipStatement? ToStatement(
        ContactRelationshipStatementDocument? written,
        ContactCorrespondence correspondence)
    {
        if (written is null)
        {
            return null;
        }

        var sources = (written.Sources ?? [])
            .Distinct()
            .Select(position => Cited(position, correspondence))
            .OfType<ContactRelationshipSource>()
            .ToArray();

        return ContactRelationshipStatement.Create(written.Text, sources);
    }

    /// <summary>Resolves one number the model wrote into what the turn published under it.</summary>
    /// <remarks>
    /// The conversations are numbered from zero and the documents run on from the end of them, which is exactly how
    /// <see cref="ContactRelationshipInstructions.ComposeRelationshipTurn" /> wrote them out. A number below zero or
    /// past the two lists together names nothing.
    /// </remarks>
    private static ContactRelationshipSource? Cited(int position, ContactCorrespondence correspondence)
    {
        if (position < 0)
        {
            return null;
        }

        if (position < correspondence.Threads.Count)
        {
            return ContactRelationshipSource.Conversation(correspondence.Threads[position]);
        }

        var document = position - correspondence.Threads.Count;

        return document < correspondence.Documents.Count
            ? ContactRelationshipSource.Document(correspondence.Documents[document])
            : null;
    }

    private static ContactRelationshipDocument? ReadDocument(string? answerText)
    {
        if (Unfenced(answerText) is not { } json)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, ContactRelationshipJsonContext.Default.ContactRelationshipDocument);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Finds the JSON object inside whatever the model wrote around it.</summary>
    /// <remarks>
    /// A model told to answer with one object still fences it, prefaces it, or writes a sentence after it often enough
    /// that treating any of those as a failed derivation would throw away a usable card. The outermost braces are what
    /// is read; anything either side of them is discarded unexamined.
    /// </remarks>
    private static string? Unfenced(string? answerText)
    {
        if (string.IsNullOrWhiteSpace(answerText))
        {
            return null;
        }

        var text = answerText.Replace(JsonFence, string.Empty, StringComparison.Ordinal);
        var opening = text.IndexOf('{', StringComparison.Ordinal);
        var closing = text.LastIndexOf('}');

        return opening >= 0 && closing > opening ? text[opening..(closing + 1)] : null;
    }
}
