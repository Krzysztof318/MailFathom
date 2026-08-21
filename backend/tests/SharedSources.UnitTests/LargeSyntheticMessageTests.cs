// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the message every cost claim in this repository is measured against.</summary>
/// <remarks>
/// Every allocation budget is a share of this message's length, so a generator change that quietly made it small would
/// leave each of those budgets satisfied by a path that buffered everything. The fixture refuses to compose a message
/// below its own floor, and this is what makes that refusal a failure somebody sees rather than one nobody runs.
/// </remarks>
public sealed class LargeSyntheticMessageTests
{
    [Fact]
    public void RawMime_ComposedFromTheCorpus_IsALargeMultipartMessage()
    {
        // Act
        var rawMime = LargeSyntheticMessage.RawMime;

        // Assert
        Assert.True(
            rawMime.Length > 2 * 1024 * 1024,
            $"The measured message is {rawMime.Length} bytes, too small for a budget stated as a share of it.");
    }

    [Fact]
    public void AsStored_ComposedFromTheCorpus_RecordsAPayloadThatVerifies()
    {
        // Act
        var stored = LargeSyntheticMessage.AsStored();

        // Assert
        Assert.Null(stored.FindIntegrityDefect());
    }

    [Fact]
    public void AsExtractedText_ComposedFromTheCorpus_CarriesTextWorthCutting()
    {
        // Act
        var text = LargeSyntheticMessage.AsExtractedText();

        // Assert
        Assert.True(text.HasText);
        Assert.True(
            text.TrimmedText!.Length > 10_000,
            $"The measured text is {text.TrimmedText.Length} characters, too short for a chunking budget over it to mean anything.");
    }
}
