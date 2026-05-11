# Unity Find Similar Texture2D

Một công cụ mạnh mẽ và thông minh dành cho Unity Editor, giúp bạn tìm kiếm các Texture2D tương tự trong dự án giống như tính năng "Find Image" của Google. Công cụ này cực kỳ hữu ích để dọn dẹp tài nguyên trùng lặp hoặc tìm kiếm assets dựa trên ảnh tham chiếu bên ngoài.

![License](https://img.shields.io/badge/Unity-2021.3+-blue.svg)
![Odin](https://img.shields.io/badge/Requirement-Odin_Inspector-red.svg)

---

## ✨ Tính năng nổi bật

- **🔍 Tìm kiếm đa năng:** Hỗ trợ tìm kiếm các asset `Texture2D` có nội dung tương đồng với ảnh đầu vào.
- **🖼️ Hai chế độ đầu vào (Input Modes):**
    - **Project Asset:** Kéo thả trực tiếp Texture từ cửa sổ Project.
    - **External File:** Sử dụng ảnh từ bên ngoài dự án (Browse file, Paste đường dẫn).
- **📋 Hỗ trợ Clipboard thông minh:** 
    - **Ctrl + V:** Dán trực tiếp ảnh từ Clipboard (Ví dụ: Ảnh chụp màn hình `Win + Shift + S` hoặc "Copy Image" từ trình duyệt).
    - Hỗ trợ kéo thả ảnh (Drag & Drop) trực tiếp từ File Explorer vào Tool.
- **🧠 Thuật toán (Pure C#):** Kết hợp 3 phương pháp để đưa ra kết quả chính xác nhất:
    - **pHash (Perceptual Hash):** Phân tích cấu trúc và tần số ảnh (bất biến với thay đổi màu sắc nhẹ).
    - **HSV Histogram:** Phân tích phân phối màu sắc theo cảm nhận mắt người (Alpha-aware).
    - **SSIM (Structural Similarity):** So sánh độ tương đồng về mặt cấu trúc pixel.
- **⚙️ Tùy chỉnh linh hoạt:** Điều chỉnh trọng số (Weights) của từng thuật toán để ưu tiên tìm theo Màu sắc hay Cấu trúc.

---

## 📦 Cài đặt qua Git URL

Để cài đặt tool này vào dự án của bạn qua Unity Package Manager:

1. Mở Unity dự án của bạn.
2. Vào **Window > Package Manager**.
3. Nhấp vào nút **+** ở góc trên bên trái và chọn **Add package from git URL...**.
4. Nhập URL của kho lưu trữ này:
   https://github.com/thanhthai18/FindSameTexture2D.git?path=/Assets/FindSameTexture2D/#1.0.0

---

## 🛠 Yêu cầu hệ thống

- **Unity:** Phiên bản 2021.3 trở lên.
- **Plugin:** [Odin Inspector](https://odininspector.com/) (Bắt buộc để hiển thị giao diện tool).

---

## 🚀 Hướng dẫn sử dụng

1. Truy cập menu: **Tools > Zitga > Find Similar Texture2D**.
2. Chọn **Nguồn ảnh tham chiếu**:
    - Nếu chọn **Project Asset**: Kéo Texture2D từ project vào.
    - Nếu chọn **External File**: Bạn có thể bấm **Browse**, **Paste Path**, hoặc đơn giản là nhấn **Ctrl + V** nếu đã copy một tấm ảnh nào đó.
3. (Tùy chọn) Điều chỉnh **Trọng số & Cài đặt** để lọc kết quả chính xác hơn.
4. Bấm nút **🔍 SEARCH**.
5. Kết quả sẽ hiển thị danh sách các ảnh tương đồng kèm theo điểm số (Similarity Score). Bạn có thể bấm **Ping** để tìm nhanh vị trí file trong Project.

---

## 📝 Thuật toán & Kỹ thuật

Công cụ sử dụng các kỹ thuật xử lý ảnh nâng cao được viết hoàn toàn bằng C#:
- **DCT (Discrete Cosine Transform):** Chuyển đổi ảnh sang miền tần số để trích xuất đặc trưng cấu trúc (pHash).
- **HSV Space:** Chuyển đổi RGB sang không gian màu HSV để so sánh màu sắc chính xác hơn với thị giác con người.
- **Bhattacharyya Coefficient:** Dùng để so sánh độ tương đồng giữa hai biểu đồ màu (Histogram).

---

## ⚖️ Copyright

Copyright © 2026 **[3HP Zitga]**. All rights reserved.
