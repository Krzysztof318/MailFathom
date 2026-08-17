// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Contacts.Failures;

/// <summary>The failure raised when a request names a contact with text the book cannot resolve anybody by.</summary>
/// <remarks>
/// <para>
/// A protocol adapter converts a caller's text into the identity a read or a write is expressed in, and text that is
/// neither an identifier this system issued nor a usable address is refused there, before anything is looked up. The
/// failure lives in this assembly rather than in the adapter because the identity it is about is the book's, so a
/// second entrypoint refusing the same text reports it under the same code instead of inventing one.
/// </para>
/// <para>
/// It is deliberately distinct from an answer reporting that the book holds nobody. That one answers a request that
/// named somebody, and a caller acts on it by looking somebody else up; this one says the request named nobody at all,
/// which no repeated read will change.
/// </para>
/// <para>
/// No message here carries the refused text. An identifier a caller invented says nothing an operator needs that the
/// code does not already say, and an address is somebody's address whether or not it was mistyped.
/// </para>
/// </remarks>
public sealed class ContactIdentifierMalformedException : MailFathomException
{
    private ContactIdentifierMalformedException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.ContactIdentifierMalformed;

    /// <summary>Refuses text that is no identifier this system issues.</summary>
    /// <returns>The failure to raise.</returns>
    public static ContactIdentifierMalformedException NotAnIdentifier() =>
        new("The contact identifier is not one this system issues.");

    /// <summary>Refuses text that is no address anybody could be recorded as using.</summary>
    /// <returns>The failure to raise.</returns>
    public static ContactIdentifierMalformedException NotAnAddress() =>
        new("The contact address is not a usable mail address.");

    /// <summary>Refuses a lookup that names a contact in neither way, or in both at once.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// Naming both is refused rather than resolved by precedence, because the two can name different people and a caller
    /// reading the answer would have no way to tell which of its two questions was answered.
    /// </remarks>
    public static ContactIdentifierMalformedException NotExactlyOneWay() =>
        new("A contact lookup names either an identifier or an address, and exactly one of the two.");
}
