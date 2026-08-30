// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation;

/// <summary>What an AI-generated message was generated under: its language and its topic.</summary>
/// <param name="Language">The code of the language the message's content was asked for, as the invocation named it.</param>
/// <param name="Topic">The subject matter the message's content was asked for.</param>
/// <remarks>
/// Carried beside the message for the reason the body's decoy is: the run has to be able to say what a message
/// carries without a reader having to recognise it — a listing that printed a Polish subject and its language only
/// as the language happened to be readable would be telling half of what the seed decided. A message the seeded
/// vocabulary writes carries none.
/// </remarks>
internal sealed record SyntheticEmailAiOrigin(string Language, SyntheticMailTopic Topic);
