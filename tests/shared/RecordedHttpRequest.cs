// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Text;

namespace MailMcp.TestSupport;

/// <summary>
/// An immutable snapshot of one request observed by <see cref="FakeHttpMessageHandler" />.
/// </summary>
/// <remarks>
/// The snapshot exists because the caller owns the <see cref="HttpRequestMessage" />: <see cref="HttpClient" /> leaves
/// it alive after the response completes, so the code under test is free to dispose it, mutate its headers, or reuse
/// it before an assertion runs. A double that kept the message itself, or its header collections, would hand
/// assertions state that changed underneath them. Every value here is copied at send time, so a recorded request
/// stays readable and unchanged for the whole test.
/// <para>
/// Assert against the members, not against a whole instance. The compiler-generated equality compares the header
/// dictionaries and the payload by reference, so two snapshots holding equal values are not equal to each other.
/// </para>
/// </remarks>
/// <param name="Method">The request method.</param>
/// <param name="RequestUri">The request URI, or <see langword="null" /> when the caller left it unset.</param>
/// <param name="Headers">The request headers, keyed case-insensitively as the HTTP grammar defines them.</param>
/// <param name="ContentHeaders">The entity headers of the request body, empty when the request carried no body.</param>
/// <param name="Content">The request body as sent, empty when the request carried no body.</param>
internal sealed record RecordedHttpRequest(
    HttpMethod Method,
    Uri? RequestUri,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ContentHeaders,
    ReadOnlyMemory<byte> Content)
{
    /// <summary>
    /// Decodes the recorded body as UTF-8 text.
    /// </summary>
    /// <returns>The body as text, or an empty string when the request carried no body.</returns>
    /// <remarks>
    /// Only call this for a body the test itself encoded as UTF-8, which covers JSON and form payloads. A body sent
    /// under another charset, or a binary payload, is asserted against <see cref="Content" /> directly.
    /// </remarks>
    public string ContentAsUtf8String() => Encoding.UTF8.GetString(this.Content.Span);
}
