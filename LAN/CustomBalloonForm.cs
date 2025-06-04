using System;
using System.Drawing;
using System.Windows.Forms;

namespace Messenger
{
    public class CustomBalloonForm : Form
    {
        private Timer _timer; // Timer để tự động ẩn form
        private Label _titleLabel; // Label hiển thị tiêu đề
        private Label _messageLabel; // Label hiển thị nội dung tin nhắn
        private PictureBox _iconPictureBox; // PictureBox hiển thị icon
        private Action _onClick; // Action được gọi khi form được click
        private int _screenBottom; // Vị trí đáy màn hình

        // Hàm khởi tạo của CustomBalloonForm
        public CustomBalloonForm(string title, string message, Icon icon, int timeout, Action onClick)
        {
            InitializeComponents(); // Gọi phương thức khởi tạo các component
            _screenBottom = Screen.PrimaryScreen.WorkingArea.Bottom; // Lấy vị trí đáy màn hình
            this.Location = new Point(Screen.PrimaryScreen.WorkingArea.Width - this.Width - 10, _screenBottom); // Đặt vị trí ban đầu của form ở góc dưới bên phải màn hình
            this.Opacity = 0.0; // Bắt đầu với độ trong suốt (form trong suốt)
            this.timeout = timeout; // Gán thời gian chờ tự động ẩn
            _onClick = onClick; // Gán action khi click
            SetEventHandlers(); // Thiết lập các event handler
        }

        // Khởi tạo và cấu hình các component trên form
        private void InitializeComponents()
        {
            this.FormBorderStyle = FormBorderStyle.None; // Loại bỏ viền form
            this.BackColor = Color.White; // Đặt màu nền trắng
            this.ShowInTaskbar = false; // Không hiển thị trên thanh taskbar
            this.TopMost = true; // Luôn hiển thị trên cùng
            this.StartPosition = FormStartPosition.Manual; // Thiết lập vị trí thủ công
            this.Size = new Size(300, 85); // Kích thước form
            this.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, this.Width, this.Height, 18, 18)); // Tạo góc bo tròn cho form

            // Khởi tạo và cấu hình PictureBox cho icon
            _iconPictureBox = new PictureBox
            {
                Size = new Size(32, 32),
                Location = new Point(10, 10),
                SizeMode = PictureBoxSizeMode.StretchImage // Chế độ hiển thị ảnh
            };
            this.Controls.Add(_iconPictureBox); // Thêm PictureBox vào form

            // Khởi tạo và cấu hình Label cho tiêu đề
            _titleLabel = new Label
            {
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(50, 10),
                AutoSize = true
            };
            this.Controls.Add(_titleLabel); // Thêm Label tiêu đề vào form

            // Khởi tạo và cấu hình Label cho nội dung tin nhắn
            _messageLabel = new Label
            {
                Font = new Font("Segoe UI", 9),
                Location = new Point(50, 30),
                Size = new Size(240, 60),
                AutoEllipsis = true // Nếu nội dung dài quá sẽ hiển thị dấu ...
            };
            this.Controls.Add(_messageLabel); // Thêm Label nội dung vào form
        }

        // Thiết lập các event handler cho form và các component
        private void SetEventHandlers()
        {
            this.Click += OnFormClicked;
            _titleLabel.Click += OnFormClicked;
            _messageLabel.Click += OnFormClicked;
            _iconPictureBox.Click += OnFormClicked;
        }

        private int timeout; // Thời gian chờ trước khi form tự động ẩn

        // Hiển thị form từ dưới lên
        public void ShowFromBottom(string title, string message, Icon icon)
        {
            _titleLabel.Text = title; // Đặt tiêu đề
            _messageLabel.Text = message; // Đặt nội dung tin nhắn
            if (icon != null)
            {
                _iconPictureBox.Image = icon.ToBitmap(); // Đặt icon
            }

            this.Show(); // Hiển thị form
            this.Top = _screenBottom; // Đặt vị trí ban đầu ở đáy màn hình
            Timer showTimer = new Timer { Interval = 35 }; // Timer để tạo hiệu ứng hiển thị
            showTimer.Tick += (sender, args) =>
            {
                // Di chuyển form từ dưới lên
                if (this.Top > _screenBottom - this.Height - 10)
                {
                    this.Top -= 5;
                    this.Opacity += 0.05; // Tăng độ trong suốt (hiển thị rõ hơn)
                }
                else
                {
                    this.Top = _screenBottom - this.Height - 10; // Đặt vị trí cuối cùng
                    this.Opacity = 0.95; // Đặt độ trong suốt cuối cùng
                    showTimer.Stop(); // Dừng timer
                    StartCloseTimer(); // Bắt đầu timer tự động ẩn
                }
            };
            showTimer.Start(); // Bắt đầu timer hiển thị
        }

        // Bắt đầu timer để tự động ẩn form sau một khoảng thời gian
        private void StartCloseTimer()
        {
            _timer = new Timer { Interval = timeout }; // Timer để tự động đóng
            _timer.Tick += (sender, args) =>
            {
                _timer.Stop(); // Dừng timer
                HideToBottom(); // Ẩn form
            };
            _timer.Start(); // Bắt đầu timer tự động đóng
        }

        // Ẩn form xuống dưới
        private void HideToBottom()
        {
            Timer hideTimer = new Timer { Interval = 35 }; // Timer để tạo hiệu ứng ẩn
            hideTimer.Tick += (sender, args) =>
            {
                // Di chuyển form xuống dưới
                if (this.Top < _screenBottom)
                {
                    this.Top += 5;
                    this.Opacity -= 0.05; // Giảm độ trong suốt (ẩn dần)
                }
                else
                {
                    hideTimer.Stop(); // Dừng timer
                    this.Hide(); // Ẩn form
                    this.Dispose(); // Giải phóng tài nguyên
                }
            };
            hideTimer.Start(); // Bắt đầu timer ẩn
        }

        // Xử lý sự kiện click vào form
        private void OnFormClicked(object sender, EventArgs e)
        {
            _onClick?.Invoke(); // Gọi action được truyền vào
            HideToBottom(); // Ẩn form
        }

        // Import hàm từ thư viện gdi32.dll để tạo vùng có góc bo tròn
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        // Giải phóng tài nguyên khi form bị hủy
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer?.Dispose();
                _iconPictureBox?.Dispose();
                _titleLabel?.Dispose();
                _messageLabel?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}