using Windows.Security.Credentials;

namespace DevPane.Widgets;

/// <summary>
/// Keeps an access token in Windows Credential Locker, encrypted for the current Windows user.
/// </summary>
internal sealed class TokenVault(string resource, string userName)
{
    // HRESULT_FROM_WIN32(ERROR_NOT_FOUND), thrown when no credential is saved.
    private const int ElementNotFound = unchecked((int)0x80070490);

    public static TokenVault GitHub { get; } = new("DevPane.GitHub", "github");

    public static TokenVault Vercel { get; } = new("DevPane.Vercel", "vercel");

    /// <summary>The Jira site, email, and API token, saved together as JSON.</summary>
    public static TokenVault Jira { get; } = new("DevPane.Jira", "jira");

    public string? Load()
    {
        try
        {
            var credential = new PasswordVault().Retrieve(resource, userName);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (Exception e) when (e.HResult == ElementNotFound)
        {
            return null;
        }
    }

    public void Save(string token)
    {
        Remove();
        new PasswordVault().Add(new PasswordCredential(resource, userName, token));
    }

    public void Remove()
    {
        try
        {
            var vault = new PasswordVault();
            vault.Remove(vault.Retrieve(resource, userName));
        }
        catch (Exception e) when (e.HResult == ElementNotFound)
        {
        }
    }
}
