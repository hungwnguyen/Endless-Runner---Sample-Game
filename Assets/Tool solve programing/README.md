# Tool Solve Programming

Mở tool trong Unity bằng menu:

`Tools > Solve Programming > Test Hoc Sinh`

## Cách học sinh làm bài

1. Bấm `Tạo code mẫu` trong tool.
2. Mở file `Assets/Tool solve programing/StudentCode/BaiLamHocSinh.cs`.
3. Chỉ viết code bên trong hàm `public static object Main(List<object> input)`.
4. Lấy dữ liệu từ `input[0]`, `input[1]`, ...
5. Trả kết quả bằng `return`.
6. Bấm `Test`.
7. Màu xanh là pass, màu đỏ là chưa pass.

## Cách giáo viên tạo bài mới

1. Sao chép hoặc sửa file `Assets/Tool solve programing/Exercises/bai_tap_mau.json`.
2. Với bài test bằng `Main`, đặt `testMode` là `main`.
3. Mỗi test case cần có:
   - `inputTypes`: kiểu dữ liệu cho từng phần tử trong `List<object>`.
   - `expectedType`: kiểu dữ liệu của kết quả trả về.
   - `inputValues`: giá trị tool sẽ convert và truyền vào `List<object>`.
   - `expectedValue`: kết quả mong đợi.

Kiểu dữ liệu đang hỗ trợ: `int`, `float`, `double`, `bool`, `string`.

Học sinh không cần sửa file JSON hoặc file trong thư mục `Editor`.
