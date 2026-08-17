// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Reports that work this suite drives is MailFathom's own rather than anybody's request.</summary>
/// <remarks>
/// A composition root supplies this port from the request being served, and this suite starts no request: it composes
/// the production registrations and calls the classes under them directly, which is the same thing a worker does. The
/// answer is therefore the process identity rather than a caller, so a use case that requires a permission is refused
/// here exactly as it would be in a worker — which is the behaviour a test would want to meet rather than one to
/// arrange around.
/// </remarks>
internal sealed class ProcessAuthorizedPrincipalSource : IAuthorizedPrincipalSource
{
    /// <inheritdoc />
    public AuthorizedPrincipal? Current => AuthorizedPrincipal.Process;
}
