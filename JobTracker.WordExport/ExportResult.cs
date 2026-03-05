namespace JobTracker.WordExport;

/// <summary>
/// Represents the outcome of a resume export operation.
/// </summary>
/// <param name="Success">True if the export succeeded.</param>
/// <param name="FilePath">Absolute path to the exported .docx file, or null on failure.</param>
/// <param name="Error">Error message when Success is false.</param>
public record ExportResult(bool Success, string? FilePath, string? Error);
