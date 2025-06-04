using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Drawing.Text;
using System.Collections.Generic;
using System.Diagnostics;

namespace Messenger
{
    // Lớp tĩnh chịu trách nhiệm vẽ các tin nhắn lên giao diện người dùng.
    public static class MessageRenderer
    {
        // Biểu thức chính quy để tìm kiếm URL trong nội dung tin nhắn.
        // Nhận diện URL (http, https), địa chỉ IP, và cả đường dẫn file Windows (ổ đĩa hoặc UNC)
        private static readonly Regex UrlRegex = new Regex(
        // Phần ban đầu cho http/https URL (vẫn giữ lại)
        @"((http|https)://((([a-zA-Z0-9\-]+\.)+[a-zA-Z]{2,})|(\d{1,3}(\.\d{1,3}){3}))(:\d+)?(/[^\s]*)?)" +
        // Các đường dẫn file Windows và UNC (vẫn giữ lại)
        @"|([a-zA-Z]:\\[^\s]+)" +
        @"|(\\\\[^\s]+)" +
        // Mẫu mới để khớp với địa chỉ IP có hoặc không có cổng và đường dẫn
        // Lưu ý: \b được sử dụng ở đầu để đảm bảo khớp một IP hoàn chỉnh
        @"|(\b\d{1,3}(\.\d{1,3}){3}(:\d+)?(/[^\s]*)?\b)", // Đã thay đổi và thêm phần này
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // Màu nền cho tin nhắn của người dùng hiện tại.
        private static readonly SolidBrush MyMessageBackgroundBrush = new SolidBrush(Color.FromArgb(152, 251, 152)); // Màu xanh lá cây nhạt
        // Màu nền cho tin nhắn của người dùng khác.
        private static readonly SolidBrush OtherMessageBackgroundBrush = new SolidBrush(Color.PaleGoldenrod); // Màu vàng nhạt
        // Màu chữ cho tin nhắn của người dùng hiện tại.
        private static readonly SolidBrush MyMessageTextBrush = new SolidBrush(Color.Black);
        // Màu chữ cho tin nhắn của người dùng khác.
        private static readonly SolidBrush OtherMessageTextBrush = new SolidBrush(Color.Black);
        // Màu chữ cho thời gian gửi tin nhắn.
        private static readonly SolidBrush TimestampBrush = new SolidBrush(Color.Gray);
        // Màu chữ cho các URL được phát hiện trong tin nhắn.
        private static readonly SolidBrush UrlBrush = new SolidBrush(Color.Blue);
        // Font chữ sử dụng để vẽ thời gian gửi tin nhắn.
        private static Font _timestampFont;

        // Nhận vào một font chữ cơ bản để tạo font chữ cho thời gian.
        public static void Initialize(Font baseFont)
        {
            // Kiểm tra nếu font chữ cơ bản được cung cấp và font chữ thời gian chưa được khởi tạo.
            if (baseFont != null && _timestampFont == null)
            {
                // Tạo một font chữ mới cho thời gian dựa trên font chữ cơ bản nhưng có kích thước nhỏ hơn (70%).
                _timestampFont = new Font(baseFont.FontFamily, baseFont.Size * 0.7f);
            }
        }

        // Phương thức lấy nội dung tin nhắn đã được định dạng (hiện tại chỉ trả về nội dung gốc).
        private static string GetFormattedMessageText(ChatMessage message)
        {
            return message.Content; // Chỉ trả về nội dung tin nhắn
        }

        // Nhận vào đối tượng ChatMessage, đối tượng Graphics để vẽ, chiều rộng của ListBox chứa tin nhắn và font chữ mặc định.
        public static void PrepareMessageForDrawing(ChatMessage message, Graphics g, int listBoxWidth, Font defaultFont)
        {
            // Đảm bảo font chữ thời gian đã được khởi tạo nếu chưa.
            if (_timestampFont == null)
            {
                Initialize(new Font("Arial", 10));
            }

            // Xóa các vùng URL đã được tính toán trước đó cho tin nhắn này.
            message.UrlRegions.Clear();

            // Lấy nội dung tin nhắn đã được định dạng để hiển thị.
            string displayText = GetFormattedMessageText(message);
            // Xác định tên người gửi (rỗng nếu là tin nhắn của người dùng hiện tại).
            string senderName = message.IsMyMessage ? "" : message.SenderName;

            // Tính toán các kích thước và khoảng cách cố định cho việc vẽ tin nhắn.
            int maxBubbleContentWidth = (int)(listBoxWidth * 0.70f);
            int horizontalBubblePadding = 15;
            int verticalBubblePadding = 10;
            int timestampBubbleGap = 8;
            int itemBottomMargin = 8;
            int avatarSize = 40;
            int avatarPadding = 5;

            // Tạo StringFormat để hỗ trợ xuống dòng và đo kích thước chính xác.
            StringFormat stringFormat = new StringFormat(StringFormat.GenericTypographic)
            {
                FormatFlags = StringFormatFlags.MeasureTrailingSpaces, // Bỏ LineLimit để đảm bảo đo toàn bộ văn bản
                Trimming = StringTrimming.Word
            };

            // Đo kích thước nội dung tin nhắn.
            float maxWidth = maxBubbleContentWidth - (2 * horizontalBubblePadding);
            float calculatedContentWidth = 0; // Biến để theo dõi chiều rộng thực tế của nội dung
            float maxContentHeight = 0; // Chiều cao tối đa của nội dung
            float currentY = 0; // Vị trí Y hiện tại khi xử lý nhiều dòng.
            float lineSpacing = defaultFont.GetHeight(g); // Sử dụng chiều cao dòng chuẩn của font thay vì g.MeasureString(" ", defaultFont).Height / 4

            // Tìm kiếm và xử lý các URL trong nội dung tin nhắn để tính toán kích thước và vùng vẽ.
            MatchCollection matches = UrlRegex.Matches(displayText);
            int lastIndex = 0; // Theo dõi vị trí cuối cùng đã xử lý trong chuỗi.

            // Duyệt qua tất cả các URL được tìm thấy.
            foreach (Match match in matches)
            {
                // Xử lý phần văn bản trước URL (nếu có).
                if (match.Index > lastIndex)
                {
                    string preUrlText = displayText.Substring(lastIndex, match.Index - lastIndex);
                    SizeF preUrlSize = TextRenderer.MeasureText(g, preUrlText, defaultFont, new Size((int)maxWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                    maxContentHeight = Math.Max(maxContentHeight, currentY + preUrlSize.Height);
                    calculatedContentWidth = Math.Max(calculatedContentWidth, preUrlSize.Width);
                    currentY += preUrlSize.Height + lineSpacing;
                }

                // Xử lý URL.
                string url = match.Value;
                SizeF urlSize = TextRenderer.MeasureText(g, url, defaultFont, new Size((int)maxWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                maxContentHeight = Math.Max(maxContentHeight, currentY + urlSize.Height);
                calculatedContentWidth = Math.Max(calculatedContentWidth, urlSize.Width);
                message.UrlRegions.Add(new UrlRegion(new RectangleF(0, currentY - urlSize.Height, urlSize.Width, urlSize.Height), url));
                currentY += urlSize.Height + lineSpacing;

                // Cập nhật vị trí cuối cùng đã xử lý.
                lastIndex = match.Index + match.Length;
            }

            // Xử lý phần văn bản còn lại sau URL cuối cùng (nếu có).
            if (lastIndex < displayText.Length)
            {
                string remainingText = displayText.Substring(lastIndex);
                SizeF remainingSize = TextRenderer.MeasureText(g, remainingText, defaultFont, new Size((int)maxWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                maxContentHeight = Math.Max(maxContentHeight, currentY + remainingSize.Height);
                calculatedContentWidth = Math.Max(calculatedContentWidth, remainingSize.Width);
                currentY += remainingSize.Height + lineSpacing;
            }

            // Lưu kích thước đã tính toán của nội dung tin nhắn.
            message.CalculatedContentSize = new SizeF(calculatedContentWidth, maxContentHeight);

            // Đo kích thước tên người gửi (nếu có).
            SizeF senderNameSizeF = new SizeF(0, 0);
            if (!string.IsNullOrEmpty(senderName))
            {
                using (Font boldFont = new Font("Segoe UI", 9, FontStyle.Bold))
                {
                    senderNameSizeF = TextRenderer.MeasureText(g, senderName, boldFont, new Size(maxBubbleContentWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                    message.CalculatedSenderNameSize = new SizeF((float)Math.Ceiling(senderNameSizeF.Width), (float)Math.Ceiling(senderNameSizeF.Height));
                }
            }
            else
            {
                message.CalculatedSenderNameSize = new SizeF(0, 0);
            }

            // Đo kích thước thời gian gửi tin nhắn.
            SizeF timestampSizeF = TextRenderer.MeasureText(g, message.Timestamp.ToString("HH:mm"), _timestampFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine);
            message.CalculatedTimestampSize = new SizeF((float)Math.Ceiling(timestampSizeF.Width), (float)Math.Ceiling(timestampSizeF.Height));

            // Tính toán chiều rộng cuối cùng của "bong bóng" tin nhắn.
            float bubbleWidth = Math.Min(message.CalculatedContentSize.Width + (2 * horizontalBubblePadding), maxBubbleContentWidth);

            // Tính toán kích thước cuối cùng của toàn bộ mục tin nhắn.
            float totalWidth = bubbleWidth + (message.IsMyMessage ? 0 : avatarSize + avatarPadding);
            float totalHeight = Math.Max(message.CalculatedContentSize.Height + verticalBubblePadding * 2 + message.CalculatedSenderNameSize.Height, avatarSize) + itemBottomMargin;

            // Nếu tin nhắn không phải của người dùng hiện tại, cộng thêm khoảng cách cho avatar.
            if (!message.IsMyMessage)
            {
                // Đã được tính trong totalWidth
            }

            // Cộng thêm khoảng cách cho thời gian
            totalHeight += timestampSizeF.Height + timestampBubbleGap;

            message.CalculatedTotalSize = new SizeF((float)Math.Ceiling(totalWidth), (float)Math.Ceiling(totalHeight));
            message.LastCalculatedWidth = listBoxWidth;

            // Log chi tiết để debug
            Debug.WriteLine($"[PrepareMessageForDrawing] maxContentHeight={maxContentHeight}, calculatedContentWidth={calculatedContentWidth}, CalculatedTotalSize.Height={message.CalculatedTotalSize.Height}, ListBoxWidth={listBoxWidth}");
        }

        public static void DrawMessage(Graphics g, Rectangle bounds, ChatMessage message, DrawItemState state, Font defaultFont, Image avatar)
        {
            if (g == null) return;

            try
            {
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit; // Sử dụng ClearType để cải thiện hiển thị văn bản
                g.SmoothingMode = SmoothingMode.HighQuality;

                if (_timestampFont == null)
                {
                    Initialize(defaultFont);
                }

                SolidBrush backgroundBrush = message.IsMyMessage ? MyMessageBackgroundBrush : OtherMessageBackgroundBrush;
                SolidBrush textBrush = message.IsMyMessage ? MyMessageTextBrush : OtherMessageTextBrush;
                string displayText = GetFormattedMessageText(message);
                string senderName = message.IsMyMessage ? "" : message.SenderName;

                SizeF contentSize = message.CalculatedContentSize;
                SizeF timestampSize = message.CalculatedTimestampSize;
                SizeF senderNameSize = message.CalculatedSenderNameSize;

                int horizontalBubblePadding = 15;
                int verticalBubblePadding = 10;
                int borderRadius = 10;
                int bubbleMarginFromEdge = 10;
                int avatarSize = 40;
                int avatarPadding = 5;
                int senderNameGap = 5;

                int bubbleWidth = (int)contentSize.Width + (2 * horizontalBubblePadding);
                int bubbleHeight = (int)contentSize.Height + (2 * verticalBubblePadding);

                RectangleF bubbleRect;
                RectangleF avatarRect;
                RectangleF contentRect;
                RectangleF timestampRect;
                RectangleF senderNameRect = RectangleF.Empty;

                if (!message.BubbleBounds.IsEmpty && !message.AvatarBounds.IsEmpty)
                {
                    bubbleRect = message.BubbleBounds;
                    avatarRect = message.AvatarBounds;
                    contentRect = new RectangleF(bubbleRect.X + horizontalBubblePadding, bubbleRect.Y + verticalBubblePadding, contentSize.Width, contentSize.Height);
                    timestampRect = new RectangleF(
                        message.IsMyMessage ? bubbleRect.Right - timestampSize.Width : bubbleRect.X,
                        bubbleRect.Bottom + 2,
                        timestampSize.Width,
                        timestampSize.Height);
                    if (!string.IsNullOrEmpty(senderName))
                    {
                        senderNameRect = new RectangleF(
                            bubbleRect.X,
                            bubbleRect.Y - senderNameSize.Height - senderNameGap,
                            senderNameSize.Width,
                            senderNameSize.Height);
                    }
                }
                else
                {
                    if (message.IsMyMessage)
                    {
                        bubbleRect = new RectangleF(
                            bounds.Right - bubbleWidth - bubbleMarginFromEdge - avatarSize - avatarPadding,
                            bounds.Y,
                            bubbleWidth,
                            bubbleHeight);
                        avatarRect = new RectangleF(
                            bubbleRect.Right + avatarPadding,
                            bounds.Y,
                            avatarSize,
                            avatarSize);
                        contentRect = new RectangleF(
                            bubbleRect.X + horizontalBubblePadding,
                            bubbleRect.Y + verticalBubblePadding,
                            contentSize.Width,
                            contentSize.Height);
                        timestampRect = new RectangleF(
                            bubbleRect.Right - timestampSize.Width,
                            bubbleRect.Bottom + 2,
                            timestampSize.Width,
                            timestampSize.Height);
                    }
                    else
                    {
                        avatarRect = new RectangleF(
                            bounds.X + bubbleMarginFromEdge,
                            bounds.Y,
                            avatarSize,
                            avatarSize);
                        bubbleRect = new RectangleF(
                            avatarRect.Right + avatarPadding,
                            bounds.Y + (senderNameSize.Height > 0 ? senderNameSize.Height + senderNameGap : 0),
                            bubbleWidth,
                            bubbleHeight);
                        contentRect = new RectangleF(
                            bubbleRect.X + horizontalBubblePadding,
                            bubbleRect.Y + verticalBubblePadding,
                            contentSize.Width,
                            contentSize.Height);
                        timestampRect = new RectangleF(
                            bubbleRect.X,
                            bubbleRect.Bottom + 2,
                            timestampSize.Width,
                            timestampSize.Height);
                        if (!string.IsNullOrEmpty(senderName))
                        {
                            senderNameRect = new RectangleF(
                                bubbleRect.X,
                                bubbleRect.Y - senderNameSize.Height - senderNameGap,
                                senderNameSize.Width,
                                senderNameSize.Height);
                        }
                    }
                    message.BubbleBounds = bubbleRect;
                    message.AvatarBounds = avatarRect;
                }

                // Sử dụng TextRenderer để vẽ tên người gửi
                if (!string.IsNullOrEmpty(senderName))
                {
                    using (Font boldFont = new Font("Segoe UI", 9, FontStyle.Bold))
                    {
                        TextRenderer.DrawText(
                            g,
                            senderName,
                            boldFont,
                            Rectangle.Round(senderNameRect),
                            Color.DarkSlateBlue, // Đổi màu tại đây
                            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl
                        );
                    }
                }
                if (avatar != null)
                {
                    g.DrawImage(avatar, avatarRect);
                }

                using (GraphicsPath path = RoundedRectangle(Rectangle.Round(bubbleRect), borderRadius))
                {
                    // Chọn màu gradient cho từng loại tin nhắn
                    Color colorStart, colorEnd;
                    if (message.IsMyMessage)//Tin nhắn gửi đi
                    {
                        colorStart = Color.FromArgb(114, 247, 101); // Xanh lá nhạt
                        colorEnd = Color.FromArgb(220, 247, 168); // Xanh nhạt
                    }
                    else//Tin nhắn gửi đến
                    {
                        colorStart = Color.FromArgb(252, 228, 159); // Vàng nhạt
                        colorEnd = Color.FromArgb(252, 250, 159); // Trắng nhạt
                    }

                    using (LinearGradientBrush gradientBrush = new LinearGradientBrush(
                        bubbleRect,
                        colorStart,
                        colorEnd,
                        LinearGradientMode.Vertical)) // Gradient ngang Horizontal,Gradient dọc Vertical
                    {
                        g.FillPath(gradientBrush, path);
                    }
                    using (Pen gradientBorder = new Pen(colorEnd, 1.0f)) // viền cùng màu phần dưới của gradient
                    {
                        g.DrawPath(gradientBorder, path);
                    }
                }

                // Đảm bảo contentRect đủ lớn để chứa toàn bộ nội dung
                if (contentRect.Height < contentSize.Height)
                {
                    contentRect.Height = contentSize.Height;
                }
                DrawFormattedText(g, displayText, contentRect, textBrush, defaultFont, message.UrlRegions);

                if (_timestampFont != null)
                {
                    TextRenderer.DrawText(g, message.Timestamp.ToString("HH:mm"), _timestampFont, Rectangle.Round(timestampRect), ((SolidBrush)TimestampBrush).Color, TextFormatFlags.SingleLine);
                }
            }
            catch (ArgumentException ex)
            {
                Debug.WriteLine($"Lỗi thiết lập thuộc tính đồ họa: {ex.Message}");
                g.TextRenderingHint = TextRenderingHint.SystemDefault;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Lỗi vẽ tin nhắn: {ex.Message}");
            }
        }

        // Phương thức tạo một đường dẫn đồ họa (GraphicsPath) cho hình chữ nhật bo tròn.
        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }
            int diameter = radius * 2;
            Rectangle arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }

        // Phương thức vẽ văn bản đã được định dạng, bao gồm việc tô màu đặc biệt cho các URL.
        private static void DrawFormattedText(Graphics g, string text, RectangleF rect, Brush defaultBrush, Font defaultFont, List<UrlRegion> urlRegions)
        {
            StringFormat stringFormat = new StringFormat(StringFormat.GenericTypographic)
            {
                FormatFlags = StringFormatFlags.MeasureTrailingSpaces,
                Trimming = StringTrimming.Word
            };

            MatchCollection matches = UrlRegex.Matches(text);
            int lastIndex = 0;
            float currentX = rect.X;
            float currentY = rect.Y;
            float maxWidth = rect.Width;
            int urlRegionIndex = 0;

            foreach (Match match in matches)
            {
                // Vẽ phần văn bản trước URL.
                if (match.Index > lastIndex)
                {
                    string preUrlText = text.Substring(lastIndex, match.Index - lastIndex);
                    SizeF preUrlSize = TextRenderer.MeasureText(g, preUrlText, defaultFont, new Size((int)maxWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                    RectangleF preUrlRect = new RectangleF(currentX, currentY, maxWidth, preUrlSize.Height);
                    TextRenderer.DrawText(g, preUrlText, defaultFont, Rectangle.Round(preUrlRect), ((SolidBrush)defaultBrush).Color, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

                    currentX = rect.X; // Reset X cho dòng tiếp theo
                    currentY += preUrlSize.Height;
                }

                // Vẽ URL với màu đặc biệt.
                if (urlRegionIndex < urlRegions.Count)
                {
                    UrlRegion storedUrlRegion = urlRegions[urlRegionIndex];
                    string url = storedUrlRegion.Url;
                    SizeF urlSize = TextRenderer.MeasureText(g, url, defaultFont, new Size((int)maxWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

                    // Lưu tọa độ tương đối của URL so với vùng nội dung.
                    RectangleF urlBounds = new RectangleF(currentX - rect.X, currentY - rect.Y, urlSize.Width, urlSize.Height);
                    storedUrlRegion.Bounds = urlBounds;
                    urlRegions[urlRegionIndex] = storedUrlRegion;

                    RectangleF urlRect = new RectangleF(currentX, currentY, maxWidth, urlSize.Height);
                    TextRenderer.DrawText(g, url, defaultFont, Rectangle.Round(urlRect), ((SolidBrush)UrlBrush).Color, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

                    currentX = rect.X; // Reset X cho dòng tiếp theo
                    currentY += urlSize.Height;

                    urlRegionIndex++;
                }

                lastIndex = match.Index + match.Length;
            }

            // Vẽ phần văn bản còn lại sau URL cuối cùng.
            if (lastIndex < text.Length)
            {
                string remainingText = text.Substring(lastIndex);
                SizeF remainingSize = TextRenderer.MeasureText(g, remainingText, defaultFont, new Size((int)maxWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                RectangleF remainingRect = new RectangleF(currentX, currentY, maxWidth, remainingSize.Height);
                TextRenderer.DrawText(g, remainingText, defaultFont, Rectangle.Round(remainingRect), ((SolidBrush)defaultBrush).Color, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            }
        }
    }
}