namespace JobTracker.Tests;

using JobTracker.Core.Commands;
using JobTracker.Messaging.Handlers;
using Microsoft.Extensions.Logging;
using Moq;
using NServiceBus;
using NUnit.Framework;

[TestFixture]
public class ResumeExportedHandlerTests
{
    private Mock<ILogger<ResumeExportedHandler>> _logger = null!;
    private ResumeExportedHandler _handler = null!;
    private Mock<IMessageHandlerContext> _context = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = new Mock<ILogger<ResumeExportedHandler>>();
        _handler = new ResumeExportedHandler(_logger.Object);
        _context = new Mock<IMessageHandlerContext>();
    }

    [Test]
    public async Task Handle_LogsJobId()
    {
        var command = BuildCommand();

        await _handler.Handle(command, _context.Object);

        _logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("JOB-123")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task Handle_LogsJobTitle()
    {
        var command = BuildCommand();

        await _handler.Handle(command, _context.Object);

        _logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Senior Software Engineer")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task Handle_LogsScore()
    {
        var command = BuildCommand();

        await _handler.Handle(command, _context.Object);

        _logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("9")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task Handle_LogsExportedFilePath()
    {
        var command = BuildCommand();

        await _handler.Handle(command, _context.Object);

        _logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Resume_SeniorSE")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task Handle_CompletesWithoutException()
    {
        var command = new ResumeExportedCommand();

        Assert.DoesNotThrowAsync(async () =>
            await _handler.Handle(command, _context.Object));
    }

    [Test]
    public async Task Handle_CompletesWithMinimalData()
    {
        var command = new ResumeExportedCommand
        {
            JobId = "",
            ExportedFilePath = ""
        };

        await _handler.Handle(command, _context.Object);

        _logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static ResumeExportedCommand BuildCommand() => new()
    {
        ScrapedJobId = 42,
        JobId = "JOB-123",
        JobTitle = "Senior Software Engineer",
        JobLocation = "Redmond, WA",
        JobUrl = "https://careers.microsoft.com/123",
        JobMatchId = 7,
        Score = 9,
        RecommendApply = true,
        ExportedFilePath = @"C:\Resumes\Resume_SeniorSE_JOB-123_2026-03-05.docx",
        ExportedAtUtc = new DateTime(2026, 3, 5, 12, 0, 0, DateTimeKind.Utc)
    };
}
