using System;
using System.Windows.Forms;

namespace Messenger
{
    // Định nghĩa một partial class (lớp không hoàn chỉnh) cho form đổi tên.
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
            // Thêm sự kiện TextChanged để kiểm tra theo thời gian thực
            txtNewName.TextChanged += TxtNewName_TextChanged;
        }
        // Xử lý sự kiện TextChanged
        private void TxtNewName_TextChanged(object sender, EventArgs e)
        {
            if (txtNewName.Text.Length > 15)
            {
                errorProvider.SetError(txtNewName, "Tên không được dài quá 15 ký tự.");
                btnOK.Enabled = false; // Vô hiệu hóa nút OK
            }
            else
            {
                errorProvider.SetError(txtNewName, ""); // Xóa thông báo lỗi
                btnOK.Enabled = !string.IsNullOrWhiteSpace(txtNewName.Text); // Chỉ bật OK nếu có nội dung
            }
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
        // Override phương thức Dispose để giải phóng ErrorProvider
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
                errorProvider.Dispose(); // Giải phóng ErrorProvider
            }
            base.Dispose(disposing);
        }
        // Sự kiện được gọi khi form RenameForm được load (hiển thị).
        private void RenameForm_Load(object sender, EventArgs e)
        {
        }
    }
}