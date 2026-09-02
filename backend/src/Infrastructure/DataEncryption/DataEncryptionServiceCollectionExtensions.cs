// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.References;
using Microsoft.Extensions.DependencyInjection;

namespace MailFathom.Infrastructure.DataEncryption;

/// <summary>Registers what seals and opens the values MailFathom stores under its own protection.</summary>
public static class DataEncryptionServiceCollectionExtensions
{
    /// <summary>Registers the key ring and the encryptor over it.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="readSettings">Reads the currently published key ring settings for a resolved provider.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// Both are singletons because neither holds anything a scope owns: the ring resolves a key per operation and erases
    /// it, and the encryptor holds only the ring. The settings arrive through a delegate rather than as a value so a
    /// reloaded snapshot reaches the next operation, which is what lets an operator add a key to a running deployment.
    /// </remarks>
    public static IServiceCollection AddDataEncryption(
        this IServiceCollection services,
        Func<IServiceProvider, DataEncryptionKeyRingSettings> readSettings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(readSettings);

        services.AddSingleton(provider => new DataEncryptionKeyRing(
            () => readSettings(provider),
            () => provider.GetRequiredService<ISecretReferenceResolver>()));
        services.AddSingleton<FieldEncryptor>();

        return services;
    }
}
