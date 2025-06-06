using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Diagnostics;
using Messenger.Properties;
using System.Net.NetworkInformation;


namespace Messenger
{
    public partial class MainForm : Form
    {
        private NetworkService _networkService;
        private List<ChatMessage> _chatMessages = new List<ChatMessage>();
        private BindingList<string> _onlineUsers = new BindingList<string>();
        private const int TcpPort = 14000;
        //private const string MulticastAddress = "239.255.0.1";//Dải Administratively Scoped,dải này được dành riêng cho sử dụng trong mạng cục bộ
        //private const string MulticastAddress = "224.0.1.0";//multicast giữa các subnet (Ví dụ: 224.0.1.100, 232.10.5.20, 239.1.1.1)
        //private const string MulticastAddress = "239.0.0.0";//multicast trong mạng LAN cục bộ
        //private const string MulticastAddress = "224.0.0.2";//multicast (all routers)
        private const string MulticastAddress = "224.0.0.1";// Địa chỉ multicast "all hosts" (tất cả các máy chủ trong mạng cục bộ)
        private const int MulticastPort = 14001; // Cổng (port) được sử dụng cho giao tiếp multicast
        private Image _myAvatar; // Biến lưu trữ ảnh đại diện (avatar) của người dùng hiện tại
        private string _myUserName; // Biến lưu trữ tên người dùng của người dùng hiện tại
        private Random _random = new Random(); // Đối tượng Random để tạo số ngẫu nhiên (ví dụ: cho màu sắc, ID)
        private System.Windows.Forms.Timer _typingTimer = new System.Windows.Forms.Timer(); // Timer để theo dõi trạng thái "đang gõ" của người dùng
        private System.Windows.Forms.Timer _dateTimeTimer; // Timer để cập nhật thời gian và ngày tháng hiển thị trên giao diện
        private bool _isTyping = false; // Biến cờ (flag) cho biết người dùng hiện tại có đang gõ tin nhắn hay không
        private string _remoteTypingUser = ""; // Biến lưu trữ tên người dùng của người đang gõ tin nhắn từ xa
        private Dictionary<string, Image> _userAvatars = new Dictionary<string, Image>(); // Dictionary lưu trữ avatar của các người dùng khác, với key là tên người dùng
        private string _profileDirectory; // Đường dẫn đến thư mục chứa thông tin hồ sơ người dùng (ví dụ: avatar, tên)
        private string _userNameFilePath; // Đường dẫn đến tệp tin lưu trữ tên người dùng
        private string _chatHistoryDirectory; // Đường dẫn đến thư mục lưu trữ lịch sử trò chuyện
        private string _currentPeer; // Tên người dùng của người đang trò chuyện cùng hiện tại
        private NotifyIcon _trayIcon; // Đối tượng NotifyIcon để hiển thị biểu tượng ứng dụng dưới khay hệ thống
        private bool _isClosing = false; // Biến cờ cho biết form đang trong quá trình đóng
        private const int AvatarWidth = 40; // Chiều rộng cố định cho avatar hiển thị trong danh sách người dùng hoặc tin nhắn
        private const int AvatarHeight = 40; // Chiều cao cố định cho avatar hiển thị
        private const int AvatarPadding = 5; // Khoảng cách (padding) xung quanh avatar
        private System.Windows.Forms.Timer _rainbowTimer; // Timer để tạo hiệu ứng cầu vồng (thay đổi màu sắc) cho một số thành phần giao diện
        private float _rainbowPhase = 0f; // Pha hiện tại của hiệu ứng cầu vồng (quyết định màu sắc)
        private readonly float _rainbowSpeed = 0.05f; // Tốc độ thay đổi pha của hiệu ứng cầu vồng
        private bool _isSendingMessage = false; // Biến cờ cho biết ứng dụng đang trong quá trình gửi tin nhắn
        private readonly HashSet<Guid> _displayedMessageIds = new HashSet<Guid>(); // HashSet để theo dõi các ID tin nhắn đã hiển thị, tránh trùng lặp
        private const int TopListBoxMargin = 12;
        private int _unreadMessageCount = 0; // Thêm biến đếm tin nhắn chưa đọc
        private ContextMenuStrip messageTextBoxContextMenuStrip;
        private ToolStripMenuItem pasteMenuItem;
        private ToolStripMenuItem cutMenuItem;
        private ToolStripMenuItem copyMenuItem;
        private ToolStripMenuItem selectAllMenuItem;

        public MainForm()
        {
            InitializeComponent(); // Gọi phương thức được tạo bởi trình thiết kế để khởi tạo các thành phần giao diện
            this.DoubleBuffered = true; // Thiết lập thuộc tính DoubleBuffered để giảm hiện tượng nhấp nháy khi vẽ lại giao diện

            // Cấu hình ListBox hiển thị tin nhắn (chatListBox) để vẽ tùy chỉnh các mục
            chatListBox.DrawMode = DrawMode.OwnerDrawVariable; // Cho phép tự vẽ các mục trong ListBox với kích thước thay đổi
            chatListBox.DrawItem += ChatListBox_DrawItem; // Gán sự kiện vẽ mục cho phương thức ChatListBox_DrawItem
            chatListBox.MeasureItem += ChatListBox_MeasureItem; // Gán sự kiện đo kích thước mục cho phương thức ChatListBox_MeasureItem
            chatListBox.MouseDown += ChatListBox_MouseDown; // Gán sự kiện nhấn chuột cho phương thức ChatListBox_MouseDown
            chatListBox.MouseClick += ChatListBox_MouseClick; // Gán sự kiện click chuột cho phương thức ChatListBox_MouseClick
            chatListBox.MouseMove += ChatListBox_MouseMove; // Gán sự kiện di chuyển chuột cho phương thức ChatListBox_MouseMove

            // Xử lý placeholder (văn bản gợi ý) trong hộp thoại nhập tin nhắn (messageTextBox)
            messageTextBox.Enter += (s, e) => RemovePlaceholder(); // Khi con trỏ vào hộp thoại, loại bỏ placeholder
            messageTextBox.Leave += (s, e) => SetPlaceholder(); // Khi con trỏ rời khỏi hộp thoại, thiết lập lại placeholder nếu không có văn bản
            SetPlaceholder(); // Thiết lập placeholder ban đầu khi form được tải

            // Thiết lập menu ngữ cảnh (right-click menu) cho ListBox hiển thị tin nhắn
            chatListBox.ContextMenuStrip = messageContextMenuStrip; // Gán ContextMenuStrip cho chatListBox
            copyToolStripMenuItem.Click += copyToolStripMenuItem_Click; // Gán sự kiện click cho mục "Sao chép" trong menu ngữ cảnh

            // Khởi tạo bộ xử lý hiển thị tin nhắn (MessageRenderer) với font chữ của chatListBox
            MessageRenderer.Initialize(chatListBox.Font);

            // Liên kết danh sách người dùng trực tuyến (_onlineUsers) với ListBox hiển thị người dùng trực tuyến
            onlineUsersListBox.DataSource = _onlineUsers;
            onlineUsersListBox.SelectedIndexChanged += OnlineUsersListBox_SelectedIndexChanged; // Gán sự kiện thay đổi lựa chọn cho phương thức OnlineUsersListBox_SelectedIndexChanged

            // Xử lý sự kiện bàn phím và thay đổi văn bản trong hộp thoại nhập tin nhắn
            messageTextBox.KeyDown += messageTextBox_KeyDown; // Gán sự kiện nhấn phím cho phương thức messageTextBox_KeyDown
            messageTextBox.TextChanged += messageTextBox_TextChanged; // Gán sự kiện thay đổi văn bản cho phương thức messageTextBox_TextChanged

            // Cấu hình Timer để theo dõi trạng thái "đang gõ"
            _typingTimer.Interval = 2000; // Đặt khoảng thời gian kiểm tra là 2000 mili giây (2 giây)
            _typingTimer.Tick += _typingTimer_Tick; // Gán sự kiện Tick cho phương thức _typingTimer_Tick

            // Cấu hình Timer để cập nhật thời gian và ngày tháng
            _dateTimeTimer = new System.Windows.Forms.Timer();
            _dateTimeTimer.Interval = 500; // Đặt khoảng thời gian cập nhật là 500 mili giây (0.5 giây)
            _dateTimeTimer.Tick += DateTimeTimer_Tick; // Gán sự kiện Tick cho phương thức DateTimeTimer_Tick
            _dateTimeTimer.Start(); // Bắt đầu Timer cập nhật thời gian

            // Cấu hình Timer để tạo hiệu ứng cầu vồng cho nhãn tác giả
            _rainbowTimer = new System.Windows.Forms.Timer();
            _rainbowTimer.Interval = 50; // Đặt khoảng thời gian cập nhật màu là 50 mili giây
            _rainbowTimer.Tick += RainbowTimer_Tick; // Gán sự kiện Tick cho phương thức RainbowTimer_Tick
            authorLabel.MouseEnter += AuthorLabel_MouseEnter; // Gán sự kiện khi chuột vào nhãn tác giả
            authorLabel.MouseLeave += AuthorLabel_MouseLeave; // Gán sự kiện khi chuột rời khỏi nhãn tác giả

            // Gán các sự kiện cho form chính
            this.Load += MainForm_Load; // Gán sự kiện Load (khi form được tải) cho phương thức MainForm_Load
            this.FormClosing += MainForm_FormClosing; // Gán sự kiện FormClosing (khi form đang đóng) cho phương thức MainForm_FormClosing
            this.Resize += MainForm_Resize; // Gán sự kiện Resize (khi form thay đổi kích thước) cho phương thức MainForm_Resize

            // Thiết lập các đường dẫn thư mục và tệp tin liên quan đến hồ sơ và lịch sử chat
            _profileDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Messenger"); // Đường dẫn đến thư mục ApplicationData cho ứng dụng
            _userNameFilePath = Path.Combine(_profileDirectory, "user_profile.ini"); // Đường dẫn đến tệp tin lưu trữ tên người dùng
            _chatHistoryDirectory = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "LOGCHAT"); // Đường dẫn đến thư mục "LOG" cùng cấp với tệp thực thi để lưu lịch sử chat
            Directory.CreateDirectory(_profileDirectory); // Tạo thư mục hồ sơ nếu chưa tồn tại
            Directory.CreateDirectory(_chatHistoryDirectory); // Tạo thư mục lịch sử chat nếu chưa tồn tại
            InitializeSystemTray(); // Gọi phương thức để khởi tạo biểu tượng khay hệ thống
            InitializeMessageTextBoxContextMenu(); // << THÊM DÒNG NÀY
        }

        // Phương thức trả về tên thứ trong tuần bằng tiếng Việt
        private string GetVietnameseDayOfWeek(DayOfWeek dayOfWeek)
        {
            switch (dayOfWeek)
            {
                case DayOfWeek.Monday: return "(Thứ Hai)";
                case DayOfWeek.Tuesday: return "(Thứ Ba)";
                case DayOfWeek.Wednesday: return "(Thứ Tư)";
                case DayOfWeek.Thursday: return "(Thứ Năm)";
                case DayOfWeek.Friday: return "(Thứ Sáu)";
                case DayOfWeek.Saturday: return "(Thứ Bảy)";
                case DayOfWeek.Sunday: return "(Chủ Nhật)";
                default: return "(Không xác định)";
            }
        }

        // Phương thức được gọi khi Timer cập nhật thời gian (dateTimeTimer) kích hoạt
        private void DateTimeTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // Lấy thông tin múi giờ GMT+7 (SE Asia Standard Time)
                TimeZoneInfo gmtPlus7 = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                // Chuyển đổi thời gian UTC hiện tại sang múi giờ GMT+7
                DateTime gmtPlus7Time = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, gmtPlus7);
                // Cập nhật nhãn hiển thị thời gian
                timeLabel.Text = gmtPlus7Time.ToString("HH:mm:ss");
                // Cập nhật nhãn hiển thị ngày tháng
                dateLabel.Text = gmtPlus7Time.ToString("dd/MM/yyyy");
                // Cập nhật nhãn hiển thị thứ trong tuần bằng tiếng Việt
                dayLabel.Text = GetVietnameseDayOfWeek(gmtPlus7Time.DayOfWeek);
            }
            catch (TimeZoneNotFoundException)
            {
                Debug.WriteLine("Múi giờ SE Asia Standard Time không tìm thấy, sử dụng thời gian hệ thống.");
                timeLabel.Text = DateTime.Now.ToString("HH:mm:ss");
                dateLabel.Text = DateTime.Now.ToString("dd/MM/yyyy");
                dayLabel.Text = GetVietnameseDayOfWeek(DateTime.Now.DayOfWeek);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Lỗi khi lấy thời gian GMT+7: {ex.Message}");
                timeLabel.Text = "Lỗi thời gian";
                dateLabel.Text = "Lỗi ngày";
                dayLabel.Text = "Lỗi thứ";
            }
        }

        // Phương thức được gọi khi Timer hiệu ứng cầu vồng (rainbowTimer) kích hoạt
        private void RainbowTimer_Tick(object sender, EventArgs e)
        {
            // Tăng pha của hiệu ứng cầu vồng
            _rainbowPhase += _rainbowSpeed;
            // Nếu pha vượt quá 2*PI, đặt lại về 0
            if (_rainbowPhase >= 2 * Math.PI) _rainbowPhase -= (float)(2 * Math.PI);

            // Tính toán giá trị màu đỏ (R), xanh lá cây (G), xanh lam (B) dựa trên pha
            int r = (int)(Math.Sin(_rainbowPhase) * 127 + 128);
            int g = (int)(Math.Sin(_rainbowPhase + 2 * Math.PI / 3) * 127 + 128);
            int b = (int)(Math.Sin(_rainbowPhase + 4 * Math.PI / 3) * 127 + 128);

            // Đặt màu chữ của nhãn tác giả thành màu cầu vồng
            authorLabel.ForeColor = Color.FromArgb(r, g, b);
        }

        // Phương thức được gọi khi chuột di chuyển vào nhãn tác giả
        private void AuthorLabel_MouseEnter(object sender, EventArgs e)
        {
            _rainbowTimer.Start(); // Bắt đầu Timer hiệu ứng cầu vồng
        }

        // Phương thức được gọi khi chuột di chuyển ra khỏi nhãn tác giả
        private void AuthorLabel_MouseLeave(object sender, EventArgs e)
        {
            _rainbowTimer.Stop(); // Dừng Timer hiệu ứng cầu vồng
            authorLabel.ForeColor = Color.Silver; // Đặt lại màu chữ của nhãn tác giả về màu bạc
        }

        // Phương thức khởi tạo biểu tượng khay hệ thống (system tray)
        private void InitializeSystemTray()
        {
            _trayIcon = new NotifyIcon
            {
                Text = "Messenger", // Văn bản hiển thị khi di chuột qua biểu tượng
                Visible = true // Đặt biểu tượng hiển thị
            };

            // Gán biểu tượng cho NotifyIcon
            if (this.Icon != null)
            {
                _trayIcon.Icon = this.Icon; // Sử dụng biểu tượng của form chính
                Debug.WriteLine("Đã gán biểu tượng của MainForm cho khay hệ thống.");
            }
            else
            {
                _trayIcon.Icon = SystemIcons.Application; // Sử dụng biểu tượng ứng dụng mặc định nếu không tìm thấy
                Debug.WriteLine("Không tìm thấy biểu tượng của MainForm. Sử dụng biểu tượng mặc định SystemIcons.Application. Vui lòng kiểm tra thiết lập biểu tượng trong Properties của dự án hoặc MainForm.");
            }

            // Tạo menu ngữ cảnh cho biểu tượng khay hệ thống
            ContextMenuStrip trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("MỞ", null, (s, e) => RestoreFromTray()); // Thêm mục "Mở" để khôi phục ứng dụng
            trayMenu.Items.Add("THOÁT", null, (s, e) => ExitApplication()); // Thêm mục "Thoát" để đóng ứng dụng
            _trayIcon.ContextMenuStrip = trayMenu; // Gán menu ngữ cảnh cho NotifyIcon

            // Xử lý sự kiện nhấp đúp chuột vào biểu tượng khay hệ thống để khôi phục ứng dụng
            _trayIcon.DoubleClick += (s, e) => RestoreFromTray();
            UpdateTrayIcon();
        }
        // Phương thức được gọi khi form thay đổi kích thước
        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized && !_isClosing)
            {
                this.Hide();
                _trayIcon.Visible = true;
                UpdateTrayIcon(); // Cập nhật text của tray icon khi thu nhỏ
                if (_trayIcon.Icon != null)
                {
                    var balloon = new CustomBalloonForm(
                        "Messenger",
                        "Ứng dụng đã được thu nhỏ vào khay hệ thống. Nhấp đúp để khôi phục.",
                        this.Icon,
                        7000,
                        RestoreFromTray
                    );
                    balloon.Show();
                    balloon.ShowFromBottom(
                        "Messenger", // Tiêu đề thông báo
                        "Ứng dụng đã được thu nhỏ vào khay hệ thống. Nhấp đúp để khôi phục.", // Nội dung thông báo
                        this.Icon // Biểu tượng
                    );
                    Debug.WriteLine("Hiển thị thông báo tùy chỉnh khi thu nhỏ.");
                }
            }
            else
            {
                // Invalidate cached sizes for all messages when form resizes
                foreach (ChatMessage msg in _chatMessages)
                {
                    msg.CalculatedTotalSize = SizeF.Empty;
                    msg.LastCalculatedWidth = -1;
                }
                chatListBox.Refresh(); // Buộc vẽ lại tất cả các mục
            }
        }

        private void InitializeMessageTextBoxContextMenu()
        {
            messageTextBoxContextMenuStrip = new ContextMenuStrip();

            pasteMenuItem = new ToolStripMenuItem("Dán");
            pasteMenuItem.Click += PasteMenuItem_Click;
            messageTextBoxContextMenuStrip.Items.Add(pasteMenuItem);

            // Thêm dòng phân cách (tùy chọn)
            messageTextBoxContextMenuStrip.Items.Add(new ToolStripSeparator());

            cutMenuItem = new ToolStripMenuItem("Cắt");
            cutMenuItem.Click += CutMenuItem_Click;
            messageTextBoxContextMenuStrip.Items.Add(cutMenuItem);

            copyMenuItem = new ToolStripMenuItem("Sao chép");
            copyMenuItem.Click += CopyMenuItem_Click;
            messageTextBoxContextMenuStrip.Items.Add(copyMenuItem);

            // Thêm dòng phân cách (tùy chọn)
            messageTextBoxContextMenuStrip.Items.Add(new ToolStripSeparator());

            selectAllMenuItem = new ToolStripMenuItem("Chọn tất cả");
            selectAllMenuItem.Click += SelectAllMenuItem_Click;
            messageTextBoxContextMenuStrip.Items.Add(selectAllMenuItem);

            messageTextBox.ContextMenuStrip = messageTextBoxContextMenuStrip;

            // Xử lý sự kiện Opening để bật/tắt các mục menu
            messageTextBoxContextMenuStrip.Opening += MessageTextBoxContextMenuStrip_Opening;
        }

        private void PasteMenuItem_Click(object sender, EventArgs e)
        {
            // Kiểm tra xem có dữ liệu văn bản trong Clipboard không trước khi Dán
            if (Clipboard.ContainsText())
            {
                messageTextBox.Paste();
            }
        }

        private void CutMenuItem_Click(object sender, EventArgs e)
        {
            // Kiểm tra xem có văn bản nào được chọn không trước khi Cắt
            if (messageTextBox.SelectionLength > 0)
            {
                messageTextBox.Cut();
            }
        }

        private void CopyMenuItem_Click(object sender, EventArgs e)
        {
            // Kiểm tra xem có văn bản nào được chọn không trước khi Sao chép
            if (messageTextBox.SelectionLength > 0)
            {
                messageTextBox.Copy();
            }
        }

        private void SelectAllMenuItem_Click(object sender, EventArgs e)
        {
            // Kiểm tra xem có văn bản trong ô không trước khi Chọn tất cả
            if (messageTextBox.TextLength > 0)
            {
                messageTextBox.SelectAll();
            }
        }

        private void MessageTextBoxContextMenuStrip_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            RemovePlaceholder();
            pasteMenuItem.Enabled = Clipboard.ContainsText(TextDataFormat.UnicodeText) || Clipboard.ContainsText(TextDataFormat.Text);
            cutMenuItem.Enabled = messageTextBox.SelectionLength > 0;
            copyMenuItem.Enabled = messageTextBox.SelectionLength > 0;
            selectAllMenuItem.Enabled = messageTextBox.TextLength > 0;
        }

        // Phương thức khôi phục ứng dụng từ khay hệ thống
        private void RestoreFromTray()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            _trayIcon.Visible = false;
            _unreadMessageCount = 0;
            UpdateTrayIcon();

            // Kiểm tra tính toàn vẹn của _chatMessages
            EnsureMessageIntegrity();

            // Xóa và tái tạo danh sách tin nhắn trong chatListBox
            chatListBox.Items.Clear();
            foreach (var message in _chatMessages)
            {
                if (message.Type == ChatMessage.MessageType.System || message.Type == ChatMessage.MessageType.Text)
                {
                    chatListBox.Items.Add(message);
                }
            }

            if (chatListBox.Items.Count > 0)
            {
                chatListBox.TopIndex = chatListBox.Items.Count - 1;
            }
            chatListBox.Refresh();
            Debug.WriteLine($"[RestoreFromTray] Đã tải lại {chatListBox.Items.Count} tin nhắn Broadcast");
        }


        // Phương thức thoát ứng dụng
        private void ExitApplication()
        {
            _isClosing = true; // Đánh dấu là đang trong quá trình đóng
            _trayIcon.Visible = false; // Ẩn biểu tượng khay hệ thống
            _trayIcon.Dispose(); // Giải phóng tài nguyên của NotifyIcon
            Application.Exit(); // Đóng ứng dụng
        }

        // Phương thức tải tên người dùng đã lưu từ tệp tin
        private string LoadUserName()
        {
            // Kiểm tra xem tệp tin lưu tên người dùng có tồn tại hay không
            if (File.Exists(_userNameFilePath))
            {
                try
                {
                    // Đọc toàn bộ nội dung từ tệp tin
                    string savedName = File.ReadAllText(_userNameFilePath).Trim();
                    // Kiểm tra xem tên đã lưu có hợp lệ hay không (không rỗng, không chứa dấu ":")
                    if (!string.IsNullOrWhiteSpace(savedName) && !savedName.Contains(":"))
                    {
                        return savedName; // Trả về tên người dùng đã lưu
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Lỗi tải tên người dùng: {ex.Message}");
                }
            }
            return null; // Trả về null nếu không tải được tên người dùng
        }
        private void EnsureMessageIntegrity()
        {
            var invalidMessages = _chatMessages.Where(m => !_displayedMessageIds.Contains(m.MessageId)).ToList();
            foreach (var message in invalidMessages)
            {
                Debug.WriteLine($"[EnsureMessageIntegrity] Xóa tin nhắn không hợp lệ: {message.MessageId}");
                _chatMessages.Remove(message);
            }

            var invalidIds = _displayedMessageIds.Where(id => !_chatMessages.Any(m => m.MessageId == id)).ToList();
            foreach (var id in invalidIds)
            {
                Debug.WriteLine($"[EnsureMessageIntegrity] Xóa ID tin nhắn không hợp lệ: {id}");
                _displayedMessageIds.Remove(id);
            }
        }
        // Phương thức lưu tên người dùng vào tệp tin
        private void SaveUserName(string userName)
        {
            try
            {
                // Ghi tên người dùng vào tệp tin
                File.WriteAllText(_userNameFilePath, userName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Lỗi lưu tên người dùng: {ex.Message}");
            }
        }

        private void SaveChatHistory(string peerName, ChatMessage message)
        {
            try
            {
                if (!string.Equals(peerName, "Broadcast", StringComparison.OrdinalIgnoreCase))
                    return; // Chỉ lưu tin nhắn Broadcast

                if (!Directory.Exists(_chatHistoryDirectory))
                {
                    Directory.CreateDirectory(_chatHistoryDirectory);
                    Debug.WriteLine($"[SaveChatHistory] Đã tạo thư mục {_chatHistoryDirectory}");
                }

                string fileName = $"chat_history_{peerName}_{DateTime.Today:yyyy-MM-dd}.log";
                string filePath = Path.Combine(_chatHistoryDirectory, fileName);
                // Thay thế xuống dòng bằng [[NEWLINE]]
                string safeContent = message.Content.Replace("\r\n", "[[NEWLINE]]").Replace("\n", "[[NEWLINE]]").Replace("\r", "[[NEWLINE]]");
                string line = $"[{message.Timestamp:yyyy-MM-dd HH:mm:ss}]|{message.SenderName}|{safeContent}";
                Debug.WriteLine($"[SaveChatHistory] Lưu vào {filePath}: {line}");
                File.AppendAllText(filePath, line + Environment.NewLine);

                CleanupOldChatHistory(peerName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SaveChatHistory] Lỗi lưu lịch sử chat: {ex.Message}");
                MessageBox.Show($"Không thể lưu lịch sử chat: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        private void UpdateChatHistoryName(string oldName, string newName)
        {
            try
            {
                var files = Directory.GetFiles(_chatHistoryDirectory, $"chat_history_{oldName}_*.log");
                foreach (var file in files)
                {
                    try
                    {
                        string newFileName = file.Replace($"chat_history_{oldName}_", $"chat_history_{newName}_");
                        File.Move(file, newFileName);
                        Debug.WriteLine($"[UpdateChatHistoryName] Đã đổi tên: {file} -> {newFileName}");

                        var lines = File.ReadAllLines(newFileName).ToList();
                        for (int i = 0; i < lines.Count; i++)
                        {
                            if (lines[i].Contains($" {oldName}:"))
                            {
                                lines[i] = lines[i].Replace($" {oldName}:", $" {newName}:");
                            }
                        }
                        File.WriteAllLines(newFileName, lines);
                        Debug.WriteLine($"[UpdateChatHistoryName] Đã cập nhật nội dung: {newFileName}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[UpdateChatHistoryName] Lỗi cập nhật file {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateChatHistoryName] Lỗi cập nhật lịch sử {oldName} -> {newName}: {ex.Message}");
            }
        }

        private void CleanupOldChatHistory(string peerName)
        {
            try
            {
                // Lấy danh sách các file lịch sử chat của người nhận, chọn ra đường dẫn và ngày từ tên file
                var files = Directory.GetFiles(_chatHistoryDirectory, $"chat_history_{peerName}_*.log")
                    .Select(f => new { Path = f, Date = ParseDateFromFileName(f) })
                    .Where(f => f.Date != null) // Chỉ giữ lại các file có thể парсить được ngày
                    .ToList();

                // Tính toán ngày cắt để xóa (7 ngày trước ngày hiện tại)
                DateTime cutoffDate = DateTime.Today.AddDays(-7);

                // Duyệt qua từng file trong danh sách
                foreach (var file in files)
                {
                    // Nếu ngày của file cũ hơn ngày cắt
                    if (file.Date < cutoffDate)
                    {
                        try
                        {
                            // Xóa file
                            File.Delete(file.Path);
                            Debug.WriteLine($"Đã xóa file lịch sử cũ hơn 7 ngày: {file.Path}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Lỗi xóa file lịch sử {file.Path}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Lỗi dọn dẹp lịch sử chat: {ex.Message}");
            }
        }

        private void LoadChatHistory(string peerName)
        {
            try
            {
                if (!Directory.Exists(_chatHistoryDirectory))
                {
                    Directory.CreateDirectory(_chatHistoryDirectory);
                    Debug.WriteLine($"[LoadChatHistory] Đã tạo thư mục {_chatHistoryDirectory}");
                }

                var existingMessages = _chatMessages.Where(m => m.SenderName == peerName || m.SenderName == _myUserName).ToList();

                if (_currentPeer != peerName)
                {
                    Debug.WriteLine($"[LoadChatHistory] Thay đổi từ {_currentPeer} sang {peerName}, xóa tin nhắn.");
                    _chatMessages.Clear();
                    chatListBox.Items.Clear();
                    _displayedMessageIds.Clear();
                    _currentPeer = peerName;
                }

                var files = Directory.GetFiles(_chatHistoryDirectory, $"chat_history_{peerName}_*.log")
                                    .OrderBy(f => ParseDateFromFileName(f))
                                    .ToList();

                if (!files.Any())
                {
                    Debug.WriteLine($"[LoadChatHistory] Không tìm thấy tệp lịch sử cho {peerName}");
                    return;
                }

                foreach (string filePath in files)
                {
                    if (File.Exists(filePath))
                    {
                        var lines = File.ReadAllLines(filePath);
                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            if (line.Length > 21 && line[0] == '[')
                            {
                                int closeBracket = line.IndexOf(']');
                                if (closeBracket > 0 && line.Length > closeBracket + 2)
                                {
                                    string timestampStr = line.Substring(1, closeBracket - 1);
                                    if (DateTime.TryParse(timestampStr, out DateTime timestamp))
                                    {
                                        // Tách các phần bằng ký tự |
                                        string[] parts = line.Substring(closeBracket + 1).Split(new[] { '|' }, 3);
                                        if (parts.Length == 3)
                                        {
                                            string sender = parts[1].Trim();
                                            string content = parts[2].Trim().Replace("[[NEWLINE]]", Environment.NewLine);
                                            if (string.IsNullOrWhiteSpace(sender) || string.IsNullOrWhiteSpace(content))
                                            {
                                                Debug.WriteLine($"[LoadChatHistory] Dòng không hợp lệ: {line}");
                                                continue;
                                            }
                                            bool isMyMessage = sender == _myUserName;
                                            var message = new ChatMessage(sender, content, isMyMessage, Guid.NewGuid())
                                            {
                                                Timestamp = timestamp
                                            };
                                            if (!_chatMessages.Any(m => m.SenderName == sender && m.Content == content && m.Timestamp == timestamp))
                                            {
                                                AddMessageToChat(message);
                                            }
                                        }
                                        else
                                        {
                                            Debug.WriteLine($"[LoadChatHistory] Định dạng dòng sai: {line}");
                                        }
                                    }
                                    else
                                    {
                                        Debug.WriteLine($"[LoadChatHistory] Timestamp không hợp lệ: {line}");
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine($"[LoadChatHistory] Không tìm thấy ']' phân cách: {line}");
                                }
                            }
                            else
                            {
                                Debug.WriteLine($"[LoadChatHistory] Định dạng dòng sai: {line}");
                            }
                        }
                        Debug.WriteLine($"[LoadChatHistory] Đã tải lịch sử từ {filePath}");
                    }
                }

                foreach (var message in existingMessages)
                {
                    if (!_chatMessages.Any(m => m.SenderName == message.SenderName && m.Content == message.Content && m.Timestamp == message.Timestamp))
                    {
                        AddMessageToChat(message);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LoadChatHistory] Lỗi tải lịch sử chat: {ex.Message}");
                MessageBox.Show($"Không thể tải lịch sử chat cho {peerName}: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
        }

        private DateTime? ParseDateFromFileName(string filePath)
        {
            try
            {
                // Lấy tên file không bao gồm phần mở rộng
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                // Tách tên file dựa trên dấu '_' và lấy phần cuối cùng (được giả định là ngày)
                string datePart = fileName.Split('_').Last();
                // Cố gắng парсить (phân tích cú pháp) phần ngày theo định dạng "yyyy-MM-dd"
                if (DateTime.TryParseExact(datePart, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out DateTime date))
                {
                    return date; // Trả về đối tượng DateTime nếu парсить thành công
                }
                Debug.WriteLine($"Không thể phân tích ngày từ tên tệp: {filePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Lỗi phân tích ngày từ tên tệp {filePath}: {ex.Message}");
            }
            return null; // Trả về null nếu không tải được ngày
        }

        private void SetPlaceholder()
        {
            // Nếu hộp thoại nhập tin nhắn trống và không có focus
            if (string.IsNullOrEmpty(messageTextBox.Text) && !messageTextBox.Focused)
            {
                messageTextBox.ForeColor = Color.Gray; // Đặt màu chữ thành màu xám
                messageTextBox.Text = "Nhập tin nhắn..."; // Hiển thị văn bản gợi ý
            }
        }

        private void RemovePlaceholder()
        {
            // Nếu nội dung hộp thoại là văn bản gợi ý và màu chữ là xám
            if (messageTextBox.Text == "Nhập tin nhắn..." && messageTextBox.ForeColor == Color.Gray)
            {
                messageTextBox.Text = ""; // Xóa văn bản gợi ý
                messageTextBox.ForeColor = Color.Black; // Đặt lại màu chữ thành màu đen
            }
        }

        private void copyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Nếu có mục tin nhắn được chọn trong ListBox
            if (chatListBox.SelectedItem is ChatMessage selectedMessage)
            {
                try
                {
                    // Sao chép nội dung tin nhắn đã chọn vào Clipboard
                    Clipboard.SetText(selectedMessage.Content);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Không thể sao chép: {ex.Message}", "Lỗi sao chép", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ChatListBox_MouseDown(object sender, MouseEventArgs e)
        {
            // Nếu nút chuột phải được nhấn
            if (e.Button == MouseButtons.Right)
            {
                // Lấy index của item tại vị trí chuột
                int index = chatListBox.IndexFromPoint(e.Location);
                // Nếu tìm thấy item
                if (index >= 0 && index < chatListBox.Items.Count)
                {
                    chatListBox.SelectedIndex = index;
                }
                else
                {
                    chatListBox.SelectedIndex = -1;
                }
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            _myUserName = LoadUserName();
            if (string.IsNullOrWhiteSpace(_myUserName))
            {
                _myUserName = Environment.UserName;
                if (string.IsNullOrWhiteSpace(_myUserName))
                {
                    _myUserName = "User" + _random.Next(100, 1000);
                }
                SaveUserName(_myUserName);
            }

            try
            {
                _myAvatar = Properties.Resources.default_avatar;
                Debug.WriteLine("Đã gán default_avatar.png cho _myAvatar.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Lỗi tải default_avatar.png: {ex.Message}");
                _myAvatar = SystemIcons.Question.ToBitmap();
            }
            this.Text = $"Messenger";
            userNameLabel.Text = $"{_myUserName}\n(Tôi)";

            try
            {
                _networkService = new NetworkService(_myUserName, TcpPort, MulticastAddress, MulticastPort);
                _networkService.MessageReceived += NetworkService_MessageReceived;
                _networkService.PeerDiscovered += NetworkService_PeerDiscovered;
                _networkService.PeerDisconnected += NetworkService_PeerDisconnected;
                _networkService.TypingStatusReceived += NetworkService_TypingStatusReceived;

                _networkService.Start();
                UpdateOnlineUsersList();

                Debug.WriteLine("[MainForm_Load] Dọn dẹp lịch sử trò chuyện cũ.");
                CleanupOldChatHistory("Broadcast");

                Debug.WriteLine("[MainForm_Load] Đang tải lịch sử trò chuyện công cộng.");
                _currentPeer = "Broadcast";
                selectedPeerLabel.Text = "Trò chuyện: Broadcast";
                selectedPeerLabel.Tag = "Broadcast";
                LoadChatHistory("Broadcast");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể khởi động dịch vụ mạng.\nLỗi: {ex.Message}", "Lỗi khởi động mạng", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ExitApplication();
            }
        }

        private async void btnRename_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("btnRename_Click triggered"); // Ghi log khi nút "Đổi tên" được nhấn

            // Sử dụng khối 'using' để đảm bảo form RenameForm được giải phóng đúng cách
            using (RenameForm renameForm = new RenameForm(_myUserName))
            {
                // Hiển thị form đổi tên dưới dạng hộp thoại và kiểm tra kết quả
                if (renameForm.ShowDialog(this) == DialogResult.OK)
                {
                    string newName = renameForm.NewUserName; // Lấy tên người dùng mới từ form

                    // Kiểm tra xem tên mới có hợp lệ và khác với tên cũ không
                    if (!string.IsNullOrWhiteSpace(newName) && newName != _myUserName)
                    {
                        // Kiểm tra xem tên mới có chứa ký tự ':' không (ký tự này được dùng làm dấu phân cách trong giao thức)
                        if (newName.Contains(":"))
                        {
                            MessageBox.Show("Tên không được chứa ký tự ':' vì nó được sử dụng làm dấu phân cách trong giao thức mạng.", "Tên không hợp lệ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return; // Dừng lại nếu tên không hợp lệ
                        }
                        try
                        {
                            string oldName = _myUserName; // Lưu tên cũ để sử dụng sau này
                            _myUserName = newName; // Cập nhật tên người dùng hiện tại
                            SaveUserName(_myUserName); // Lưu tên người dùng mới vào file
                            this.Text = $"Messenger"; // Cập nhật tiêu đề form // Đã thay đổi dòng này để giữ tính nhất quán, hoặc sử dụng $"Messenger - {_myUserName}" nếu bạn thích
                            userNameLabel.Text = $"{_myUserName}\n(Tôi)"; // <<<< THÊM DÒNG NÀY ĐỂ CẬP NHẬT LABEL TÊN KHI THAY ĐỔI
                            // Nếu dịch vụ mạng đã được khởi tạo
                            if (_networkService != null)
                            {
                                _networkService.UpdateLocalUserName(_myUserName); // Cập nhật tên người dùng trong dịch vụ mạng
                                await _networkService.SendNameUpdate(oldName, _myUserName); // Gửi thông báo đổi tên đến các peer khác

                                UpdateChatMessagesName(oldName, _myUserName); // Cập nhật tên người gửi trong các tin nhắn đã hiển thị
                                UpdateChatHistoryName(oldName, _myUserName); // Cập nhật tên trong các file lịch sử chat

                                // Nếu peer hiện tại là tên cũ, cập nhật lại peer hiện tại
                                if (_currentPeer == oldName)
                                {
                                    _currentPeer = _myUserName;
                                    selectedPeerLabel.Text = $"Trò chuyện: {_myUserName}";
                                    selectedPeerLabel.Tag = _myUserName;
                                    LoadChatHistory(_myUserName); // Tải lại lịch sử chat với tên mới
                                }

                                UpdateOnlineUsersList(); // Cập nhật lại danh sách người dùng trực tuyến

                                // Thêm tin nhắn hệ thống thông báo đổi tên
                                AddMessageToChat(new ChatMessage("Hệ thống", $"Bạn đã đổi tên từ {oldName} thành {newName}", false, ChatMessage.MessageType.System));
                            }

                            MessageBox.Show($"Tên của bạn đã được đổi thành: {newName}", "Đổi tên thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        catch (InvalidOperationException ex)
                        {
                            // Xử lý lỗi nếu có vấn đề về thao tác không hợp lệ
                            MessageBox.Show(ex.Message, "Lỗi đổi tên", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        catch (Exception ex)
                        {
                            // Xử lý các lỗi khác trong quá trình đổi tên
                            Debug.WriteLine($"Lỗi gửi thông báo đổi tên: {ex.Message}");
                            MessageBox.Show($"Không thể thông báo đổi tên đến các peer khác: {ex.Message}", "Lỗi Đổi Tên", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                }
            }
        }

        // Phương thức cập nhật tên người gửi trong các tin nhắn đã hiển thị trên giao diện
        private void UpdateChatMessagesName(string oldName, string newName)
        {
            // Nếu không có tin nhắn nào, không cần làm gì
            if (!_chatMessages.Any())
            {
                Debug.WriteLine("Không có tin nhắn để cập nhật tên.");
                return;
            }

            // Duyệt qua tất cả các tin nhắn trong danh sách
            foreach (var message in _chatMessages)
            {
                // Nếu tên người gửi của tin nhắn trùng với tên cũ
                if (message.SenderName == oldName)
                {
                    message.SenderName = newName; // Cập nhật tên người gửi thành tên mới
                    message.IsMyMessage = newName == _myUserName; // Cập nhật lại cờ IsMyMessage
                }
            }
            chatListBox.Items.Clear(); // Xóa tất cả các mục hiện có trong ListBox
                                       // Thêm lại tất cả các tin nhắn vào ListBox (đã được cập nhật tên)
            foreach (var message in _chatMessages)
            {
                chatListBox.Items.Add(message);
            }
            // Cuộn xuống cuối ListBox để hiển thị tin nhắn mới nhất
            if (chatListBox.Items.Count > 0)
            {
                chatListBox.TopIndex = chatListBox.Items.Count - 1;
            }
        }

        // Phương thức xử lý sự kiện khi form đang đóng
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_isClosing && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.WindowState = FormWindowState.Minimized;
                this.Hide();
                _trayIcon.Visible = true;
                UpdateTrayIcon(); // Cập nhật text của tray icon khi đóng (thu nhỏ)
                if (_trayIcon.Icon != null)
                {
                    var balloon = new CustomBalloonForm(
                        "Messenger",
                        "Ứng dụng đã được thu nhỏ vào khay hệ thống. Nhấp đúp để khôi phục.",
                        this.Icon,
                        5000,
                        RestoreFromTray
                    );
                    balloon.Show();
                    Debug.WriteLine("Hiển thị thông báo tùy chỉnh khi đóng.");
                }
            }
            else
            {
                // This block is executed when the form is actually closing (e.g., Application.Exit() is called)
                _dateTimeTimer?.Stop();
                _dateTimeTimer?.Dispose();
                _rainbowTimer?.Stop();
                _rainbowTimer?.Dispose();
                _typingTimer?.Stop(); // Stop typing timer
                _typingTimer?.Dispose(); // Dispose typing timer

                try
                {
                    // Save chat history for all messages before disposing network service
                    foreach (var message in _chatMessages)
                    {
                        string peerName = message.IsMyMessage ? _currentPeer : message.SenderName;
                        SaveChatHistory(peerName, message);
                    }
                    _networkService?.Dispose(); // Dispose the network service
                    _trayIcon?.Dispose(); // Dispose the tray icon
                    Debug.WriteLine("[MainForm_FormClosing] Đã lưu lịch sử chat và giải phóng tài nguyên.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MainForm_FormClosing] Lỗi khi đóng: {ex.Message}");
                }
            }
        }

        // Phương thức xử lý sự kiện khi nhận được tin nhắn từ dịch vụ mạng
        private void NetworkService_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new MethodInvoker(() => NetworkService_MessageReceived(sender, e)));
                return;
            }

            // Bỏ qua tin nhắn từ chính mình nếu không phải tin nhắn hệ thống
            if (e.Message.SenderName == _myUserName && e.Message.Type != ChatMessage.MessageType.System)
            {
                Debug.WriteLine($"[NetworkService_MessageReceived] Bỏ qua tin nhắn từ chính mình: {e.Message.SenderName}");
                return;
            }

            Debug.WriteLine($"[NetworkService_MessageReceived] Tin nhắn từ {e.Message.SenderName}, nội dung: {e.Message.Content}, ID tin nhắn: {e.Message.MessageId}");

            // Lưu tin nhắn vào lịch sử chat (luôn là Broadcast vì chỉ dùng chế độ Broadcast)
            SaveChatHistory("Broadcast", e.Message);

            // Thêm tin nhắn vào _chatMessages nếu chưa có
            if (!_displayedMessageIds.Contains(e.Message.MessageId))
            {
                _chatMessages.Add(e.Message);
                _displayedMessageIds.Add(e.Message.MessageId);
                Debug.WriteLine($"[NetworkService_MessageReceived] Đã thêm tin nhắn vào _chatMessages: {e.Message.Content}");

                // Hiển thị tin nhắn ngay lập tức nếu ứng dụng đang mở
                if (this.WindowState != FormWindowState.Minimized)
                {
                    Debug.WriteLine($"[NetworkService_MessageReceived] Hiển thị tin nhắn từ {e.Message.SenderName}");
                    AddMessageToChat(e.Message);
                }
                else
                {
                    Debug.WriteLine($"[NetworkService_MessageReceived] Ứng dụng đang ẩn, không hiển thị tin nhắn ngay lập tức: {e.Message.Content}");
                }

                // Hiển thị thông báo và tăng số tin nhắn chưa đọc nếu ứng dụng đang ẩn
                if (this.WindowState == FormWindowState.Minimized)
                {
                    _unreadMessageCount++;
                    UpdateTrayIcon();
                    ShowNewMessageNotification(e.Message);
                }
            }
            else
            {
                if (this.WindowState == FormWindowState.Minimized)
                {
                    _unreadMessageCount++;
                    UpdateTrayIcon();
                }
                Debug.WriteLine($"[NetworkService_MessageReceived] Bỏ qua tin nhắn trùng lặp (MessageId: {e.Message.MessageId})");
            }
        }

        // Phương thức xử lý sự kiện khi một peer mới được phát hiện bởi dịch vụ mạng
        private void NetworkService_PeerDiscovered(object sender, PeerDiscoveryEventArgs e)
        {
            Debug.WriteLine($"[NetworkService_PeerDiscovered] Peer đã phát hiện: {e.PeerName} at {e.PeerEndPoint}");
            // Kiểm tra xem có cần Invoke lên luồng UI hay không
            if (this.InvokeRequired)
            {
                Debug.WriteLine($"[NetworkService_PeerDiscovered] Gọi trên luồng UI cho đối tác: {e.PeerName}");
                this.Invoke(new Action(() => HandlePeerDiscovered(e))); // Invoke lên luồng UI để xử lý
            }
            else
            {
                HandlePeerDiscovered(e); // Xử lý trực tiếp nếu đã ở trên luồng UI
            }
        }

        // Phương thức xử lý khi một peer mới được phát hiện (được gọi trên luồng UI)
        private void HandlePeerDiscovered(PeerDiscoveryEventArgs e)
        {
            Debug.WriteLine($"[HandlePeerDiscovered] Xử lý peer: {e.PeerName}");
            UpdateOnlineUsersList(); // Cập nhật danh sách người dùng trực tuyến                                     
        }

        // Phương thức cập nhật danh sách người dùng trực tuyến trên giao diện
        private void UpdateOnlineUsersList()
        {
            // Lấy danh sách tên các peer đang hoạt động từ dịch vụ mạng
            var currentPeers = _networkService.GetActivePeerNames().ToList();
            Debug.WriteLine($"[UpdateOnlineUsersList] Đã cập nhật lại các user từ NetworkService: {string.Join(", ", currentPeers)}");

            // Lưu trữ peer hiện tại đang được chọn
            string previousSelectedPeer = onlineUsersListBox.SelectedItem?.ToString();

            // Tạm thời ngắt xử lý sự kiện SelectedIndexChanged để tránh kích hoạt không cần thiết khi cập nhật DataSource
            onlineUsersListBox.SelectedIndexChanged -= OnlineUsersListBox_SelectedIndexChanged;

            _onlineUsers.Clear(); // Xóa danh sách người dùng trực tuyến hiện có
            _onlineUsers.Add("Broadcast"); // Thêm mục "Broadcast"
            foreach (var peerName in currentPeers.OrderBy(p => p))
            {
                if (peerName != _myUserName)
                {
                    _onlineUsers.Add(peerName);
                }
            }
            onlineUsersListBox.DataSource = null; // Đặt DataSource về null để làm mới
            onlineUsersListBox.DataSource = _onlineUsers; // Gán lại DataSource với danh sách đã cập nhật
            Debug.WriteLine($"[UpdateOnlineUsersList] Đã cập nhật danh sách người dùng: {string.Join(", ", _onlineUsers)}");

            // Khôi phục peer đã được chọn trước đó, nếu nó vẫn còn trong danh sách
            if (!string.IsNullOrEmpty(previousSelectedPeer) && _onlineUsers.Contains(previousSelectedPeer))
            {
                onlineUsersListBox.SelectedItem = previousSelectedPeer;
            }
            else if (_onlineUsers.Count > 0) // Nếu không, chọn mục đầu tiên (thường là "Broadcast")
            {
                onlineUsersListBox.SelectedIndex = 0;
            }

            // Kết nối lại xử lý sự kiện SelectedIndexChanged
            onlineUsersListBox.SelectedIndexChanged += OnlineUsersListBox_SelectedIndexChanged;
        }

        // Phương thức xử lý sự kiện khi một peer bị ngắt kết nối
        private void NetworkService_PeerDisconnected(object sender, PeerDiscoveryEventArgs e)
        {
            // Kiểm tra xem có cần Invoke lên luồng UI hay không
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => HandlePeerDisconnected(e))); // Invoke lên luồng UI để xử lý
            }
            else
            {
                HandlePeerDisconnected(e); // Xử lý trực tiếp nếu đã ở trên luồng UI
            }
        }

        private void HandlePeerDisconnected(PeerDiscoveryEventArgs e)
        {
            UpdateOnlineUsersList(); // Cập nhật lại danh sách người dùng trực tuyến sau khi một peer ngắt kết nối

            if (_currentPeer == e.PeerName)
            {
                _currentPeer = "Broadcast"; // Chuyển về chế độ trò chuyện Broadcast
                selectedPeerLabel.Text = $"Trò chuyện: Broadcast";
                selectedPeerLabel.Tag = "Broadcast";
                LoadChatHistory("Broadcast"); // Tải lịch sử chat Broadcast
            }
            // Thêm một tin nhắn hệ thống thông báo rằng peer đã ngắt kết nối
            AddMessageToChat(new ChatMessage("Hệ thống", $"{e.PeerName} đã ngắt kết nối.", false, ChatMessage.MessageType.System));
        }

        // Phương thức xử lý sự kiện khi nhận được trạng thái "đang gõ" từ dịch vụ mạng
        private void NetworkService_TypingStatusReceived(object sender, TypingStatusEventArgs e)
        {
            // Kiểm tra xem phương thức có cần được gọi trên luồng UI hay không.
            // Nếu cần, sử dụng Invoke để đảm bảo an toàn luồng.
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateRemoteTypingStatus(e.SenderName, e.IsTyping)));
            }
            else
            {
                // Nếu không cần Invoke, gọi trực tiếp phương thức cập nhật trạng thái gõ
                UpdateRemoteTypingStatus(e.SenderName, e.IsTyping);
            }
        }

        // Phương thức cập nhật trạng thái "đang gõ" của người dùng từ xa trên giao diện
        private void UpdateRemoteTypingStatus(string senderName, bool isTyping)
        {
            // Lấy tên peer hiện tại đang được chọn để trò chuyện
            string selectedPeer = selectedPeerLabel.Tag as string;

            // Nếu peer hiện tại đang được chọn là người gửi trạng thái gõ
            if (selectedPeer != null && selectedPeer == senderName)
            {
                if (isTyping) // Nếu người đó đang gõ
                {
                    _remoteTypingUser = senderName; // Lưu tên người đang gõ
                    _typingStatusLabel.Text = $"{senderName} đang nhập tin nhắn..."; // Hiển thị thông báo
                    _typingStatusLabel.BackColor = Color.Transparent; // Đặt màu nền trong suốt
                }
                else // Nếu người đó đã ngừng gõ
                {
                    // Chỉ xóa thông báo nếu người đang gõ trước đó là người này
                    if (_remoteTypingUser == senderName)
                    {
                        _remoteTypingUser = ""; // Xóa tên người đang gõ
                        _typingStatusLabel.Text = ""; // Xóa thông báo
                    }
                }
            }
            // Nếu đang ở chế độ Broadcast và có người đang gõ
            else if (selectedPeer == "Broadcast" && isTyping)
            {
                _remoteTypingUser = senderName; // Lưu tên người đang gõ
                _typingStatusLabel.Text = $"{senderName} đang nhập tin nhắn..."; // Hiển thị thông báo
            }
            // Nếu đang ở chế độ Broadcast, người đó ngừng gõ và người đó là người đang gõ trước đó
            else if (selectedPeer == "Broadcast" && !isTyping && _remoteTypingUser == senderName)
            {
                _remoteTypingUser = ""; // Xóa tên người đang gõ
                _typingStatusLabel.Text = ""; // Xóa thông báo
            }
        }

        // Phương thức xử lý sự kiện khi văn bản trong hộp nhập tin nhắn thay đổi
        private void messageTextBox_TextChanged(object sender, EventArgs e)
        {
            // Nếu người dùng bắt đầu gõ (chưa ở trạng thái đang gõ, có văn bản và không phải placeholder)
            if (!_isTyping && messageTextBox.Text.Length > 0 && messageTextBox.ForeColor == Color.Black)
            {
                _isTyping = true; // Đặt trạng thái là đang gõ
                SendTypingStatus(true); // Gửi thông báo "đang gõ"
            }
            // Nếu người dùng ngừng gõ (đang ở trạng thái đang gõ và hộp thoại trống)
            else if (_isTyping && messageTextBox.Text.Length == 0)
            {
                _isTyping = false; // Đặt trạng thái là ngừng gõ
                SendTypingStatus(false); // Gửi thông báo "ngừng gõ"
            }
            // Dừng timer "ngừng gõ" mỗi khi văn bản thay đổi
            _typingTimer.Stop();
            // Nếu có văn bản và không phải placeholder, khởi động lại timer "ngừng gõ"
            if (messageTextBox.Text.Length > 0 && messageTextBox.ForeColor == Color.Black)
            {
                _typingTimer.Start();
            }
        }

        // Phương thức xử lý sự kiện Tick của timer _typingTimer (khi người dùng ngừng gõ trong một khoảng thời gian)
        private void _typingTimer_Tick(object sender, EventArgs e)
        {
            _typingTimer.Stop(); // Dừng timer
            _isTyping = false; // Đặt trạng thái là ngừng gõ
            SendTypingStatus(false); // Gửi thông báo "ngừng gõ"
        }

        // Phương thức gửi trạng thái "đang gõ" đến các peer khác
        private async void SendTypingStatus(bool isTyping)
        {
            // Lấy tên peer hiện tại đang được chọn
            string selectedPeer = selectedPeerLabel.Tag as string;
            // Nếu không có peer nào được chọn, mặc định là "Broadcast"
            if (string.IsNullOrEmpty(selectedPeer))
            {
                selectedPeer = "Broadcast";
            }

            try
            {
                // Không gửi trạng thái gõ cho chính mình
                if (selectedPeer == _myUserName) return;

                // Gửi trạng thái gõ đến Broadcast hoặc peer cụ thể
                if (selectedPeer == "Broadcast")
                {
                    await _networkService.SendTypingStatus(_myUserName, isTyping, true).ConfigureAwait(false);
                }
                else
                {
                    await _networkService.SendTypingStatus(_myUserName, isTyping, false, selectedPeer).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Lỗi gửi trạng thái typing: {ex.Message}");
            }
        }

        // Phương thức thêm một tin nhắn vào danh sách hiển thị chat
        private void AddMessageToChat(ChatMessage message)
        {
            if (chatListBox.IsDisposed || !chatListBox.IsHandleCreated) return;

            if (_displayedMessageIds.Contains(message.MessageId))
            {
                Debug.WriteLine($"[AddMessageToChat] Bỏ qua tin nhắn trùng lặp: {message.MessageId}");
                return;
            }

            using (Graphics g = chatListBox.CreateGraphics())
            {
                MessageRenderer.PrepareMessageForDrawing(message, g, chatListBox.Width - AvatarWidth - AvatarPadding - 20, chatListBox.Font);
            }

            if (message.IsMyMessage)
            {
                message.Avatar = _myAvatar;
            }
            else
            {
                message.Avatar = GetAvatarForUser(message.SenderName);
            }

            _chatMessages.Add(message);
            _displayedMessageIds.Add(message.MessageId);

            chatListBox.BeginUpdate();
            chatListBox.Items.Add(message);
            chatListBox.EndUpdate();

            if (chatListBox.Items.Count > 0)
            {
                chatListBox.TopIndex = chatListBox.Items.Count - 1;
                Debug.WriteLine($"[AddMessageToChat] Cuộn đến tin nhắn mới: {message.Content}");
            }

            chatListBox.Refresh();
        }

        // Phương thức lấy avatar cho một người dùng cụ thể
        private Image GetAvatarForUser(string senderName)
        {
            try
            {
                // Kiểm tra xem avatar của người gửi đã có trong cache chưa
                if (_userAvatars.ContainsKey(senderName))
                {
                    return _userAvatars[senderName]; // Trả về avatar từ cache
                }
                else
                {
                    // Nếu chưa có, trả về avatar mặc định cho người khác
                    return Properties.Resources.other_default_avatar;
                }
            }
            catch (Exception ex)
            {
                // Xử lý lỗi nếu không thể tải avatar mặc định
                Debug.WriteLine($"Lỗi tải other_default_avatar.png cho {senderName}: {ex.Message}");
                return SystemIcons.Question.ToBitmap(); // Trả về biểu tượng dấu hỏi nếu lỗi
            }
        }

        // Phương thức xử lý sự kiện khi lựa chọn trong danh sách người dùng trực tuyến thay đổi
        private void OnlineUsersListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Đảm bảo có một mục được chọn
            if (onlineUsersListBox.SelectedItem != null)
            {
                string selectedPeer = onlineUsersListBox.SelectedItem.ToString();
                // Nếu người dùng được chọn khác với người đang trò chuyện hiện tại
                if (_currentPeer != selectedPeer)
                {
                    Debug.WriteLine($"[OnlineUsersListBox_SelectedIndexChanged] Người dùng đã thay đổi từ {_currentPeer} sang {selectedPeer}");
                    _currentPeer = selectedPeer; // Cập nhật peer hiện tại
                    selectedPeerLabel.Text = $"Trò chuyện: {selectedPeer}"; // Cập nhật nhãn hiển thị peer
                    selectedPeerLabel.Tag = selectedPeer; // Cập nhật tag của nhãn

                    LoadChatHistory(selectedPeer); // Tải lịch sử chat với peer mới

                    // Xóa trạng thái "đang gõ" cũ
                    _typingStatusLabel.Text = "";
                    _remoteTypingUser = "";
                }
                else
                {
                    Debug.WriteLine($"[OnlineUsersListBox_SelectedIndexChanged] Người dùng không thay đổi: {selectedPeer}");
                    // Nếu vẫn là cùng một peer (ví dụ: nhấp lại vào "Broadcast"), tải lại lịch sử Broadcast
                    if (selectedPeer == "Broadcast")
                    {
                        LoadChatHistory("Broadcast");
                    }
                }
            }
        }

        private async void sendButton_Click(object sender, EventArgs e)
        {
            string messageText = messageTextBox.Text.Trim();
            // Không gửi tin nhắn nếu nội dung trống hoặc là placeholder
            if (string.IsNullOrEmpty(messageText) || messageTextBox.ForeColor == Color.Gray) return;

            // Xác định người nhận tin nhắn, mặc định là "Broadcast" nếu không có ai được chọn
            string selectedPeer = selectedPeerLabel.Tag as string ?? "Broadcast";

            // Tạo đối tượng tin nhắn của chính mình
            ChatMessage myMessage = new ChatMessage(_myUserName, messageText, true);
            AddMessageToChat(myMessage); // Thêm tin nhắn vào giao diện chat

            // Lưu tin nhắn vào lịch sử chat
            SaveChatHistory(selectedPeer, myMessage);

            try
            {
                // Gửi tin nhắn tùy thuộc vào người nhận
                if (selectedPeer == "Broadcast")
                {
                    Debug.WriteLine($"[sendButton_Click] Gửi phát sóng: {messageText}");
                    await _networkService.SendMulticastMessage(messageText).ConfigureAwait(false); // Gửi tin nhắn Broadcast
                    Debug.WriteLine($"[sendButton_Click] Đã gửi phát sóng thành công");
                }
                else
                {
                    Debug.WriteLine($"[sendButton_Click] Gửi đến {selectedPeer}: {messageText}");
                    await _networkService.SendMessageToPeer(selectedPeer, messageText).ConfigureAwait(false); // Gửi tin nhắn đến một peer cụ thể
                    Debug.WriteLine($"[sendButton_Click] Tin nhắn đã được gửi thành công tới {selectedPeer}");
                }
            }
            catch (InvalidOperationException ioe)
            {
                // Xử lý lỗi nếu dịch vụ mạng không ở trạng thái hợp lệ để gửi
                Debug.WriteLine($"[sendButton_Click] Không gửi được tin nhắn: {ioe.Message}");
                MessageBox.Show(ioe.Message, "Lỗi gửi tin nhắn", MessageBoxButtons.OK, MessageBoxIcon.Error);
                AddMessageToChat(new ChatMessage("Hệ thống", ioe.Message, false, ChatMessage.MessageType.Error));
            }
            catch (Exception ex)
            {
                // Xử lý các lỗi không mong muốn khác khi gửi tin nhắn
                Debug.WriteLine($"[sendButton_Click] Lỗi không mong muốn khi gửi tin nhắn: {ex.Message}");
                string errorMsg = selectedPeer == "Broadcast"
                    ? "Không thể gửi tin nhắn Broadcast. Vui lòng kiểm tra cấu hình mạng hoặc tường lửa."
                    : $"Không thể gửi tin nhắn đến {selectedPeer}. Vui lòng kiểm tra trạng thái người dùng hoặc mạng.";
                MessageBox.Show(errorMsg, "Lỗi Gửi Tin Nhắn", MessageBoxButtons.OK, MessageBoxIcon.Error);
                AddMessageToChat(new ChatMessage("Hệ thống", errorMsg, false, ChatMessage.MessageType.Error));
            }
            finally
            {
                // Sau khi gửi, làm sạch hộp nhập và đặt lại trạng thái
                // Đảm bảo các thao tác UI này được thực hiện trên luồng UI chính
                if (messageTextBox.InvokeRequired)
                {
                    messageTextBox.Invoke(new MethodInvoker(() =>
                    {
                        messageTextBox.Clear();
                        SetPlaceholder();
                        _typingTimer.Stop();
                        _isTyping = false;
                        SendTypingStatus(false); // Gửi trạng thái ngừng gõ
                    }));
                }
                else
                {
                    messageTextBox.Clear();
                    SetPlaceholder();
                    _typingTimer.Stop();
                    _isTyping = false;
                    SendTypingStatus(false); // Gửi trạng thái ngừng gõ
                }
            }
        }
        private void messageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            // Xử lý phím Enter để gửi tin nhắn (chỉ khi không giữ Shift và không đang trong quá trình gửi)
            if (e.KeyCode == Keys.Enter && !e.Shift && !_isSendingMessage)
            {
                e.SuppressKeyPress = true; // Ngăn không cho phím Enter tạo dòng mới trong TextBox
                _isSendingMessage = true; // Đánh dấu đang gửi tin nhắn để tránh gửi trùng lặp
                try
                {
                    sendButton_Click(sender, EventArgs.Empty); // Gọi sự kiện click của nút gửi
                }
                finally
                {
                    _isSendingMessage = false; // Đặt lại trạng thái đã gửi xong
                }
            }
            // Nếu nhấn Enter và giữ Shift, có thể xử lý việc thêm dòng mới (tùy chọn)
            else if (e.KeyCode == Keys.Enter && e.Shift)
            {
                // Có thể thêm xử lý cho việc xuống dòng trong TextBox nếu cần
            }
        }

        private void ChatListBox_MeasureItem(object sender, MeasureItemEventArgs e)
        {
            if (e.Index == 0)
            {
                e.ItemHeight += TopListBoxMargin;
            }

            if (e.Index < 0 || e.Index >= chatListBox.Items.Count) return;

            ChatMessage message = chatListBox.Items[e.Index] as ChatMessage;
            if (message == null) return;

            try
            {
                using (Graphics g = chatListBox.CreateGraphics())
                {
                    if (message.CalculatedTotalSize.IsEmpty || message.LastCalculatedWidth != chatListBox.Width - AvatarWidth - AvatarPadding - 20)
                    {
                        MessageRenderer.PrepareMessageForDrawing(message, g, chatListBox.Width - AvatarWidth - AvatarPadding - 20, chatListBox.Font);
                        message.LastCalculatedWidth = chatListBox.Width - AvatarWidth - AvatarPadding - 20;

                        // Tính chiều cao tổng cộng
                        float totalHeight = message.CalculatedContentSize.Height + (2 * 10); // verticalBubblePadding * 2
                        if (!message.IsMyMessage && !string.IsNullOrEmpty(message.SenderName))
                        {
                            totalHeight += message.CalculatedSenderNameSize.Height + 5; // senderNameGap
                        }
                        totalHeight += message.CalculatedTimestampSize.Height + 2; // Khoảng cách timestamp
                        totalHeight = Math.Max(totalHeight, 40); // Đảm bảo đủ chỗ cho avatar (nếu có)
                        message.CalculatedTotalSize = new SizeF(message.CalculatedContentSize.Width + (2 * 15), totalHeight); // horizontalBubblePadding * 2
                    }
                }
                e.ItemHeight = (int)message.CalculatedTotalSize.Height + 10; // Tăng padding từ 5 lên 10
                Debug.WriteLine($"MeasureItem Index {e.Index}: ItemHeight = {e.ItemHeight}, CalculatedTotalSize.Height = {message.CalculatedTotalSize.Height}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Lỗi đo kích thước tin nhắn tại index {e.Index}: {ex.Message}");
                e.ItemHeight = 40; // Giá trị mặc định nếu lỗi
            }
        }

        private void ChatListBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= chatListBox.Items.Count) return;

            ChatMessage message = chatListBox.Items[e.Index] as ChatMessage;
            if (message == null) return;

            if (message.CalculatedTotalSize.IsEmpty || message.LastCalculatedWidth != chatListBox.Width - AvatarWidth - AvatarPadding - 20)
            {
                using (Graphics g = chatListBox.CreateGraphics())
                {
                    MessageRenderer.PrepareMessageForDrawing(message, g, chatListBox.Width - AvatarWidth - AvatarPadding - 20, chatListBox.Font);
                    message.LastCalculatedWidth = chatListBox.Width - AvatarWidth - AvatarPadding - 20;
                }
            }

            // Đặt lại BubbleBounds và AvatarBounds để đảm bảo vẽ đúng vị trí
            message.BubbleBounds = RectangleF.Empty;
            message.AvatarBounds = RectangleF.Empty;

            Image avatar = message.IsMyMessage ? _myAvatar : GetAvatarForUser(message.SenderName);

            // Tạo bounds mới nếu là item đầu tiên
            Rectangle bounds = e.Bounds;
            if (e.Index == 0)
            {
                bounds = new Rectangle(e.Bounds.X, e.Bounds.Y + TopListBoxMargin, e.Bounds.Width, e.Bounds.Height - TopListBoxMargin);
            }

            try
            {
                MessageRenderer.DrawMessage(e.Graphics, bounds, message, e.State, chatListBox.Font, avatar);
                Debug.WriteLine($"Vẽ tin nhắn tại chỉ mục {e.Index}, Tọa độ Y = {bounds.Y}, Chiều cao = {bounds.Height}, Hiển thị = {bounds.IntersectsWith(chatListBox.ClientRectangle)}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Lỗi vẽ tin nhắn tại chỉ mục {e.Index}: {ex.Message}");
                e.Graphics.Clear(SystemColors.Window);
            }
        }


        private void ChatListBox_MouseClick(object sender, MouseEventArgs e)
        {
            // Xử lý sự kiện click chuột trên ListBox (để mở URL)
            if (e.Button == MouseButtons.Left)
            {
                int index = chatListBox.IndexFromPoint(e.Location);
                // Kiểm tra xem có click vào một item tin nhắn hợp lệ không
                if (index != ListBox.NoMatches && index < _chatMessages.Count)
                {
                    ChatMessage message = _chatMessages[index];
                    Rectangle itemBounds = chatListBox.GetItemRectangle(index);

                    // Các giá trị padding và margin để tính toán vị trí bubble và nội dung
                    int horizontalBubblePadding = 15;
                    int verticalBubblePadding = 10;
                    int bubbleMarginFromEdge = 10;
                    int senderNameGap = 5;
                    int avatarWidth = 40;
                    int avatarPadding = 5;

                    RectangleF bubbleRect;
                    float senderNameHeight = message.IsMyMessage ? 0 : message.CalculatedSenderNameSize.Height;
                    // Tính toán vị trí bubble dựa trên việc tin nhắn là của mình hay của người khác
                    if (message.IsMyMessage)
                    {
                        bubbleRect = new RectangleF(
                            itemBounds.Right - ((int)message.CalculatedContentSize.Width + (2 * horizontalBubblePadding)) - bubbleMarginFromEdge - avatarWidth - avatarPadding,
                            itemBounds.Y + 5,
                            (int)message.CalculatedContentSize.Width + (2 * horizontalBubblePadding),
                            (int)message.CalculatedContentSize.Height + (2 * verticalBubblePadding));
                    }
                    else
                    {
                        bubbleRect = new RectangleF(
                            itemBounds.X + bubbleMarginFromEdge + avatarWidth + avatarPadding,
                            itemBounds.Y + 5 + (senderNameHeight > 0 ? senderNameHeight + senderNameGap : 0),
                            (int)message.CalculatedContentSize.Width + (2 * horizontalBubblePadding),
                            (int)message.CalculatedContentSize.Height + (2 * verticalBubblePadding));
                    }

                    // Tính toán vị trí của nội dung bên trong bong bóng chat
                    RectangleF contentRect = new RectangleF(
                        bubbleRect.X + horizontalBubblePadding,
                        bubbleRect.Y + verticalBubblePadding,
                        message.CalculatedContentSize.Width,
                        message.CalculatedContentSize.Height);

                    Debug.WriteLine($"[ChatListBox_MouseClick] Bấm vào ({e.X}, {e.Y}), Là tin nhắn của tôi: {message.IsMyMessage}, Chiều cao tên người gửi: {senderNameHeight}, Vùng nội dung: X={contentRect.X}, Y={contentRect.Y}, Rộng={contentRect.Width}, Cao={contentRect.Height}");
                    // Nếu không có URL trong tin nhắn
                    if (!message.UrlRegions.Any())
                    {
                        Debug.WriteLine($"[ChatListBox_MouseClick] Không tìm thấy vùng URL nào cho tin nhắn: {message.Content}");
                    }
                    // Duyệt qua các vùng URL đã được xác định trong tin nhắn
                    foreach (var urlRegion in message.UrlRegions)
                    {
                        // Tính toán vị trí tuyệt đối của vùng URL trong ListBox
                        RectangleF absoluteUrlBounds = new RectangleF(
                            contentRect.X + urlRegion.Bounds.X,
                            contentRect.Y + urlRegion.Bounds.Y,
                            urlRegion.Bounds.Width,
                            urlRegion.Bounds.Height);
                        Debug.WriteLine($"[ChatListBox_MouseClick] URL Region: X={absoluteUrlBounds.X}, Y={absoluteUrlBounds.Y}, Width={absoluteUrlBounds.Width}, Height={absoluteUrlBounds.Height}, URL={urlRegion.Url}");

                        // Kiểm tra xem vị trí click chuột có nằm trong vùng URL không
                        if (absoluteUrlBounds.Contains(e.X, e.Y))
                        {
                            Debug.WriteLine($"[ChatListBox_MouseClick] Đã nhấp vào URL: {urlRegion.Url}");
                            try
                            {
                                string urlToOpen = urlRegion.Url;
                                // Thêm giao thức http nếu URL không có
                                if (!urlToOpen.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                                    !urlToOpen.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                                {
                                    urlToOpen = urlToOpen.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                                        ? "http://" + urlToOpen
                                        : "http://www." + urlToOpen;
                                }
                                Debug.WriteLine($"[ChatListBox_MouseClick] Mở URL: {urlToOpen}");
                                Process.Start(new ProcessStartInfo(urlToOpen) { UseShellExecute = true }); // Mở URL bằng trình duyệt mặc định
                                return; // Thoát khỏi sự kiện click sau khi mở URL
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[ChatListBox_MouseClick] Lỗi mở URL {urlRegion.Url}: {ex.Message}");
                                MessageBox.Show($"Không thể mở liên kết: {ex.Message}", "Lỗi mở liên kết", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"[ChatListBox_MouseClick] Bấm vào ({e.X}, {e.Y}) không ở vùng URL: X={absoluteUrlBounds.X}, Y={absoluteUrlBounds.Y}, Width={absoluteUrlBounds.Width}, Height={absoluteUrlBounds.Height}");
                        }
                    }
                }
                else
                {
                    Debug.WriteLine($"[ChatListBox_MouseClick] Không có tin nhắn ở vị trí nhấp chuột ({e.X}, {e.Y})");
                }
            }
        }

        private void ChatListBox_MouseMove(object sender, MouseEventArgs e)
        {
            // Thay đổi con trỏ chuột khi di chuyển qua một URL trong tin nhắn
            bool isOverUrl = false;
            int index = chatListBox.IndexFromPoint(e.Location);

            if (index != ListBox.NoMatches && index < _chatMessages.Count)
            {
                ChatMessage message = chatListBox.Items[index] as ChatMessage;
                if (message == null) return;
                Rectangle itemBounds = chatListBox.GetItemRectangle(index);
                int horizontalBubblePadding = 15;
                int verticalBubblePadding = 10;
                int bubbleMarginFromEdge = 10;
                int senderNameGap = 5;
                int avatarWidth = 40;
                int avatarPadding = 5;

                RectangleF bubbleRect;
                float senderNameHeight = message.IsMyMessage ? 0 : message.CalculatedSenderNameSize.Height;
                if (message.IsMyMessage)
                {
                    bubbleRect = new RectangleF(
                        itemBounds.Right - ((int)message.CalculatedContentSize.Width + (2 * horizontalBubblePadding)) - bubbleMarginFromEdge - avatarWidth - avatarPadding,
                        itemBounds.Y + 5,
                        (int)message.CalculatedContentSize.Width + (2 * horizontalBubblePadding),
                        (int)message.CalculatedContentSize.Height + (2 * verticalBubblePadding));
                }
                else
                {
                    bubbleRect = new RectangleF(
                        itemBounds.X + bubbleMarginFromEdge + avatarWidth + avatarPadding,
                        itemBounds.Y + 5 + (senderNameHeight > 0 ? senderNameHeight + senderNameGap : 0),
                        (int)message.CalculatedContentSize.Width + (2 * horizontalBubblePadding),
                        (int)message.CalculatedContentSize.Height + (2 * verticalBubblePadding));
                }

                RectangleF contentRect = new RectangleF(
                    bubbleRect.X + horizontalBubblePadding,
                    bubbleRect.Y + verticalBubblePadding,
                    message.CalculatedContentSize.Width,
                    message.CalculatedContentSize.Height);

                Debug.WriteLine($"[ChatListBox_MouseMove] Chuột vào ({e.X}, {e.Y}), Là tin nhắn của tôi: {message.IsMyMessage}, Tên người gửi: {message.SenderName}, Chiều cao tên người gửi: {senderNameHeight}, Vùng nội dung: X={contentRect.X}, Y={contentRect.Y}, Rộng={contentRect.Width}, Cao={contentRect.Height}");

                if (!message.UrlRegions.Any())
                {
                    Debug.WriteLine($"[ChatListBox_MouseMove] Không có liên kết nào trong tin nhắn: {message.Content}");
                }
                else
                {
                    foreach (var urlRegion in message.UrlRegions)
                    {
                        RectangleF absoluteUrlBounds = new RectangleF(
                            contentRect.X + urlRegion.Bounds.X,
                            contentRect.Y + urlRegion.Bounds.Y,
                            urlRegion.Bounds.Width,
                            urlRegion.Bounds.Height);
                        Debug.WriteLine($"[ChatListBox_MouseMove] Vùng URL: X={absoluteUrlBounds.X}, Y={absoluteUrlBounds.Y}, Width={absoluteUrlBounds.Width}, Height={absoluteUrlBounds.Height}, URL={urlRegion.Url}");

                        if (absoluteUrlBounds.Contains(e.X, e.Y))
                        {
                            Debug.WriteLine($"[ChatListBox_MouseMove] Di chuột qua URL: {urlRegion.Url}");
                            isOverUrl = true;
                            break; // Nếu chuột nằm trên một URL, không cần kiểm tra các URL khác trong cùng tin nhắn
                        }
                    }
                }
            }
            else
            {
                Debug.WriteLine($"[ChatListBox_MouseMove] Không có tin nhắn ở vị trí chuột ({e.X}, {e.Y})");
            }

            // Thay đổi con trỏ chuột dựa trên việc có đang di chuột qua URL hay không
            chatListBox.Cursor = isOverUrl ? Cursors.Hand : Cursors.Default;
        }

        // Phương thức hiển thị thông báo tin nhắn mới khi ứng dụng ở chế độ thu nhỏ
        private void ShowNewMessageNotification(ChatMessage message)
        {
            // Kiểm tra xem biểu tượng khay hệ thống hoặc biểu tượng form có null không
            if (_trayIcon == null || this.Icon == null)
            {
                Debug.WriteLine("[ShowNewMessageNotification] Tray icon hoặc biểu tượng form là null. Không thể hiển thị thông báo.");
                return;
            }

            string title = message.SenderName;
            string content = message.Content;

            // Giới hạn độ dài nội dung để tránh thông báo quá dài
            if (content.Length > 100) // Ví dụ: giới hạn 100 ký tự
            {
                content = content.Substring(0, 97) + "...";
            }

            // Tạo một CustomBalloonForm mới
            var balloon = new CustomBalloonForm(
                title,
                content,
                this.Icon, // Sử dụng biểu tượng của form
                5000,      // Hiển thị trong 5 giây
                () => RestoreFromTray() // Hành động khi nhấp vào: khôi phục form
            );
            // Hiển thị thông báo từ dưới lên
            balloon.ShowFromBottom(title, content, this.Icon);
            Debug.WriteLine($"[ShowNewMessageNotification] Đã hiển thị thông báo tin nhắn mới từ {message.SenderName}.");
        }
        private Icon CreateTextIcon(string text, Icon baseIcon)
        {
            Bitmap bitmap = null;
            Graphics graphics = null;
            Icon newIcon = null;
            Bitmap baseBitmapScaled = null; // Khai báo ở đây để accessible trong finally

            try
            {
                // Kích thước mục tiêu cho bitmap và icon.
                // NotifyIcon thường hiển thị ở 16x16, 24x24, 32x32.
                // Vẽ trên một bitmap lớn hơn để có chất lượng tốt khi Windows scale xuống.
                int targetIconSize = 64; // Kích thước để vẽ, Windows sẽ scale nó

                // Đảm bảo icon gốc được vẽ lên bitmap có chất lượng tốt
                if (baseIcon != null)
                {
                    baseBitmapScaled = baseIcon.ToBitmap();
                    if (baseBitmapScaled.Width < targetIconSize || baseBitmapScaled.Height < targetIconSize)
                    {
                        // Scale base icon lên kích thước targetIconSize nếu nó nhỏ hơn
                        baseBitmapScaled = new Bitmap(baseBitmapScaled, targetIconSize, targetIconSize);
                    }
                }
                else
                {
                    // Fallback nếu baseIcon là null, có thể dùng SystemIcons.Application
                    baseBitmapScaled = SystemIcons.Application.ToBitmap();
                    baseBitmapScaled = new Bitmap(baseBitmapScaled, targetIconSize, targetIconSize);
                }

                bitmap = new Bitmap(targetIconSize, targetIconSize);
                graphics = Graphics.FromImage(bitmap);

                // Cài đặt chất lượng vẽ cao
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit; // Thường tốt hơn cho văn bản trên Windows
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // Vẽ icon gốc lên bitmap.
                graphics.DrawImage(baseBitmapScaled, 0, 0, targetIconSize, targetIconSize);

                // --- Bắt đầu vẽ "badge" số tin nhắn ---
                // Chỉ vẽ nếu có tin nhắn chưa đọc và không phải là "0"
                if (!string.IsNullOrEmpty(text) && text != "0" && int.TryParse(text, out int messageCount) && messageCount > 0)
                {
                    // Giới hạn số tin nhắn hiển thị để tránh quá dài
                    if (messageCount > 99)
                    {
                        text = "99+";
                    }

                    // Kích thước và vị trí của hình tròn nền (badge)
                    // Điều chỉnh badgeDiameter để nó chiếm khoảng 40-50% của targetIconSize, 
                    // và không quá lớn để che icon chính.
                    float badgeDiameter = targetIconSize * 0.45f;
                    // Có thể tăng nhẹ nếu số tin nhắn có nhiều chữ số
                    if (text.Length > 2) badgeDiameter = targetIconSize * 0.5f;

                    // Vị trí badge ở góc dưới bên phải
                    // Điều chỉnh để nó nằm gọn trong góc
                    float badgePadding = targetIconSize * 0.05f; // Khoảng đệm nhỏ từ mép icon
                    float badgeX = targetIconSize - badgeDiameter - badgePadding;
                    float badgeY = targetIconSize - badgeDiameter - badgePadding;

                    using (SolidBrush backgroundBrush = new SolidBrush(Color.Red)) // Màu nền đỏ cho số
                    {
                        // Vẽ hình tròn nền
                        graphics.FillEllipse(backgroundBrush, badgeX, badgeY, badgeDiameter, badgeDiameter);

                        // Vẽ viền trắng xung quanh hình tròn để làm nổi bật (tùy chọn, có thể bỏ nếu không thích)
                        float borderThickness = 1.5f; // Độ dày của viền
                        using (Pen borderPen = new Pen(Color.White, borderThickness))
                        {
                            graphics.DrawEllipse(borderPen, badgeX, badgeY, badgeDiameter, badgeDiameter);
                        }
                    }

                    // Thiết lập font và màu cho số
                    // Điều chỉnh kích thước font để vừa với kích thước badge
                    float fontSize = badgeDiameter * 0.5f; // Kích thước font, khoảng 50% đường kính badge
                    if (text.Length > 2) fontSize = badgeDiameter * 0.4f; // Giảm nếu số có nhiều chữ số (e.g., "99+")

                    using (Font font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel)) // Font chữ hiện đại hơn
                    {
                        using (SolidBrush textBrush = new SolidBrush(Color.White)) // Màu chữ trắng
                        {
                            StringFormat sf = new StringFormat
                            {
                                Alignment = StringAlignment.Center,      // Căn giữa theo chiều ngang
                                LineAlignment = StringAlignment.Center   // Căn giữa theo chiều dọc
                            };

                            // Vùng để vẽ văn bản (ở giữa hình tròn nền)
                            RectangleF textRect = new RectangleF(badgeX, badgeY, badgeDiameter, badgeDiameter);
                            graphics.DrawString(text, font, textBrush, textRect, sf);
                        }
                    }
                }
                // --- Kết thúc vẽ "badge" số tin nhắn ---

                // Chuyển Bitmap đã vẽ thành Icon
                using (MemoryStream ms = new MemoryStream())
                {
                    // Lưu bitmap dưới dạng PNG để giữ được kênh alpha (độ trong suốt)
                    bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Seek(0, SeekOrigin.Begin);
                    newIcon = new Icon(ms); // Tạo Icon từ MemoryStream
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Lỗi khi tạo icon với văn bản: {ex.Message}");
                return baseIcon; // Trả về icon gốc nếu có lỗi
            }
            finally
            {
                graphics?.Dispose();
                bitmap?.Dispose();
                baseBitmapScaled?.Dispose(); // Rất quan trọng: giải phóng bitmap gốc đã được scale
            }
            return newIcon;
        }
        private void UpdateTrayIcon()
        {
            if (_trayIcon == null) return;

            string baseText = "Messenger";
            Icon currentIcon = null;

            if (_unreadMessageCount > 0)
            {
                _trayIcon.Text = $"{baseText} ({_unreadMessageCount} tin nhắn mới)";
                // Tạo icon mới với số tin nhắn
                currentIcon = CreateTextIcon(_unreadMessageCount.ToString(), this.Icon);
            }
            else
            {
                _trayIcon.Text = baseText;
                // Sử dụng icon mặc định của form
                currentIcon = this.Icon;
            }

            // Giải phóng icon cũ trước khi gán icon mới để tránh rò rỉ bộ nhớ
            if (_trayIcon.Icon != null && _trayIcon.Icon != this.Icon) // Chỉ giải phóng nếu đó là icon động đã tạo
            {
                _trayIcon.Icon.Dispose();
            }

            _trayIcon.Icon = currentIcon;
        }
    }
}