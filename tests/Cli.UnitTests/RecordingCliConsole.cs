// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.UnitTests;

/// <summary>A terminal that supplies a credential and remembers what was written to it.</summary>
/// <remarks>Ordinary output and failures are kept apart, because which stream a line went to is part of the contract: a redirected invocation captures the first and the operator reads the second.</remarks>
internal sealed class RecordingCliConsole : ICliConsole
{
    /// <summary>Gets the lines written to standard output.</summary>
    internal List<string> Lines { get; } = [];

    /// <summary>Gets the lines written to standard error.</summary>
    internal List<string> Errors { get; } = [];

    /// <summary>Gets or sets the credential the command reads when it asks for one.</summary>
    internal string SecretToSupply { get; set; } = string.Empty;

    /// <summary>Gets the prompt the command last asked with, or <see langword="null" /> when it asked for nothing.</summary>
    internal string? LastPrompt { get; private set; }

    /// <summary>Gets or sets a value indicating whether a person is at this terminal to answer a question.</summary>
    /// <remarks>Set to <see langword="false" /> to model the case the flags exist for: input redirected from a pipe, where the answer would otherwise be read out of whatever the caller piped in.</remarks>
    internal bool AnswersQuestions { get; set; } = true;

    /// <summary>Gets or sets the answer this terminal gives to a question.</summary>
    internal bool AnswerToGive { get; set; }

    /// <summary>Gets the questions the command asked, in order.</summary>
    internal List<string> Questions { get; } = [];

    /// <inheritdoc />
    public bool CanConfirm => this.AnswersQuestions;

    /// <inheritdoc />
    public void WriteLine(string message) => this.Lines.Add(message);

    /// <inheritdoc />
    public void WriteError(string message) => this.Errors.Add(message);

    /// <inheritdoc />
    public string ReadSecret(string prompt)
    {
        this.LastPrompt = prompt;

        return this.SecretToSupply;
    }

    /// <inheritdoc />
    public bool Confirm(string question)
    {
        this.Questions.Add(question);

        return this.AnswerToGive;
    }
}
