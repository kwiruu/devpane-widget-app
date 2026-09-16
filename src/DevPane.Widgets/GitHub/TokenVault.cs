using Windows.Security.Credentials;

namespace DevPane.Widgets.GitHub;

/// <summary>
/// Keeps the GitHub access token in Windows Credential Locker, encrypted for the current Windows user.
/// </summary>
internal static class TokenVault
{
    private const string Resource = "DevPane.GitHub";
    private const string UserName = "github";

    // HRESULT_FROM_WIN32(ERROR_NOT_FOUND), thrown when no credential is saved.
    private const int ElementNotFound = unchecked((int)0x80070490);

    public static string? Load()
    {
        try
        {
            var credential = new PasswordVault().Retrieve(Resource, UserName);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (Exception e) when (e.HResult == ElementNotFound)
        {
            return null;
        }
    }

    public static void Save(string token)
    {
        Remove();
        new PasswordVault().Add(new PasswordCredential(Resource, UserName, token));
    }

    public static void Remove()
    {
        try
        {
            var vault = new PasswordVault();
            vault.Remove(vault.Retrieve(Resource, UserName));
        }
        catch (Exception e) when (e.HResult == ElementNotFound)
        {
        }
    }
}
