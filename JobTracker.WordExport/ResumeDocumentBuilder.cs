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

    #region Public entry point

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

    #endregion

    #region Resume line parsing

    /// <summary>
    /// Parses the specified resume text and appends formatted paragraphs representing the candidate's name, contact
    /// information, section headings, and body content to the provided document body.
    /// </summary>
    /// <remarks>The method expects the resume text to be organized with the candidate's name on the first
    /// non-blank line, followed by optional contact information, section headers, and body content. Blank lines and
    /// section headers are used to determine formatting and structure.</remarks>
    /// <param name="body">The document body to which the parsed paragraphs will be appended. Must not be null.</param>
    /// <param name="resumeText">The plain text content of the resume to parse. Each line is interpreted and formatted according to its position
    /// and content.</param>
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

    /// <summary>
    /// Determines whether the specified line represents a section header based on formatting or known section names.
    /// </summary>
    /// <remarks>A line is considered a section header if it is all uppercase letters (ignoring punctuation
    /// and whitespace), ends with a colon, or matches a known section name (with or without a trailing
    /// colon).</remarks>
    /// <param name="line">The line of text to evaluate as a potential section header. Cannot be null.</param>
    /// <returns>true if the line is considered a section header; otherwise, false.</returns>
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

    #endregion

    #region Paragraph factories

    /// <summary>
    /// Creates a centered paragraph containing the specified candidate name, formatted with bold, large font and custom
    /// color.
    /// </summary>
    /// <remarks>The returned paragraph is centered and styled with increased spacing after the text. The
    /// candidate name is rendered in bold, 20-point font, and a specific color.</remarks>
    /// <param name="name">The candidate name to display in the paragraph. Cannot be null.</param>
    /// <returns>A Paragraph object containing the formatted candidate name.</returns>
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

    /// <summary>
    /// Creates a centered paragraph containing the specified text line, formatted with additional spacing and a 9-point
    /// font size.
    /// </summary>
    /// <param name="line">The text to display in the paragraph. Leading and trailing spaces are preserved.</param>
    /// <returns>A <see cref="Paragraph"/> object representing the formatted line of text.</returns>
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

    /// <summary>
    /// Creates a formatted paragraph to be used as a section heading in a document.
    /// </summary>
    /// <remarks>The returned paragraph includes increased spacing, a colored bottom border, bold text, and a
    /// larger font size to visually distinguish it as a heading.</remarks>
    /// <param name="text">The text to display as the section heading.</param>
    /// <returns>A Paragraph object representing the formatted section heading.</returns>
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

    /// <summary>
    /// Creates a new paragraph containing the specified text with standard spacing applied.
    /// </summary>
    /// <param name="text">The text to include in the paragraph. Leading and trailing whitespace is preserved.</param>
    /// <returns>A <see cref="Paragraph"/> object containing the provided text.</returns>
    private static Paragraph BodyParagraph(string text)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new SpacingBetweenLines { After = "60" }));
        p.AppendChild(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        return p;
    }

    /// <summary>
    /// Creates a new empty paragraph with default spacing applied.
    /// </summary>
    /// <returns>A <see cref="Paragraph"/> instance representing an empty paragraph with standard spacing settings.</returns>
    private static Paragraph EmptyParagraph()
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new SpacingBetweenLines { After = "60" }));
        return p;
    }

    /// <summary>
    /// Creates a paragraph element representing a horizontal rule for use in a WordprocessingML document.
    /// </summary>
    /// <remarks>The returned paragraph uses a single top border with a specified thickness and color to
    /// simulate a horizontal line. This can be inserted into a document to visually separate sections.</remarks>
    /// <returns>A <see cref="Paragraph"/> configured to visually appear as a horizontal rule.</returns>
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

    #endregion

    #region Job metadata block

    /// <summary>
    /// Appends job summary and match information to the specified document body.
    /// </summary>
    /// <remarks>This method adds a formatted summary of the job and its match analysis to the provided body,
    /// including key details such as job title, location, match score, recommendations, and any identified top matches
    /// or gaps. The generated information is intended for display in a user-facing report or summary view.</remarks>
    /// <param name="body">The document body to which the job information will be appended. Must not be null.</param>
    /// <param name="match">The job match details, including score and match analysis. Must not be null.</param>
    /// <param name="job">The job metadata to display, such as title, location, and URL. Must not be null.</param>
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

    /// <summary>
    /// Creates a formatted paragraph containing the specified text, styled as an informational heading.
    /// </summary>
    /// <param name="text">The text to display in the heading paragraph.</param>
    /// <returns>A <see cref="Paragraph"/> object representing the formatted informational heading.</returns>
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

    /// <summary>
    /// Creates a paragraph containing a formatted label and value pair, with the label styled in bold and italic.
    /// </summary>
    /// <param name="label">The text to use as the label. This appears before the value and is followed by a colon.</param>
    /// <param name="value">The text to use as the value. This appears after the label.</param>
    /// <returns>A Paragraph object containing the formatted label and value.</returns>
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

    #endregion

    #region Helpers

    /// <summary>
    /// Deserializes a JSON array string into a list of strings.
    /// </summary>
    /// <param name="json">A JSON-formatted string representing an array of strings. Can be null or empty.</param>
    /// <returns>A list of strings deserialized from the JSON array. Returns an empty list if the input is null, empty, or not a
    /// valid JSON array.</returns>
    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }

    #endregion
}
