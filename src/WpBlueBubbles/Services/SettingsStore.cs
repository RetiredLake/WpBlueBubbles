using Windows.Storage;
using Windows.Security.Credentials;

namespace WpBlueBubbles.Services
{
    public sealed class ServerSettings
    {
        public string Address { get; set; }
        public string Password { get; set; }
        public bool IsComplete { get { return !string.IsNullOrWhiteSpace(Address) && !string.IsNullOrWhiteSpace(Password); } }
    }

    public static class SettingsStore
    {
        private const string AddressKey = "ServerAddress";
        private const string CredentialResource = "WpBlueBubbles.Server";
        private const string CredentialUser = "guid";

        public static ServerSettings Load()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            return new ServerSettings
            {
                Address = values.ContainsKey(AddressKey) ? values[AddressKey] as string : string.Empty,
                Password = LoadPassword()
            };
        }

        public static void Save(string address, string password)
        {
            ApplicationData.Current.LocalSettings.Values[AddressKey] = address == null ? string.Empty : address.Trim();
            var vault = new PasswordVault();
            try
            {
                var existing = vault.Retrieve(CredentialResource, CredentialUser);
                vault.Remove(existing);
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(password))
            {
                vault.Add(new PasswordCredential(CredentialResource, CredentialUser, password.Trim()));
            }
        }

        private static string LoadPassword()
        {
            try
            {
                var credential = new PasswordVault().Retrieve(CredentialResource, CredentialUser);
                credential.RetrievePassword();
                return credential.Password ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
