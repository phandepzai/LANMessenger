using System;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Messenger
{
    // Cấu trúc để lưu trữ vùng URL và URL thực tế
    public struct UrlRegion
    {
        public RectangleF Bounds { get; set; }
        public string Url { get; set; }

        public UrlRegion(RectangleF bounds, string url)
        {
            Bounds = bounds;
            Url = url;
        }
    }

    // Lớp đại diện cho một tin nhắn trong cuộc trò chuyện
    public class ChatMessage
    {
        public string SenderName { get; set; } // Thêm setter công khai
        public string Content { get; private set; } // Giữ setter riêng tư
        public DateTime Timestamp { get; set; }
        public bool IsMyMessage { get; set; } // Thêm setter công khai
        public bool IsTyping { get; set; }
        public MessageType Type { get; private set; }
        public SizeF CalculatedContentSize { get; set; }
        public SizeF CalculatedTimestampSize { get; set; }
        public SizeF CalculatedTotalSize { get; set; }
        public List<UrlRegion> UrlRegions { get; set; }
        public Image Avatar { get; set; }
        public Guid MessageId { get; private set; }
        public RectangleF BubbleBounds { get; set; }
        public RectangleF AvatarBounds { get; set; }
        public SizeF CalculatedSenderNameSize { get; set; }
        public int LastCalculatedWidth { get; set; } = -1;

        public ChatMessage(string senderName, string content, bool isMyMessage, MessageType type = MessageType.Text)
        {
            SenderName = senderName;
            Content = content;
            Timestamp = DateTime.Now;
            IsMyMessage = isMyMessage;
            IsTyping = false;
            Type = type;
            UrlRegions = new List<UrlRegion>();
            MessageId = Guid.NewGuid();
        }

        public ChatMessage(string senderName, string content, bool isMyMessage, Guid messageId, MessageType type = MessageType.Text)
        {
            SenderName = senderName;
            Content = content;
            Timestamp = DateTime.Now;
            IsMyMessage = isMyMessage;
            IsTyping = false;
            Type = type;
            UrlRegions = new List<UrlRegion>();
            MessageId = messageId;
        }

        // Các loại tin nhắn khác nhau
        public enum MessageType
        {
            Text, // Tin nhắn văn bản thông thường
            File, // Tin nhắn file (chưa triển khai)
            Join, // Thông báo tham gia
            Leave, // Thông báo rời đi
            Normal, // Tin nhắn bình thường
            Error, // Tin nhắn lỗi
            System // Thêm giá trị System cho tin nhắn hệ thống
        }
    }
}