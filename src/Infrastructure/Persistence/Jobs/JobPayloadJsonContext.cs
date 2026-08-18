// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.Jobs;

namespace MailFathom.Infrastructure.Persistence.Jobs;

/// <summary>The payload contracts a stored job document is written and read through, generated rather than discovered.</summary>
/// <remarks>
/// <para>
/// Source-generated because a stored document is one of the few things in this system a reflection-based serializer
/// would be handed at all, and it is exactly the wrong place for one: the shapes it may encounter would then be
/// whatever the assembly happens to contain rather than the set stated here, and every payload record added later would
/// serialize without anybody having reviewed what it carries.
/// </para>
/// <para>
/// One entry per declared <see cref="JobType" />, which is the pairing the type exists to publish. Adding a type
/// therefore adds a line here, and a payload record with no line fails to compile into the store rather than falling
/// back to reflection.
/// </para>
/// <para>
/// Names are camel-cased and matched without regard to case, because the document is read by an operator looking at a
/// queue as well as by this process.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(EmailOccurrenceJobPayload))]
[JsonSerializable(typeof(MailAccountJobPayload))]
[JsonSerializable(typeof(StoredMailScopeJobPayload))]
internal sealed partial class JobPayloadJsonContext : JsonSerializerContext;
