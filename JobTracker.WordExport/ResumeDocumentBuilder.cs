namespace JobTracker.WordExport;

using JobTracker.Core;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// Builds a formatted Word (.docx) document from a tailored resume and job metadata.
/// </summary>
internal static class ResumeDocumentBuilder
{
    // Well-known resume section names that trigger heading style even if not all-caps.
    private static readonly HashSet<string> KnownSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "Summary", "Objective", "Profile", "Experience", "Work Experience",
        "Professional Experience", "Employment History", "Education", "Skills",
        "Technical Skills", "Core Competencies", "Certifications", "Certificates",
        "Projects", "Publications", "Languages", "Volunteer", "References",
        "Interests", "Achievements", "Awards", "Qualifications"
    };

    // -----------------------------------------------------------------------
    // Public entry point
    // -----------------------------------------------------------------------

    /// <summary>
    /// Writes a .docx document to <paramref name="target"/> and flushes it.
    /// The stream is left open and positioned at the end.
    /// </summary>
    internal static void Build(Stream target, JobMatch match, ScrapedJob job)
    {
        using var doc = WordprocessingDocument.Create(target, WordprocessingDocumentType.Document, false);

        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = new Body();
        mainPart.Document.AppendChild(body);

        // --- Resume body ---
        ParseAndEmitLines(body, match.TailoredResume ?? string.Empty);

        // --- Divider + job metadata block ---
        body.AppendChild(HorizontalRule());
        EmitJobInfo(body, match, job);

        // --- Page layout (must be last child of Body) ---
        body.AppendChild(new SectionProperties(
            new PageMargin { Top = 720, Bottom = 720, Left = 1080, Right = 1080 }));

        mainPart.Document.Save();
    }

    // -----------------------------------------------------------------------
    // Resume line parsing
    // -----------------------------------------------------------------------

    private static void ParseAndEmitLines(Body body, string resumeText)
    {
        var lines = resumeText.Split('\n');

        bool nameEmitted = false;
        bool inHeaderBlock = true;  // contact-info lines just after the name

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();

            if (!nameEmitted)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;  // skip leading blanks
                body.AppendChild(CandidateNameParagraph(line));
                nameEmitted = true;
                continue;
            }

            if (inHeaderBlock)
            {
                // An empty line or a section header ends the header block.
                if (string.IsNullOrWhiteSpace(line) || IsSectionHeader(line))
                {
                    inHeaderBlock = false;
                    // Fall through to emit current line normally.
                }
                else
                {
                    body.AppendChild(ContactLineParagraph(line));
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                body.AppendChild(EmptyParagraph());
            }
            else if (IsSectionHeader(line))
            {
                body.AppendChild(SectionHeadingParagraph(line));
            }
            else
            {
                body.AppendChild(BodyParagraph(line));
            }
        }
    }

    private static bool IsSectionHeader(string line)
    {
        var t = line.Trim();
        if (t.Length < 2) return false;

        // All-uppercase (letters only, ignoring punctuation/whitespace)
        var letters = t.Where(char.IsLetter).ToArray();
        if (letters.Length > 0 && letters.All(char.IsUpper)) return true;

        // Ends with colon
        if (t.EndsWith(':')) return true;

        // Matches a well-known section name (strip trailing colon before checking)
        return KnownSections.Contains(t.TrimEnd(':').Trim());
    }

    // -----------------------------------------------------------------------
    // Paragraph factories
    // -----------------------------------------------------------------------

    private static Paragraph CandidateNameParagraph(string name)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new Justification { Val = JustificationValues.Center },
            new SpacingBetweenLines { Before = "0", After = "80" }));

        var run = new Run(new Text(name) { Space = SpaceProcessingModeValues.Preserve });
        run.RunProperties = new RunProperties(
            new Bold(),
            new FontSize { Val = "40" },          // 20 pt
            new Color { Val = "1F3864" });
        p.AppendChild(run);
        return p;
    }

    private static Paragraph ContactLineParagraph(string line)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new Justification { Val = JustificationValues.Center },
            new SpacingBetweenLines { After = "60" }));

        var run = new Run(new Text(line) { Space = SpaceProcessingModeValues.Preserve });
        run.RunProperties = new RunProperties(new FontSize { Val = "18" }); // 9 pt
        p.AppendChild(run);
        return p;
    }

    private static Paragraph SectionHeadingParagraph(string text)
    {
        var p = new Paragraph();

        var pPr = new ParagraphProperties(
            new SpacingBetweenLines { Before = "200", After = "80" });

        var borders = new ParagraphBorders();
        borders.AppendChild(new BottomBorder
        {
            Val = BorderValues.Single,
            Size = 4,
            Space = 1,
            Color = "2E74B5"
        });
        pPr.AppendChild(borders);
        p.AppendChild(pPr);

        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        run.RunProperties = new RunProperties(
            new Bold(),
            new FontSize { Val = "24" },          // 12 pt
            new Color { Val = "2E74B5" });
        p.AppendChild(run);
        return p;
    }

    private static Paragraph BodyParagraph(string text)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new SpacingBetweenLines { After = "60" }));
        p.AppendChild(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        return p;
    }

    private static Paragraph EmptyParagraph()
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new SpacingBetweenLines { After = "60" }));
        return p;
    }

    private static Paragraph HorizontalRule()
    {
        var p = new Paragraph();
        var pPr = new ParagraphProperties(
            new SpacingBetweenLines { Before = "160", After = "160" });

        var borders = new ParagraphBorders();
        borders.AppendChild(new TopBorder { Val = BorderValues.Single, Size = 6, Color = "AAAAAA" });
        pPr.AppendChild(borders);
        p.AppendChild(pPr);
        return p;
    }

    // -----------------------------------------------------------------------
    // Job metadata block
    // -----------------------------------------------------------------------

    private static void EmitJobInfo(Body body, JobMatch match, ScrapedJob job)
    {
        body.AppendChild(InfoHeadingParagraph("Match Summary"));

        body.AppendChild(InfoLineParagraph("Job Title", job.Title ?? "Unknown"));
        body.AppendChild(InfoLineParagraph("Location", job.Location ?? "Not specified"));
        body.AppendChild(InfoLineParagraph("Match Score", $"{match.Score} / 10"));
        body.AppendChild(InfoLineParagraph("Recommended", match.RecommendApply ? "Yes" : "No"));

        if (!string.IsNullOrWhiteSpace(job.Url))
            body.AppendChild(InfoLineParagraph("URL", job.Url));

        var topMatches = DeserializeList(match.TopMatchesJson);
        if (topMatches.Count > 0)
            body.AppendChild(InfoLineParagraph("Top Matches", string.Join(" • ", topMatches)));

        var gaps = DeserializeList(match.GapsJson);
        if (gaps.Count > 0)
            body.AppendChild(InfoLineParagraph("Gaps", string.Join(" • ", gaps)));

        body.AppendChild(InfoLineParagraph("Generated", DateTime.Now.ToString("yyyy-MM-dd HH:mm")));
    }

    private static Paragraph InfoHeadingParagraph(string text)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new SpacingBetweenLines { Before = "80", After = "80" }));

        var run = new Run(new Text(text));
        run.RunProperties = new RunProperties(
            new Bold(),
            new FontSize { Val = "20" },
            new Color { Val = "555555" });
        p.AppendChild(run);
        return p;
    }

    private static Paragraph InfoLineParagraph(string label, string value)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new SpacingBetweenLines { After = "40" }));

        var labelRun = new Run(new Text($"{label}: ") { Space = SpaceProcessingModeValues.Preserve });
        labelRun.RunProperties = new RunProperties(
            new Bold(),
            new Italic(),
            new Color { Val = "555555" },
            new FontSize { Val = "18" });
        p.AppendChild(labelRun);

        var valueRun = new Run(new Text(value) { Space = SpaceProcessingModeValues.Preserve });
        valueRun.RunProperties = new RunProperties(new FontSize { Val = "18" });
        p.AppendChild(valueRun);

        return p;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }
}
