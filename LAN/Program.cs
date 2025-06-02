using System;
using System.Threading; // Thêm namespace này
using System.Windows.Forms;

namespace Messenger
{
    static class Program
    {
        // Tên duy nhất cho Mutex của ứng dụng
        private static Mutex _mutex = null;
        private const string AppMutexName = "MessengerSingleInstanceMutex";

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Kiểm tra xem đã có phiên bản nào của ứng dụng đang chạy chưa
            bool createdNew;
            _mutex = new Mutex(true, AppMutexName, out createdNew);

            if (!createdNew)
            {
                // Đã có một phiên bản khác đang chạy
                MessageBox.Show("Ứng dụng Messenger đã được mở. Chỉ có thể chạy một phiên bản.", "LAN Messenger", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return; // Thoát ứng dụng
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm()); // Đảm bảo khởi chạy MainForm

            // Giải phóng Mutex khi ứng dụng thoát
            _mutex.ReleaseMutex();
        }
    }
}