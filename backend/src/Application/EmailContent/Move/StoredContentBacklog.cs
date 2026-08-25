// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Move;

/// <summary>How much of a deployment's stored content the database still holds.</summary>
/// <remarks>
/// Two figures rather than one, because they answer different questions and neither predicts the other: the count says
/// how much walking is left, and the volume says what the move will take off the database and put on the network. A
/// mailbox of ten thousand notifications and one of two hundred messages carrying attachments are the same job by the
/// first figure and nothing alike by the second.
/// </remarks>
/// <param name="PayloadCount">How many payloads of any kind are still held in the database.</param>
/// <param name="ByteCount">How many bytes of raw MIME they carry between them.</param>
public sealed record StoredContentBacklog(long PayloadCount, long ByteCount)
{
    /// <summary>The backlog of a deployment that holds nothing in the database.</summary>
    public static StoredContentBacklog Empty { get; } = new(0, 0);
}
