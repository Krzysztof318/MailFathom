// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Policies;

/// <summary>A governed record as the policies governing it resolve it: the document it is served from, and where each of its properties came from.</summary>
/// <param name="EffectiveJson">The record's own document with every forced value and every default that applies written over it, which is what a binder reads.</param>
/// <param name="Properties">Every property a policy may name, in path order, each with its source and who holds it.</param>
/// <remarks>
/// The effective document exists for the length of a read and is never stored: the record keeps holding only what
/// somebody stated on it, so lifting a force or removing a default brings back exactly what was there.
/// </remarks>
internal sealed record ResolvedRecord(string EffectiveJson, IReadOnlyList<ResolvedProperty> Properties)
{
    /// <summary>Finds one property by its path.</summary>
    /// <param name="path">The path, compared without regard to case.</param>
    /// <returns>The property as it resolved.</returns>
    /// <exception cref="ArgumentException">Thrown when the path names no property a policy governs for this kind of record.</exception>
    internal ResolvedProperty Find(string path) =>
        this.Properties.FirstOrDefault(resolved => resolved.Property.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"'{path}' names no property a settings policy governs for this record.", nameof(path));
}
