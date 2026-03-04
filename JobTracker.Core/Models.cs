namespace JobTracker.Core;

// JobTracker.Core/Models.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

#region Microsoft Careers PCSX API (Eightfold) 

/// <summary>
/// Represents a standard response from the PCSX API, including status information, error details, and the response
/// data.
/// </summary>
/// <remarks>This class is typically used to deserialize responses from the Microsoft Careers PCSX (Eightfold)
/// API. It encapsulates the HTTP status code, any error information, and the main data payload. The generic parameter
/// allows the response to be strongly typed according to the expected data structure for each API endpoint.</remarks>
/// <typeparam name="T">The type of the data payload returned by the API response.</typeparam>
public class PcsxApiResponse<T>
{
    [JsonPropertyName("status")] public int Status { get; set; }
    [JsonPropertyName("error")] public PcsxError? Error { get; set; }
    [JsonPropertyName("data")] public T? Data { get; set; }
}

/// <summary>
/// Represents an error response returned by the PCSX API, including a message and optional details.
/// </summary>
/// <remarks>This class is typically used to deserialize error information received from the PCSX API. The
/// properties correspond to fields in the API's error response payload.</remarks>
public class PcsxError
{
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
}

/// <summary>
/// Represents the result of a search operation in the PCSX context, including the list of matching positions, the total
/// count, and the applied sort order.
/// </summary>
/// <remarks>This class is typically used to transfer search results between components or services that interact
/// with PCSX data. The properties correspond to the search results, the number of items found, and the criteria used to
/// sort the results.</remarks>
public class PcsxSearchData
{
    [JsonPropertyName("positions")] public List<PcsxPosition>? Positions { get; set; }
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("sortBy")] public string? SortBy { get; set; }
}

/// <summary>
/// Represents a job position as defined in the PCSX system, including identifiers, location information, and related
/// metadata.
/// </summary>
/// <remarks>This class is typically used to transfer job position data between systems or layers, such as when
/// deserializing responses from an external API. All properties correspond to fields commonly found in job postings,
/// such as job IDs, names, locations, and department information.</remarks>
public class PcsxPosition
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("displayJobId")] public string? DisplayJobId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("locations")] public List<string>? Locations { get; set; }
    [JsonPropertyName("standardizedLocations")] public List<string>? StandardizedLocations { get; set; }
    [JsonPropertyName("postedTs")] public long PostedTs { get; set; }
    [JsonPropertyName("department")] public string? Department { get; set; }
    [JsonPropertyName("workLocationOption")] public string? WorkLocationOption { get; set; }
    [JsonPropertyName("atsJobId")] public string? AtsJobId { get; set; }
    [JsonPropertyName("positionUrl")] public string? PositionUrl { get; set; }
}

/// <summary>
/// Represents detailed information about a position in the PCSX system, including job description, location, and public
/// URL.
/// </summary>
public class PcsxPositionDetail : PcsxPosition
{
    [JsonPropertyName("jobDescription")] public string? JobDescription { get; set; }
    [JsonPropertyName("location")] public string? Location { get; set; }
    [JsonPropertyName("publicUrl")] public string? PublicUrl { get; set; }
}

#endregion

#region Legacy API types (kept for backward compatibility)

/// <summary>
/// Represents the response returned from a job search operation, including the result of the operation.
/// </summary>
/// <remarks>This class is maintained for backward compatibility with legacy APIs. For new development, consider
/// using updated response types if available.</remarks>
public class JobSearchResponse
{
    [JsonPropertyName("operationResult")]
    public OperationResult? OperationResult { get; set; }
}

/// <summary>
/// Represents the result of an operation, including the associated search result if available.
/// </summary>
public class OperationResult
{
    [JsonPropertyName("result")]
    public SearchResult? Result { get; set; }
}

/// <summary>
/// Represents the result of a job search, including the list of matching jobs and the total number of jobs found.
/// </summary>
public class SearchResult
{
    [JsonPropertyName("jobs")] public List<MsJob>? Jobs { get; set; }
    [JsonPropertyName("totalJobs")] public int TotalJobs { get; set; }
}

/// <summary>
/// Represents a job posting with details such as job ID, title, location, posting date, and description.
/// </summary>
public class MsJob
{
    [JsonPropertyName("jobId")] public string? JobId { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("primaryLocation")] public string? PrimaryLocation { get; set; }
    [JsonPropertyName("postingDate")] public string? PostingDate { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}

/// <summary>
/// Represents the result of evaluating a candidate for a job, including the overall score, top matching criteria,
/// identified gaps, and whether the candidate should be considered for application.
/// </summary>
public class JobScore
{
    [JsonPropertyName("score")] public int Score { get; set; }
    [JsonPropertyName("top_matches")] public List<string>? TopMatches { get; set; }
    [JsonPropertyName("gaps")] public List<string>? Gaps { get; set; }
    [JsonPropertyName("apply")] public bool Apply { get; set; }
}

#endregion

#region Database Entities 

/// <summary>
/// Represents a job posting that has been scraped from an external source and stored in the database.
/// </summary>
/// <remarks>This entity contains metadata and content for a single scraped job, including identifiers,
/// descriptive fields, and the time the job was scraped. It is typically used to persist job postings for further
/// processing, analysis, or matching within the application.</remarks>
[Table("ScrapedJobs")]
public class ScrapedJob
{
    [Key] public int Id { get; set; }
    [Required, MaxLength(100)] public string JobId { get; set; } = "";
    [MaxLength(300)] public string? Title { get; set; }
    [MaxLength(200)] public string? Location { get; set; }
    public string? DescriptionFull { get; set; }
    [MaxLength(500)] public string? Url { get; set; }
    public DateTime? PostedDate { get; set; }
    public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
    public JobMatch? Match { get; set; }
}

/// <summary>
/// Represents the result of evaluating a job match, including match score, recommendations, and related application
/// data.
/// </summary>
/// <remarks>This entity is typically used to store the outcome of matching a candidate's profile to a specific
/// job posting, along with supporting information such as tailored documents and match details. It is mapped to the
/// 'JobMatches' table in the database.</remarks>
[Table("JobMatches")]
public class JobMatch
{
    [Key] public int Id { get; set; }
    public int ScrapedJobId { get; set; }
    public int Score { get; set; }
    public string? TopMatchesJson { get; set; }
    public string? GapsJson { get; set; }
    public bool RecommendApply { get; set; }
    public string? TailoredResume { get; set; }
    public string? CoverLetter { get; set; }
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
    public ScrapedJob? ScrapedJob { get; set; }
    public ApplicationRecord? Application { get; set; }
}

/// <summary>
/// Represents an application record for a job match, including status, timestamps, notes, and related events.
/// </summary>
/// <remarks>This class is typically used to track the lifecycle and status of a job application within the
/// system. It includes references to the associated job match and a collection of related application events. The class
/// is mapped to the "Applications" table in the database.</remarks>
[Table("Applications")]
public class ApplicationRecord
{
    [Key] public int Id { get; set; }
    public int JobMatchId { get; set; }
    [MaxLength(50)] public string Status { get; set; } = "Pending";
    public DateTime? AppliedAt { get; set; }
    public DateTime? FollowUpAt { get; set; }
    public DateTime? LastUpdatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(2000)] public string? Notes { get; set; }
    public JobMatch? JobMatch { get; set; }
    public List<ApplicationEvent> Events { get; set; } = new();
}

/// <summary>
/// Represents an event that has occurred within an application, including event type, details, and the time of
/// occurrence.
/// </summary>
/// <remarks>This entity is typically used to record significant actions or changes related to an application,
/// such as status updates or user-triggered events. Each event is associated with a specific application and may
/// include additional detail describing the event context.</remarks>
[Table("ApplicationEvents")]
public class ApplicationEvent
{
    [Key] public int Id { get; set; }
    public int ApplicationId { get; set; }
    [MaxLength(100)] public string EventType { get; set; } = "";
    [MaxLength(2000)] public string? Detail { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public ApplicationRecord? Application { get; set; }
}

#endregion

#region Settings 

/// <summary>
/// Represents the application configuration settings used to control connection information, API keys, search
/// parameters, scheduling, and resume data.
/// </summary>
/// <remarks>This class is typically used to centralize and manage configurable values required by the
/// application, such as database connection strings, external API credentials, job search criteria, and scheduling
/// intervals. It is commonly populated from a configuration file or environment variables at application
/// startup.</remarks>
public class AppSettings
{
    public string ConnectionString { get; set; } = "";
    public string AnthropicApiKey { get; set; } = "";
    public string SearchQuery { get; set; } = "senior software engineer";
    public string SearchLocation { get; set; } = "United States";
    public int MaxPages { get; set; } = 3;
    public int MinScoreToApply { get; set; } = 7;
    public int ScheduleHours { get; set; } = 24;  // service run interval
    public string Resume { get; set; } = "";
    public string ResumePath { get; set; } = "";

    public string GetResume() =>
        !string.IsNullOrEmpty(ResumePath) && File.Exists(ResumePath)
            ? File.ReadAllText(ResumePath)
            : Resume; // fallback to inline
}

#endregion
