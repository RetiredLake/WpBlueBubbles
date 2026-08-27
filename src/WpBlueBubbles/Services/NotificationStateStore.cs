using System.Collections.Generic;
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

        public static IReadOnlyDictionary<string, NotificationChatState> Load()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            var text = values.ContainsKey(StateKey) ? values[StateKey] as string : null;
            var result = new Dictionary<string, NotificationChatState>();
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
            return result;
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
            ApplicationData.Current.LocalSettings.Values[StateKey] = items.Stringify();
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
            var states = Load().ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach (var chat in chats)
            {
                NotificationChatState prior;
                if (states.TryGetValue(chat.Guid, out prior) && string.Equals(prior.MessageGuid, chat.LastMessageGuid) && !prior.IsUnread)
                {
                    chat.IsUnread = false;
                }
                else
                {
                    states[chat.Guid] = new NotificationChatState
                    {
                        ChatGuid = chat.Guid,
                        MessageGuid = chat.LastMessageGuid,
                        Timestamp = chat.LastMessageTimestamp,
                        IsUnread = chat.IsUnread
                    };
                }
            }
            Save(states.Values);
        }

        public static int UnreadConversationCount()
        {
            return Load().Values.Count(state => state.IsUnread);
        }
    }
}
