// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Administration.Owners;

/// <summary>Whether a credential should go on authenticating requests.</summary>
/// <param name="Enabled">The state to put the credential in.</param>
internal sealed record OwnerCredentialEnablementRequest(bool Enabled);
