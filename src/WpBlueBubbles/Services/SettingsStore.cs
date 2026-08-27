using Windows.Storage;
using Windows.Security.Credentials;

namespace WpBlueBubbles.Services
{
    public sealed class ServerSettings
    {
        public string Address { get; set; }
        public string Password { get; set; }
        public int MessagesPerChat { get; set; }
        public int SyncTimeframeDays { get; set; }
        public bool OledBlack { get; set; }
        public bool UseAccentColor { get; set; }
        public bool DeveloperMode { get; set; }
        public bool IsComplete { get { return !string.IsNullOrWhiteSpace(Address) && !string.IsNullOrWhiteSpace(Password); } }
    }

    public static class SettingsStore
    {
        private const string AddressKey = "ServerAddress";
        private const string CredentialResource = "WpBlueBubbles.Server";
        private const string CredentialUser = "guid";
        private const string MessagesPerChatKey = "MessagesPerChat";
        private const string SyncTimeframeDaysKey = "SyncTimeframeDays";
        private const string SyncDefaultsVersionKey = "SyncDefaultsVersion";
        private const string OledBlackKey = "OledBlack";
        private const string UseAccentColorKey = "UseAccentColor";
        private const string DeveloperModeKey = "DeveloperMode";

        public static ServerSettings Load()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            return new ServerSettings
            {
                Address = values.ContainsKey(AddressKey) ? values[AddressKey] as string : string.Empty,
                Password = LoadPassword(),
                MessagesPerChat = values.ContainsKey(MessagesPerChatKey) ? (int)values[MessagesPerChatKey] : 15,
                SyncTimeframeDays = values.ContainsKey(SyncTimeframeDaysKey) ? (int)values[SyncTimeframeDaysKey] : 7,
                OledBlack = ReadBoolean(values, OledBlackKey),
                UseAccentColor = ReadBoolean(values, UseAccentColorKey),
                DeveloperMode = ReadBoolean(values, DeveloperModeKey)
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

        public static void SaveSyncOptions(int messagesPerChat, int timeframeDays)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            values[MessagesPerChatKey] = messagesPerChat < 1 ? 1 : messagesPerChat;
            values[SyncTimeframeDaysKey] = timeframeDays < 0 ? 0 : timeframeDays;
        }

        public static void SaveAppearance(bool oledBlack, bool useAccentColor)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            values[OledBlackKey] = oledBlack;
            values[UseAccentColorKey] = useAccentColor;
        }

        public static void SaveDeveloperMode(bool enabled)
        {
            ApplicationData.Current.LocalSettings.Values[DeveloperModeKey] = enabled;
        }

        public static void EnsureVersion019Defaults()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            if (values.ContainsKey(SyncDefaultsVersionKey) && (int)values[SyncDefaultsVersionKey] >= 19) return;
            values[MessagesPerChatKey] = 15;
            values[SyncTimeframeDaysKey] = 7;
            values[SyncDefaultsVersionKey] = 19;
        }

        public static void Clear()
        {
            ApplicationData.Current.LocalSettings.Values.Clear();
            var vault = new PasswordVault();
            try { vault.Remove(vault.Retrieve(CredentialResource, CredentialUser)); }
            catch { }
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

        private static bool ReadBoolean(Windows.Foundation.Collections.IPropertySet values, string key)
        {
            return values.ContainsKey(key) && values[key] is bool && (bool)values[key];
        }
    }
}
