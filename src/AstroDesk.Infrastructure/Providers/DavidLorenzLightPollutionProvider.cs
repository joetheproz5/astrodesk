using System.Collections.Concurrent;
using System.IO.Compression;
using AstroDesk.Core.Interfaces;
using AstroDesk.Core.Models;
using Microsoft.Extensions.Logging;

namespace AstroDesk.Infrastructure.Providers;

public sealed class DavidLorenzLightPollutionProvider(
    HttpClient httpClient,
    ILogger<DavidLorenzLightPollutionProvider> logger) : ILightPollutionProvider
{
    private const int DataYear = 2024;
    private const int TilePixels = 600;
    private const int ExpectedTileLength = (TilePixels * TilePixels) + 1;
    private readonly ConcurrentDictionary<string, byte[]> _tileCache = new();
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<DavidLorenzLightPollutionProvider> _logger = logger;

    public string Name => "David Lorenz Light Pollution Atlas";

    public async Task<LightPollutionConditions?> GetConditionsAsync(
        GeoCoordinate coordinate,
        CancellationToken cancellationToken = default)
    {
        if (coordinate.Latitude is < -65 or >= 75)
        {
            return null;
        }

        try
        {
            TileCoordinate tile = GetTileCoordinate(coordinate);
            string requestUri =
                $"astronomy/binary_tiles/{DataYear}/binary_tile_{tile.TileX}_{tile.TileY}.dat.gz";
            if (!_tileCache.TryGetValue(requestUri, out byte[]? data))
            {
                data = await DownloadTileAsync(requestUri, cancellationToken)
                    .ConfigureAwait(false);
                if (data is null)
                {
                    return null;
                }

                _tileCache.TryAdd(requestUri, data);
            }

            int compressedBrightness = DecodeBrightness(data, tile.PixelX, tile.PixelY);
            double brightnessRatio =
                (5d / 195d) * (Math.Exp(0.0195 * compressedBrightness) - 1);
            double magnitudesPerSquareArcSecond =
                22 - (5 * Math.Log(1 + brightnessRatio) / Math.Log(100));
            string zone = GetZone(brightnessRatio);

            return new LightPollutionConditions(
                brightnessRatio,
                magnitudesPerSquareArcSecond,
                zone,
                GetDescription(zone),
                Name,
                DataYear,
                DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
                InvalidDataException or
                IOException or
                ArgumentOutOfRangeException)
        {
            _logger.LogWarning(
                exception,
                "Light-pollution data is unavailable for the selected coordinate.");
            return null;
        }
    }

    private async Task<byte[]?> DownloadTileAsync(
        string requestUri,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient
            .GetAsync(requestUri, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        byte[] payload = await response.Content
            .ReadAsByteArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        byte[] data = IsGzip(payload) ? Decompress(payload) : payload;
        if (data.Length < ExpectedTileLength)
        {
            _logger.LogWarning(
                "Light-pollution tile {RequestUri} was shorter than expected.",
                requestUri);
            return null;
        }

        return data;
    }

    private static bool IsGzip(IReadOnlyList<byte> payload) =>
        payload.Count >= 2 && payload[0] == 0x1f && payload[1] == 0x8b;

    private static byte[] Decompress(byte[] payload)
    {
        using MemoryStream source = new(payload, writable: false);
        using GZipStream gzip = new(source, CompressionMode.Decompress);
        using MemoryStream destination = new(ExpectedTileLength);
        gzip.CopyTo(destination);
        return destination.ToArray();
    }

    private static TileCoordinate GetTileCoordinate(GeoCoordinate coordinate)
    {
        double longitudeFromDateLine = PositiveModulo(coordinate.Longitude + 180, 360);
        double latitudeFromStart = coordinate.Latitude + 65;
        int tileX = (int)Math.Floor(longitudeFromDateLine / 5) + 1;
        int tileY = (int)Math.Floor(latitudeFromStart / 5) + 1;
        int pixelX = (int)Math.Round(
            120 * (longitudeFromDateLine - (5 * (tileX - 1)) + (1d / 240d)),
            MidpointRounding.AwayFromZero);
        int pixelY = (int)Math.Round(
            120 * (latitudeFromStart - (5 * (tileY - 1)) + (1d / 240d)),
            MidpointRounding.AwayFromZero);

        return new TileCoordinate(
            tileX,
            tileY,
            Math.Clamp(pixelX, 1, TilePixels),
            Math.Clamp(pixelY, 1, TilePixels));
    }

    private static int DecodeBrightness(byte[] data, int pixelX, int pixelY)
    {
        int compressedBrightness =
            (128 * ToSigned(data[0])) + ToSigned(data[1]);
        for (int row = 1; row < pixelY; row++)
        {
            compressedBrightness += ToSigned(data[(TilePixels * row) + 1]);
        }

        int rowStart = TilePixels * (pixelY - 1);
        for (int column = 1; column < pixelX; column++)
        {
            compressedBrightness += ToSigned(data[rowStart + 1 + column]);
        }

        return compressedBrightness;
    }

    private static int ToSigned(byte value) => unchecked((sbyte)value);

    private static double PositiveModulo(double value, double divisor) =>
        ((value % divisor) + divisor) % divisor;

    private static string GetZone(double brightnessRatio) =>
        brightnessRatio switch
        {
            < 0.01 => "0",
            < 0.06 => "1a",
            < 0.11 => "1b",
            < 0.19 => "2a",
            < 0.33 => "2b",
            < 0.58 => "3a",
            < 1 => "3b",
            < 1.73 => "4a",
            < 3 => "4b",
            < 5.2 => "5a",
            < 9 => "5b",
            < 15.59 => "6a",
            < 27 => "6b",
            < 46.77 => "7a",
            _ => "7b",
        };

    private static string GetDescription(string zone) =>
        zone[0] switch
        {
            '0' => "pristine natural sky",
            '1' => "very dark sky",
            '2' => "dark rural sky",
            '3' => "rural sky",
            '4' => "rural/suburban sky",
            '5' => "suburban sky",
            '6' => "bright suburban sky",
            _ => "urban-bright sky",
        };

    private readonly record struct TileCoordinate(
        int TileX,
        int TileY,
        int PixelX,
        int PixelY);
}
