// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;
using MailFathom.Application.Preferences;

namespace MailFathom.Infrastructure.Persistence.Preferences;

/// <summary>The <c>jsonb</c> document one person's client preferences are stored as.</summary>
/// <param name="TelemetryEnabled">What they said about telemetry, or nothing where they never said.</param>
/// <param name="Theme">What they chose the client to be painted in, or nothing where they never chose.</param>
/// <param name="OpenMailInTabs">What they said about tabs, or nothing where they never said.</param>
/// <remarks>
/// <para>
/// Sparse, and every member is therefore optional: a key the document does not carry reads as that preference's own
/// default rather than as an absent value. That is what lets a build publishing one more preference read a document
/// written before it existed, and what keeps this type from having to be migrated alongside the column.
/// </para>
/// <para>
/// It is the persistence shape rather than the application one, which is why it exists beside
/// <see cref="ClientPreferences" /> instead of that record being serialized directly. The application record answers
/// what a person's client does and never has an unanswered preference in it; this one is what the row holds, and the
/// mapping between them is where an unwritten key becomes an answer.
/// </para>
/// <para>
/// A key nothing here binds is ignored on the way in rather than refused, because the strict binding belongs at the
/// boundary a person writes through: what reaches this type has already been through it, and a document holding a key
/// this build does not know is one a later build wrote.
/// </para>
/// </remarks>
internal sealed record ClientPreferencesDocument(
    bool? TelemetryEnabled,
    ClientThemeChoice? Theme,
    bool? OpenMailInTabs)
{
    /// <summary>How the column is written and read, which is fixed here rather than inherited from a host's own options.</summary>
    /// <remarks>
    /// The persisted shape is a contract with the next start rather than with a client, so it must not move because
    /// something reconfigured the transport's serializer. Naming the policy here is what keeps the two apart, and
    /// writing every member — including one holding a default — keeps a stored document a complete statement of what
    /// its writer meant rather than a difference from whatever the defaults were on the day.
    /// </remarks>
    private static readonly JsonSerializerOptions StoredFormat = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Renders one person's preferences as the document the row holds.</summary>
    /// <param name="preferences">What they set.</param>
    /// <returns>The JSON object to store.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="preferences" /> is <see langword="null" />.</exception>
    public static string Render(ClientPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var document = new ClientPreferencesDocument(
            preferences.TelemetryEnabled,
            preferences.Theme,
            preferences.OpenMailInTabs);

        return JsonSerializer.Serialize(document, StoredFormat);
    }

    /// <summary>Reads a stored document back as the preferences a client is answered with.</summary>
    /// <param name="json">The JSON object the row holds.</param>
    /// <returns>What the document states, with every key it does not carry answered by that preference's default.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="json" /> is <see langword="null" />.</exception>
    /// <exception cref="JsonException">Thrown when the row is not a document of preferences, which is a row something other than this store wrote.</exception>
    public static ClientPreferences Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var document = JsonSerializer.Deserialize<ClientPreferencesDocument>(json, StoredFormat)
            ?? throw new JsonException("A stored preferences row is a JSON object rather than a null literal.");

        return new ClientPreferences(
            document.TelemetryEnabled ?? ClientPreferences.Unset.TelemetryEnabled,
            document.Theme ?? ClientPreferences.Unset.Theme,
            document.OpenMailInTabs ?? ClientPreferences.Unset.OpenMailInTabs);
    }
}
