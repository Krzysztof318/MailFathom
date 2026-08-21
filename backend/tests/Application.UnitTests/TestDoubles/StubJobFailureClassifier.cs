// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>A failure classifier that answers whatever the test decided, and remembers what it was asked about.</summary>
/// <remarks>
/// Hand-written rather than substituted because every test using it wants the same one-line arrangement — this verdict,
/// for whatever a handler raised — and because what the tests read back is the exception it was handed, which is state.
/// The verdict itself is the boundary being modelled: how a real failure is classified belongs to the implementation's
/// own tests, in the assembly that can see the failure types.
/// </remarks>
internal sealed class StubJobFailureClassifier(
    JobFailureClassification classification,
    string reason = "ClassifiedFailure") : IJobFailureClassifier
{
    /// <summary>Gets the failure the classifier was last handed, and <see langword="null" /> while it has been handed none.</summary>
    internal Exception? ClassifiedFailure { get; private set; }

    /// <inheritdoc />
    public JobFailureRecord Classify(Exception failure)
    {
        this.ClassifiedFailure = failure;

        return JobFailureRecord.Create(classification, reason);
    }
}
