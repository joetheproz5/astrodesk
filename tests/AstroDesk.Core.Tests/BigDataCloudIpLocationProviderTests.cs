using System.Net;
using System.Text;
using AstroDesk.Core.Models;
using AstroDesk.Infrastructure.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstroDesk.Core.Tests;

public sealed class BigDataCloudIpLocationProviderTests
{
    [Fact]
    public async Task GetCurrentAsync_MapsCoarseIpLocationAndTimeZone()
    {
        const string responseJson =
            """
            {
              "latitude": 33.84,
              "longitude": 35.53,
              "countryName": "Lebanon",
              "countryCode": "LB",
              "principalSubdivision": "Mohafazat Mont-Liban",
              "city": "Al Hadath",
              "locality": "Al Hadath",
              "localityInfo": {
                "informative": [
                  {
                    "name": "Asia/Beirut",
                    "description": "time zone"
                  }
                ]
              }
            }
            """;
        RecordingHttpMessageHandler handler = new(responseJson);
        using HttpClient client = new(handler)
        {
            BaseAddress = new Uri("https://api.bigdatacloud.net/"),
        };
        BigDataCloudIpLocationProvider provider = new(
            client,
            NullLogger<BigDataCloudIpLocationProvider>.Instance);

        LocationSearchResult? result = await provider.GetCurrentAsync();

        Assert.NotNull(result);
        Assert.Equal(33.84, result.Coordinate.Latitude);
        Assert.Equal(35.53, result.Coordinate.Longitude);
        Assert.Equal("LB", result.CountryCode);
        Assert.Equal("Asia/Beirut", result.TimeZoneId);
        Assert.Equal(
            "Al Hadath, Mohafazat Mont-Liban, Lebanon (IP estimate)",
            result.DisplayName);
        Assert.Equal(
            new Uri(
                "https://api.bigdatacloud.net/data/reverse-geocode-client?localityLanguage=en"),
            handler.RequestUri);
    }

    private sealed class RecordingHttpMessageHandler(string responseJson) : HttpMessageHandler
    {
        private readonly string _responseJson = responseJson;

        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestUri = request.RequestUri;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        _responseJson,
                        Encoding.UTF8,
                        "application/json"),
                });
        }
    }
}
