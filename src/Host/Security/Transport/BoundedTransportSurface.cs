// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Security.Transport;

namespace MailFathom.Host.Security.Transport;

/// <summary>One transport surface and the limits its traffic is admitted under.</summary>
/// <remarks>
/// The pair exists because the process-wide limiter is one object for the whole application and therefore has to be
/// registered knowing every surface at once: it recognizes a surface from the request's path and then needs that
/// surface's own permit count. Passing the two separately would leave the correspondence implicit in the order of two
/// lists.
/// </remarks>
/// <param name="Surface">The surface being bounded, which names its policy and publishes the route prefix its traffic arrives under.</param>
/// <param name="Limits">The limits that surface's traffic is admitted under, read from its own configuration section.</param>
internal sealed record BoundedTransportSurface(TransportSurface Surface, TransportRateLimits Limits);
