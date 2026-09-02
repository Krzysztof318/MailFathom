// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Credentials.SecretStores;

/// <summary>Chooses the secret store of the platform the command is running on.</summary>
/// <remarks>
/// The command is published for Windows and Linux, so those are the two implementations that exist. Anything else
/// reaches <see cref="NoSecretStore" /> and keeps working against the sealed credentials file — macOS is deliberately
/// among them, because Keychain Services would be an implementation nothing this project publishes would ever run.
/// </remarks>
internal static class PlatformSecretStore
{
    /// <summary>Reports the store this machine offers.</summary>
    /// <returns>The store, which may be the one that reports having none.</returns>
    internal static IOperatorSecretStore ForThisMachine()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialManager();
        }

        return OperatingSystem.IsLinux() ? new SecretServiceKeyring() : NoSecretStore.Instance;
    }
}

/// <summary>The answer on a platform whose secret store this command does not reach.</summary>
/// <remarks>
/// An implementation that reports having no store rather than an absent registration, so the fallback is a behaviour
/// with one shape everywhere instead of a null every caller has to remember to check. It is also what a test uses when
/// the subject is the fallback rather than a platform.
/// </remarks>
internal sealed class NoSecretStore : IOperatorSecretStore
{
    /// <summary>Gets the one instance, which holds nothing and therefore needs no second.</summary>
    internal static NoSecretStore Instance { get; } = new();

    /// <inheritdoc />
    public string Description => "no secret store";

    /// <inheritdoc />
    public string? Read(ProfileSecret secret) => throw Absent();

    /// <inheritdoc />
    public void Write(ProfileSecret secret, string value) => throw Absent();

    /// <inheritdoc />
    public bool Clear(ProfileSecret secret) => throw Absent();

    private static SecretStoreUnavailable Absent() =>
        new("this platform offers no secret store the command reaches");
}
