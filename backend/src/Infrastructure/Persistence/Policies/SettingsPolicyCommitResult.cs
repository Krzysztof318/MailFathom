// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Policies;

/// <summary>How an attempt to commit a settings policy ended.</summary>
public enum SettingsPolicyCommitResult
{
    /// <summary>The policy moved to a new version, and every stored projection it changed moved with it.</summary>
    Committed = 0,

    /// <summary>The policy had already moved past the version the write was composed over, or the organization is gone.</summary>
    Superseded = 1,

    /// <summary>The policy governs more records than one write judges, so none was read and nothing was written.</summary>
    TooManyRecords = 2,

    /// <summary>The policy would have left a record it governs one the deployment could not serve, so nothing was written.</summary>
    RecordsLeftInvalid = 3,
}
