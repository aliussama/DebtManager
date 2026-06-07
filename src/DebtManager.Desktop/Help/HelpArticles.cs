namespace DebtManager.Desktop.Help;

/// <summary>
/// Offline-first help articles embedded in the application.
/// No web links required — all content is local.
/// </summary>
public static class HelpArticles
{
    public sealed record HelpArticle(string Id, string Title, string Category, string Body);

    public static readonly IReadOnlyList<HelpArticle> All = new List<HelpArticle>
    {
        new("getting-started", "Getting Started", "General",
@"Welcome to DebtManager — your offline-first personal finance management tool.

FIRST STEPS
1. Complete the onboarding wizard to set your default currency and timezone.
2. Create accounts (bank, cash, credit card) in the Accounts page.
3. Record expenses and income in the Cash Ledger.
4. Set up budgets and recurring transactions.

NAVIGATION
• Use the left sidebar to navigate between sections.
• Press Ctrl+F to focus the search bar on supported pages.
• Press Escape to close any open dialog.

All data is stored locally on your device and encrypted at rest."),

        new("backup-restore", "Backup & Restore", "Data",
@"CREATING A BACKUP
Go to Settings ? Backup & Restore ? 'Backup Vault'.
This creates a .dmvault file containing your entire database.
Store this file in a safe location (external drive, cloud storage).

RESTORING FROM BACKUP
Go to Settings ? Backup & Restore ? 'Restore from Backup'.
Select a .dmvault file. The app will validate integrity before restoring.
IMPORTANT: Restore replaces ALL current data. The current vault is backed up automatically before restore.

EXPORT / IMPORT
Use the Export Vault feature to create a portable package of your data.
Import always creates a NEW vault — it never overwrites your existing data in-place."),

        new("import-troubleshooting", "Import Troubleshooting", "Data",
@"CSV IMPORT ISSUES
• Ensure your CSV has a header row matching the expected format.
• Check that date formats match the profile configuration (e.g., yyyy-MM-dd).
• Amount columns should use '.' as decimal separator.
• If a batch import fails, use 'Undo Batch' to revert all transactions.

BANK PROFILE SETUP
• Create a bank import profile with the correct column mapping.
• Use 'Preview' to verify before committing.
• Import Rules can auto-categorize transactions — set them up in Import Rules.

RECONCILIATION
• After import, review the reconciliation candidates.
• Match, apply, or ignore each imported transaction.
• Use Bulk Apply for unmatched transactions after review."),

        new("fx-currency", "FX & Currency Issues", "Finance",
@"MULTI-CURRENCY SUPPORT
DebtManager supports multiple currencies. Set your reporting currency in Currency & FX settings.

EXCHANGE RATES
• Record FX rates manually via Assets ? Record FX Rate.
• Rates are used for net worth calculations and cross-currency reports.
• The FX policy (Latest, Historical, Average) can be configured in Currency & FX settings.

COMMON ISSUES
• If net worth shows unexpected values, check that FX rates are recorded for all currencies.
• Cross-currency transfers require both source and destination amounts."),

        new("data-quality", "Data Quality & Notifications", "Administration",
@"DATA QUALITY SCANS
Run a data quality scan from the Data Health page to detect issues like:
• Orphaned references (deleted accounts still linked to budgets)
• Missing FX rates for active currencies
• Unreconciled bank imports
• Budget overruns

NOTIFICATIONS
Configure notification rules to get alerts for:
• Upcoming bill due dates
• Budget threshold breaches
• Goal milestones
• Overdue payments

All notifications are local — no push notifications or external services."),

        new("safe-mode", "Safe Mode", "Troubleshooting",
@"WHAT IS SAFE MODE?
If the application detects an unclean shutdown (crash), it may offer to start in Safe Mode.

IN SAFE MODE:
• Heavy operations (Forecast, AI Analysis) are disabled.
• Auto-apply import rules are paused.
• You can navigate and review your data safely.
• No data is lost — Safe Mode is read-heavy only.

EXITING SAFE MODE
Click the 'Exit Safe Mode' button in the recovery banner to return to normal operation.

OPENING LOGS
Click 'Open Logs' to view diagnostic log files. Each error includes a Correlation ID that helps pinpoint the issue."),

        new("data-location", "Where Is My Data Stored?", "Data",
@"All data is stored locally under your user profile:

WINDOWS: %LOCALAPPDATA%\DebtManager\
• debtmanager_local.db — Main encrypted database (events + projections)
• auth_vault.bin — Encrypted authentication vault
• logs\ — Rolling diagnostic log files
• Backups\ — Safety backups created during restore operations

The database is encrypted at rest using AES-256-GCM.
Encryption keys are protected by the operating system's key storage (DPAPI on Windows).

DATA PORTABILITY
Use Export Vault to create a portable .dmvault package.
This package can be imported on another device running DebtManager."),

        new("keyboard-shortcuts", "Keyboard Shortcuts", "General",
@"GLOBAL
• Ctrl+F — Focus search (on supported pages)
• Escape — Close active dialog or return to previous view
• Ctrl+N — Open Add menu

NAVIGATION
• Use Tab to move between sidebar items and content areas.
• Enter — Activate focused button or link.

DIALOGS
• Enter — Submit the dialog form (when focused on the submit button).
• Escape — Cancel and close the dialog.

GRIDS
• Arrow keys — Navigate between rows.
• Enter — Open detail view for selected row."),
    };

    public static IReadOnlyList<HelpArticle> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return All;

        var q = query.Trim();
        return All.Where(a =>
            a.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            a.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            a.Body.Contains(q, StringComparison.OrdinalIgnoreCase)
        ).ToList();
    }
}
