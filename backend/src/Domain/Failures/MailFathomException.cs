// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Failures;

/// <summary>Represents a failure MailFathom diagnosed itself, carrying a stable code and a message already safe to surface.</summary>
/// <remarks>
/// <para>
/// Deriving from this type is what separates a failure this system decided to raise from one a library, the runtime, or
/// a caller's own mistake produced. A boundary maps the first kind by <see cref="ErrorCode" /> and answers the second
/// with one generic code, so neither has to be recognized by a growing list of concrete types.
/// </para>
/// <para>
/// <b>Every message is written for an operator to read.</b> It must never carry a credential, a token, a certificate,
/// a host name, a remote folder path, the mechanisms a server advertised, message content, or any other personal data.
/// The constructors name their parameter for that obligation because they are the only route to
/// <see cref="Exception.Message" /> a derived type has, and because every exception here can reach a log, a startup
/// diagnostic, or operator-facing output. An account alias, a folder alias, a rule identity, a size, and a limit are
/// permitted: they are MailFathom's own configured names for things, chosen by the operator rather than by a remote party.
/// </para>
/// </remarks>
public abstract class MailFathomException : Exception
{
    /// <summary>Initializes a new failure with a message safe to surface.</summary>
    /// <param name="operatorSafeMessage">A message free of credentials, hosts, remote paths, message content, and personal data.</param>
    protected MailFathomException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    /// <summary>Initializes a new failure with a message safe to surface and the failure that revealed it.</summary>
    /// <param name="operatorSafeMessage">A message free of credentials, hosts, remote paths, message content, and personal data.</param>
    /// <param name="innerException">The failure this one was raised for.</param>
    /// <remarks>An inner exception is diagnostic detail for a log. A boundary that serializes a failure reports <see cref="ErrorCode" /> and never reaches into it.</remarks>
    protected MailFathomException(string operatorSafeMessage, Exception innerException)
        : base(operatorSafeMessage, innerException)
    {
    }

    /// <summary>Gets the stable code identifying this failure to a boundary that must report it without naming a type.</summary>
    public abstract MailFathomErrorCode ErrorCode { get; }
}
