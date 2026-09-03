// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Portraits;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>
/// Covers the three routes a person reads, replaces, and removes their own portrait over. What has to hold is that an
/// absent picture is answered plainly rather than refused, that an upload is judged by its octets rather than by what
/// the request declared them to be, that both refusals name what they refused against, and that a client already
/// holding the picture is told it has not changed instead of being served it again.
/// </summary>
public sealed class ClientPortraitEndpointTests
{
    private static readonly byte[] Png =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x0D, 0x0A];

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    [Fact]
    public async Task ReadAsync_APersonWhoSuppliedAPicture_ServesItUnderTheKindItIs()
    {
        // Arrange
        var context = Requesting();

        // Act
        var result = await ClientPortraitEndpoint.ReadAsync(
            SignedIn(StoreHolding(Png)),
            context,
            TestContext.Current.CancellationToken);

        // Assert
        var served = Assert.IsType<FileContentHttpResult>(result.Result);

        Assert.Equal("image/png", served.ContentType);
        Assert.Equal(Png, served.FileContents.ToArray());
    }

    /// <summary>A client draws the initials it already has, so an absent picture is a plain answer rather than a refusal it would have to read as one.</summary>
    [Fact]
    public async Task ReadAsync_APersonWhoSuppliedNone_AnswersThatThereIsNoneRatherThanRefusing()
    {
        // Arrange
        var store = Substitute.For<IOwnerPortraitStore>();
        store.ReadAsync(Arg.Any<MailOwnerId>(), Arg.Any<CancellationToken>())
            .Returns((ReadOnlyMemory<byte>?)null);

        // Act
        var result = await ClientPortraitEndpoint.ReadAsync(
            SignedIn(store),
            Requesting(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NoContent>(result.Result);
    }

    /// <summary>A portrait is personal data, so it is held only against a revalidation rather than cached freely for whatever a proxy decides.</summary>
    [Fact]
    public async Task ReadAsync_APictureItServes_AsksTheClientToRevalidateAndKeepsItPrivate()
    {
        // Arrange
        var context = Requesting();

        // Act
        await ClientPortraitEndpoint.ReadAsync(
            SignedIn(StoreHolding(Png)),
            context,
            TestContext.Current.CancellationToken);

        // Assert
        var caching = context.Response.GetTypedHeaders().CacheControl!;

        Assert.True(caching.Private);
        Assert.True(caching.NoCache);
        Assert.Null(caching.MaxAge);
    }

    /// <summary>
    /// The kind was proven from a signature rather than by decoding the file, so a picture that opens as one and holds
    /// markup after that must never be a page the browser sniffs its way into rendering on the deployment's own origin.
    /// It is the same defence a message's attachments are served with.
    /// </summary>
    [Fact]
    public async Task ReadAsync_APictureItServes_TellsTheBrowserNotToSniffItsTypeFromTheOctets()
    {
        // Arrange
        var context = Requesting();

        // Act
        await ClientPortraitEndpoint.ReadAsync(
            SignedIn(StoreHolding([.. Png, .. "<script>alert(1)</script>"u8])),
            context,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions.ToString());
    }

    /// <summary>
    /// What the acceptance calls avoiding a refetch on every screen that draws it: the served response names the
    /// octets, and a client presenting that name back is answered that nothing changed rather than the picture again.
    /// </summary>
    [Fact]
    public async Task ReadAsync_ARequestPresentingWhatItWasAlreadyServed_IsAnsweredThatNothingChanged()
    {
        // Arrange
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();

        var first = Requesting(services);
        var served = await ClientPortraitEndpoint.ReadAsync(
            SignedIn(StoreHolding(Png)),
            first,
            TestContext.Current.CancellationToken);

        await Assert.IsType<FileContentHttpResult>(served.Result).ExecuteAsync(first);

        var again = Requesting(services);
        again.Request.Headers.IfNoneMatch = first.Response.Headers.ETag;

        // Act
        var repeated = await ClientPortraitEndpoint.ReadAsync(
            SignedIn(StoreHolding(Png)),
            again,
            TestContext.Current.CancellationToken);

        await Assert.IsType<FileContentHttpResult>(repeated.Result).ExecuteAsync(again);

        // Assert
        Assert.NotEmpty(first.Response.Headers.ETag.ToString());
        Assert.Equal(StatusCodes.Status304NotModified, again.Response.StatusCode);
        Assert.Empty(((MemoryStream)again.Response.Body).ToArray());
    }

    /// <summary>Two different pictures are two different names, which is what makes a replaced portrait reach the next screen rather than the next expiry.</summary>
    [Fact]
    public async Task ReadAsync_ADifferentPicture_IsNamedDifferently()
    {
        // Act
        var one = await ClientPortraitEndpoint.ReadAsync(
            SignedIn(StoreHolding(Png)),
            Requesting(),
            TestContext.Current.CancellationToken);

        var other = await ClientPortraitEndpoint.ReadAsync(
            SignedIn(StoreHolding(Jpeg)),
            Requesting(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(TagOf(one.Result), TagOf(other.Result));
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    public async Task ReplaceAsync_AnUploadOfAKindThisDeploymentStores_StoresTheOctetsAsTheyWereSupplied(string kind)
    {
        // Arrange
        var supplied = kind == "image/png" ? Png : Jpeg;
        var store = Substitute.For<IOwnerPortraitStore>();
        store.SaveAsync(Arg.Any<MailOwnerId>(), Arg.Any<OwnerPortrait>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await ClientPortraitEndpoint.ReplaceAsync(
            SignedIn(store),
            Uploading(supplied),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NoContent>(result.Result);
        await store.Received(1).SaveAsync(
            SyntheticMailOwner.Deployment,
            Arg.Is<OwnerPortrait>(portrait =>
                portrait!.Type.MediaType == kind && portrait.Content.ToArray().SequenceEqual(supplied)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The regression this exists for: a declared content type is a string an uploader wrote, so an upload that says
    /// it is an image and is not must be refused on what it holds rather than admitted on what it claims.
    /// </summary>
    [Fact]
    public async Task ReplaceAsync_OctetsThatAreNoImageDeclaredAsOne_AreRefusedNamingTheKindsOnOffer()
    {
        // Arrange
        var store = Substitute.For<IOwnerPortraitStore>();
        var upload = Uploading("<script>alert(1)</script>"u8.ToArray());
        upload.Request.ContentType = "image/png";

        // Act
        var result = await ClientPortraitEndpoint.ReplaceAsync(
            SignedIn(store),
            upload,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, refusal.StatusCode);
        Assert.Contains("image/jpeg", refusal.ProblemDetails.Detail!, StringComparison.Ordinal);
        Assert.Contains("image/png", refusal.ProblemDetails.Detail!, StringComparison.Ordinal);

        await store.DidNotReceive()
            .SaveAsync(Arg.Any<MailOwnerId>(), Arg.Any<OwnerPortrait>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The transport refuses the body and says nothing about why, so the one thing a person needs from it — the bound they went over — is stated here.</summary>
    [Fact]
    public async Task ReplaceAsync_AnUploadOverTheBound_IsRefusedNamingTheLimitItHit()
    {
        // Arrange
        var store = Substitute.For<IOwnerPortraitStore>();
        var upload = Requesting();
        upload.Request.Body = new RefusedBodyStream();

        // Act
        var result = await ClientPortraitEndpoint.ReplaceAsync(
            SignedIn(store),
            upload,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, refusal.StatusCode);
        Assert.Contains("1 MB", refusal.ProblemDetails.Detail!, StringComparison.Ordinal);

        await store.DidNotReceive()
            .SaveAsync(Arg.Any<MailOwnerId>(), Arg.Any<OwnerPortrait>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplaceAsync_ARequestCarryingNoBodyAtAll_IsRefused()
    {
        // Arrange
        var store = Substitute.For<IOwnerPortraitStore>();

        // Act
        var result = await ClientPortraitEndpoint.ReplaceAsync(
            SignedIn(store),
            Uploading([]),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);

        await store.DidNotReceive()
            .SaveAsync(Arg.Any<MailOwnerId>(), Arg.Any<OwnerPortrait>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The caller is a person whose row was erased under a credential that has not yet been withdrawn, and the answer to them is that there is nothing here of theirs.</summary>
    [Fact]
    public async Task ReplaceAsync_ADeploymentHoldingNoRecordForTheCaller_ReportsThatRatherThanStoring()
    {
        // Arrange
        var store = Substitute.For<IOwnerPortraitStore>();
        store.SaveAsync(Arg.Any<MailOwnerId>(), Arg.Any<OwnerPortrait>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await ClientPortraitEndpoint.ReplaceAsync(
            SignedIn(store),
            Uploading(Png),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            StatusCodes.Status404NotFound,
            Assert.IsType<NotFound<ProblemDetails>>(result.Result).Value!.Status);
    }

    [Fact]
    public async Task RemoveAsync_APersonTakingTheirPictureDown_RemovesItAndAnswersThatThereIsNone()
    {
        // Arrange
        var store = Substitute.For<IOwnerPortraitStore>();

        // Act
        var result = await ClientPortraitEndpoint.RemoveAsync(
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status204NoContent, result.StatusCode);
        await store.Received(1).RemoveAsync(SyntheticMailOwner.Deployment, Arg.Any<CancellationToken>());
    }

    /// <summary>Removing what is not there leaves the caller with no portrait, which is the whole of what they asked for.</summary>
    [Fact]
    public async Task RemoveAsync_APersonWhoSuppliedNoPicture_AnswersTheSameWay()
    {
        // Arrange
        var store = Substitute.For<IOwnerPortraitStore>();
        store.RemoveAsync(Arg.Any<MailOwnerId>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        // Act
        var result = await ClientPortraitEndpoint.RemoveAsync(
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status204NoContent, result.StatusCode);
    }

    private static string TagOf(IResult served) =>
        Assert.IsType<FileContentHttpResult>(served).EntityTag!.Tag.ToString();

    private static IOwnerPortraitStore StoreHolding(byte[] content)
    {
        var store = Substitute.For<IOwnerPortraitStore>();
        store.ReadAsync(Arg.Any<MailOwnerId>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<byte>(content));

        return store;
    }

    private static DefaultHttpContext Requesting(IServiceProvider? services = null)
    {
        var context = new DefaultHttpContext();

        if (services is not null)
        {
            context.RequestServices = services;
        }

        context.Response.Body = new MemoryStream();

        return context;
    }

    private static DefaultHttpContext Uploading(byte[] content)
    {
        var context = Requesting();
        context.Request.Body = new MemoryStream(content);

        return context;
    }

    private static OwnPortrait SignedIn(IOwnerPortraitStore store) => new(
        AccessAuthorizations.ForOwnerGranted(SyntheticMailOwner.Deployment, MailFathomPermission.MailRead),
        store);

    /// <summary>A body the server stops reading because it went past the bound the route published, which is how the transport reports an upload over the limit.</summary>
    private sealed class RefusedBodyStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }
    }
}
