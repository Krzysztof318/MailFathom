// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.OwnerSettings.Administration;

/// <summary>What an administrative stored-secret write did.</summary>
internal enum StoredSecretProvisioningOutcome
{
    Stored = 0,
    UnknownOwner = 1,
    KeyRingUnavailable = 2,
}
