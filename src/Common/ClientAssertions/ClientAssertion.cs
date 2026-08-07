// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Common.ClientAssertions;

/// <summary>The shape of the assertion a client signs with its private key to authenticate.</summary>
/// <remarks>
/// <para>
/// It is the arrangement RFC 7523 already describes and OpenID Connect deploys as <c>private_key_jwt</c>: the client
/// mints a short-lived JSON Web Token, signs it with a key only it holds, and presents it as an ordinary HTTP
/// <c>Bearer</c> credential. Nothing about the transport changes, so the header, the refusal, and the rate-limit
/// partition stay exactly what they are for a key or a token; the deployment holds one public key per client and there
/// is nothing on the host worth stealing from it.
/// </para>
/// <para>
/// The values here are the contract between the two halves, which is why they live in a project both of them reference
/// rather than beside either one. <c>mfctl</c> mints against them and the endpoint verifies against them, so a change to
/// what an assertion must carry cannot reach one side and miss the other.
/// </para>
/// <para>
/// The whole assertion is minted per request. Nothing about it is stored, resumed, or renewed, which is what makes the
/// operator's rotation a matter of replacing one registered public key rather than coordinating a secret across two
/// machines.
/// </para>
/// </remarks>
public static class ClientAssertion
{
    /// <summary>The media type an assertion declares in its <c>typ</c> header, which is what a MailFathom credential is recognized by.</summary>
    /// <remarks>
    /// <para>
    /// RFC 8725 section 3.11 asks for explicit typing wherever a deployment reads more than one kind of JSON Web Token,
    /// and this endpoint reads two: an access token an authorization server issued, and an assertion a client minted for
    /// itself. Declaring the type is what keeps one from ever being judged by the other's rules — an access token cannot
    /// be presented as an assertion, and an assertion cannot be replayed at an authorization server as anything.
    /// </para>
    /// <para>
    /// The name is MailFathom's own rather than a registered media type, because nothing outside this deployment and its
    /// own clients ever reads it. It is stated in full — with the <c>+jwt</c> structural suffix RFC 8417 established for
    /// exactly this — so a value that reaches a log or a diagnostic says what it is.
    /// </para>
    /// </remarks>
    public const string DeclaredType = "mailfathom-client-assertion+jwt";

    /// <summary>The audience an assertion presented to the MCP endpoint must name.</summary>
    /// <remarks>
    /// A URN rather than the endpoint's address, because the address is a deployment's own and the two surfaces have to
    /// be told apart without one. What it buys is the separation that matters here: an assertion minted to read a
    /// mailbox is refused by the administrative endpoint and the other way round, whatever the operator registered the
    /// key on. What it deliberately does not buy is separation between two deployments that registered the same public
    /// key, which is a client the operator of either can then speak for; a client that must not be is given a key pair
    /// per deployment.
    /// </remarks>
    public const string McpAudience = "urn:mailfathom:mcp";

    /// <summary>The audience an assertion presented to the administrative endpoint must name.</summary>
    /// <remarks>Separate from <see cref="McpAudience" /> for the reason the surfaces are separate at all: reading a mailbox and administering the service that reads it are different authorities.</remarks>
    public const string AdminAudience = "urn:mailfathom:admin";

    /// <summary>The longest window an assertion may claim between the moment it is verified and its own expiry.</summary>
    /// <remarks>
    /// <para>
    /// This is the setting a shared secret does not have. A captured API key works until an operator notices; a captured
    /// assertion stops working within this window whatever anyone does, which is the posture the method exists for. It
    /// is a constant rather than a setting for the reason every other acceptance rule this deployment applies to a
    /// signed credential is one: a deployment able to widen it would eventually be a deployment that had.
    /// </para>
    /// <para>
    /// It also bounds what the endpoint has to remember. An identifier is kept only until the assertion carrying it
    /// expires, so the replay store holds no more than one window's worth of a client's requests — which the surface's
    /// own rate limit already bounds.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(5);

    /// <summary>The lifetime <c>mfctl</c> mints an assertion with, well inside <see cref="MaximumLifetime" />.</summary>
    /// <remarks>Short enough that a captured assertion is spent almost immediately, and long enough to survive the clock disagreement between two machines that the endpoint tolerates.</remarks>
    public static readonly TimeSpan MintedLifetime = TimeSpan.FromSeconds(60);

    /// <summary>The longest replay identifier an assertion may carry.</summary>
    /// <remarks>
    /// The identifier is the one value a client chooses that the endpoint has to remember, so its length is the one
    /// thing about an assertion that decides how much memory a verified client can spend. A random 128-bit value
    /// encodes well inside this; nothing legitimate approaches it.
    /// </remarks>
    public const int IdentifierLengthLimit = 128;

    /// <summary>The claim naming the assertion's own replay identifier.</summary>
    public const string IdentifierClaimName = "jti";

    /// <summary>The claim naming the surface the assertion was minted for.</summary>
    public const string AudienceClaimName = "aud";

    /// <summary>The claim naming when the assertion stops being accepted.</summary>
    public const string ExpiresAtClaimName = "exp";
}
