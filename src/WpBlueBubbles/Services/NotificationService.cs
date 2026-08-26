using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using WpBlueBubbles.Models;

namespace WpBlueBubbles.Services
{
    public static class NotificationService
    {
        public const string TaskName = "WpBlueBubbles.MessageSync";
        private const string EnabledKey = "NotificationsEnabled";
        private const string StatusKey = "NotificationStatus";
        private const string LastSyncKey = "NotificationLastSync";

        public static bool IsEnabled
        {
            get
            {
                var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                return values.ContainsKey(EnabledKey) && values[EnabledKey] is bool && (bool)values[EnabledKey];
            }
        }

        public static string Status
        {
            get
            {
                var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                return values.ContainsKey(StatusKey) ? values[StatusKey] as string : "Off";
            }
        }

        public static async Task<string> EnableAsync()
        {
            var access = await BackgroundExecutionManager.RequestAccessAsync();
            if (!access.ToString().StartsWith("Allowed", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("Background access was not granted (" + access + ").");
                return Status;
            }

            Unregister();
            var builder = new BackgroundTaskBuilder { Name = TaskName };
            builder.SetTrigger(new TimeTrigger(15, false));
            builder.Register();
            Windows.Storage.ApplicationData.Current.LocalSettings.Values[EnabledKey] = true;
            SetStatus("On. Background sync is best effort every 15 minutes.");
            await UpdateTileAsync();
            return Status;
        }

        public static async Task DisableAsync()
        {
            Unregister();
            Windows.Storage.ApplicationData.Current.LocalSettings.Values[EnabledKey] = false;
            SetStatus("Off");
            TileUpdateManager.CreateTileUpdaterForApplication().Clear();
            BadgeUpdateManager.CreateBadgeUpdaterForApplication().Clear();
            await Task.CompletedTask;
        }

        public static async Task RunBackgroundSyncAsync()
        {
            if (!IsEnabled) return;
            var settings = SettingsStore.Load();
            if (!settings.IsComplete) return;
            try
            {
                using (var client = new BlueBubblesClient(settings.Address, settings.Password))
                {
                    var chats = await client.GetChatsAsync();
                    await ObserveChatsAsync(chats, true);
                }
                Windows.Storage.ApplicationData.Current.LocalSettings.Values[LastSyncKey] = DateTimeOffset.UtcNow.ToString("o");
                SetStatus("On. Last checked " + DateTimeOffset.Now.ToString("t") + ".");
            }
            catch (Exception ex)
            {
                SetStatus("Background sync failed: " + ex.Message);
            }
        }

        public static async Task ObserveChatsAsync(IEnumerable<ChatItem> chats, bool showToasts)
        {
            var states = NotificationStateStore.Load().ToDictionary(pair => pair.Key, pair => pair.Value);
            var toastCandidates = new List<ChatItem>();
            foreach (var chat in chats)
            {
                if (string.IsNullOrWhiteSpace(chat.Guid) || chat.LastMessageTimestamp <= 0) continue;
                NotificationChatState prior;
                if (!states.TryGetValue(chat.Guid, out prior))
                {
                    states[chat.Guid] = NewState(chat, false);
                    continue;
                }
                var changed = chat.LastMessageTimestamp > prior.Timestamp ||
                    (chat.LastMessageTimestamp == prior.Timestamp && !string.Equals(chat.LastMessageGuid, prior.MessageGuid, StringComparison.Ordinal));
                if (!changed) continue;
                prior.Timestamp = chat.LastMessageTimestamp;
                prior.MessageGuid = chat.LastMessageGuid;
                if (!chat.LastMessageIsFromMe)
                {
                    prior.IsUnread = true;
                    toastCandidates.Add(chat);
                }
            }
            NotificationStateStore.Save(states.Values);
            await UpdateTileAsync();
            if (showToasts) foreach (var chat in toastCandidates.Take(5)) ShowPreviewToast(chat);
        }

        public static Task UpdateTileAsync()
        {
            var unread = NotificationStateStore.UnreadConversationCount();
            var tile = TileUpdateManager.CreateTileUpdaterForApplication();
            var badge = BadgeUpdateManager.CreateBadgeUpdaterForApplication();
            if (unread <= 0)
            {
                tile.Clear();
                badge.Clear();
                return Task.CompletedTask;
            }
            var display = unread > 99 ? "99+" : unread.ToString();
            var tileXml = new XmlDocument();
            tileXml.LoadXml("<tile><visual><binding template=\"TileSquare150x150Text04\"><text id=\"1\">" + display + "</text></binding><binding template=\"TileWide310x150Text09\"><text id=\"1\">" + display + " unread chats</text></binding></visual></tile>");
            tile.Update(new TileNotification(tileXml));
            var badgeXml = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeNumber);
            badgeXml.SelectSingleNode("/badge").Attributes.GetNamedItem("value").NodeValue = unread > 99 ? "99" : unread.ToString();
            badge.Update(new BadgeNotification(badgeXml));
            return Task.CompletedTask;
        }

        private static NotificationChatState NewState(ChatItem chat, bool unread)
        {
            return new NotificationChatState { ChatGuid = chat.Guid, MessageGuid = chat.LastMessageGuid, Timestamp = chat.LastMessageTimestamp, IsUnread = unread };
        }

        private static void ShowPreviewToast(ChatItem chat)
        {
            var template = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            template.DocumentElement.SetAttribute("launch", "chat=" + Uri.EscapeDataString(chat.Guid ?? string.Empty));
            var text = template.GetElementsByTagName("text");
            text.Item(0).AppendChild(template.CreateTextNode(string.IsNullOrWhiteSpace(chat.Title) ? "BlueBubbles" : chat.Title));
            text.Item(1).AppendChild(template.CreateTextNode(TrimPreview(chat.Preview)));
            ToastNotificationManager.CreateToastNotifier().Show(new ToastNotification(template) { ExpirationTime = DateTimeOffset.Now.AddHours(12) });
        }

        private static string TrimPreview(string preview)
        {
            if (string.IsNullOrWhiteSpace(preview)) return "New message";
            preview = preview.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return preview.Length <= 120 ? preview : preview.Substring(0, 117) + "...";
        }

        private static void Unregister()
        {
            foreach (var task in BackgroundTaskRegistration.AllTasks.Where(pair => pair.Value.Name == TaskName).Select(pair => pair.Value).ToList()) task.Unregister(true);
        }

        private static void SetStatus(string status)
        {
            Windows.Storage.ApplicationData.Current.LocalSettings.Values[StatusKey] = status;
        }
    }
}
