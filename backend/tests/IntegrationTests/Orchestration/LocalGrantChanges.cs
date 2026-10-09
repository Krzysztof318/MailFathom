// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access.Grants;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Forgets this process's computed grants when a grant changes, and tells no other replica, because the suite runs none.</summary>
/// <param name="grants">The grants this process computed.</param>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Resolved by the container the harness composes rather than constructed by name.")]
internal sealed class LocalGrantChanges(UserGrantCache grants) : IGrantChangeAnnouncer
{
    /// <inheritdoc />
    public Task AnnounceAsync()
    {
        grants.Forget();

        return Task.CompletedTask;
    }
}
