using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Data.Json;
using Windows.Storage;

namespace WpBlueBubbles.Services
{
    public sealed class NotificationChatState
    {
        public string ChatGuid { get; set; }
        public string MessageGuid { get; set; }
        public long Timestamp { get; set; }
        public bool IsUnread { get; set; }
    }

    public static class NotificationStateStore
    {
        private const string StateKey = "NotificationChatState";
        private const string StateFileName = "notification-state.json";
        private const string TemporaryStateFileName = "notification-state.tmp";
        private const string InitializedKey = "LocalReadStateInitialized";
        private static readonly object StateLock = new object();

        public static IReadOnlyDictionary<string, NotificationChatState> Load()
        {
            lock (StateLock)
            {
                var result = new Dictionary<string, NotificationChatState>();
                try
                {
                    var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, StateFileName);
                    var text = File.Exists(path) ? File.ReadAllText(path) : ReadLegacyState();
                    JsonArray items;
                    if (string.IsNullOrWhiteSpace(text) || !JsonArray.TryParse(text, out items)) return result;
                    foreach (var item in items)
                    {
                        if (item.ValueType != JsonValueType.Object) continue;
                        var data = item.GetObject();
                        var guid = WpBlueBubbles.Models.JsonValueReader.String(data, "chatGuid");
                        if (string.IsNullOrWhiteSpace(guid)) continue;
                        result[guid] = new NotificationChatState
                        {
                            ChatGuid = guid,
                            MessageGuid = WpBlueBubbles.Models.JsonValueReader.String(data, "messageGuid"),
                            Timestamp = WpBlueBubbles.Models.JsonValueReader.Long(data, "timestamp"),
                            IsUnread = WpBlueBubbles.Models.JsonValueReader.Boolean(data, "isUnread")
                        };
                    }
                }
                catch
                {
                    // Read state must never prevent the app from starting or refreshing chats.
                }
                return result;
            }
        }

        public static void Save(IEnumerable<NotificationChatState> states)
        {
            var items = new JsonArray();
            foreach (var state in states)
            {
                items.Add(new JsonObject
                {
                    ["chatGuid"] = JsonValue.CreateStringValue(state.ChatGuid ?? string.Empty),
                    ["messageGuid"] = JsonValue.CreateStringValue(state.MessageGuid ?? string.Empty),
                    ["timestamp"] = JsonValue.CreateNumberValue(state.Timestamp),
                    ["isUnread"] = JsonValue.CreateBooleanValue(state.IsUnread)
                });
            }
            lock (StateLock)
            {
                try
                {
                    var folder = ApplicationData.Current.LocalFolder.Path;
                    var path = Path.Combine(folder, StateFileName);
                    var temporaryPath = Path.Combine(folder, TemporaryStateFileName);
                    File.WriteAllText(temporaryPath, items.Stringify());
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(temporaryPath, path);
                    ApplicationData.Current.LocalSettings.Values.Remove(StateKey);
                }
                catch
                {
                    // Unread decoration is non-critical; storage failure must not crash messaging.
                }
            }
        }

        public static void MarkRead(string chatGuid)
        {
            var states = Load().ToDictionary(pair => pair.Key, pair => pair.Value);
            NotificationChatState state;
            if (!states.TryGetValue(chatGuid, out state)) return;
            state.IsUnread = false;
            Save(states.Values);
        }

        public static void ReconcileReadState(IEnumerable<WpBlueBubbles.Models.ChatItem> chats)
        {
            var previousStates = Load();
            var values = ApplicationData.Current.LocalSettings.Values;
            var initialized = values.ContainsKey(InitializedKey) && values[InitializedKey] is bool && (bool)values[InitializedKey];
            var states = new Dictionary<string, NotificationChatState>();
            foreach (var chat in chats)
            {
                NotificationChatState prior;
                if (!initialized)
                {
                    chat.IsUnread = false;
                }
                else if (previousStates.TryGetValue(chat.Guid, out prior))
                {
                    chat.IsUnread = string.Equals(prior.MessageGuid, chat.LastMessageGuid)
                        ? prior.IsUnread
                        : !chat.LastMessageIsFromMe;
                }
                else
                {
                    chat.IsUnread = !chat.LastMessageIsFromMe;
                }
                states[chat.Guid] = new NotificationChatState
                {
                    ChatGuid = chat.Guid,
                    MessageGuid = chat.LastMessageGuid,
                    Timestamp = chat.LastMessageTimestamp,
                    IsUnread = chat.IsUnread
                };
            }
            Save(states.Values);
            values[InitializedKey] = true;
        }

        public static int UnreadConversationCount()
        {
            return Load().Values.Count(state => state.IsUnread);
        }

        private static string ReadLegacyState()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            var text = values.ContainsKey(StateKey) ? values[StateKey] as string : null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                SaveLegacyText(text);
                values.Remove(StateKey);
            }
            return text;
        }

        private static void SaveLegacyText(string text)
        {
            var folder = ApplicationData.Current.LocalFolder.Path;
            var path = Path.Combine(folder, StateFileName);
            File.WriteAllText(path, text);
        }
    }
}
