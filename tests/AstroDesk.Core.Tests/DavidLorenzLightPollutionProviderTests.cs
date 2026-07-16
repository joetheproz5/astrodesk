using System.IO.Compression;
using System.Net;
using AstroDesk.Core.Models;
using AstroDesk.Infrastructure.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstroDesk.Core.Tests;

public sealed class DavidLorenzLightPollutionProviderTests
{
    [Fact]
    public async Task GetConditionsAsync_DecodesAtlasTileAndCachesIt()
    {
        byte[] tile = new byte[(600 * 600) + 1];
        tile[0] = 2;
        tile[1] = 87;
        RecordingBinaryHandler handler = new(Compress(tile));
        using HttpClient client = new(handler)
        {
            BaseAddress = new Uri("https://djlorenz.github.io/"),
        };
        DavidLorenzLightPollutionProvider provider = new(
            client,
            NullLogger<DavidLorenzLightPollutionProvider>.Instance);
        GeoCoordinate coordinate = new(33.8938, 35.5018);

        LightPollutionConditions? first = await provider.GetConditionsAsync(coordinate);
        LightPollutionConditions? second = await provider.GetConditionsAsync(coordinate);

        Assert.NotNull(first);
        Assert.Equal(20.567, first.ArtificialBrightnessRatio, 3);
        Assert.Equal(18.666, first.MagnitudesPerSquareArcSecond, 3);
        Assert.Equal("6b", first.Zone);
        Assert.Equal("bright suburban sky", first.Description);
        Assert.Equal(2024, first.DataYear);
        Assert.NotNull(second);
        Assert.Equal(1, handler.RequestCount);
        Assert.NotNull(handler.RequestUri);
        Assert.EndsWith(
            "astronomy/binary_tiles/2024/binary_tile_44_20.dat.gz",
            handler.RequestUri.AbsoluteUri,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetConditionsAsync_ReturnsNullOutsideAtlasCoverage()
    {
        RecordingBinaryHandler handler = new([]);
        using HttpClient client = new(handler)
        {
            BaseAddress = new Uri("https://djlorenz.github.io/"),
        };
        DavidLorenzLightPollutionProvider provider = new(
            client,
            NullLogger<DavidLorenzLightPollutionProvider>.Instance);

        LightPollutionConditions? result = await provider.GetConditionsAsync(
            new GeoCoordinate(80, 20));

        Assert.Null(result);
        Assert.Equal(0, handler.RequestCount);
    }

    private static byte[] Compress(byte[] data)
    {
        using MemoryStream destination = new();
        using (GZipStream gzip = new(destination, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(data);
        }

        return destination.ToArray();
    }

    private sealed class RecordingBinaryHandler(byte[] responseBytes) : HttpMessageHandler
    {
        private readonly byte[] _responseBytes = responseBytes;

        public int RequestCount { get; private set; }

        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            RequestUri = request.RequestUri;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_responseBytes),
                });
        }
    }
}
