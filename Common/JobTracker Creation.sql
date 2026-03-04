-- ============================================================
-- JobTracker Database Creation Script
-- Target: Microsoft SQL Server (local instance)
-- ============================================================

USE master;
GO

-- ── Create Database ──────────────────────────────────────────
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'JobTracker')
BEGIN
    CREATE DATABASE JobTracker;
    PRINT 'Database JobTracker created.';
END
ELSE
    PRINT 'Database JobTracker already exists, skipping.';
GO

USE JobTracker;
GO

-- ── ScrapedJobs ──────────────────────────────────────────────
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ScrapedJobs')
BEGIN
    CREATE TABLE ScrapedJobs (
        Id              INT             NOT NULL IDENTITY(1,1) PRIMARY KEY,
        JobId           NVARCHAR(100)   NOT NULL,               -- Microsoft's job ID
        Title           NVARCHAR(300)   NULL,
        Location        NVARCHAR(200)   NULL,
        DescriptionFull NVARCHAR(MAX)   NULL,
        Url             NVARCHAR(500)   NULL,
        PostedDate      DATETIME2       NULL,
        ScrapedAt       DATETIME2       NOT NULL DEFAULT GETUTCDATE(),

        CONSTRAINT UQ_ScrapedJobs_JobId UNIQUE (JobId)
    );
    PRINT 'Table ScrapedJobs created.';
END
ELSE
    PRINT 'Table ScrapedJobs already exists, skipping.';
GO

-- ── JobMatches ───────────────────────────────────────────────
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'JobMatches')
BEGIN
    CREATE TABLE JobMatches (
        Id              INT             NOT NULL IDENTITY(1,1) PRIMARY KEY,
        ScrapedJobId    INT             NOT NULL,               -- FK → ScrapedJobs
        Score           INT             NOT NULL,               -- 1-10
        TopMatchesJson  NVARCHAR(MAX)   NULL,                   -- JSON string[]
        GapsJson        NVARCHAR(MAX)   NULL,                   -- JSON string[]
        RecommendApply  BIT             NOT NULL DEFAULT 0,
        TailoredResume  NVARCHAR(MAX)   NULL,
        CoverLetter     NVARCHAR(MAX)   NULL,
        EvaluatedAt     DATETIME2       NOT NULL DEFAULT GETUTCDATE(),

        CONSTRAINT FK_JobMatches_ScrapedJobs
            FOREIGN KEY (ScrapedJobId) REFERENCES ScrapedJobs(Id)
            ON DELETE CASCADE,

        CONSTRAINT UQ_JobMatches_ScrapedJobId UNIQUE (ScrapedJobId) -- one-to-one
    );
    PRINT 'Table JobMatches created.';
END
ELSE
    PRINT 'Table JobMatches already exists, skipping.';
GO

-- ── Applications ─────────────────────────────────────────────
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Applications')
BEGIN
    CREATE TABLE Applications (
        Id              INT             NOT NULL IDENTITY(1,1) PRIMARY KEY,
        JobMatchId      INT             NOT NULL,               -- FK → JobMatches
        Status          NVARCHAR(50)    NOT NULL DEFAULT 'Pending',
        AppliedAt       DATETIME2       NULL,
        FollowUpAt      DATETIME2       NULL,
        LastUpdatedAt   DATETIME2       NULL DEFAULT GETUTCDATE(),
        Notes           NVARCHAR(2000)  NULL,

        CONSTRAINT FK_Applications_JobMatches
            FOREIGN KEY (JobMatchId) REFERENCES JobMatches(Id)
            ON DELETE CASCADE,

        CONSTRAINT UQ_Applications_JobMatchId UNIQUE (JobMatchId), -- one-to-one

        CONSTRAINT CK_Applications_Status CHECK (
            Status IN ('Pending', 'Applied', 'Interviewing', 'Offer', 'Rejected')
        )
    );
    PRINT 'Table Applications created.';
END
ELSE
    PRINT 'Table Applications already exists, skipping.';
GO

-- ── ApplicationEvents ────────────────────────────────────────
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ApplicationEvents')
BEGIN
    CREATE TABLE ApplicationEvents (
        Id              INT             NOT NULL IDENTITY(1,1) PRIMARY KEY,
        ApplicationId   INT             NOT NULL,               -- FK → Applications
        EventType       NVARCHAR(100)   NOT NULL,               -- e.g. 'StatusChanged'
        Detail          NVARCHAR(2000)  NULL,
        OccurredAt      DATETIME2       NOT NULL DEFAULT GETUTCDATE(),

        CONSTRAINT FK_ApplicationEvents_Applications
            FOREIGN KEY (ApplicationId) REFERENCES Applications(Id)
            ON DELETE CASCADE
    );
    PRINT 'Table ApplicationEvents created.';
END
ELSE
    PRINT 'Table ApplicationEvents already exists, skipping.';
GO

-- ── Indexes ──────────────────────────────────────────────────
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ScrapedJobs_PostedDate')
    CREATE INDEX IX_ScrapedJobs_PostedDate
        ON ScrapedJobs(PostedDate DESC);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ScrapedJobs_ScrapedAt')
    CREATE INDEX IX_ScrapedJobs_ScrapedAt
        ON ScrapedJobs(ScrapedAt DESC);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_JobMatches_Score')
    CREATE INDEX IX_JobMatches_Score
        ON JobMatches(Score DESC);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Applications_Status')
    CREATE INDEX IX_Applications_Status
        ON Applications(Status);
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Applications_FollowUpAt')
    CREATE INDEX IX_Applications_FollowUpAt
        ON Applications(FollowUpAt)
        WHERE FollowUpAt IS NOT NULL;
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ApplicationEvents_ApplicationId')
    CREATE INDEX IX_ApplicationEvents_ApplicationId
        ON ApplicationEvents(ApplicationId);
GO

-- ── Useful Views ─────────────────────────────────────────────

-- Dashboard summary view
CREATE OR ALTER VIEW vw_Dashboard AS
SELECT
    (SELECT COUNT(*) FROM ScrapedJobs)                                          AS TotalScraped,
    (SELECT COUNT(*) FROM JobMatches)                                           AS TotalScored,
    (SELECT COUNT(*) FROM JobMatches WHERE Score >= 7)                          AS StrongMatches,
    (SELECT COUNT(*) FROM Applications WHERE Status = 'Applied')                AS Applied,
    (SELECT COUNT(*) FROM Applications WHERE Status = 'Interviewing')           AS Interviewing,
    (SELECT COUNT(*) FROM Applications WHERE Status = 'Offer')                  AS Offers,
    (SELECT COUNT(*) FROM Applications WHERE Status = 'Rejected')               AS Rejected,
    (SELECT COUNT(*) FROM Applications
     WHERE FollowUpAt <= GETUTCDATE() AND Status = 'Applied')                   AS FollowUpsDue;
GO

-- Full pipeline view (joins all tables)
CREATE OR ALTER VIEW vw_JobPipeline AS
SELECT
    j.Id            AS JobId,
    j.JobId         AS MsJobId,
    j.Title,
    j.Location,
    j.PostedDate,
    j.ScrapedAt,
    j.Url,
    m.Id            AS MatchId,
    m.Score,
    m.RecommendApply,
    m.EvaluatedAt,
    CASE WHEN m.TailoredResume IS NOT NULL THEN 1 ELSE 0 END AS HasTailoredResume,
    a.Id            AS ApplicationId,
    a.Status,
    a.AppliedAt,
    a.FollowUpAt,
    a.LastUpdatedAt,
    a.Notes,
    CASE WHEN a.FollowUpAt <= GETUTCDATE()
              AND a.Status = 'Applied' THEN 1 ELSE 0 END     AS IsFollowUpOverdue
FROM ScrapedJobs j
LEFT JOIN JobMatches       m ON j.Id = m.ScrapedJobId
LEFT JOIN Applications     a ON m.Id = a.JobMatchId;
GO

-- Follow-ups due today
CREATE OR ALTER VIEW vw_FollowUpsDue AS
SELECT
    a.Id            AS ApplicationId,
    j.Title,
    j.Location,
    j.Url,
    m.Score,
    a.AppliedAt,
    a.FollowUpAt,
    a.Notes
FROM Applications a
JOIN JobMatches   m ON a.JobMatchId  = m.Id
JOIN ScrapedJobs  j ON m.ScrapedJobId = j.Id
WHERE a.FollowUpAt <= GETUTCDATE()
  AND a.Status = 'Applied';
GO

-- ── Verify ───────────────────────────────────────────────────
PRINT '';
PRINT '=== Schema Summary ===';
SELECT
    t.name                          AS TableName,
    COUNT(c.column_id)              AS Columns,
    SUM(p.rows)                     AS Rows
FROM sys.tables     t
JOIN sys.columns    c ON t.object_id = c.object_id
JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0,1)
WHERE t.name IN ('ScrapedJobs','JobMatches','Applications','ApplicationEvents')
GROUP BY t.name
ORDER BY t.name;
GO

SELECT name AS ViewName FROM sys.views
WHERE name IN ('vw_Dashboard','vw_JobPipeline','vw_FollowUpsDue')
ORDER BY name;
GO

PRINT 'JobTracker database setup complete.';
GO