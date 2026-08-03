---
name: closed-enumeration
description: Use when modelling a fixed set of named values that carries data, behavior, or a published identity, and a C# enum would not be enough.
license: Apache-2.0
metadata:
  author: Krzysztof Kasprowicz
  repository: https://github.com/Krzysztof318/MailFathom
---

# Closed Enumeration

A closed enumeration is a `readonly record struct` with a private constructor and one static member per value. It is the repository's answer when a C# `enum` cannot carry what the value has to carry.

## Choose between an enum and this pattern first

Use a plain `enum` when the members are nothing but names the process reads: a mode, a state, a discriminator on a result type. `RemoteEmailContentFetchOutcome` and `MailTransportSecurityViolation` are enums and should stay enums. An enum is switchable with compiler-checked exhaustiveness, and that check is worth more than anything this pattern adds.

Reach for a closed enumeration when at least one of these holds:

- **A member carries data.** `MailAuthenticationMechanism` carries its registered SASL name and whether it transmits credentials in clear text. Keeping that in a separate lookup table lets the two drift apart.
- **The value has a published identity.** `MailFathomErrorCode` is the five-digit number an operator reads in a log and matches in an alert. An enum's ordinal means nothing outside the assembly, and its member name changes with a rename the identity is meant to survive.
- **A member needs behavior.** A property or method that answers a question about the value belongs on the value.
- **The value is parsed from outside the process or serialized out of it.** Configuration, JSON, and a wire format all need a stable representation the type owns rather than one a converter guesses.

If none holds, write the enum. Do not reach for this pattern because a set "might grow"; a set that grows is exactly what an enum handles well.

## The shape

Follow `src/Domain/Transport/MailAuthenticationMechanism.cs` and `src/Domain/Failures/MailFathomErrorCode.cs`. Both are the same shape:

```csharp
[JsonConverter(typeof(SampleValueJsonConverter))]
public readonly record struct SampleValue
{
    private SampleValue(string identity) => this.identity = identity;

    private readonly string? identity;

    #region Category — group members when the set has structure

    /// <summary>Gets ...</summary>
    public static SampleValue First { get; } = new("first");

    #endregion

    /// <summary>Gets every supported value.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<SampleValue> All { get; } = [First];

    /// <summary>Gets whether this value names a supported member rather than the unusable struct default.</summary>
    public bool IsSpecified => this.identity is not null;

    /// <summary>Parses an outside-supplied identity.</summary>
    public static bool TryParse(string? identity, out SampleValue value) { ... }

    /// <inheritdoc />
    public override string ToString() => this.identity ?? "(unspecified)";
}
```

Requirements, each for a reason:

- **`readonly record struct`.** Equality, hashing, and copying come from the record; readonly keeps the value immutable.
- **Private constructor.** The declared members are the whole set. A public constructor would make the enumeration open and the type pointless.
- **`static ... { get; }` properties, not `static readonly` fields.** The repository uses properties for these throughout.
- **`#region` per category when the set has structure**, with the category named in the region header. A flat set needs no regions.
- **`All`, declared after every member.** A static initializer that runs before the members it lists would capture their defaults. Put `All` last and say so in its remarks.
- **`IsSpecified`.** A struct always has a reachable `default` and C# gives no way to forbid it. Do not try to prevent it. Report it, reject it where the invariant actually matters — a factory, a validator, a serializer — and say in the remarks where that is.
- **`TryParse` accepting only declared members.** An unmatched input yields the unspecified default, which is the value the caller already gets on failure, so the method needs no separate sentinel. Never reconstruct an undeclared value; an input nothing declares is unknown, not new.
- **`ToString`.** Return the identity, and a readable marker for the default. This is what reaches a log.
- **A `JsonConverter` on the type through `JsonConverterAttribute`**, so every serializer that meets the value uses it without per-call registration. Implement all four members — `Read`, `Write`, `ReadAsPropertyName`, `WriteAsPropertyName` — throw `JsonException` for a wrong token type, an undeclared value, and the unspecified default, and serialize the published identity rather than an ordinal.

## What to document

The type's `<remarks>` states why it is not an enum, what the identity is, and that `default` is reachable and what rejects it. When members carry a structured identity, such as a numbered code, state how the identity decomposes and that a value is allocated once and never reused or renumbered.

## Tests

Cover, in the boundary's own unit-test project: uniqueness of the identities, the structure of the identity when it has one, `TryParse` for a declared and an undeclared input, the default reporting itself as unspecified and rejecting whatever it must reject, `ToString`, and a JSON round trip as both a value and a property name including the rejection cases.

`tests/Domain.UnitTests/MailFathomErrorCodeTests.cs` is the worked example, and it asserts against `All` rather than reflecting over the type.
