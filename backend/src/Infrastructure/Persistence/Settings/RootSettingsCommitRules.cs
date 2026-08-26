// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using System.Text.Json;

namespace MailFathom.Infrastructure.Persistence.Settings;

/// <summary>What a candidate persisted-configuration document has to satisfy before a statement is issued for it.</summary>
/// <remarks>
/// Pure decisions over the candidate, deliberately outside the writer that issues the statement: the statement needs a
/// database and is proved by the integration suite, while whether a document may be handed to one at all is decidable
/// here and belongs in the unit suite's measurement. It is the arrangement <see cref="RootSettingsReadFailures" /> has
/// with the reader, for the same reason.
/// </remarks>
public static class RootSettingsCommitRules
{
    /// <summary>Refuses a candidate no statement should be issued for.</summary>
    /// <param name="json">The candidate document.</param>
    /// <param name="expectedVersion">The version the candidate was composed over.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="json" /> is not JSON at all, is JSON of a shape no configuration layer composes from, or is past what the layer composes settings from.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="expectedVersion" /> is negative.</exception>
    /// <remarks>
    /// Every refusal here is an <see cref="ArgumentException" /> because every one of them is a candidate this build
    /// composed wrongly rather than anything the database has an opinion about. The <c>jsonb</c> cast refuses text that
    /// is not JSON and nothing beyond it, so a reader's <see cref="JsonException" /> is translated rather than allowed
    /// to escape as itself: the caller either supplied a document or did not, and one exception type says so.
    /// </remarks>
    public static void RefuseWhatCannotBeCommitted(string json, long expectedVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedVersion);

        int persistedOctets;

        try
        {
            RefuseWhatIsNotAnObject(json);

            persistedOctets = PersistedOctetsOf(json);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "The candidate configuration document is not JSON, so no statement is issued for it. The column would take it no further: its jsonb cast refuses exactly this, and the refusal belongs on the side that composed the document.",
                nameof(json),
                exception);
        }

        if (persistedOctets > RootSettingsDocument.MaximumOctets)
        {
            throw new ArgumentException(
                $"The candidate configuration document occupies {persistedOctets} octets as the database stores it, past the {RootSettingsDocument.MaximumOctets} this build composes settings from, so persisting it would leave a row the next start refuses.",
                nameof(json));
        }
    }

    /// <summary>Gets whether a candidate document fits what the layer composes settings from.</summary>
    /// <param name="json">The candidate document.</param>
    /// <returns><see langword="true" /> when it fits; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="json" /> is <see langword="null" />.</exception>
    /// <exception cref="JsonException">Thrown when the candidate is not JSON at all.</exception>
    public static bool FitsWhatIsComposedFrom(string json) => PersistedOctetsOf(json) <= RootSettingsDocument.MaximumOctets;

    /// <summary>Measures a candidate as the database will store it rather than as it was composed.</summary>
    /// <param name="json">The candidate document.</param>
    /// <returns>An upper bound on the octets the stored document occupies.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="json" /> is <see langword="null" />.</exception>
    /// <exception cref="JsonException">Thrown when the candidate is not JSON at all.</exception>
    /// <remarks>
    /// <para>
    /// The two directions have to agree, because there is one bound and the read is what enforces the other half of it:
    /// the reader measures <c>octet_length("Document"::text)</c>, which is PostgreSQL's own rendering of the value, and
    /// that rendering is not the compact form a candidate is composed as. It puts a space after every colon and after
    /// every comma, so a document of many small keys is stored materially larger than it was written — and a candidate
    /// accepted just under the bound on its compact length would persist a row the next start refuses to read.
    /// </para>
    /// <para>
    /// An upper bound rather than the exact figure, and deliberately so. Two properties per pair is one octet more than
    /// the first pair of each object actually takes, and the rendering can only shrink a document elsewhere — duplicate
    /// keys are dropped and insignificant whitespace is removed, neither of which a candidate composed here carries.
    /// The slack is a handful of octets against a megabyte, and it errs towards refusing a document early rather than
    /// storing one that cannot be read back.
    /// </para>
    /// </remarks>
    public static int PersistedOctetsOf(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var utf8 = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(utf8);
        var separators = 0;

        // Whether what is being read sits directly inside an array, which is what decides whether a value carries a
        // separator of its own: an object's value is already counted by the property name that introduced it.
        var enclosingIsArray = new Stack<bool>();
        enclosingIsArray.Push(false);

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    separators += 2;
                    break;

                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    separators += enclosingIsArray.Peek() ? 1 : 0;
                    enclosingIsArray.Push(reader.TokenType == JsonTokenType.StartArray);
                    break;

                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    enclosingIsArray.Pop();
                    break;

                default:
                    separators += enclosingIsArray.Peek() ? 1 : 0;
                    break;
            }
        }

        return utf8.Length + separators;
    }

    /// <summary>Refuses a candidate whose root is not an object, which no configuration layer can be composed from.</summary>
    /// <remarks>
    /// A configuration source publishes colon-delimited keys, and only an object has any. An array, a number, or a bare
    /// string is a valid <c>jsonb</c> value the column stores without complaint and the next start then refuses to
    /// read — a row that commits and stops the deployment, which is the shape of defect this whole type exists to catch
    /// before a statement is issued rather than after one has been.
    /// </remarks>
    private static void RefuseWhatIsNotAnObject(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));

        if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            return;
        }

        throw new ArgumentException(
            "The candidate configuration document is JSON whose root is not an object, so it carries no configuration keys at all. The column would store it, and the next start would refuse to read it: only an object composes into a configuration layer.",
            nameof(json));
    }
}
