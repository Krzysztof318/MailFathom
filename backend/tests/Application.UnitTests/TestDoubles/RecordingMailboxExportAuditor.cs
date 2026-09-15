// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Export;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps every act recorded, so a test can assert that an export left a trail rather than only an archive.</summary>
internal sealed class RecordingMailboxExportAuditor : IMailboxExportAuditor
{
    /// <summary>Gets the acts recorded, in the order they were recorded.</summary>
    internal List<MailboxExportAct> Acts { get; } = [];

    /// <inheritdoc />
    public Task RecordAsync(MailboxExportAct act, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(act);

        this.Acts.Add(act);

        return Task.CompletedTask;
    }
}
