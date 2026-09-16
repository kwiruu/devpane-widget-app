using System.Net.Http.Headers;
using System.Text.Json;

namespace DevPane.Integrations.GitHub;

/// <summary>A code the user enters at <see cref="VerificationUri"/> to approve sign-in.</summary>
public sealed record DeviceCode(
    string Code,
    string UserCode,
    Uri VerificationUri,
    DateTimeOffset ExpiresAt,
    TimeSpan PollInterval);

public enum DeviceFlowOutcome
{
    Approved,
    Expired,
    Denied,
}

/// <param name="AccessToken">Set when <paramref name="Outcome"/> is <see cref="DeviceFlowOutcome.Approved"/>.</param>
public sealed record DeviceFlowResult(DeviceFlowOutcome Outcome, string? AccessToken);

/// <summary>GitHub refused to start or finish sign-in, for example because device flow is turned off.</summary>
public sealed class GitHubDeviceFlowException(string message) : GitHubException(message);

/// <summary>
/// GitHub's OAuth device flow: suited to apps without a browser redirect, like a widget.
/// See https://docs.github.com/apps/oauth-apps/building-oauth-apps/authorizing-oauth-apps#device-flow.
/// </summary>
public static class GitHubDeviceFlow
{
    private const string DeviceCodeUrl = "https://github.com/login/device/code";
    private const string AccessTokenUrl = "https://github.com/login/oauth/access_token";
    private const string DeviceGrantType = "urn:ietf:params:oauth:grant-type:device_code";
    private static readonly TimeSpan SlowDownStep = TimeSpan.FromSeconds(5);

    public static async Task<DeviceCode> RequestCodeAsync(string clientId, string scopes, CancellationToken cancellationToken)
    {
        using var json = await PostFormAsync(DeviceCodeUrl, new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = scopes,
        }, cancellationToken);

        var root = json.RootElement;
        string deviceCode = GitHubHttp.GetString(root, "device_code");
        if (deviceCode.Length == 0)
        {
            throw new GitHubDeviceFlowException(DescribeError(GitHubHttp.GetString(root, "error")));
        }

        return new DeviceCode(
            deviceCode,
            GitHubHttp.GetString(root, "user_code"),
            new Uri(GitHubHttp.GetString(root, "verification_uri")),
            DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()),
            TimeSpan.FromSeconds(root.GetProperty("interval").GetInt32()));
    }

    /// <summary>Polls GitHub until the user approves or denies the code, or it expires.</summary>
    public static async Task<DeviceFlowResult> WaitForTokenAsync(string clientId, DeviceCode code, CancellationToken cancellationToken)
    {
        TimeSpan interval = code.PollInterval;
        while (DateTimeOffset.UtcNow < code.ExpiresAt)
        {
            await Task.Delay(interval, cancellationToken);

            using var json = await PostFormAsync(AccessTokenUrl, new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["device_code"] = code.Code,
                ["grant_type"] = DeviceGrantType,
            }, cancellationToken);

            var root = json.RootElement;
            string token = GitHubHttp.GetString(root, "access_token");
            if (token.Length > 0)
            {
                return new DeviceFlowResult(DeviceFlowOutcome.Approved, token);
            }

            switch (GitHubHttp.GetString(root, "error"))
            {
                case "authorization_pending":
                    break;
                case "slow_down":
                    interval = root.TryGetProperty("interval", out var newInterval) && newInterval.ValueKind == JsonValueKind.Number
                        ? TimeSpan.FromSeconds(newInterval.GetInt32())
                        : interval + SlowDownStep;
                    break;
                case "expired_token":
                    return new DeviceFlowResult(DeviceFlowOutcome.Expired, null);
                case "access_denied":
                    return new DeviceFlowResult(DeviceFlowOutcome.Denied, null);
                case var error:
                    throw new GitHubDeviceFlowException(DescribeError(error));
            }
        }

        return new DeviceFlowResult(DeviceFlowOutcome.Expired, null);
    }

    private static async Task<JsonDocument> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await GitHubHttp.Client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType != "application/json")
        {
            throw new GitHubDeviceFlowException(DescribeError("incorrect_client_credentials"));
        }

        return await GitHubHttp.ReadJsonAsync(response, cancellationToken);
    }

    private static string DescribeError(string error) => error switch
    {
        "device_flow_disabled" => "Device flow is turned off for Dev Pane's GitHub OAuth App.",
        "incorrect_client_credentials" or "unsupported_grant_type" => "GitHub didn't accept Dev Pane's OAuth App client ID.",
        "" => "GitHub sign-in failed.",
        _ => $"GitHub sign-in failed ({error}).",
    };
}
