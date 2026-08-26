using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace WpBlueBubbles.Models
{
    public sealed class MessageItem
    {
        public string Guid { get; set; }
        public string Text { get; set; }
        public string TimeLabel { get; set; }
        public bool IsFromMe { get; set; }
        public long Timestamp { get; set; }
        public HorizontalAlignment BubbleAlignment { get { return IsFromMe ? HorizontalAlignment.Right : HorizontalAlignment.Left; } }
        public Brush BubbleBrush { get { return new SolidColorBrush(IsFromMe ? Windows.UI.Color.FromArgb(255, 14, 99, 156) : Windows.UI.Color.FromArgb(255, 39, 52, 60)); } }

        public static MessageItem FromJson(JsonObject json)
        {
            var timestamp = JsonValueReader.Long(json, "dateCreated");
            var text = JsonValueReader.String(json, "text");
            if (string.IsNullOrWhiteSpace(text)) text = "Attachment";
            return new MessageItem
            {
                Guid = JsonValueReader.String(json, "guid"),
                Text = text,
                Timestamp = timestamp,
                TimeLabel = DateLabels.FromMilliseconds(timestamp).ToString("g"),
                IsFromMe = JsonValueReader.Boolean(json, "isFromMe")
            };
        }
    }
}
