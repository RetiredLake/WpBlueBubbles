using System;
using System.Collections.Generic;
using Windows.Data.Json;

namespace WpBlueBubbles.Models
{
    public sealed class ChatItem
    {
        public string Guid { get; set; }
        public string Title { get; set; }
        public string Preview { get; set; }
        public string TimeLabel { get; set; }
        public string Initials { get; set; }
        public long LastMessageTimestamp { get; set; }

        public static ChatItem FromJson(JsonObject json)
        {
            var guid = JsonValueReader.String(json, "guid");
            var displayName = JsonValueReader.String(json, "displayName");
            var identifier = JsonValueReader.String(json, "chatIdentifier");
            var participantNames = new List<string>();

            JsonArray participants;
            if (JsonValueReader.TryArray(json, "participants", out participants))
            {
                foreach (var value in participants)
                {
                    if (value.ValueType != JsonValueType.Object) continue;
                    var handle = value.GetObject();
                    var name = JsonValueReader.String(handle, "displayName");
                    if (string.IsNullOrWhiteSpace(name)) name = JsonValueReader.String(handle, "address");
                    if (!string.IsNullOrWhiteSpace(name)) participantNames.Add(name);
                }
            }

            var title = !string.IsNullOrWhiteSpace(displayName)
                ? displayName
                : participantNames.Count > 0 ? string.Join(", ", participantNames) : identifier;
            if (string.IsNullOrWhiteSpace(title)) title = "Conversation";

            JsonObject lastMessage;
            var preview = "No messages yet";
            long timestamp = 0;
            if (JsonValueReader.TryObject(json, "lastMessage", out lastMessage))
            {
                preview = JsonValueReader.String(lastMessage, "text");
                if (string.IsNullOrWhiteSpace(preview)) preview = "Attachment";
                timestamp = JsonValueReader.Long(lastMessage, "dateCreated");
            }

            return new ChatItem
            {
                Guid = guid,
                Title = title,
                Preview = preview,
                LastMessageTimestamp = timestamp,
                TimeLabel = DateLabels.Compact(timestamp),
                Initials = MakeInitials(title)
            };
        }

        private static string MakeInitials(string title)
        {
            var pieces = title.Split(new[] { ' ', ',', '+', '@' }, StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length == 0) return "?";
            var result = pieces[0].Substring(0, 1).ToUpperInvariant();
            if (pieces.Length > 1) result += pieces[1].Substring(0, 1).ToUpperInvariant();
            return result;
        }
    }

    internal static class DateLabels
    {
        private static readonly DateTimeOffset Epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public static DateTimeOffset FromMilliseconds(long value)
        {
            return value <= 0 ? DateTimeOffset.MinValue : Epoch.AddMilliseconds(value).ToLocalTime();
        }

        public static string Compact(long value)
        {
            var date = FromMilliseconds(value);
            if (date == DateTimeOffset.MinValue) return string.Empty;
            var now = DateTimeOffset.Now;
            if (date.Date == now.Date) return date.ToString("t");
            if (date.Date >= now.Date.AddDays(-6)) return date.ToString("ddd");
            return date.ToString("MMM d");
        }
    }

    internal static class JsonValueReader
    {
        public static string String(JsonObject json, string key)
        {
            IJsonValue value;
            if (!json.TryGetValue(key, out value) || value.ValueType == JsonValueType.Null) return string.Empty;
            return value.ValueType == JsonValueType.String ? value.GetString() : value.Stringify().Trim('"');
        }

        public static bool Boolean(JsonObject json, string key)
        {
            IJsonValue value;
            return json.TryGetValue(key, out value) && value.ValueType == JsonValueType.Boolean && value.GetBoolean();
        }

        public static long Long(JsonObject json, string key)
        {
            IJsonValue value;
            if (!json.TryGetValue(key, out value) || value.ValueType != JsonValueType.Number) return 0;
            return (long)value.GetNumber();
        }

        public static bool TryObject(JsonObject json, string key, out JsonObject result)
        {
            IJsonValue value;
            if (json.TryGetValue(key, out value) && value.ValueType == JsonValueType.Object)
            {
                result = value.GetObject();
                return true;
            }
            result = null;
            return false;
        }

        public static bool TryArray(JsonObject json, string key, out JsonArray result)
        {
            IJsonValue value;
            if (json.TryGetValue(key, out value) && value.ValueType == JsonValueType.Array)
            {
                result = value.GetArray();
                return true;
            }
            result = null;
            return false;
        }
    }
}
