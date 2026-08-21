// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the corpus of adversarial mail, which several suites read and none of them owns.</summary>
/// <remarks>
/// A fault here would not fail where it is: an entry with no text, a name that does not resolve, or a demand no question
/// bound accepts would leave the suites reading it green while they exercised nothing. What is asserted is therefore the
/// shape every entry has to have for a suite to be able to use it, never the wording of any attack.
/// </remarks>
public sealed class AdversarialMailCorpusTests
{
    [Fact]
    public void All_TheCorpus_NamesEveryMessageOnce()
    {
        // Act
        var names = AdversarialMailCorpus.All.Select(static message => message.Name).ToArray();

        // Assert
        Assert.NotEmpty(names);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A theory reads the corpus by name, so a name that does not resolve is a case that silently tests nothing.</summary>
    [Fact]
    public void Named_EveryNameTheCorpusPublishes_ResolvesBackToItsMessage()
    {
        // Assert
        Assert.All(
            AdversarialMailCorpus.All,
            static message => Assert.Same(message, AdversarialMailCorpus.Named(message.Name)));
    }

    /// <summary>A theory over the corpus runs one case per attack, so a corpus that grew and theory data that did not would test the addition nowhere.</summary>
    [Fact]
    public void EveryName_TheTheoryData_CarriesOneCasePerMessage()
    {
        // Assert
        Assert.Equal(AdversarialMailCorpus.All.Count, AdversarialMailCorpus.EveryName.Count);
    }

    [Fact]
    public void All_EveryMessage_CarriesASubjectABodyAndADemand()
    {
        // Assert
        Assert.All(AdversarialMailCorpus.All, static message =>
        {
            Assert.False(string.IsNullOrWhiteSpace(message.Subject));
            Assert.False(string.IsNullOrWhiteSpace(message.Text));
            Assert.False(string.IsNullOrWhiteSpace(message.Demand));
        });
    }

    /// <summary>
    /// The demand is put to boundaries that refuse a control character, so one carrying a line break would be refused
    /// for its shape and the test using it would prove nothing about the escalation it was written to attempt.
    /// </summary>
    [Fact]
    public void All_EveryDemand_IsOneLineAndFreeOfControlCharacters()
    {
        // Assert
        Assert.All(
            AdversarialMailCorpus.All,
            static message => Assert.DoesNotContain(message.Demand, char.IsControl));
    }

    /// <summary>The identifier a message asks an answer to cite and the one a suite proves absent must be the same value.</summary>
    [Fact]
    public void FabricatedCitation_TheMessage_DemandsTheIdentifierTheCorpusPublishes()
    {
        // Assert
        Assert.Contains(
            AdversarialMailCorpus.FabricatedMessageId,
            AdversarialMailCorpus.FabricatedCitation.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            AdversarialMailCorpus.FabricatedMessageId,
            AdversarialMailCorpus.FabricatedCitation.Demand,
            StringComparison.Ordinal);
    }

    /// <summary>The account a message tries to reach and the one a suite proves unreachable must be the same value.</summary>
    [Fact]
    public void WidenedScope_TheMessage_NamesTheAccountTheCorpusPublishes()
    {
        // Assert
        Assert.Contains(
            AdversarialMailCorpus.UnservedAccountId,
            AdversarialMailCorpus.WidenedScope.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            AdversarialMailCorpus.UnservedAccountId,
            AdversarialMailCorpus.WidenedScope.Demand,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Named_ANameTheCorpusDoesNotHold_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => AdversarialMailCorpus.Named("NoSuchAttack"));
    }
}
