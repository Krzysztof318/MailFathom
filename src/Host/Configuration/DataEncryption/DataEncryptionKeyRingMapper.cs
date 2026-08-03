// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.DataEncryption;

namespace MailFathom.Host.Configuration.DataEncryption;

/// <summary>Maps the bound <c>DataEncryption</c> section onto the settings the encryption adapter reads.</summary>
/// <remarks>
/// The adapter depends on a settings record rather than on the host's bindable options, which is what keeps the
/// configuration binder, its mutability, and its validation attributes out of infrastructure — the same split
/// <see cref="Persistence.DatabaseConnectionSettingsMapper" /> already applies to the database credential.
/// </remarks>
internal static class DataEncryptionKeyRingMapper
{
    /// <summary>Maps one bound snapshot.</summary>
    /// <param name="settings">The bound section.</param>
    /// <returns>The key ring as the adapter reads it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A key configuring no material is dropped rather than mapped with an absent reference. Startup already refuses
    /// such a snapshot, so this path is only reached while that refusal is being composed, and carrying a half-built
    /// key into the ring would replace a named configuration error with a null dereference.
    /// </remarks>
    internal static DataEncryptionKeyRingSettings Map(DataEncryptionOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new DataEncryptionKeyRingSettings(
            settings.ActiveKeyId,
            [.. settings.Keys
                .Where(key => key.Material is not null)
                .Select(key => new DataEncryptionKeyReference(key.KeyId, key.Material!))]);
    }
}
