// JobTracker.Tests/ResumeExporterTests.cs
using DocumentFormat.OpenXml.Packaging;
using JobTracker.Core;
using JobTracker.WordExport;
using NUnit.Framework;

namespace JobTracker.Tests;

[TestFixture]
public class ResumeExporterTests
{
    // -----------------------------------------------------------------------
    // ResumeDocumentBuilder — document structure
    // -----------------------------------------------------------------------

    [Test]
    public void Build_ProducesNonEmptyStream()
    {
        var (match, job) = MakeTestData();
        using var ms = new MemoryStream();

        ResumeDocumentBuilder.Build(ms, match, job);

        Assert.That(ms.Length, Is.GreaterThan(0));
    }

    [Test]
    public void Build_ProducesValidOpenXmlDocument()
    {
        var (match, job) = MakeTestData();
        using var ms = new MemoryStream();

        ResumeDocumentBuilder.Build(ms, match, job);

        ms.Position = 0;
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        Assert.That(doc.MainDocumentPart, Is.Not.Null);
        Assert.That(doc.MainDocumentPart!.Document, Is.Not.Null);
        Assert.That(doc.MainDocumentPart.Document.Body, Is.Not.Null);
    }

    [Test]
    public void Build_WithNullResume_ProducesValidDocument()
    {
        var (match, job) = MakeTestData();
        match.TailoredResume = null;

        using var ms = new MemoryStream();
        ResumeDocumentBuilder.Build(ms, match, job);

        ms.Position = 0;
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        Assert.That(doc.MainDocumentPart, Is.Not.Null);
    }

    [Test]
    public void Build_IncludesJobTitleInDocument()
    {
        var (match, job) = MakeTestData();
        using var ms = new MemoryStream();

        ResumeDocumentBuilder.Build(ms, match, job);

        ms.Position = 0;
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        var bodyText = doc.MainDocumentPart!.Document.Body!.InnerText;
        Assert.That(bodyText, Does.Contain(job.Title));
    }

    [Test]
    public void Build_IncludesScoreInDocument()
    {
        var (match, job) = MakeTestData();
        using var ms = new MemoryStream();

        ResumeDocumentBuilder.Build(ms, match, job);

        ms.Position = 0;
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        var bodyText = doc.MainDocumentPart!.Document.Body!.InnerText;
        Assert.That(bodyText, Does.Contain(match.Score.ToString()));
    }

    // -----------------------------------------------------------------------
    // ResumeExporter.SanitizeForFileName
    // -----------------------------------------------------------------------

    [Test]
    [TestCase("Senior Software Engineer", "Senior_Software_Engineer")]
    [TestCase("C# Developer", "C#_Developer")]
    [TestCase("Lead/Principal Engineer", "Lead_Principal_Engineer")]
    [TestCase("Role: Manager", "Role__Manager")]
    public void SanitizeForFileName_ReplacesInvalidChars(string input, string expectedStart)
    {
        var result = ResumeExporter.SanitizeForFileName(input);
        Assert.That(result, Does.StartWith(expectedStart));
    }

    [Test]
    public void SanitizeForFileName_TruncatesLongInput()
    {
        var longInput = new string('A', 100);
        var result = ResumeExporter.SanitizeForFileName(longInput);
        Assert.That(result.Length, Is.LessThanOrEqualTo(50));
    }

    [Test]
    public void SanitizeForFileName_ReplacesSpacesWithUnderscores()
    {
        var result = ResumeExporter.SanitizeForFileName("Hello World");
        Assert.That(result, Does.Not.Contain(' '));
        Assert.That(result, Is.EqualTo("Hello_World"));
    }

    // -----------------------------------------------------------------------
    // ResumeExporter.BuildFileName
    // -----------------------------------------------------------------------

    [Test]
    public void BuildFileName_ContainsJobId()
    {
        var job = new ScrapedJob { JobId = "12345", Title = "Engineer" };
        var name = ResumeExporter.BuildFileName(job);
        Assert.That(name, Does.Contain("12345"));
    }

    [Test]
    public void BuildFileName_HasDocxExtension()
    {
        var job = new ScrapedJob { JobId = "42", Title = "Developer" };
        var name = ResumeExporter.BuildFileName(job);
        Assert.That(name, Does.EndWith(".docx"));
    }

    [Test]
    public void BuildFileName_ContainsTodaysDate()
    {
        var job = new ScrapedJob { JobId = "1", Title = "X" };
        var name = ResumeExporter.BuildFileName(job);
        Assert.That(name, Does.Contain(DateTime.Now.ToString("yyyy-MM-dd")));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (JobMatch match, ScrapedJob job) MakeTestData()
    {
        var job = new ScrapedJob
        {
            Id = 1,
            JobId = "JOB-001",
            Title = "Senior Software Engineer",
            Location = "Remote, USA",
            Url = "https://example.com/job/1"
        };

        var match = new JobMatch
        {
            Id = 1,
            ScrapedJobId = 1,
            Score = 9,
            RecommendApply = true,
            TopMatchesJson = "[\"C#\",\".NET\",\"Azure\"]",
            GapsJson = "[\"Kubernetes\"]",
            TailoredResume = """
                Jane Doe
                jane.doe@example.com | (555) 000-1234

                PROFESSIONAL SUMMARY
                Experienced software engineer with 10+ years building distributed systems.

                EXPERIENCE

                Senior Software Engineer — Acme Corp
                2020 - Present
                - Led architecture of microservices platform
                - Delivered 40% latency reduction

                EDUCATION

                B.S. Computer Science — State University, 2014

                SKILLS

                C#, .NET, Azure, SQL Server, Kubernetes
                """
        };

        return (match, job);
    }
}
