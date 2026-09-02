// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Contacts;

/// <summary>Everything a deployment holds about one person, as of the instant the export was taken.</summary>
/// <param name="Contact">The complete record, or <see langword="null" /> when the book holds no such contact.</param>
/// <param name="ProducedAt">When the export was produced, absent together with the contact.</param>
/// <remarks>
/// The data-subject access path. The command prints this document as it arrived rather than a rendering of it, so what
/// an owner hands to the person who asked is the deployment's own answer and not one surface's summary of it.
/// </remarks>
internal sealed record ContactExport(
    [property: JsonPropertyName("contact")] ContactRecord? Contact,
    [property: JsonPropertyName("producedAt")] DateTimeOffset? ProducedAt);
