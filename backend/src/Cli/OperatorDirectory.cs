// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli;

/// <summary>Where the command keeps what belongs to the operator on the machine they run it from.</summary>
/// <remarks>
/// <para>
/// One directory rather than one per kind of file, so the command's whole footprint on a machine is a single path an
/// operator can find, back up, exclude from a backup, or delete. The credential store, the key that seals it, and the
/// invocation log all live here.
/// </para>
/// <para>
/// <see cref="Environment.SpecialFolder.ApplicationData" /> resolves to <c>$XDG_CONFIG_HOME</c> or <c>~/.config</c> on
/// Linux and to <c>%APPDATA%</c> on Windows, so one call gives the right per-user location on both without a platform
/// branch here. The log is state rather than configuration and would belong under <c>$XDG_STATE_HOME</c> if the
/// specification were followed to the letter; keeping the three files together is worth more to the person looking for
/// them than splitting them across two specifications to satisfy one platform's convention.
/// </para>
/// </remarks>
internal static class OperatorDirectory
{
    /// <summary>The name the command's own directory carries inside the per-user location.</summary>
    internal const string Name = "MailFathom";

    /// <summary>Reports the directory for the operator running the command.</summary>
    /// <returns>The absolute path, which may not exist yet.</returns>
    internal static string Resolve() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        Name);
}
