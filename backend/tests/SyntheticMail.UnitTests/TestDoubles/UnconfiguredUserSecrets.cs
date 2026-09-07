// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.UnitTests.TestDoubles;

/// <summary>The user-secrets store of a machine on which this project was never configured.</summary>
/// <remarks>
/// A run whose local file is absent reads the machine's store instead, which is what the failure message tells a
/// developer to write — so on the machine of anybody who did, every test of the unconfigured case would find a
/// configuration and pass nothing. The store's path is an argument to the readers for exactly that reason, and this
/// is the value a test states for it: a path under the test's own output directory that nothing writes.
/// </remarks>
internal static class UnconfiguredUserSecrets
{
    /// <summary>Reports a store path that does not exist and will not come to.</summary>
    /// <returns>The path.</returns>
    internal static string Store() =>
        Path.Combine(AppContext.BaseDirectory, $"no-user-secrets-{Guid.NewGuid():N}.json");
}
