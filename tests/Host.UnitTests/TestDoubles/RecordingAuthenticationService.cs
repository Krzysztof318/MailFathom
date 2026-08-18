// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Notes which scheme each authentication went to, and otherwise does what the framework's own service does.</summary>
/// <remarks>
/// It wraps the real service rather than replacing it, so every handler still runs and every result is the one the
/// pipeline would have produced. The forwarding a policy scheme performs passes through here as well, because the
/// framework forwards by asking this service for the target scheme — which is what makes the record read as the chain
/// a credential actually travelled.
/// </remarks>
internal sealed class RecordingAuthenticationService : IAuthenticationService
{
    private readonly IAuthenticationService authentication;
    private readonly AuthenticationSchemeLog log;

    /// <summary>Initializes a new recording authentication service.</summary>
    /// <param name="authentication">The framework's own service, which does the work.</param>
    /// <param name="log">Where the schemes are recorded.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    internal RecordingAuthenticationService(IAuthenticationService authentication, AuthenticationSchemeLog log)
    {
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(log);

        this.authentication = authentication;
        this.log = log;
    }

    /// <inheritdoc />
    public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
    {
        this.log.Record(scheme);

        return this.authentication.AuthenticateAsync(context, scheme);
    }

    /// <inheritdoc />
    public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
        this.authentication.ChallengeAsync(context, scheme, properties);

    /// <inheritdoc />
    public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
        this.authentication.ForbidAsync(context, scheme, properties);

    /// <inheritdoc />
    public Task SignInAsync(
        HttpContext context,
        string? scheme,
        ClaimsPrincipal principal,
        AuthenticationProperties? properties) =>
        this.authentication.SignInAsync(context, scheme, principal, properties);

    /// <inheritdoc />
    public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
        this.authentication.SignOutAsync(context, scheme, properties);
}
