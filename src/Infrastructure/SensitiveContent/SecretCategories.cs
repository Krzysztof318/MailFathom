// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;

namespace MailFathom.Infrastructure.SensitiveContent;

/// <summary>The kinds of secret this scanner looks for, which are the names an operator configures it by.</summary>
/// <remarks>
/// <para>
/// The six named categories are what the corpus recognises by shape: a credential that says which service issued it,
/// or that is unmistakable in its own right. Each is on unless a deployment names a set of its own.
/// </para>
/// <para>
/// <see cref="HighEntropyString" /> is the seventh and is off by default, because it is the only one that reaches
/// beyond a shape. It is the recall layer for a credential with no format to recognise, and it is equally what turns a
/// base64 attachment fragment, a message identifier, and a tracking URL into findings. That is a trade an operator
/// should be choosing rather than discovering, so switching it on means naming the categories explicitly.
/// </para>
/// </remarks>
internal static class SecretCategories
{
    /// <summary>An API token, key, or session credential a named service issued and prefixes as its own.</summary>
    public static SensitiveContentCategory ProviderToken { get; } = SensitiveContentCategory.Create("ProviderToken");

    /// <summary>An access key or client secret for a cloud platform's own control plane.</summary>
    public static SensitiveContentCategory CloudAccessKey { get; } = SensitiveContentCategory.Create("CloudAccessKey");

    /// <summary>A private key or certificate bundle, whether armoured as PEM or encoded whole.</summary>
    public static SensitiveContentCategory PrivateKey { get; } = SensitiveContentCategory.Create("PrivateKey");

    /// <summary>A JSON Web Token, in its ordinary form or encoded a second time.</summary>
    public static SensitiveContentCategory JsonWebToken { get; } = SensitiveContentCategory.Create("JsonWebToken");

    /// <summary>A connection string carrying the credential it connects with.</summary>
    public static SensitiveContentCategory ConnectionString { get; } = SensitiveContentCategory.Create("ConnectionString");

    /// <summary>A URL carrying a credential in its user information, its path, or its query.</summary>
    public static SensitiveContentCategory CredentialUrl { get; } = SensitiveContentCategory.Create("CredentialUrl");

    /// <summary>A string dense enough to be a credential, recognised by its randomness rather than its shape.</summary>
    public static SensitiveContentCategory HighEntropyString { get; } = SensitiveContentCategory.Create("HighEntropyString");

    /// <summary>Every category, in the order a catalog declares them and a finding is reported under.</summary>
    public static IReadOnlyList<SensitiveContentCategory> All { get; } =
    [
        ProviderToken,
        CloudAccessKey,
        PrivateKey,
        JsonWebToken,
        ConnectionString,
        CredentialUrl,
        HighEntropyString,
    ];

    /// <summary>Reports whether a category is looked for by a deployment that names none of its own.</summary>
    /// <param name="category">The category to judge.</param>
    /// <returns><see langword="true" /> for every category but the entropy heuristic.</returns>
    public static bool IsDetectedByDefault(SensitiveContentCategory category) => category != HighEntropyString;
}
