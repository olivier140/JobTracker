namespace JobTracker.WordExport;

using JobTracker.Core;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// Builds a formatted Word (.docx) cover letter document from a <see cref="JobMatch"/>.
/// </summary>
internal static class CoverLetterDocumentBuilder
{
    // Recognised closing salutations that trigger signature formatting.
    private static readonly HashSet<string> ClosingPhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sincerely", "Sincerely,", "Best regards", "Best regards,",
        "Kind regards", "Kind regards,", "Regards", "Regards,",
        "Warm regards", "Warm regards,", "Thank you", "Thank you,",
        "Respectfully", "Respectfully,"
    };

    #region Public entry point

    /// <summary>
    /// Writes a cover-letter .docx document to <paramref name="target"/> and flushes it.
    /// The stream is left open and positioned at the end.
    /// </summary>
    internal static void Build(Stream target, JobMatch match, ScrapedJob job)
    {
        using var doc = WordprocessingDocument.Create(target, WordprocessingDocumentType.Document, true);

        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = new Body();
        mainPart.Document.AppendChild(body);

        ParseAndEmitLines(body, match.CoverLetter ?? string.Empty);

        // Page layout — slightly wider margins than resume for letter feel
        body.AppendChild(new SectionProperties(
            new PageMargin { Top = 1080, Bottom = 1080, Left = 1260, Right = 1260 }));

        mainPart.Document.Save();
    }

    #endregion

    #region Cover letter line parsing

    /// <summary>
    /// Parses the specified cover letter text and appends formatted paragraphs to the provided document body. The
    /// method identifies and emits candidate name, contact information, greeting, body, closing, and signature lines
    /// according to standard cover letter structure.
    /// </summary>
    /// <remarks>This method processes the input text line by line, applying formatting rules based on the
    /// typical structure of a cover letter. It distinguishes between name, contact information, greeting, body,
    /// closing, and signature sections, and emits appropriate paragraph elements for each. Blank lines are interpreted
    /// as spacers. The method does not return a value; all output is appended directly to the provided body.</remarks>
    /// <param name="body">The document body to which the parsed and formatted paragraphs will be appended. Must not be null.</param>
    /// <param name="text">The raw cover letter text to parse. Lines are separated by newline characters.</param>
    private static void ParseAndEmitLines(Body body, string text)
    {
        var lines = text.Split('\n');

        bool nameEmitted = false;
        bool inHeaderBlock = true;   // contact lines immediately after the candidate name
        bool inClosing = false;      // after a closing salutation line

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();

            //  Candidate name (first non-blank line) 
            if (!nameEmitted)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                body.AppendChild(CandidateNameParagraph(line));
                nameEmitted = true;
                continue;
            }

            // Header block (contact info after name) 
            if (inHeaderBlock)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    inHeaderBlock = false;
                    body.AppendChild(SpacerParagraph());
                    continue;
                }
                body.AppendChild(ContactLineParagraph(line));
                continue;
            }

            //  Empty line → blank spacer 
            if (string.IsNullOrWhiteSpace(line))
            {
                body.AppendChild(SpacerParagraph());
                inClosing = false;
                continue;
            }

            // Closing salutation triggers signature style 
            var trimmed = line.Trim();
            if (!inClosing && ClosingPhrases.Contains(trimmed.TrimEnd(',')))
            {
                inClosing = true;
                body.AppendChild(BodyParagraph(line));
                continue;
            }

            //  Lines after the closing = candidate signature 
            if (inClosing)
            {
                body.AppendChild(SignatureParagraph(line));
                continue;
            }

            //  Greeting line ("Dear ...") 
            if (trimmed.StartsWith("Dear ", StringComparison.OrdinalIgnoreCase))
            {
                body.AppendChild(GreetingParagraph(line));
                continue;
            }

            //  Regular body paragraph 
            body.AppendChild(BodyParagraph(line));
        }
    }

    #endregion

    #region Paragraph factories

    /// <summary>
    /// Creates a centered paragraph containing the specified candidate name, formatted with bold, large font, and a
    /// specific color.
    /// </summary>
    /// <remarks>The returned paragraph uses a 16-point bold font and a dark blue color for the candidate
    /// name, with spacing applied below the text. The paragraph is horizontally centered.</remarks>
    /// <param name="name">The candidate name to display in the paragraph. Cannot be null.</param>
    /// <returns>A <see cref="Paragraph"/> object representing the formatted candidate name.</returns>
    private static Paragraph CandidateNameParagraph(string name)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new Justification { Val = JustificationValues.Center },
            new SpacingBetweenLines { Before = "0", After = "60" }));

        var run = new Run(new Text(name) { Space = SpaceProcessingModeValues.Preserve });
        run.RunProperties = new RunProperties(
            new Bold(),
            new FontSize { Val = "32" },   // 16 pt
            new Color { Val = "1F3864" });
        p.AppendChild(run);
        return p;
    }

    /// <summary>
    /// Creates a centered paragraph containing the specified contact line text, formatted with additional spacing and a
    /// smaller font size.
    /// </summary>
    /// <remarks>The returned paragraph is centered and includes extra spacing after the line. The text is
    /// rendered at 9-point size.</remarks>
    /// <param name="line">The text to display in the contact line. Leading and trailing whitespace is preserved.</param>
    /// <returns>A <see cref="Paragraph"/> object representing the formatted contact line.</returns>
    private static Paragraph ContactLineParagraph(string line)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new Justification { Val = JustificationValues.Center },
            new SpacingBetweenLines { After = "40" }));

        var run = new Run(new Text(line) { Space = SpaceProcessingModeValues.Preserve });
        run.RunProperties = new RunProperties(new FontSize { Val = "18" });   // 9 pt
        p.AppendChild(run);
        return p;
    }

    /// <summary>
    /// Creates a paragraph containing the specified text, formatted with standard greeting spacing and font size.
    /// </summary>
    /// <param name="line">The text to include in the paragraph. Leading and trailing spaces are preserved.</param>
    /// <returns>A <see cref="Paragraph"/> object containing the formatted greeting text.</returns>
    private static Paragraph GreetingParagraph(string line)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(new SpacingBetweenLines { Before = "120", After = "120" }));

        var run = new Run(new Text(line) { Space = SpaceProcessingModeValues.Preserve });
        run.RunProperties = new RunProperties(new FontSize { Val = "22" });   // 11 pt
        p.AppendChild(run);
        return p;
    }

    /// <summary>
    /// Creates a new paragraph containing the specified text, formatted with standard spacing and font size.
    /// </summary>
    /// <remarks>The returned paragraph uses a line spacing of 13.8 points and an 11-point font size.
    /// Whitespace in the input text is preserved in the output.</remarks>
    /// <param name="text">The text to include in the paragraph. Leading and trailing whitespace is preserved.</param>
    /// <returns>A Paragraph object containing the formatted text.</returns>
    private static Paragraph BodyParagraph(string text)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new SpacingBetweenLines { After = "160", Line = "276", LineRule = LineSpacingRuleValues.Auto })
        );

        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        run.RunProperties = new RunProperties(new FontSize { Val = "22" });   // 11 pt
        p.AppendChild(run);
        return p;
    }

    /// <summary>
    /// Creates a formatted paragraph containing the specified text, styled for use as a signature line in a document.
    /// </summary>
    /// <param name="text">The text to display in the signature paragraph. This text will appear bold and in 11-point font.</param>
    /// <returns>A <see cref="Paragraph"/> object representing the formatted signature paragraph.</returns>
    private static Paragraph SignatureParagraph(string text)
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(
            new SpacingBetweenLines { After = "60" }));

        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        run.RunProperties = new RunProperties(
            new Bold(),
            new FontSize { Val = "22" });   // 11 pt
        p.AppendChild(run);
        return p;
    }

    /// <summary>
    /// Creates a new paragraph element with spacing applied after the paragraph.
    /// </summary>
    /// <returns>A <see cref="Paragraph"/> instance with spacing configured after the paragraph.</returns>
    private static Paragraph SpacerParagraph()
    {
        var p = new Paragraph();
        p.AppendChild(new ParagraphProperties(new SpacingBetweenLines { After = "120" }));
        return p;
    }

    #endregion
}
