USE JobTracker

  SELECT TOP (1000) [Id]
      ,[ScrapedJobId]
      ,[Score]
      ,[TopMatchesJson]
      ,[GapsJson]
      ,[RecommendApply]
      ,[TailoredResume]
      ,[CoverLetter]
      ,[EvaluatedAt]
  FROM [JobTracker].[dbo].[JobMatches]

SELECT TOP (1000) [Id]
      ,[JobId]
      ,[Title]
      ,[Location]
      ,[DescriptionFull]
      ,[Url]
      ,[PostedDate]
      ,[ScrapedAt]
  FROM [JobTracker].[dbo].[ScrapedJobs]

  /*
  DELETE [JobTracker].[dbo].[ScrapedJobs]

  DELETE    [JobTracker].[dbo].[JobMatches]
  */
