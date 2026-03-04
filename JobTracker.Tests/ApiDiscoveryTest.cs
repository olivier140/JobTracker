using System.Net;
using System.Net.Http.Json;
using JobTracker.Core;
using NUnit.Framework;

namespace JobTracker.Tests;

[TestFixture]
[Category("Discovery")]
public class ApiDiscoveryTest
{
    [Test]
    public async Task CaptureSearchApiResponse()
    {
        // Uses the new PCSX API directly — no Playwright needed
        var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true };
        using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.Add("Accept", "application/json");

        // Step 1: Initialize session (get cookies)
        TestContext.Out.WriteLine("=== Initializing session ===");
        var initResponse = await http.GetAsync("https://apply.careers.microsoft.com/careers");
        TestContext.Out.WriteLine($"  Session init: HTTP {(int)initResponse.StatusCode}");

        // Step 2: Search for jobs
        TestContext.Out.WriteLine("\n=== Searching for 'software engineer' in 'United States' ===");
        var searchUrl = "https://apply.careers.microsoft.com/api/pcsx/search?domain=microsoft.com&query=software+engineer&location=United+States&start=0&";
        var searchResponse = await http.GetAsync(searchUrl);
        TestContext.Out.WriteLine($"  Search: HTTP {(int)searchResponse.StatusCode}");

        var result = await searchResponse.Content.ReadFromJsonAsync<PcsxApiResponse<PcsxSearchData>>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Status, Is.EqualTo(200));
        Assert.That(result.Data, Is.Not.Null);
        Assert.That(result.Data!.Positions, Is.Not.Null);
        Assert.That(result.Data.Positions, Is.Not.Empty);

        TestContext.Out.WriteLine($"  Total count: {result.Data.Count}");
        TestContext.Out.WriteLine($"  Positions returned: {result.Data.Positions!.Count}");

        foreach (var pos in result.Data.Positions.Take(3))
        {
            TestContext.Out.WriteLine($"\n  Position: {pos.Name}");
            TestContext.Out.WriteLine($"    ID: {pos.Id} (displayJobId: {pos.DisplayJobId})");
            TestContext.Out.WriteLine($"    Locations: {string.Join("; ", pos.Locations ?? new())}");
            TestContext.Out.WriteLine($"    Department: {pos.Department}");
            TestContext.Out.WriteLine($"    Posted: {DateTimeOffset.FromUnixTimeSeconds(pos.PostedTs):yyyy-MM-dd}");
            TestContext.Out.WriteLine($"    URL: {pos.PositionUrl}");
        }

        // Step 3: Get position details for the first position
        var firstPos = result.Data.Positions[0];
        TestContext.Out.WriteLine($"\n=== Getting details for position {firstPos.Id} ===");
        var detailUrl = $"https://apply.careers.microsoft.com/api/pcsx/position_details?position_id={firstPos.Id}&domain=microsoft.com&hl=en";
        var detailResponse = await http.GetAsync(detailUrl);
        TestContext.Out.WriteLine($"  Detail: HTTP {(int)detailResponse.StatusCode}");

        var detail = await detailResponse.Content.ReadFromJsonAsync<PcsxApiResponse<PcsxPositionDetail>>();
        Assert.That(detail?.Data, Is.Not.Null);
        Assert.That(detail!.Data!.JobDescription, Is.Not.Null.And.Not.Empty,
            "Position should have a job description");

        TestContext.Out.WriteLine($"  Job Description length: {detail.Data.JobDescription?.Length ?? 0} chars");
        var descPreview = detail.Data.JobDescription?.Length > 200
            ? detail.Data.JobDescription[..200] + "..."
            : detail.Data.JobDescription;
        TestContext.Out.WriteLine($"  Preview: {descPreview}");

        TestContext.Out.WriteLine("\n=== Discovery complete! API is working. ===");
    }
}
