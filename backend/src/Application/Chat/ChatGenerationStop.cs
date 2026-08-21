// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Chat;

/// <summary>Names why the model stopped generating, on a call that produced text.</summary>
/// <remarks>
/// <para>
/// This sits on the answer rather than among the failures deliberately. A truncated generation and a generation the
/// provider's safety system cut short are both answers: the text before the stop is real, the call has been paid for,
/// and discarding it would spend the money for nothing. Reporting them here also makes the guarantee the failures
/// cannot give — nothing repeats a call that returned an answer, so neither can ever be retried as though it were a
/// transport fault.
/// </para>
/// <para>
/// What a caller does with each differs, which is why they are distinguished at all. A completed answer is presented as
/// it stands; a truncated one is presented with the truncation stated, or asked for again with a longer output budget;
/// a withheld one is presented as a refusal rather than as an answer, because the text that survives it is a fragment
/// of reasoning the provider stopped mid-way.
/// </para>
/// </remarks>
public enum ChatGenerationStop
{
    /// <summary>The model finished what it had to say.</summary>
    Completed = 0,

    /// <summary>The model was cut off by the output budget this deployment configured.</summary>
    /// <remarks>The answer is a prefix of the one the model was producing, so it ends mid-sentence rather than short.</remarks>
    OutputLimitReached = 1,

    /// <summary>The provider's content filter stopped the generation.</summary>
    /// <remarks>Distinct from a request the provider refused outright: that one produces no call result at all and is a failure, while this one is an answer the provider began and then withheld the rest of.</remarks>
    ContentFiltered = 2,

    /// <summary>The provider named no reason.</summary>
    /// <remarks>Not an error. A provider is free to answer without saying why it stopped, and treating silence as completion would state something the provider did not.</remarks>
    Unreported = 3,
}
