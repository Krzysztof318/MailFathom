// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>The links a user record carries to stored files, read from the record and rewritten through its administration.</summary>
/// <remarks>
/// A rewrite goes through <see cref="UserRecordAdministration" /> rather than to the row, so a link is judged by the binder
/// and by the rule refusing a file that is not the user's exactly as an edited record is.
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The dependency injection container materializes this service.")]
internal sealed class OwnPortraitLinks(
    IUserSettingsDocumentReader documents,
    UserRecordAdministration records) : IUserRecordFileLinks
{
    /// <inheritdoc />
    public async Task<IReadOnlySet<StoredFileId>> ReadLinkedFilesAsync(
        MailUserId user,
        CancellationToken cancellationToken) =>
        await this.FindPortraitAsync(user, cancellationToken) is { } portrait
            ? new HashSet<StoredFileId> { portrait }
            : new HashSet<StoredFileId>();

    /// <inheritdoc />
    public async Task<StoredFileId?> FindPortraitAsync(MailUserId user, CancellationToken cancellationToken) =>
        await documents.ReadAsync(user, cancellationToken) is { } record ? PortraitOf(record.Json) : null;

    /// <inheritdoc />
    public Task<PortraitRelinking> RelinkOwnPortraitAsync(StoredFileId? portrait, CancellationToken cancellationToken) =>
        records.RelinkOwnPortraitAsync(portrait, cancellationToken);

    /// <summary>Reads the portrait link a stored record carries.</summary>
    /// <param name="documentJson">The record as its row holds it.</param>
    /// <returns>The linked file, or <see langword="null" /> where the record links none.</returns>
    /// <exception cref="JsonException">Thrown when the row is not JSON.</exception>
    /// <remarks>
    /// The key is matched case-insensitively and the value parsed by <see cref="Guid.TryParse(string?, out Guid)" />,
    /// because that is how the configuration binder reads the same record: a link the binder accepts is a link this
    /// reads, so the sweep never takes a written link for none.
    /// </remarks>
    internal static StoredFileId? PortraitOf(string documentJson)
    {
        using var document = JsonDocument.Parse(documentJson);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return document.RootElement
            .EnumerateObject()
            .Where(property => string.Equals(
                    property.Name,
                    nameof(UserAccountOptions.Portrait),
                    StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            .Select(property => Guid.TryParse(property.Value.GetString(), out var file) && file != Guid.Empty
                ? StoredFileId.Create(file)
                : (StoredFileId?)null)
            .FirstOrDefault(file => file is not null);
    }
}
