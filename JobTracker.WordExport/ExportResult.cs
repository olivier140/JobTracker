namespace JobTracker.WordExport;

/// <summary>
/// Represents the outcome of a resume (and optional cover letter) export operation.
/// </summary>
/// <param name="Success">True if the resume export succeeded.</param>
/// <param name="FilePath">Absolute path to the exported resume .docx file, or null on failure.</param>
/// <param name="Error">Error message when Success is false.</param>
/// <param name="CoverLetterFilePath">Absolute path to the exported cover letter .docx file, or null if not produced.</param>
public record ExportResult(bool Success, string? FilePath, string? Error, string? CoverLetterFilePath = null);
