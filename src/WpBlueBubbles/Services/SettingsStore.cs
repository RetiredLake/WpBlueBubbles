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
        public bool LargerUi { get; set; }
        public bool DeveloperMode { get; set; }
        public int PollIntervalSeconds { get; set; }
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
        private const string LargerUiKey = "LargerUi";
        private const string DeveloperModeKey = "DeveloperMode";
        private const string PollIntervalSecondsKey = "PollIntervalSeconds";

        public static ServerSettings Load()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            return new ServerSettings
            {
                Address = values.ContainsKey(AddressKey) ? values[AddressKey] as string : string.Empty,
                Password = LoadPassword(),
                MessagesPerChat = ReadInteger(values, MessagesPerChatKey, 15, 1, 50),
                SyncTimeframeDays = ReadInteger(values, SyncTimeframeDaysKey, 7, 0, 3650),
                OledBlack = ReadBoolean(values, OledBlackKey, true),
                UseAccentColor = ReadBoolean(values, UseAccentColorKey),
                LargerUi = ReadBoolean(values, LargerUiKey),
                DeveloperMode = ReadBoolean(values, DeveloperModeKey),
                PollIntervalSeconds = ReadInteger(values, PollIntervalSecondsKey, 5, 3, 60)
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

        public static void SaveAppearance(bool oledBlack, bool useAccentColor, bool largerUi)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            values[OledBlackKey] = oledBlack;
            values[UseAccentColorKey] = useAccentColor;
            values[LargerUiKey] = largerUi;
        }

        public static void SaveDeveloperMode(bool enabled)
        {
            ApplicationData.Current.LocalSettings.Values[DeveloperModeKey] = enabled;
        }

        public static void SavePollInterval(int seconds)
        {
            ApplicationData.Current.LocalSettings.Values[PollIntervalSecondsKey] = seconds < 3 ? 3 : seconds > 60 ? 60 : seconds;
        }

        public static void EnsureVersion019Defaults()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            if (ReadInteger(values, SyncDefaultsVersionKey, 0, 0, 1000) >= 19) return;
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

        private static bool ReadBoolean(Windows.Foundation.Collections.IPropertySet values, string key, bool fallback = false)
        {
            return values.ContainsKey(key) && values[key] is bool ? (bool)values[key] : fallback;
        }

        private static int ReadInteger(Windows.Foundation.Collections.IPropertySet values, string key, int fallback, int minimum, int maximum)
        {
            object raw;
            if (!values.TryGetValue(key, out raw) || raw == null) return fallback;
            int value;
            if (raw is int) value = (int)raw;
            else if (!int.TryParse(raw.ToString(), out value)) return fallback;
            return value < minimum || value > maximum ? fallback : value;
        }
    }
}
