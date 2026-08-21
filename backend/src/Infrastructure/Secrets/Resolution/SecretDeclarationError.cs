// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Secrets.Resolution;

/// <summary>One secret whose declared identity or lifetime an operator must correct.</summary>
/// <param name="ConfigurationPath">The setting to edit, which is the path the discovery walk reached the block by.</param>
/// <param name="Failure">What is wrong with the declaration. It is the whole permitted vocabulary: no material and no reference target accompanies it.</param>
public sealed record SecretDeclarationError(string ConfigurationPath, SecretDeclarationFailure Failure);
