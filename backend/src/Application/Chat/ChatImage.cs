// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Chat;

/// <summary>One picture a turn carries beside its text.</summary>
/// <param name="MediaType">What the octets are, read from the octets by whoever composed this rather than copied from anything a stranger declared.</param>
/// <param name="Content">The image itself, sent as it arrived.</param>
/// <remarks>
/// <para>
/// Octets and a media type, and nothing that describes them. Whether the format may be sent at all, how large a pixel
/// grid it may declare, and what it is being asked about are the composing caller's decisions and are taken before a
/// turn exists; what this boundary owns is the ceiling on how many octets one request carries, which
/// <see cref="ChatMessage" /> documents.
/// </para>
/// <para>
/// **An image is not scanned before it leaves.** The sensitive-content guard every outbound turn's text passes through
/// detects regions in a string, and there is no such thing to do to a photograph, so nothing pretends otherwise by
/// passing the octets through a guard that would return them unchanged. An image reaching this type is therefore an
/// image the deployment has decided to disclose whole.
/// </para>
/// <para>
/// Held as a <see cref="ReadOnlyMemory{T}" /> rather than an array, so a caller reading into a pooled buffer sends a
/// window of it without copying. The memory has to stay valid until the call it is passed to completes, which is what
/// a caller renting a buffer has to hold it across.
/// </para>
/// </remarks>
public sealed record ChatImage(string MediaType, ReadOnlyMemory<byte> Content);
