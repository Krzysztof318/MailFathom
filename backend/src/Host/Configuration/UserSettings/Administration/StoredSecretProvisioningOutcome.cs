// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>What an administrative stored-secret write did.</summary>
internal enum StoredSecretProvisioningOutcome
{
    Stored = 0,
    UnknownUser = 1,
    KeyRingUnavailable = 2,
}
