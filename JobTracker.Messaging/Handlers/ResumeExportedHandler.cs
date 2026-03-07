namespace JobTracker.Messaging.Handlers;

using JobTracker.Core.Commands;
using Microsoft.Extensions.Logging;
using NServiceBus;

/// <summary>
/// Handles <see cref="ResumeExportedCommand"/> messages sent after a tailored resume
/// has been exported to a Word document.
/// </summary>
/// <remarks>
/// This handler currently logs the received command data. Add downstream business logic
/// here — notifications, external API calls, analytics, workflow triggers, etc.
/// </remarks>
public class ResumeExportedHandler : IHandleMessages<ResumeExportedCommand>
{
    private readonly ILogger<ResumeExportedHandler> _logger;

    public ResumeExportedHandler(ILogger<ResumeExportedHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(ResumeExportedCommand message, IMessageHandlerContext context)
    {
        _logger.LogInformation(
            "Resume exported — JobId={JobId}, Title={Title}, Score={Score}, Path={Path}",
            message.JobId,
            message.JobTitle,
            message.Score,
            message.ExportedFilePath);

        // TODO: Add downstream business logic here
        // Examples:
        //   - Send email/Teams notification
        //   - Update external tracking system
        //   - Record analytics event
        //   - Trigger additional workflow steps

        return Task.CompletedTask;
    }
}
