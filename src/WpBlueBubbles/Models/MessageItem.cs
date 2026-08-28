using Windows.Data.Json;
using System.ComponentModel;
using System.Collections.Generic;
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
        public List<MessageAttachmentItem> Attachments { get; set; } = new List<MessageAttachmentItem>();
        public bool IsImageAttachment { get; set; }
        public bool IsVideoAttachment { get; set; }
        public bool UsesSmsColor { get; set; }
        public string SenderAddress { get; set; }
        public string SenderServerName { get; set; }
        public string SenderName { get; set; }
        public bool ShowSenderName { get; set; }
        public bool HasText { get { return !string.IsNullOrWhiteSpace(Text); } }
        public bool HasNonPreviewAttachment { get { return HasAttachments && !IsImageAttachment && !IsVideoAttachment; } }
        public event PropertyChangedEventHandler PropertyChanged;
        public HorizontalAlignment BubbleAlignment { get { return IsFromMe ? HorizontalAlignment.Right : HorizontalAlignment.Left; } }
        public Brush BubbleBrush { get { return Application.Current.Resources[IsFromMe ? "MessengerBlueBrush" : "IncomingMessageBrush"] as Brush; } }
        public Brush BubbleTextBrush { get { return Application.Current.Resources[IsFromMe ? "OutgoingMessageTextBrush" : "IncomingMessageTextBrush"] as Brush; } }

        public static MessageItem FromJson(JsonObject json)
        {
            var timestamp = JsonValueReader.Long(json, "dateCreated");
            var text = JsonValueReader.String(json, "text");
            JsonObject handle;
            var hasHandle = JsonValueReader.TryObject(json, "handle", out handle);
            var senderAddress = hasHandle ? JsonValueReader.String(handle, "address") : JsonValueReader.String(json, "otherHandle");
            var senderServerName = hasHandle ? JsonValueReader.String(handle, "displayName") : string.Empty;
            JsonArray attachments;
            var hasAttachments = JsonValueReader.TryArray(json, "attachments", out attachments) && attachments.Count > 0;
            var attachmentGuid = string.Empty;
            var mimeType = string.Empty;
            var uti = string.Empty;
            var attachmentName = string.Empty;
            var attachmentItems = new List<MessageAttachmentItem>();
            if (hasAttachments)
            {
                foreach (var value in attachments)
                {
                    if (value.ValueType != JsonValueType.Object) continue;
                    var attachment = value.GetObject();
                    var item = new MessageAttachmentItem
                    {
                        Guid = JsonValueReader.String(attachment, "guid"),
                        MimeType = JsonValueReader.String(attachment, "mimeType"),
                        Uti = JsonValueReader.String(attachment, "uti"),
                        Name = JsonValueReader.String(attachment, "transferName")
                    };
                    item.IsImage = item.MimeType.StartsWith("image/", System.StringComparison.OrdinalIgnoreCase) || item.Uti.IndexOf("image", System.StringComparison.OrdinalIgnoreCase) >= 0 || item.Uti.IndexOf("jpeg", System.StringComparison.OrdinalIgnoreCase) >= 0 || item.Uti.IndexOf("png", System.StringComparison.OrdinalIgnoreCase) >= 0 || item.Uti.IndexOf("heic", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    item.IsVideo = item.MimeType.StartsWith("video/", System.StringComparison.OrdinalIgnoreCase) || item.Uti.IndexOf("video", System.StringComparison.OrdinalIgnoreCase) >= 0 || item.Uti.IndexOf("movie", System.StringComparison.OrdinalIgnoreCase) >= 0 || item.Uti.IndexOf("quicktime", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    attachmentItems.Add(item);
                }
                var first = attachmentItems.Count > 0 ? attachmentItems[0] : null;
                if (first != null)
                {
                    attachmentGuid = first.Guid;
                    mimeType = first.MimeType;
                    uti = first.Uti;
                    attachmentName = first.Name;
                }
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
                Attachments = attachmentItems,
                IsImageAttachment = isImage,
                IsVideoAttachment = isVideo,
                TimeLabel = DateLabels.FromMilliseconds(timestamp).ToString("g"),
                IsFromMe = JsonValueReader.Boolean(json, "isFromMe"),
                SenderAddress = senderAddress,
                SenderServerName = senderServerName
            };
        }

        public void ResolveSender(bool isGroupChat, System.Collections.Generic.IReadOnlyDictionary<string, string> contactNames)
        {
            ShowSenderName = isGroupChat && !IsFromMe;
            if (!ShowSenderName) { SenderName = string.Empty; return; }
            var contactName = WpBlueBubbles.Services.ContactsService.Lookup(contactNames, SenderAddress);
            SenderName = !string.IsNullOrWhiteSpace(contactName) ? contactName
                : !string.IsNullOrWhiteSpace(SenderServerName) ? SenderServerName
                : !string.IsNullOrWhiteSpace(SenderAddress) ? SenderAddress : "Unknown sender";
        }

        public void SetAttachmentUri(string uri)
        {
            AttachmentUri = uri;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AttachmentUri)));
        }

        public void RefreshBubbleBrush()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BubbleBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BubbleTextBrush)));
        }

        public void MarkAttachmentFailed()
        {
            var wasVideo = IsVideoAttachment;
            AttachmentUri = string.Empty;
            IsImageAttachment = false;
            IsVideoAttachment = false;
            AttachmentLabel = wasVideo ? "Video unavailable" : "Image unavailable";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AttachmentUri)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsImageAttachment)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVideoAttachment)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNonPreviewAttachment)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AttachmentLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasText)));
        }
    }

    public sealed class MessageAttachmentItem
    {
        public string Guid { get; set; }
        public string Name { get; set; }
        public string MimeType { get; set; }
        public string Uti { get; set; }
        public bool IsImage { get; set; }
        public bool IsVideo { get; set; }
    }
}
