using System;
using System.Drawing;
using System.Windows.Forms;

namespace Messenger
{
    public class CustomBalloonForm : Form
    {
        private Timer _timer;
        private Label _titleLabel;
        private Label _messageLabel;
        private PictureBox _iconPictureBox;
        private Action _onClick;
        private int _screenBottom; // Vị trí đáy màn hình

        public CustomBalloonForm(string title, string message, Icon icon, int timeout, Action onClick)
        {
            InitializeComponents(); // Gọi phương thức khởi tạo các component
            _screenBottom = Screen.PrimaryScreen.WorkingArea.Bottom;
            this.Location = new Point(Screen.PrimaryScreen.WorkingArea.Width - this.Width - 10, _screenBottom);
            this.Opacity = 0.0; // Bắt đầu với độ trong suốt
            this.timeout = timeout;
            _onClick = onClick;
            SetEventHandlers();
        }

        private void InitializeComponents()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.White;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.Manual;
            this.Size = new Size(300, 80);
            this.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, this.Width, this.Height, 10, 10));

            _iconPictureBox = new PictureBox
            {
                Size = new Size(32, 32),
                Location = new Point(10, 10),
                SizeMode = PictureBoxSizeMode.StretchImage
            };
            this.Controls.Add(_iconPictureBox);

            _titleLabel = new Label
            {
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(50, 10),
                AutoSize = true
            };
            this.Controls.Add(_titleLabel);

            _messageLabel = new Label
            {
                Font = new Font("Segoe UI", 9),
                Location = new Point(50, 30),
                Size = new Size(240, 60),
                AutoEllipsis = true
            };
            this.Controls.Add(_messageLabel);
        }

        private void SetEventHandlers()
        {
            this.Click += OnFormClicked;
            _titleLabel.Click += OnFormClicked;
            _messageLabel.Click += OnFormClicked;
            _iconPictureBox.Click += OnFormClicked;
        }

        private int timeout;

        public void ShowFromBottom(string title, string message, Icon icon)
        {
            _titleLabel.Text = title;
            _messageLabel.Text = message;
            if (icon != null)
            {
                _iconPictureBox.Image = icon.ToBitmap();
            }

            this.Show();
            this.Top = _screenBottom;
            Timer showTimer = new Timer { Interval = 35 };
            showTimer.Tick += (sender, args) =>
            {
                if (this.Top > _screenBottom - this.Height - 10)
                {
                    this.Top -= 5;
                    this.Opacity += 0.05;
                }
                else
                {
                    this.Top = _screenBottom - this.Height - 10;
                    this.Opacity = 0.95;
                    showTimer.Stop();
                    StartCloseTimer();
                }
            };
            showTimer.Start();
        }

        private void StartCloseTimer()
        {
            _timer = new Timer { Interval = timeout };
            _timer.Tick += (sender, args) =>
            {
                _timer.Stop();
                HideToBottom();
            };
            _timer.Start();
        }

        private void HideToBottom()
        {
            Timer hideTimer = new Timer { Interval = 35 };
            hideTimer.Tick += (sender, args) =>
            {
                if (this.Top < _screenBottom)
                {
                    this.Top += 5;
                    this.Opacity -= 0.05;
                }
                else
                {
                    hideTimer.Stop();
                    this.Hide();
                    this.Dispose();
                }
            };
            hideTimer.Start();
        }

        private void OnFormClicked(object sender, EventArgs e)
        {
            _onClick?.Invoke();
            HideToBottom();
        }


        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

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