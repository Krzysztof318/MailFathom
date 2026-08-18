// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Contacts;

/// <summary>States what erasing the collected half of the book removed.</summary>
/// <param name="ContactsErased">How many contacts of the collected origin went.</param>
/// <param name="AddressesErased">How many addresses went with them.</param>
/// <remarks>
/// The counts are what an owner reversing their mind about collection is owed: an answer saying how much of a record
/// about other people this deployment had built and has now disposed of, rather than a call that returned without
/// complaint. Erasing a book that had collected nobody is a completed erasure reporting two zeroes, for the reason
/// erasing a person the book does not hold is one. It names nobody, because what an erasure reports about people is
/// that they are gone.
/// </remarks>
public sealed record CollectedContactErasure(int ContactsErased, int AddressesErased);
