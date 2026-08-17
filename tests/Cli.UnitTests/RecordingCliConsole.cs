// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Output;

namespace MailFathom.Cli.UnitTests;

/// <summary>A terminal that supplies a credential and remembers what was written to it.</summary>
/// <remarks>
/// <para>
/// Ordinary output and everything else are kept apart, because which stream a line went to is part of the contract: a
/// redirected invocation captures the first and the operator reads the second. What a line reported about itself is
/// recorded beside that rather than instead of it, so an assertion about a failure still finds it among the lines
/// standard error carried.
/// </para>
/// <para>
/// A listing and a record are drawn by the production renderer with colour off, and what it drew is recorded line by
/// line. That is what keeps an assertion here a claim about text a person would read: the alternative — recording the
/// shapes and asserting on their cells — would pass while the drawing itself was wrong.
/// </para>
/// </remarks>
internal sealed class RecordingCliConsole : ICliConsole
{
    /// <summary>Gets the lines written to standard output.</summary>
    internal List<string> Lines { get; } = [];

    /// <summary>Gets the lines written to standard error, whatever each one reported about itself.</summary>
    internal List<string> Errors { get; } = [];

    /// <summary>Gets the lines written as a caution.</summary>
    internal List<string> Warnings { get; } = [];

    /// <summary>Gets the lines written as a failure.</summary>
    internal List<string> Failures { get; } = [];

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
    public void WriteNotice(string message) => this.Errors.Add(message);

    /// <inheritdoc />
    public void WriteWarning(string message)
    {
        this.Errors.Add(message);
        this.Warnings.Add(message);
    }

    /// <inheritdoc />
    public void WriteError(string message)
    {
        this.Errors.Add(message);
        this.Failures.Add(message);
    }

    /// <inheritdoc />
    public void Write(CliTable table) => this.Lines.AddRange(Draw(renderer => renderer.Write(table)));

    /// <inheritdoc />
    public void Write(CliDetails details) => this.Lines.AddRange(Draw(renderer => renderer.Write(details)));

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

    /// <summary>Draws one shape the way a redirected run would see it, and returns what was drawn line by line.</summary>
    /// <remarks>
    /// The colour is off and the width is the redirected one, which is what an assertion here needs: the recorded lines
    /// are then the text itself, with nothing in them that depends on the terminal the suite happens to run under. The
    /// final newline closes the last line rather than opening another, so it is dropped.
    /// </remarks>
    private static IReadOnlyList<string> Draw(Action<CliRenderer> drawing)
    {
        using StringWriter writer = new();

        drawing(new CliRenderer(writer, new CliTerminal(PermitsColour: false, CliTerminal.WidthWhenRedirected)));

        return [.. writer.ToString().Split('\n').SkipLast(1).Select(line => line.TrimEnd('\r'))];
    }
}
