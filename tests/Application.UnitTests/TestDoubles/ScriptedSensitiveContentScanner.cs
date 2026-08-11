// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>A detector that reports what a test told it to, and can be made to fail, hang, or overrun the text.</summary>
/// <remarks>
/// Hand-written rather than substituted because the redaction tests assert what was handed to the scanner, how many
/// calls ran at once, and what each failure mode produces, and a recorded list of texts reports all three without a
/// matcher.
/// </remarks>
internal sealed class ScriptedSensitiveContentScanner(SensitiveContentScannerKind scanner) : ISensitiveContentScanner
{
    private readonly List<string> scannedTexts = [];
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public SensitiveContentScannerKind Scanner { get; } = scanner;

    /// <summary>Gets or sets the findings every call reports.</summary>
    public IReadOnlyList<SensitiveContentFinding> Findings { get; set; } = [];

    /// <summary>Gets or sets the failure raised instead of findings, or <see langword="null" /> to always answer.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Gets or sets a gate every call waits on, so a test can hold a scan open while it starts another.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>Gets or sets whether a call waits for its own cancellation rather than answering.</summary>
    public bool NeverAnswers { get; set; }

    /// <summary>Gets the texts this scanner was handed, in order.</summary>
    public IReadOnlyList<string> ScannedTexts => this.scannedTexts;

    /// <summary>Gets a task completing once a first call is inside <see cref="ScanAsync" />.</summary>
    public Task Entered => this.entered.Task;

    /// <summary>Gets how many calls are inside <see cref="ScanAsync" /> right now.</summary>
    public int ConcurrentCalls { get; private set; }

    /// <summary>Gets the greatest number of calls that were ever inside <see cref="ScanAsync" /> at once.</summary>
    public int PeakConcurrentCalls { get; private set; }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SensitiveContentFinding>> ScanAsync(
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        this.scannedTexts.Add(text);
        this.ConcurrentCalls++;
        this.PeakConcurrentCalls = Math.Max(this.PeakConcurrentCalls, this.ConcurrentCalls);
        this.entered.TrySetResult();

        try
        {
            if (this.Gate is { } gate)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            if (this.NeverAnswers)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            return this.Failure is null ? this.Findings : throw this.Failure;
        }
        finally
        {
            this.ConcurrentCalls--;
        }
    }
}
