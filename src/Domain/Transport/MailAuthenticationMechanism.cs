// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Domain.Transport;

/// <summary>Identifies a SASL authentication mechanism a mail transport policy may permit.</summary>
/// <remarks>
/// The set is closed on purpose so a policy can classify a mechanism as clear-text exhaustively instead of comparing
/// server-provided text. OAuth mechanisms are absent until mailbox OAuth authentication is implemented, and GSSAPI is
/// unsupported. New members are appended with the next value so a persisted numeric value never changes meaning.
/// </remarks>
public enum MailAuthenticationMechanism
{
    /// <summary>Sends the user name and password in clear text inside the SASL exchange.</summary>
    Plain = 0,

    /// <summary>Sends the user name and password in clear text as separate base64 challenges.</summary>
    Login = 1,

    /// <summary>Proves the password with an HMAC-MD5 challenge response.</summary>
    CramMd5 = 2,

    /// <summary>Proves the password with a digest challenge response.</summary>
    DigestMd5 = 3,

    /// <summary>Proves the password with a salted challenge response over SHA-1.</summary>
    ScramSha1 = 4,

    /// <summary>Proves the password with a salted challenge response over SHA-1 bound to the TLS channel.</summary>
    ScramSha1Plus = 5,

    /// <summary>Proves the password with a salted challenge response over SHA-256.</summary>
    ScramSha256 = 6,

    /// <summary>Proves the password with a salted challenge response over SHA-256 bound to the TLS channel.</summary>
    ScramSha256Plus = 7,

    /// <summary>Proves the password with a salted challenge response over SHA-512.</summary>
    ScramSha512 = 8,

    /// <summary>Proves the password with a salted challenge response over SHA-512 bound to the TLS channel.</summary>
    ScramSha512Plus = 9,

    /// <summary>Proves the password with the NTLM challenge-response exchange.</summary>
    Ntlm = 10,
}
