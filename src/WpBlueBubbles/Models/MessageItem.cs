using Windows.Data.Json;
using System.ComponentModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace WpBlueBubbles.Models
{
    public sealed class MessageItem : INotifyPropertyChanged
    {
        public string Guid { get; set; }
        public string Text { get; set; }
        public string TimeLabel { get; set; }
        public bool IsFromMe { get; set; }
        public long Timestamp { get; set; }
        public bool HasAttachments { get; set; }
        public string AttachmentLabel { get; set; }
        public string AttachmentGuid { get; set; }
        public string AttachmentMimeType { get; set; }
        public string AttachmentUri { get; set; }
        public bool IsImageAttachment { get; set; }
        public bool IsVideoAttachment { get; set; }
        public bool UsesSmsColor { get; set; }
        public bool HasText { get { return !string.IsNullOrWhiteSpace(Text); } }
        public bool HasNonPreviewAttachment { get { return HasAttachments && !IsImageAttachment && !IsVideoAttachment; } }
        public event PropertyChangedEventHandler PropertyChanged;
        public HorizontalAlignment BubbleAlignment { get { return IsFromMe ? HorizontalAlignment.Right : HorizontalAlignment.Left; } }
        public Brush BubbleBrush { get { return new SolidColorBrush(IsFromMe ? (UsesSmsColor ? Windows.UI.Color.FromArgb(255, 46, 125, 50) : Windows.UI.Color.FromArgb(255, 14, 99, 156)) : Windows.UI.Color.FromArgb(255, 39, 52, 60)); } }

        public static MessageItem FromJson(JsonObject json)
        {
            var timestamp = JsonValueReader.Long(json, "dateCreated");
            var text = JsonValueReader.String(json, "text");
            JsonArray attachments;
            var hasAttachments = JsonValueReader.TryArray(json, "attachments", out attachments) && attachments.Count > 0;
            var attachmentGuid = string.Empty;
            var mimeType = string.Empty;
            var uti = string.Empty;
            var attachmentName = string.Empty;
            if (hasAttachments && attachments[0].ValueType == JsonValueType.Object)
            {
                var attachment = attachments[0].GetObject();
                attachmentGuid = JsonValueReader.String(attachment, "guid");
                mimeType = JsonValueReader.String(attachment, "mimeType");
                uti = JsonValueReader.String(attachment, "uti");
                attachmentName = JsonValueReader.String(attachment, "transferName");
            }
            var isImage = mimeType.StartsWith("image/", System.StringComparison.OrdinalIgnoreCase) || uti.IndexOf("image", System.StringComparison.OrdinalIgnoreCase) >= 0 || uti.IndexOf("jpeg", System.StringComparison.OrdinalIgnoreCase) >= 0 || uti.IndexOf("png", System.StringComparison.OrdinalIgnoreCase) >= 0 || uti.IndexOf("heic", System.StringComparison.OrdinalIgnoreCase) >= 0;
            var isVideo = mimeType.StartsWith("video/", System.StringComparison.OrdinalIgnoreCase) || uti.IndexOf("video", System.StringComparison.OrdinalIgnoreCase) >= 0 || uti.IndexOf("movie", System.StringComparison.OrdinalIgnoreCase) >= 0 || uti.IndexOf("quicktime", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (string.IsNullOrWhiteSpace(text) && hasAttachments && !isImage && !isVideo) text = "Attachment";
            return new MessageItem
            {
                Guid = JsonValueReader.String(json, "guid"),
                Text = text,
                Timestamp = timestamp,
                HasAttachments = hasAttachments,
                AttachmentLabel = hasAttachments ? (string.IsNullOrWhiteSpace(attachmentName) ? "Attachment available" : attachmentName) : string.Empty,
                AttachmentGuid = attachmentGuid,
                AttachmentMimeType = mimeType,
                IsImageAttachment = isImage,
                IsVideoAttachment = isVideo,
                TimeLabel = DateLabels.FromMilliseconds(timestamp).ToString("g"),
                IsFromMe = JsonValueReader.Boolean(json, "isFromMe")
            };
        }

        public void SetAttachmentUri(string uri)
        {
            AttachmentUri = uri;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AttachmentUri)));
        }

        public void MarkAttachmentFailed()
        {
            AttachmentUri = string.Empty;
            IsImageAttachment = false;
            IsVideoAttachment = false;
            AttachmentLabel = "Image unavailable";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AttachmentUri)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsImageAttachment)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVideoAttachment)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNonPreviewAttachment)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AttachmentLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasText)));
        }
    }
}
