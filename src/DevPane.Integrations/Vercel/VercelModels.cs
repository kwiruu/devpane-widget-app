namespace DevPane.Integrations.Vercel;

public enum VercelDeploymentState
{
    Unknown,
    Queued,
    Initializing,
    Building,
    Ready,
    Error,
    Canceled,
    Blocked,
    Deleted,
}

/// <param name="ProjectName">The project the deployment belongs to (Vercel calls this the deployment's name).</param>
/// <param name="IsProduction">True for production deployments, false for previews.</param>
/// <param name="InspectorUrl">The deployment's page on the Vercel dashboard, or null if Vercel didn't send one.</param>
/// <param name="Branch">The Git branch it was built from, or null for deployments not from Git.</param>
/// <param name="CommitMessage">The first line of the Git commit message, or null.</param>
/// <param name="Creator">Who deployed it: the commit author's Git login, or the Vercel user.</param>
/// <param name="BuildingAt">When the build started, or null if it hasn't.</param>
/// <param name="ReadyAt">When the deployment finished building, or null if it hasn't.</param>
/// <param name="ErrorMessage">Why the deployment failed or was canceled, or null.</param>
/// <param name="CommitSha">The full Git commit SHA it was built from, or null for deployments not from Git.</param>
public sealed record VercelDeployment(
    string Id,
    string ProjectId,
    string ProjectName,
    VercelDeploymentState State,
    bool IsProduction,
    string? Url,
    string? InspectorUrl,
    string? Branch,
    string? CommitMessage,
    DateTimeOffset CreatedAt,
    string? Creator = null,
    DateTimeOffset? BuildingAt = null,
    DateTimeOffset? ReadyAt = null,
    string? ErrorMessage = null,
    string? CommitSha = null)
{
    /// <summary>How long the build took, once it's finished.</summary>
    public TimeSpan? BuildDuration => BuildingAt is { } start && ReadyAt is { } end && end > start ? end - start : null;
}

/// <param name="IsError">True for lines written to stderr or marked as errors.</param>
public sealed record VercelLogLine(DateTimeOffset Time, string Text, bool IsError);

public sealed record VercelTeam(string Id, string Name);

/// <param name="Domain">The project's main production domain, such as "keiru.dev", or null if it has none.</param>
/// <param name="Repository">The linked Git repository as "owner/name", or null if the project isn't linked to Git.</param>
/// <param name="GitProvider">"github", "gitlab", or "bitbucket", or null.</param>
/// <param name="Live">The deployment serving production right now, or null if nothing is in production.</param>
public sealed record VercelProject(
    string Id,
    string Name,
    string? Domain = null,
    string? Repository = null,
    string? GitProvider = null,
    VercelDeployment? Live = null);

/// <summary>A Vercel call failed in a way the caller can explain to the user.</summary>
public class VercelException(string message) : Exception(message);

/// <summary>Vercel rejected the access token (HTTP 401), usually because it expired or was deleted.</summary>
public sealed class VercelUnauthorizedException(string message) : VercelException(message);

/// <summary>The token is valid but can't reach this team or project (HTTP 403).</summary>
public sealed class VercelForbiddenException(string message) : VercelException(message);

public sealed class VercelRateLimitException(DateTimeOffset resetAt)
    : VercelException($"Vercel's rate limit was reached. It resets at {resetAt:u}.")
{
    public DateTimeOffset ResetAt { get; } = resetAt;
}
