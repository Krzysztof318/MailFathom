// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.IntegrationTests.ObjectStorage;

/// <summary>Every S3 operation and behaviour MailFathom's object backend depends on, and the test that exercises it.</summary>
/// <remarks>
/// <para>
/// This list is the compatibility claim written down. "S3-compatible" is not a standard anybody certifies against, so
/// what MailFathom can honestly say about a vendor is the set of requests it makes and the answers it relies on — and
/// somebody judging a second implementation needs that as a list rather than as a reading of the adapter's source.
/// </para>
/// <para>
/// Naming the exercising test in the entry rather than in a comment is what keeps the list from becoming prose. Each
/// name is a <see langword="nameof" /> over a real method, so a renamed test breaks the build, and
/// <c>S3SurfaceCoverageTests</c> holds the two claims a compiler cannot make: that every entry names a test the runner
/// will actually run, and that no test in the class is missing from the list.
/// </para>
/// <para>
/// The entries below are what the adapter issues, not what S3 defines. An operation MailFathom stops making leaves this
/// list in the change that stops making it; one it starts making arrives with the test that proves the endpoint answers
/// it.
/// </para>
/// </remarks>
internal static class S3Surface
{
    /// <summary>Gets the operations and behaviours the object backend cannot work without.</summary>
    internal static IReadOnlyList<S3Dependency> Dependencies { get; } =
    [
        new(
            "PutObject",
            "A payload written under a minted key is held by the endpoint before the write answers.",
            typeof(OrchestratedS3SurfaceTests),
            nameof(OrchestratedS3SurfaceTests.PlaceAsync_ForOnePayloadOfEachKind_WritesItUnderTheKindsOwnPrefixedKey)),
        new(
            "PutObject with x-amz-checksum-sha256",
            "The endpoint verifies the digest it is handed and refuses a payload that disagrees with it rather than storing one, which is what makes the digest a row carries a statement about the object rather than about the writer.",
            typeof(OrchestratedS3SurfaceTests),
            nameof(OrchestratedS3SurfaceTests.PutObject_WithAChecksumThePayloadDoesNotMatch_IsRefusedAndStoresNothing)),
        new(
            "PutObject with If-None-Match: *",
            "A key the endpoint already holds is refused rather than overwritten, which is what turns a collision between two minted keys into a failure instead of a silently replaced message.",
            typeof(OrchestratedS3SurfaceTests),
            nameof(OrchestratedS3SurfaceTests.PutObject_UnderAKeyTheEndpointAlreadyHolds_IsRefusedAndLeavesTheFirstPayload)),
        new(
            "GetObject",
            "The payload comes back byte for byte under the key it was written with.",
            typeof(OrchestratedS3SurfaceTests),
            nameof(OrchestratedS3SurfaceTests.FindAsync_ForAPayloadThisRunPlaced_AnswersEveryByteOfIt)),
        new(
            "GetObject for a key nothing holds",
            "An absent key is answered as absent rather than as a failure, which is what lets a reader grade it as a content defect instead of as an endpoint that could not be reached.",
            typeof(OrchestratedS3SurfaceTests),
            nameof(OrchestratedS3SurfaceTests.FindAsync_ForAKeyTheEndpointDoesNotHold_AnswersWithNothing)),
        new(
            "DeleteObject",
            "The endpoint holds nothing under the key afterwards, which is what carries a committed deletion through to the bucket.",
            typeof(OrchestratedS3SurfaceTests),
            nameof(OrchestratedS3SurfaceTests.DeleteAsync_ForAPayloadThisRunPlaced_LeavesTheEndpointHoldingNothingUnderItsKey)),
        new(
            "DeleteObject for a key nothing holds",
            "Accepted rather than refused, which is what makes both the deletion path and the reclamation safe to repeat after a crash.",
            typeof(OrchestratedS3SurfaceTests),
            nameof(OrchestratedS3SurfaceTests.DeleteAsync_ForAKeyTheEndpointDoesNotHold_Succeeds)),
        new(
            "ListObjectsV2 with Prefix",
            "The listing names nothing outside this deployment's own key prefix, which is the whole of what separates two deployments sharing one bucket — and the whole of reclamation's authority to delete.",
            typeof(OrchestratedS3SurfaceTests),
            nameof(OrchestratedS3SurfaceTests.ListAsync_WithAnObjectWrittenOutsideThePrefix_NamesOnlyWhatIsBeneathIt)),
        new(
            "ListObjectsV2 with MaxKeys and ContinuationToken",
            "A bounded page reports whether the listing was truncated and hands back the token the next one is asked for, which is what carries a sweep across a bucket nothing may read whole.",
            typeof(OrchestratedS3SurfaceTests),
            nameof(OrchestratedS3SurfaceTests.ListAsync_OverMoreObjectsThanOnePageHolds_PagesThroughThemWithoutRepeatingOne)),
        new(
            "ListObjectsV2 LastModified",
            "Every listed object states the moment its age is measured from, which is what the reclamation age floor compares against and therefore what keeps a write still in flight from being swept.",
            typeof(OrchestratedS3SurfaceTests),
            nameof(OrchestratedS3SurfaceTests.ListAsync_ForAPayloadThisRunPlaced_StatesTheMomentTheEndpointRecordedItAt)),
        new(
            "ListObjectsV2 beneath a prefix nothing is under",
            "An empty listing rather than an error, which is the answer a deployment that has stored nothing yet gets on every sweep.",
            typeof(OrchestratedS3SurfaceTests),
            nameof(OrchestratedS3SurfaceTests.ListAsync_BeneathAPrefixNothingIsWrittenUnder_AnswersAnEmptyPage)),
        new(
            "Path-style addressing against a stated endpoint, signed for a stated region",
            "Every request above is addressed as bucket-in-path against a host that answers no bucket subdomain, and signed for a region the endpoint does not otherwise know about; a server disagreeing about either refuses the signature rather than the request.",
            typeof(OrchestratedS3SurfaceTests),
            nameof(OrchestratedS3SurfaceTests.FindAsync_ForAPayloadThisRunPlaced_AnswersEveryByteOfIt)),
        new(
            "A refused credential",
            "The endpoint answers a signature it will not accept with a code the failure classification reads as an authentication failure rather than as something to retry.",
            typeof(OrchestratedS3SurfaceTests),
            nameof(OrchestratedS3SurfaceTests.Classify_ForWhatTheEndpointAnswersAWrongCredentialWith_ReportsAnAuthenticationFailure)),
    ];
}

/// <summary>One S3 operation or behaviour the object backend depends on, and where that dependence is proved.</summary>
/// <param name="Operation">The request, named as the S3 API names it, together with the header or parameter that matters.</param>
/// <param name="Behaviour">What the adapter relies on the endpoint doing, and what breaks in MailFathom when it does something else.</param>
/// <param name="ExercisedBy">The test class that proves it.</param>
/// <param name="TestMethod">The test method within that class.</param>
internal sealed record S3Dependency(string Operation, string Behaviour, Type ExercisedBy, string TestMethod);
