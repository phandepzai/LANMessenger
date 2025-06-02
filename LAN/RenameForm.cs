using System;
using System.Windows.Forms;

namespace Messenger
{
    // Định nghĩa một partial class (lớp không hoàn chỉnh) cho form đổi tên.
    // Partial class cho phép chia định nghĩa của một lớp thành nhiều file.
    public partial class RenameForm : Form
    {
        // Thuộc tính công khai chỉ đọc để lấy tên người dùng mới sau khi form được đóng với kết quả OK.
        public string NewUserName { get; private set; }

        // Constructor của form đổi tên, nhận vào tên người dùng hiện tại làm tham số.
        public RenameForm(string currentUserName)
        {
            // Gọi phương thức InitializeComponent() được tạo bởi trình thiết kế form để khởi tạo các control trên form.
            InitializeComponent();
            // Đặt giá trị ban đầu cho textbox txtNewName bằng tên người dùng hiện tại.
            txtNewName.Text = currentUserName;
            // Chọn toàn bộ văn bản trong textbox để người dùng có thể dễ dàng chỉnh sửa.
            txtNewName.SelectAll();
        }

        // Sự kiện được gọi khi người dùng click vào nút OK.
        private void BtnOK_Click(object sender, EventArgs e)
        {
            // Debug: Ghi log sự kiện click nút OK vào cửa sổ Output (trong Visual Studio).
            System.Diagnostics.Debug.WriteLine("BtnOK_Click triggered");

            // Lấy văn bản từ textbox txtNewName và loại bỏ khoảng trắng ở đầu và cuối chuỗi.
            string input = txtNewName.Text.Trim();
            // Kiểm tra xem chuỗi nhập vào có phải là null, rỗng hoặc chỉ chứa khoảng trắng hay không.
            if (string.IsNullOrWhiteSpace(input))
            {
                // Hiển thị hộp thoại thông báo lỗi nếu tên người dùng không hợp lệ.
                MessageBox.Show("Tên người dùng không được để trống.", "Lỗi",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                // Đặt focus trở lại textbox để người dùng có thể nhập lại.
                txtNewName.Focus();
                // Thoát khỏi phương thức xử lý sự kiện, ngăn không cho form đóng.
                return;
            }

            // Gán tên người dùng đã nhập (sau khi trim) cho thuộc tính NewUserName.
            NewUserName = input;
            // Đặt kết quả trả về của hộp thoại thành OK, cho biết người dùng đã xác nhận thay đổi.
            DialogResult = DialogResult.OK;
            // Lưu ý: Không cần gọi Close() một cách явно; thuộc tính DialogResult sẽ tự động đóng form.
        }

        // Sự kiện được gọi khi người dùng click vào nút Cancel.
        private void BtnCancel_Click(object sender, EventArgs e)
        {
            // Debug: Ghi log sự kiện click nút Cancel vào cửa sổ Output.
            System.Diagnostics.Debug.WriteLine("BtnCancel_Click triggered");

            // Đặt kết quả trả về của hộp thoại thành Cancel, cho biết người dùng đã hủy bỏ việc đổi tên.
            DialogResult = DialogResult.Cancel;
            // Lưu ý: Không cần gọi Close() một cách явно; thuộc tính DialogResult sẽ tự động đóng form.
        }

        // Sự kiện được gọi khi form RenameForm được load (hiển thị).
        private void RenameForm_Load(object sender, EventArgs e)
        {
            // Hiện tại không có logic đặc biệt nào được thực hiện khi form được load.
        }
    }
}