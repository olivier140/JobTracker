// JobTracker.WinForms/MainForm.cs
using JobTracker.Core;
using JobTracker.WordExport;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

/// <summary>
/// Represents the main user interface window for the JobTracker application, providing job scraping, scoring,
/// application tracking, and resume management features.
/// </summary>
/// <remarks>MainForm serves as the central hub for interacting with job data, matches, and applications. It
/// integrates job scraping, scoring, and status management workflows, and displays relevant information using a tabbed
/// interface. The form is designed for use with Microsoft Careers job data and provides controls for initiating
/// scraping, scoring, and updating application statuses. All user actions and system events are logged in the dashboard
/// tab for reference. This form is not thread-safe; all UI updates should occur on the UI thread.</remarks>
public partial class MainForm : Form
{
    private readonly IDbContextFactory<JobTrackerDbContext> _dbFactory;
    private readonly IJobScraper _scraper;
    private readonly IJobMatcher _matcher;
    private readonly AppSettings _settings;
    private readonly IResumeExporter _exporter;

    #region Controls 

    private TabControl _tabs = null!;
    private DataGridView _gridJobs = null!;
    private DataGridView _gridMatches = null!;
    private DataGridView _gridApps = null!;
    private RichTextBox _rtbResume = null!;
    private RichTextBox _rtbLog = null!;
    private ProgressBar _progress = null!;
    private Label _lblStatus = null!;
    private Button _btnScrape = null!;
    private Button _btnScore = null!;
    private Button _btnViewResume = null!;
    private Button _btnExportResume = null!;
    private Button _btnUpdateStatus = null!;
    private ComboBox _cmbStatus = null!;
    private Panel _statsPanel = null!;

    #endregion

    /// <summary>
    /// Initializes a new instance of the MainForm class with the specified database context factory, job scraper, job
    /// matcher, and application settings.
    /// </summary>
    /// <param name="dbFactory">The factory used to create instances of the JobTrackerDbContext for database operations. Cannot be null.</param>
    /// <param name="scraper">The job scraper responsible for retrieving job postings from external sources. Cannot be null.</param>
    /// <param name="matcher">The job matcher used to match job postings to user criteria. Cannot be null.</param>
    /// <param name="settings">The application settings that configure the behavior of the form. Cannot be null.</param>
    /// <param name="exporter">The resume exporter used to write tailored resumes to Word documents. Cannot be null.</param>
    public MainForm(
        IDbContextFactory<JobTrackerDbContext> dbFactory,
        IJobScraper scraper,
        IJobMatcher matcher,
        AppSettings settings,
        IResumeExporter exporter)
    {
        _dbFactory = dbFactory;
        _scraper = scraper;
        _matcher = matcher;
        _settings = settings;
        _exporter = exporter;

        InitializeComponent();
        WireEvents();
    }

    #region UI Construction 

    /// <summary>
    /// Initializes and configures all user interface components of the form. This method sets up controls, layouts,
    /// event handlers, and default values required for the application's main window.
    /// </summary>
    /// <remarks>This method is typically called during form construction and should not be invoked directly
    /// by user code. It arranges the toolbar, status bar, tab pages, and other UI elements to ensure the form is ready
    /// for user interaction.</remarks>
    private void InitializeComponent()
    {
        Text = "JobTracker — Microsoft Careers";
        Size = new Size(1280, 820);
        MinimumSize = new Size(1000, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        // Top toolbar 
        var toolbar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        _btnScrape = new Button { Text = "▶  Scrape Now", Width = 130, Height = 30 };
        _btnScore = new Button { Text = "⚡  Score Unscored", Width = 150, Height = 30 };
        var btnRefresh = new Button { Text = "↻  Refresh", Width = 100, Height = 30 };

        var topFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(4),
            BackColor = Color.FromArgb(0, 120, 212)
        };
        foreach (var btn in new[] { _btnScrape, _btnScore, btnRefresh })
        {
            btn.FlatStyle = FlatStyle.Flat;
            btn.ForeColor = Color.White;
            btn.BackColor = Color.FromArgb(0, 99, 177);
            btn.Margin = new Padding(4, 4, 0, 0);
        }
        topFlow.Controls.AddRange(new Control[] { _btnScrape, _btnScore, btnRefresh });
        btnRefresh.Click += async (_, _) => await RefreshAllAsync();

        // Stats panel 
        _statsPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            BackColor = Color.WhiteSmoke,
            Padding = new Padding(8, 4, 8, 4)
        };

        // Main tabs 
        _tabs = new TabControl { Dock = DockStyle.Fill };

        var tabDashboard = new TabPage("📊  Dashboard");
        var tabJobs = new TabPage("🔍  All Jobs");
        var tabMatches = new TabPage("✅  Matches");
        var tabApps = new TabPage("📬  Applications");
        var tabSettings = new TabPage("⚙️  Settings");

        // Dashboard
        _rtbLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.LightGreen,
            Font = new Font("Consolas", 9f),
            ScrollBars = RichTextBoxScrollBars.Vertical
        };
        tabDashboard.Controls.Add(_rtbLog);

        // All Jobs grid
        _gridJobs = BuildGrid();
        tabJobs.Controls.Add(_gridJobs);

        // Matches grid + action buttons
        _gridMatches = BuildGrid();
        _btnViewResume = new Button { Text = "View Tailored Resume", Width = 180, Height = 28 };
        _btnExportResume = new Button { Text = "⬇  Export to Word", Width = 160, Height = 28 };
        var matchBottom = BuildActionBar(_btnViewResume, _btnExportResume);
        var matchPanel = new Panel { Dock = DockStyle.Fill };
        matchPanel.Controls.Add(_gridMatches);
        matchPanel.Controls.Add(matchBottom);
        tabMatches.Controls.Add(matchPanel);

        // Applications grid + status update
        _gridApps = BuildGrid();
        _btnUpdateStatus = new Button { Text = "Update Status", Width = 130, Height = 28 };
        _cmbStatus = new ComboBox
        {
            Width = 160,
            Height = 28,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbStatus.Items.AddRange(new[]
            { "Pending", "Applied", "Interviewing", "Offer", "Rejected" });
        _cmbStatus.SelectedIndex = 0;
        var appsBottom = BuildActionBar(_cmbStatus, _btnUpdateStatus);
        var appsPanel = new Panel { Dock = DockStyle.Fill };
        appsPanel.Controls.Add(_gridApps);
        appsPanel.Controls.Add(appsBottom);
        tabApps.Controls.Add(appsPanel);

        // Settings
        _rtbResume = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Text = _settings.Resume,
            ScrollBars = RichTextBoxScrollBars.Vertical
        };
        var btnSaveSettings = new Button
        { Text = "Save Resume", Dock = DockStyle.Bottom, Height = 32 };
        btnSaveSettings.Click += (_, _) =>
        {
            _settings.Resume = _rtbResume.Text;
            Log("Resume updated in memory.");
        };
        tabSettings.Controls.Add(_rtbResume);
        tabSettings.Controls.Add(btnSaveSettings);

        _tabs.TabPages.AddRange(new[] { tabDashboard, tabJobs, tabMatches, tabApps, tabSettings });

        // Status bar 
        _progress = new ProgressBar
        { Dock = DockStyle.Bottom, Height = 6, Style = ProgressBarStyle.Marquee, Visible = false };
        _lblStatus = new Label
        { Dock = DockStyle.Bottom, Height = 22, Text = "Ready", Padding = new Padding(4, 4, 0, 0) };

        // Compose form 
        Controls.Add(_tabs);
        Controls.Add(_statsPanel);
        Controls.Add(topFlow);
        Controls.Add(_lblStatus);
        Controls.Add(_progress);
    }

    /// <summary>
    /// Creates and configures a new read-only DataGridView control with standard layout and appearance settings.
    /// </summary>
    /// <remarks>The returned DataGridView is set to fill its container, disallow user-added rows, and uses
    /// alternating row colors for improved readability. The control is intended for display purposes and does not
    /// support editing by default.</remarks>
    /// <returns>A DataGridView instance configured for full-row selection, automatic column sizing, and a consistent visual
    /// style.</returns>
    private static DataGridView BuildGrid()
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            { BackColor = Color.FromArgb(245, 245, 245) },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            { Font = new Font("Segoe UI", 9f, FontStyle.Bold), BackColor = Color.FromArgb(0, 120, 212), ForeColor = Color.White }
        };
        g.EnableHeadersVisualStyles = false;
        return g;
    }

    /// <summary>
    /// Creates a bottom-docked action bar panel containing the specified controls, arranged horizontally with
    /// consistent padding.
    /// </summary>
    /// <remarks>The returned panel is docked to the bottom, has a fixed height, and applies uniform padding
    /// and margin to its child controls. This method is intended for constructing action bars in Windows Forms
    /// applications.</remarks>
    /// <param name="controls">An array of controls to add to the action bar. Controls are arranged in the order provided.</param>
    /// <returns>A FlowLayoutPanel configured as an action bar containing the specified controls.</returns>
    private static Panel BuildActionBar(params Control[] controls)
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            BackColor = Color.WhiteSmoke,
            Padding = new Padding(4)
        };
        foreach (var c in controls) { c.Margin = new Padding(4, 4, 0, 0); }
        bar.Controls.AddRange(controls);
        return bar;
    }

    #endregion

    #region Event Wiring 

    /// <summary>
    /// Attaches event handlers to UI controls and internal components to enable user interaction and progress
    /// reporting.
    /// </summary>
    /// <remarks>This method should be called during initialization to ensure that all relevant events are
    /// properly wired. It enables the form to respond to user actions such as button clicks and to update progress
    /// based on background operations.</remarks>
    private void WireEvents()
    {
        _scraper.OnProgress += SafeLog;
        _matcher.OnProgress += SafeLog;

        _btnScrape.Click += async (_, _) => await RunScrapeAsync();
        _btnScore.Click += async (_, _) => await RunScoreAsync();
        _btnViewResume.Click += OnViewResume;
        _btnExportResume.Click += async (_, _) => await OnExportResumeAsync();
        _btnUpdateStatus.Click += async (_, _) => await OnUpdateStatusAsync();

        Load += async (_, _) =>
        {
            await RefreshAllAsync();
            CheckFollowUps();
        };
    }

    #endregion

    #region Pipeline Actions 

    /// <summary>
    /// Performs an asynchronous scrape of Microsoft careers data and updates the job listings.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task RunScrapeAsync()
    {
        SetBusy(true, "Scraping Microsoft careers...");
        try
        {
            var jobs = await _scraper.ScrapeAndPersistAsync(
                _settings.SearchQuery, _settings.SearchLocation, _settings.MaxPages);
            Log($"Scrape complete: {jobs.Count} new jobs.");
            await RefreshAllAsync();
        }
        catch (Exception ex) { Log($"Error: {ex.Message}", Color.OrangeRed); }
        finally { SetBusy(false); }
    }

    /// <summary>
    /// Asynchronously scores all unscored jobs and refreshes the job list.
    /// </summary>
    /// <remarks>This method sets the busy state while scoring is in progress and logs the outcome. Any
    /// exceptions encountered during scoring are logged, and the busy state is cleared when the operation
    /// completes.</remarks>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task RunScoreAsync()
    {
        SetBusy(true, "Scoring unscored jobs via Claude...");
        try
        {
            await _matcher.ScoreAllUnscoredAsync(_settings.Resume, _settings.MinScoreToApply);
            Log("Scoring complete.");
            await RefreshAllAsync();
        }
        catch (Exception ex) { Log($"Error: {ex.Message}", Color.OrangeRed); }
        finally { SetBusy(false); }
    }

    /// <summary>
    /// Asynchronously refreshes all data grids and statistics displayed in the user interface.
    /// </summary>
    /// <remarks>This method sequentially reloads jobs, matches, applications, and statistics data. Use this
    /// method to ensure all displayed information is up to date.</remarks>
    /// <returns>A task that represents the asynchronous refresh operation.</returns>
    private async Task RefreshAllAsync()
    {
        await LoadJobsGridAsync();
        await LoadMatchesGridAsync();
        await LoadApplicationsGridAsync();
        await LoadStatsAsync();
    }

    #endregion

    #region Grid Loaders 

    /// <summary>
    /// Asynchronously loads the list of scraped jobs and updates the jobs grid with the latest data.
    /// </summary>
    /// <remarks>The jobs grid is populated with job entries ordered by the most recently scraped. This method
    /// should be called from a context that supports asynchronous operations.</remarks>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task LoadJobsGridAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var jobs = await db.ScrapedJobs
            .OrderByDescending(j => j.ScrapedAt)
            .Select(j => new
            {
                j.Id,
                j.Title,
                j.Location,
                Posted = j.PostedDate.HasValue ? j.PostedDate.Value.ToString("yyyy-MM-dd") : "",
                Scraped = j.ScrapedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                Scored = j.Match != null ? "Yes" : "No",
                j.Url
            }).ToListAsync();

        SafeInvoke(() =>
        {
            _gridJobs.DataSource = jobs;
            HideCol(_gridJobs, "Id"); HideCol(_gridJobs, "Url");
        });
    }

    /// <summary>
    /// Asynchronously loads job match data from the database and updates the matches grid with the latest results.
    /// </summary>
    /// <remarks>The grid is updated on the UI thread with the retrieved match data, including job details and
    /// application status. This method should be awaited to ensure the grid reflects the most current data.</remarks>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task LoadMatchesGridAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var matches = await db.JobMatches
            .Include(m => m.ScrapedJob)
            .Include(m => m.Application)
            .OrderByDescending(m => m.Score)
            .Select(m => new
            {
                m.Id,
                Title = m.ScrapedJob!.Title,
                Location = m.ScrapedJob.Location,
                m.Score,
                Recommend = m.RecommendApply ? "✓" : "",
                Status = m.Application != null ? m.Application.Status : "—",
                Posted = m.ScrapedJob.PostedDate.HasValue
                            ? m.ScrapedJob.PostedDate.Value.ToString("yyyy-MM-dd") : "",
                Url = m.ScrapedJob.Url,
                HasTailored = m.TailoredResume != null ? "✓" : ""
            }).ToListAsync();

        SafeInvoke(() =>
        {
            _gridMatches.DataSource = matches;
            HideCol(_gridMatches, "Id"); HideCol(_gridMatches, "Url");
            ColorRows(_gridMatches, "Score", row =>
            {
                if (int.TryParse(row.Cells["Score"].Value?.ToString(), out int s))
                    return s >= 8 ? Color.FromArgb(198, 239, 206)
                         : s >= 6 ? Color.FromArgb(255, 235, 156)
                         : Color.FromArgb(255, 199, 206);
                return Color.White;
            });
        });
    }

    /// <summary>
    /// Asynchronously loads the list of applications and updates the applications grid with the latest data.
    /// </summary>
    /// <remarks>This method retrieves application data from the database, including related job and match
    /// information, and updates the grid's data source. The method should be awaited to ensure the grid is updated
    /// before further actions are taken. Thread safety is maintained by invoking the grid update on the appropriate
    /// thread.</remarks>
    /// <returns>A task that represents the asynchronous load operation.</returns>
    private async Task LoadApplicationsGridAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var apps = await db.Applications
            .Include(a => a.JobMatch).ThenInclude(m => m!.ScrapedJob)
            .OrderByDescending(a => a.LastUpdatedAt)
            .Select(a => new
            {
                a.Id,
                Title = a.JobMatch!.ScrapedJob!.Title,
                Location = a.JobMatch.ScrapedJob.Location,
                Score = a.JobMatch.Score,
                a.Status,
                Applied = a.AppliedAt.HasValue ? a.AppliedAt.Value.ToString("yyyy-MM-dd") : "—",
                FollowUp = a.FollowUpAt.HasValue ? a.FollowUpAt.Value.ToString("yyyy-MM-dd") : "—",
                OverDue = a.FollowUpAt.HasValue && a.FollowUpAt < DateTime.UtcNow
                            && a.Status == "Applied" ? "⚠" : "",
                a.Notes
            }).ToListAsync();

        SafeInvoke(() => _gridApps.DataSource = apps);
    }

    /// <summary>
    /// Asynchronously loads job statistics from the database and updates the statistics panel with the latest values.
    /// </summary>
    /// <remarks>This method retrieves counts for various job-related metrics, such as scraped jobs, scored
    /// jobs, strong matches, applications, interviews, offers, and follow-ups due. The statistics panel is updated on
    /// the UI thread to reflect the current state. This method is intended for internal use and should be called from a
    /// context where updating the UI is appropriate.</remarks>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task LoadStatsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var scraped = await db.ScrapedJobs.CountAsync();
        var scored = await db.JobMatches.CountAsync();
        var strong = await db.JobMatches.CountAsync(m => m.Score >= 7);
        var applied = await db.Applications.CountAsync(a => a.Status == "Applied");
        var interview = await db.Applications.CountAsync(a => a.Status == "Interviewing");
        var offers = await db.Applications.CountAsync(a => a.Status == "Offer");
        var followUps = await db.Applications
            .CountAsync(a => a.FollowUpAt <= DateTime.UtcNow && a.Status == "Applied");

        SafeInvoke(() =>
        {
            _statsPanel.Controls.Clear();
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            void AddStat(string label, int val, Color? bg = null) =>
                flow.Controls.Add(new Label
                {
                    Text = $"{label}: {val}",
                    AutoSize = false,
                    Width = 160,
                    Height = 44,
                    TextAlign = ContentAlignment.MiddleCenter,
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = bg ?? Color.White,
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                    Margin = new Padding(2)
                });

            AddStat("Scraped", scraped);
            AddStat("Scored", scored);
            AddStat("Strong (7+)", strong, Color.FromArgb(198, 239, 206));
            AddStat("Applied", applied, Color.FromArgb(189, 215, 238));
            AddStat("Interview", interview, Color.FromArgb(255, 235, 156));
            AddStat("Offers", offers, Color.FromArgb(198, 239, 206));
            if (followUps > 0)
                AddStat("Follow-ups due", followUps, Color.FromArgb(255, 199, 206));

            _statsPanel.Controls.Add(flow);
        });
    }

    #endregion

    #region Action Handlers 

    /// <summary>
    /// Handles the event when the view is resumed and displays the tailored resume for the selected job match, if
    /// available.
    /// </summary>
    /// <remarks>If no job match is selected in the grid, this handler does nothing. The tailored resume is
    /// displayed in a read-only window, and users can copy its contents to the clipboard.</remarks>
    /// <param name="sender">The source of the event. This parameter is typically the control that triggered the event.</param>
    /// <param name="e">An object that contains the event data.</param>
    private void OnViewResume(object? sender, EventArgs e)
    {
        if (_gridMatches.SelectedRows.Count == 0) return;
        var row = _gridMatches.SelectedRows[0];
        var id = (int)row.Cells["Id"].Value;

        Task.Run(async () =>
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var match = await db.JobMatches.FindAsync(id);
            var text = match?.TailoredResume ?? "No tailored resume available for this job.";
            SafeInvoke(() =>
            {
                var viewer = new Form
                {
                    Text = $"Tailored Resume — {row.Cells["Title"].Value}",
                    Size = new Size(800, 700)
                };
                var rtb = new RichTextBox
                {
                    Dock = DockStyle.Fill,
                    Text = text,
                    ReadOnly = true,
                    ScrollBars = RichTextBoxScrollBars.Vertical
                };
                var btnCopy = new Button
                { Text = "Copy to Clipboard", Dock = DockStyle.Bottom, Height = 32 };
                btnCopy.Click += (_, _) => Clipboard.SetText(text);
                viewer.Controls.Add(rtb);
                viewer.Controls.Add(btnCopy);
                viewer.Show(this);
            });
        });
    }

    /// <summary>
    /// Exports the tailored resume for the selected match to a Word document and prompts
    /// the user to open the containing folder on success.
    /// </summary>
    private async Task OnExportResumeAsync()
    {
        if (_gridMatches.SelectedRows.Count == 0) return;
        var id = (int)_gridMatches.SelectedRows[0].Cells["Id"].Value;

        SetBusy(true, "Exporting resume to Word…");
        try
        {
            var result = await _exporter.ExportAsync(id);
            if (result.Success)
            {
                Log($"Exported: {result.FilePath}");
                if (MessageBox.Show(
                        $"Resume exported successfully.\n\n{result.FilePath}\n\nOpen folder?",
                        "Export Complete", MessageBoxButtons.YesNo, MessageBoxIcon.Information)
                    == DialogResult.Yes)
                {
                    Process.Start("explorer.exe", $"/select,\"{result.FilePath}\"");
                }
            }
            else
            {
                Log($"Export failed: {result.Error}", Color.OrangeRed);
                MessageBox.Show($"Export failed:\n{result.Error}",
                    "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            Log($"Export error: {ex.Message}", Color.OrangeRed);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// Updates the status of the selected application in the grid asynchronously.
    /// </summary>
    /// <remarks>If no application is selected in the grid, the method completes without performing any
    /// action. After updating the status, the applications grid is refreshed to reflect the changes.</remarks>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private async Task OnUpdateStatusAsync()
    {
        if (_gridApps.SelectedRows.Count == 0) return;
        var id = (int)_gridApps.SelectedRows[0].Cells["Id"].Value;
        var newStatus = _cmbStatus.SelectedItem?.ToString() ?? "Pending";
        await _matcher.UpdateStatusAsync(id, newStatus);
        Log($"Application {id} → {newStatus}");
        await LoadApplicationsGridAsync();
    }

    /// <summary>
    /// Checks for applications that are overdue for follow-up and notifies the user if any are found.
    /// </summary>
    /// <remarks>This method runs asynchronously and displays a reminder message if there are applications
    /// with a follow-up date that has passed and a status of "Applied." The notification includes up to five overdue
    /// applications. This method is intended for internal use and does not return a value.</remarks>
    private void CheckFollowUps()
    {
        Task.Run(async () =>
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var due = await db.Applications
                .Include(a => a.JobMatch).ThenInclude(m => m!.ScrapedJob)
                .Where(a => a.FollowUpAt <= DateTime.UtcNow && a.Status == "Applied")
                .ToListAsync();

            if (due.Count > 0)
                SafeInvoke(() =>
                    MessageBox.Show(
                        $"You have {due.Count} follow-up(s) overdue!\n\n" +
                        string.Join("\n", due.Take(5).Select(a =>
                            $"• {a.JobMatch?.ScrapedJob?.Title} (due {a.FollowUpAt:yyyy-MM-dd})")),
                        "Follow-up Reminder", MessageBoxButtons.OK, MessageBoxIcon.Information));
        });
    }

    #endregion

    #region Helpers 

    /// <summary>
    /// Appends a timestamped message to the log display, optionally using a specified text color.
    /// </summary>
    /// <remarks>This method is intended for internal use to display log messages in a RichTextBox control.
    /// The message is automatically prefixed with the current time and scrolled into view.</remarks>
    /// <param name="msg">The message text to append to the log. Cannot be null.</param>
    /// <param name="color">The color to use for the message text. If null, a default color is used.</param>
    private void Log(string msg, Color? color = null)
    {
        SafeInvoke(() =>
        {
            _rtbLog.SelectionStart = _rtbLog.TextLength;
            _rtbLog.SelectionColor = color ?? Color.LightGreen;
            _rtbLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            _rtbLog.ScrollToCaret();
        });
    }

    private void SafeLog(string msg) => Log(msg);

    /// <summary>
    /// Sets the busy state of the user interface and updates the status message accordingly.
    /// </summary>
    /// <param name="busy">true to indicate that the interface is busy and user actions should be disabled; otherwise, false.</param>
    /// <param name="status">An optional status message to display when the interface is busy. If null, a default message is shown.</param>
    private void SetBusy(bool busy, string? status = null)
    {
        SafeInvoke(() =>
        {
            _progress.Visible = busy;
            _lblStatus.Text = busy ? status ?? "Working..." : "Ready";
            _btnScrape.Enabled = !busy;
            _btnScore.Enabled = !busy;
        });
    }

    /// <summary>
    /// Hides the column with the specified name in the provided DataGridView, if it exists.
    /// </summary>
    /// <remarks>If the specified column does not exist in the DataGridView, this method has no
    /// effect.</remarks>
    /// <param name="g">The DataGridView containing the column to hide. Cannot be null.</param>
    /// <param name="name">The name of the column to hide. The comparison is case-sensitive.</param>
    private static void HideCol(DataGridView g, string name)
    {
        if (g.Columns.Contains(name)) g.Columns[name].Visible = false;
    }

    /// <summary>
    /// Applies a background color to each row in the specified DataGridView based on a user-provided function.
    /// </summary>
    /// <remarks>This method iterates through all rows in the DataGridView and sets their background color
    /// according to the provided function. The method does not validate the input parameters; callers should ensure
    /// that the DataGridView and color function are valid before calling.</remarks>
    /// <param name="g">The DataGridView whose rows will be colored. Cannot be null.</param>
    /// <param name="colName">The name of the column used for context when determining row colors. This parameter is not used directly in this
    /// method but may be relevant for the color function.</param>
    /// <param name="colorFn">A function that takes a DataGridViewRow and returns the Color to apply as the row's background. Cannot be null.</param>
    private static void ColorRows(
        DataGridView g, string colName,
        Func<DataGridViewRow, Color> colorFn)
    {
        foreach (DataGridViewRow row in g.Rows)
            row.DefaultCellStyle.BackColor = colorFn(row);
    }

    /// <summary>
    /// Invokes the specified action on the UI thread if required, or executes it directly if already on the UI thread.
    /// </summary>
    /// <remarks>Use this method to safely update UI elements from background threads. If called from a non-UI
    /// thread, the action is marshaled to the UI thread; otherwise, it is executed immediately.</remarks>
    /// <param name="action">The action to execute. Cannot be null.</param>
    private void SafeInvoke(Action action)
    {
        if (InvokeRequired) Invoke(action);
        else action();
    }

    #endregion
}


//## Final Solution Overview
//```
//JobTracker.sln
//│
//├── JobTracker.Core /
//│   ├── Models.cs                 ← API + DB + settings models
//│   ├── JobTrackerDbContext.cs    ← EF Core context + schema
//│   ├── MicrosoftJobsScraper.cs  ← career page scraper
//│   ├── ClaudeJobMatcher.cs      ← Claude scoring + tailoring
//│   └── ServiceRegistration.cs  ← shared DI wiring
//│
//├── JobTracker.WinForms/
//│   ├── Program.cs               ← DI bootstrap
//│   └── MainForm.cs              ← full dashboard UI
//│        ├── Tab: Dashboard      → live log output
//│        ├── Tab: All Jobs       → every scraped job
//│        ├── Tab: Matches        → scored jobs, color-coded
//│        ├── Tab: Applications   → status tracker + follow-up alerts
//│        └── Tab: Settings       → edit resume in-app
//│
//└── JobTracker.Service/
//    ├── appsettings.json          ← service config
//    ├── ScraperWorker.cs         ← scheduled pipeline (BackgroundService)
//    └── Program.cs               ← Windows Service host