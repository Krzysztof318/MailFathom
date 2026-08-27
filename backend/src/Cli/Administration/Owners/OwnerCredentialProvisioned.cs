// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Administration.Owners;

/// <summary>What provisioning a credential produced.</summary>
/// <param name="CredentialId">The identifier the new credential carries, which every later act on it names.</param>
/// <remarks>The identifier and nothing else. The username is what the command sent and the password is what nothing may echo, so the one thing worth answering with is the one thing the command could not have known.</remarks>
internal sealed record OwnerCredentialProvisioned(Guid CredentialId);
