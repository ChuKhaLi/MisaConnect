> **Attribution.** This document is MISA JSC's MeInvoice integration guide,
> reproduced in this repository (downloaded as Markdown from MISA's published
> Google Doc) as a wire-format reference for contributors — per Constitution
> Principle IV, DTOs must mirror MISA's published shapes. All copyright
> remains with MISA JSC. The MisaConnect project does not claim ownership of
> this content; it is included for developer reference only and is not
> covered by the project's MIT license.
>
> **Original source:** [Tài liệu API Tạo hóa đơn - V2 (Google Docs)](https://docs.google.com/document/d/1MpjpEt6huuc_w-Y1iUZLc1jXcBZsGhwXwbL0a5uKZHY/edit?tab=t.0#heading=h.nmjgclh47jnm) — always defer to the upstream document if it diverges from this snapshot.
>
> If you are MISA JSC and would prefer a link instead of a snapshot, please
> open an issue.

---

# **Tài liệu hướng dẫn tích hợp API MISA MeInvoice (Tạo hóa đơn từ ERP)**

[**1\. Giới thiệu	2**](#giới-thiệu)

[1.1. Thông tin cần trước khi kết nối	2](#thông-tin-cần-trước-khi-kết-nối)

[1.2. Thông tin chi tiết để kết nối	2](#thông-tin-chi-tiết-để-kết-nối)

[1.3. Mô tả cấu trúc của API MeInvoice	3](#mô-tả-cấu-trúc-của-api-meinvoice)

[1.4. Hướng dẫn xử lý kết quả của API (Response)	3](#hướng-dẫn-xử-lý-kết-quả-của-api-\(response\))

[1.5. Các bước thực hiện	4](#các-bước-thực-hiện)

[1.6. Những lưu ý trước khi tích hợp (Hiểu thêm về hóa đơn và API)	5](#những-lưu-ý-trước-khi-tích-hợp-\(hiểu-thêm-về-hóa-đơn-và-api\))

[**2\. API lấy mã kết nối (Get token)	6**](#api-lấy-mã-kết-nối-\(get-token\))

[**3\. API lấy danh sách mẫu hóa đơn	7**](#api-lấy-danh-sách-mẫu-hóa-đơn)

[3.1. Lấy danh sách mẫu hóa đơn	7](#lấy-danh-sách-mẫu-hóa-đơn)

[3.2. Lấy danh sách mẫu vé điện tử	8](#lấy-danh-sách-mẫu-vé-điện-tử)

[**4\. API xem trước hóa đơn	9**](#api-xem-trước-hóa-đơn)

[4.1. Xem hóa đơn qua dữ liệu (json data)	9](#xem-hóa-đơn-qua-dữ-liệu-\(json-data\))

[4.2. Xem hóa đơn đã tạo (qua RefID)	10](#xem-hóa-đơn-đã-tạo-\(qua-refid\))

[**5\. API Tạo hóa đơn chưa phát hành	11**](#api-tạo-hóa-đơn-chưa-phát-hành)

[**6\. API xóa hóa đơn chưa phát hành	12**](#api-xóa-hóa-đơn-chưa-phát-hành)

[**7\. Lấy thông tin hóa đơn	13**](#lấy-thông-tin-hóa-đơn)

[7.1. Lấy thông tin qua RefID	13](#lấy-thông-tin-qua-refid)

[7.2. Lấy thông tin qua phân trang \- Hóa đơn thường	14](#lấy-thông-tin-qua-phân-trang---hóa-đơn-thường)

[7.3. Lấy thông tin qua phân trang \- Hóa đơn qua Máy tính tiền	15](#lấy-thông-tin-qua-phân-trang---hóa-đơn-qua-máy-tính-tiền)

[**8\. Mô tả đối tượng của hóa đơn	16**](#mô-tả-đối-tượng-của-hóa-đơn)

[8.1. Template (mẫu hóa đơn)	16](#template-\(mẫu-hóa-đơn\))

[8.2. Ticket (mẫu vé)	16](#ticket-\(mẫu-vé\))

[8.3. InvoiceData (Dữ liệu hóa đơn)	17](#invoicedata-\(dữ-liệu-hóa-đơn\))

[8.4. InvoiceDetail (dữ liệu dòng hàng hóa, dịch vụ)	19](#invoicedetail-\(dữ-liệu-dòng-hàng-hóa,-dịch-vụ\))

[**9\. Công thức tính toán trên hóa đơn	21**](#công-thức-tính-toán-trên-hóa-đơn)

[9.1. Công thức trên Detail	21](#công-thức-trên-detail)

[9.2. Công thức trên Master (dựa trên dữ liệu của dòng hàng hóa)	22](#công-thức-trên-master-\(dựa-trên-dữ-liệu-của-dòng-hàng-hóa\))

[**10\. Mã lỗi thường gặp	23**](#mã-lỗi-thường-gặp)

1. # **Giới thiệu** {#giới-thiệu}

Tài liệu này dành cho các nhà phát triển ứng dụng muốn ứng dụng của mình có thể phát hành hóa đơn thông qua việc kết nối với hệ thống phần mềm **MISA MeInvoice** . Tài liệu sẽ mô tả phương thức kết nối giữa các ứng dụng từ Client (máy chủ/máy trạm của Khách hàng) tới service của hệ thống MISA MeInvoice.

**Khi thực hiện phương án kết nối này, từ ERP thực hiện tạo thành công hóa đơn qua API thì end user sẽ thực hiện các nghiệp vụ còn lại của hóa đơn trên hệ thống Webapp của MISA.**

1. ## Thông tin cần trước khi kết nối {#thông-tin-cần-trước-khi-kết-nối}

| Thông tin | Diễn giải |
| ----- | ----- |
| Tài khoản MeInvoice | Là tài khoản đăng nhập webapp [app3.meinvoice.vn](http://app3.meinvoice.vn) (gồm mst, user, pass) |
| Ký hiệu hóa đơn | Lấy thông tin từ bộ phận kế toán của đơn vị |
| Thông tin sandbox | Sẽ được cấp khi KH đăng ký sử dụng |

## 

   2. ## Thông tin chi tiết để kết nối {#thông-tin-chi-tiết-để-kết-nối}

| Thông tin | Diễn giải |
| ----- | ----- |
| API\_AIO\_Url (Base url) | SandBox: [https://testapi.meinvoice.vn/api/integration](https://testapi.meinvoice.vn/api/integration) Product: https://api.meinvoice.vn/api/integration |
| Token url (lấy token) | /webapp/token |
| Lấy danh sách mẫu hóa đơn có thể phát hành | /webapp/templates |
| Lấy danh sách mẫu vé có thể phát hành | /webapp/templates/ticket |
| Tạo hóa đơn chưa phát hành | /webapp/insert |
| Xóa hóa đơn chưa phát hành | /webapp/delete |
| Lấy trạng thái hóa đơn theo RefID | /webapp/getlist |
| Lấy trạng thái hóa đơn thường phân trang | /webapp/paging |
| Lấy trạng thái hóa đơn Máy tính tiền phân trang | /webapp/paging/calculating |

      

   3. ## Mô tả cấu trúc của API MeInvoice {#mô-tả-cấu-trúc-của-api-meinvoice}

| Thông tin | Diễn giải |
| ----- | ----- |
| Url | Theo từng API cụ thể |
| Method | Get/Post (Theo từng API cụ thể) |
| Params | Theo từng API cụ thể |
| Header | Taxcode: {token} Authorization: Bearer {token} |
| Body | Content-Type: application/json (Đối tượng theo từng API cụ thể) |
| Response | {     "success":true/false,     "errorCode": "Mã lỗi",     "descriptionErrorCode": "Mô tả lỗi",     "errors": "Danh sách mô tả lỗi",     "data": "Dữ liệu phản hồi"     "customData": "thông tin khác" } |

   4. ## Hướng dẫn xử lý kết quả của API (Response) {#hướng-dẫn-xử-lý-kết-quả-của-api-(response)}

| success | Trạng thái phản hồi của API True: Thành công False: Thất bại |
| :---- | :---- |
| errorCode | Mã lỗi trả về (VD: InvalidInvoiceData) |
| data | Kết quả trả về của mỗi API |
| error | Kết quả trả về của mỗi API (khi có lỗi) |
| Cách kiểm tra Response | Kiểm tra success-\> kiểm tra errorCode→ xử lý lỗi \-\> Kiểm tra error.errorCode \-\> xử lý lỗi \=\> lưu log response |
| Các thông tin cần lưu lại | Token: hạn 14 ngày Body của API Response của API để phục vụ việc hỗ trợ về sau nếu phát sinh |

      ## 

   5. ## Các bước thực hiện {#các-bước-thực-hiện}

| Đăng ký sử dụng dịch vụ  |  |
| :---- | :---- |
| Đăng ký sử dụng API | Đăng ký sử dụng API, liên hệ NVKD của MISA Nhận về app\_id (dùng cho cả sandbox và product) và thông tin sandbox gồm Mã số thuế Tài khoản MeInvoice Mật khẩu MeInvoice Khi thực hiện golive, lấy 3 thông tin trên từ bộ phận kế toán |
| Thực hiện lấy đăng nhập ứng dụng MeInvoice | Sandbox: [https://testapp3.meinvoice.vn/v3/ban-lam-viec](https://testapp3.meinvoice.vn/v3/ban-lam-viec) Product: [https://app3.meinvoice.vn/v3/ban-lam-viec](https://app3.meinvoice.vn/v3/ban-lam-viec) [Xem thêm](https://helpv4.meinvoice.vn/ac/meinvoice-web/) thiết lập hình thức hóa đơn sử dụng |
| Thiết lập hình thức ký số hóa đơn | \- Thiết lập [tại đây](https://testapp3.meinvoice.vn/V3/he-thong/ky-so) \- [Xem hướng dẫn](https://helpv4.meinvoice.vn/kb/r10_thiet_lap_ky_so/) |
| Lập tờ khai | [Xem tại đây](https://helpv4.meinvoice.vn/kb/nghiep-vu-phan-he-lap-to-khai/) |
| Tạo mẫu hóa đơn | [Xem tại đây](https://helpv4.meinvoice.vn/kb/nghiep-vu-phan-he-mau-hoa-don/) |
| **Thao thác với API** |  |
| Bước 1: Thực hiện gọi API lấy token | \- [Xem mô tả](#api-lấy-mã-kết-nối-\(get-token\)) **Lưu ý:** \- Token có hạn 14 ngày, chỉ nên gọi ở đầu phiên làm việc (lần đầu đăng nhập, đầu ngày, đầu tuần …) |
| Bước 2: Thực hiện lấy danh sách mẫu hóa đơn | \- [Xem mô tả](#api-lấy-danh-sách-mẫu-hóa-đơn) |
| Bước 3: Xem hóa đơn trước khi phát hành | \- [Xem mô tả](#api-xem-trước-hóa-đơn) |
| Bước 4: Tạo hóa đơn | \- [Xem mô tả](#api-tạo-hóa-đơn-chưa-phát-hành) **Lưu ý:** \- Mẫu hóa đơn phải đang ở trạng thái **Sử dụng** \- Tờ khai phải đang ở trạng thái **CQT chấp nhận** |
| **Xử lý với các nghiệp vụ khác nếu cần** |  |
| Xóa hóa đơn chưa phát hành | \- [Xem hướng dẫn](#api-xóa-hóa-đơn-chưa-phát-hành) |
| Xem hóa đơn đã tạo | \- [Xem hướng dẫn](#xem-hóa-đơn-đã-tạo-\(qua-refid\)) |
| Lấy thông tin hóa đơn | \- [Xem hướng dẫn](#lấy-thông-tin-hóa-đơn) |

      ## 

   6. ## Những lưu ý trước khi tích hợp (Hiểu thêm về hóa đơn và API) {#những-lưu-ý-trước-khi-tích-hợp-(hiểu-thêm-về-hóa-đơn-và-api)}

| Thông tin | Diễn giải |
| ----- | ----- |
| Công thức tính toán | Tham khảo sheet **Công thức (từ tài liệu được cung cấp hoặc bảng mô tả đối tượng)** |
| Ký hiệu hóa đơn | \- Sẽ thay đổi theo năm. VD năm 2024, ký hiệu là 1C**24**MYY thì sang năm 2025, ký hiệu sẽ đổi thành 1C**25**MYY (hệ thống MISA tự động xử lý theo thông tin InvDate) |
| Xem hóa đơn đã tạo | \- Môi trường test: [https://testapp3.meinvoice.vn/](https://testapp3.meinvoice.vn/) \- Môi trường product: [https://app3.meinvoice.vn](https://app3.meinvoice.vn) (Menu Hóa đơn) |
| Xác định loại hình hóa đơn | Dùng ký tự đầu tiên của ký hiệu để xác định: **X**C25TYY X \= 1: hóa đơn GTGT X \= 2: hóa đơn bán hàng X \= 5: vé điện tử X \= 6: phiếu xuất kho VD: 1C25TYY (hóa đơn GTGT) |
| Xác định loại hóa đơn **Có mã/Không mã** | Dùng ký tự thứ 2 của ký hiệu để xác định: 1**X**25MYY X \= C: hóa đơn có mã X \= K: hóa đơn không mã VD: 1C25MYY (hóa đơn có mã) |
| Xác định hình thức hóa đơn **Thường/ HĐ từ máy tính tiền** | Dùng ký tự thứ 5 của ký hiệu để xác định: 1C25**X**YY X \= T: hóa đơn thường X \= M: hóa đơn từ máy tính tiền VD: 1C25MYY (hóa đơn từ máy tính tiền) |
| Postman tham khảo | [Tải về](https://drive.usercontent.google.com/u/0/uc?id=1efU6qSy0n4-Xx7SgxZmqQixqnROz94sX&export=download) |

   # 

2. # **API lấy mã kết nối (Get token)** {#api-lấy-mã-kết-nối-(get-token)}

Sử dụng khi kết nối với ứng dụng MISA MeInvoice để lấy token phục vụ cho các hàm xử lý nghiệp vụ

| Thông tin | Diễn giải |
| ----- | ----- |
| URL | {[API\_AIO\_Url](#thông-tin-chi-tiết-để-kết-nối)}/webapp/token |
| Method | Post |
| Body request | {     "taxcode": "{taxcode}",     "username": "{user meinvoice}",     "password": "{pasword meinvoice}" } |
| Response | {     "success": \<false/true\>,     "data": "**\<Dữ liệu token trả về\>**",     "ErrorCode": "\<Trống hoặc Mã lỗi\>",     "error": "",     "error\_description": "",     "errorCode": "" } Token \= data.access\_token |
| Mã lỗi thường gặp | [Xem tại đây](#mã-lỗi-thường-gặp) |
| Lưu ý | Chỉ gọi Token theo phiên làm việc (đầu ngày, đầu tuần, lần đăng nhập đầu tiên …), tránh việc kết nối liên tục không cần thiết (kiến nghị của MISA là gọi 1 tuần 1 lần, token hạn 14 ngày) |

## 

3. # **API lấy danh sách mẫu hóa đơn** {#api-lấy-danh-sách-mẫu-hóa-đơn}

   1. ## Lấy danh sách mẫu hóa đơn {#lấy-danh-sách-mẫu-hóa-đơn}

| Thông tin | Diễn giải |
| ----- | ----- |
| URL | {[API\_AIO\_Url](#thông-tin-chi-tiết-để-kết-nối)[l](#heading=h.nngox32rchgl)}/webapp/templates |
| Method | Post |
| Params | invoiceWithCode: true/false (dùng loại hóa đơn [có mã hay không mã](#những-lưu-ý-trước-khi-tích-hợp-\(hiểu-thêm-về-hóa-đơn-và-api\))) |
| Body | {     "taxcode": "{taxcode}",     "username": "{user meinvoice}",     "password": "{pasword meinvoice}" } |
| Response | {     "success": \<false/true\>,     "data": "**\[Danh sách [Template](#template-\(mẫu-hóa-đơn\))\]**",     "ErrorCode": \[\],     "error": "\<Trống hoặc Mã lỗi\>",     "error\_description": "",     "errorCode": "" } Lưu lại thông tin ***InvSeries, IPTemplateID*** của mẫu hóa đơn trong **Data** |
| Mã lỗi thường gặp | [Xem tại đây](#mã-lỗi-thường-gặp) |
| Mô tả đối tượng | **Mô tả đối tượng ([Template](#template-\(mẫu-hóa-đơn\)))** |

# 

      

   2. ## Lấy danh sách mẫu vé điện tử {#lấy-danh-sách-mẫu-vé-điện-tử}

| Thông tin | Diễn giải |
| ----- | ----- |
| URL | {[API\_AIO\_Url](#thông-tin-chi-tiết-để-kết-nối)[l](#heading=h.nngox32rchgl)}/webapp/templates/ticket |
| Method | Post |
| Params | invoiceWithCode: true/false (dùng loại hóa đơn [có mã hay không mã](#những-lưu-ý-trước-khi-tích-hợp-\(hiểu-thêm-về-hóa-đơn-và-api\))) start: bắt đầu //mặc định 0 length: số lượng lấy (đối đa 100, mặc định 20\) invoiceCalcu: true/false (mẫu vé từ MTT) |
| Header | Content-Type: application/json Authorization: Bearer {token} TaxCode: {taxcode} |
| Response | {     "success": \<false/true\>,     "data": "**\[Danh sách [Ticket](#ticket-\(mẫu-vé\))\]**",     "ErrorCode": \[\],     "error": "\<Trống hoặc Mã lỗi\>",     "error\_description": "",     "errorCode": "" } Lưu lại thông tin ***InvSeries***  của mẫu hóa đơn trong **Data** |
| Mã lỗi thường gặp | [Xem tại đây](#mã-lỗi-thường-gặp) |
| Mô tả đối tượng | **Mô tả đối tượng ([Template](#template-\(mẫu-hóa-đơn\)))** |

# 

   # 

4. # **API xem trước hóa đơn** {#api-xem-trước-hóa-đơn}

   1. ## Xem hóa đơn qua dữ liệu (json data) {#xem-hóa-đơn-qua-dữ-liệu-(json-data)}

| Thông tin | Diễn giải |
| ----- | ----- |
| URL | {[API\_AIO\_Url](#thông-tin-chi-tiết-để-kết-nối)}/webapp/preview |
| Method | Post |
| Header | Content-Type: application/json Authorization: Bearer {token} TaxCode: {taxcode} |
| Body | {      [**InvoiceData**](#invoicedata-\(dữ-liệu-hóa-đơn\)) } |
| Response | {     "success": true,     "errorCode": null,     "descriptionErrorCode": null,     "error": "",     "data": Kết quả dạng base64string } Kiểm tra có error là chưa thành công |
| Mã lỗi thường gặp | [Xem tại đây](#mã-lỗi-thường-gặp) |
| Mô tả đối tượng | **Mô tả đối tượng ([InvoiceData](#invoicedata-\(dữ-liệu-hóa-đơn\)))** |

      

   2. ## Xem hóa đơn đã tạo (qua RefID) {#xem-hóa-đơn-đã-tạo-(qua-refid)}

| Thông tin | Diễn giải |
| ----- | ----- |
| URL | {[API\_AIO\_Url](#thông-tin-chi-tiết-để-kết-nối)}/webapp/viewrefid |
| Method | Get |
| Params | invoiceWithCode: true/false (dùng loại hóa đơn [có mã hay không mã](#những-lưu-ý-trước-khi-tích-hợp-\(hiểu-thêm-về-hóa-đơn-và-api\))) RefID: mã của hóa đơn đã tạo |
| Response | {     "success": true,     "errorCode": null,     "descriptionErrorCode": null,     "error": "",     "data": Kết quả dạng base64string } Kiểm tra có error là chưa thành công |
| Mã lỗi thường gặp | [Xem tại đây](#mã-lỗi-thường-gặp) |
| Mô tả đối tượng | **Mô tả đối tượng ([InvoiceData](#invoicedata-\(dữ-liệu-hóa-đơn\)))** |

      

5. # **API Tạo hóa đơn chưa phát hành** {#api-tạo-hóa-đơn-chưa-phát-hành}

| Thông tin | Diễn giải |
| ----- | ----- |
| URL | {[API\_AIO\_Url](#thông-tin-chi-tiết-để-kết-nối)}/webapp/insert |
| Method | Post |
| Header | Content-Type: application/json Authorization: Bearer {token} TaxCode: {taxcode} |
| Body | \[      Danh sách [**InvoiceData**](#invoicedata-\(dữ-liệu-hóa-đơn\)) \] |
| Response | {     "success": true,     "errorCode": null,     "descriptionErrorCode": null,     "error": \[Danh sách phản hồi hóa đơn tạo lỗi\],     "data": \[Danh sách phản hồi hóa đơn tạo thành công\] } Kiểm tra có error là chưa thành công |
| Mã lỗi thường gặp | [Xem tại đây](#mã-lỗi-thường-gặp) |
| Mô tả đối tượng | **Mô tả đối tượng ([InvoiceData](#invoicedata-\(dữ-liệu-hóa-đơn\)))** |
| Ví dụ của response lỗi, kiểm tra nội dung **ErrorMessage** cụ thể | **![][image1]** Lỗi request trả về **errorCode** Lỗi hóa đơn sẽ trả về danh sách hóa đơn lỗi ở **error** (kiểm tra nguyên nhân ở ErrorMessage) |

6. # **API xóa hóa đơn chưa phát hành** {#api-xóa-hóa-đơn-chưa-phát-hành}

| Thông tin | Diễn giải |
| ----- | ----- |
| URL | {[API\_AIO\_Url](#thông-tin-chi-tiết-để-kết-nối)}/webapp/delete |
| Method | Delete |
| Params | invoiceWithCode: true/false (dùng loại hóa đơn [có mã hay không mã](#những-lưu-ý-trước-khi-tích-hợp-\(hiểu-thêm-về-hóa-đơn-và-api\))) refid: refID của hóa đơn đã tạo |
| Header | Content-Type: application/json Authorization: Bearer {token} TaxCode: {taxcode} |
| Response | {     "success": true,     "errorCode": null,     "descriptionErrorCode": null,     "errors": \[\],     "data": "",     "customData": null } |
| Mã lỗi thường gặp | [Xem tại đây](#mã-lỗi-thường-gặp) |

   # 

7. # **Lấy thông tin hóa đơn** {#lấy-thông-tin-hóa-đơn}

   1. ## Lấy thông tin qua RefID {#lấy-thông-tin-qua-refid}

| Thông tin | Diễn giải |
| ----- | ----- |
| URL | {[API\_AIO\_Url](#thông-tin-chi-tiết-để-kết-nối)}/webapp/getlist |
| Method | Post |
| Header | TaxCode: {taxcode} Authorization: Bearer {token} |
| Params | invoiceWithCode: true/false (hóa đơn có mã/không mã) |
| Body | \["Danh sách RefID"\] VD: \["RefID1", "RefID2", …\] *RefID1: RefID của hóa đơn, tối đa 50 mã/lần call* |
| Response | {     "success": true,     "errorCode": null,     "descriptionErrorCode": null,     "errors": \[\],     "data": "**List [InvoiceData](#invoicedata-\(dữ-liệu-hóa-đơn\))**",     "customData": null } |
| Mã lỗi thường gặp | [Xem tại đây](#mã-lỗi-thường-gặp) |

# 

2. ## Lấy thông tin qua phân trang \- Hóa đơn thường {#lấy-thông-tin-qua-phân-trang---hóa-đơn-thường}

| Thông tin | Diễn giải |
| ----- | ----- |
| URL | {[API\_AIO\_Url](#thông-tin-chi-tiết-để-kết-nối)}/webapp/paging |
| Method | Post |
| Header | TaxCode: {taxcode} Authorization: Bearer {token} |
| Params | invoiceWithCode: true/false (hóa đơn có mã/không mã) |
| Body | {     "Start": 0, (bắt đầu)     "Length": 100, (số lượng lấy, mặc đinh 2, tối đa 100\)     "Sort": "InvDate", (thông tin sắp xếp)     "FromDate": "2025-07-01", (từ ngày)     "ToDate": "2025-12-30", (đến ngày)     "PublishStatus": "0" (trạng thái phát hành hóa đơn) }  PublishStatus:  0: chưa phát hành,  4: chờ cấp mã,  6: đã cấp mã,  7: từ chối cấp mã |
| Response | {     "success": true,     "errorCode": null,     "descriptionErrorCode": null,     "errors": \[\],     "data": "**List [InvoiceData](#invoicedata-\(dữ-liệu-hóa-đơn\))**",     "customData": null } |
| Mã lỗi thường gặp | [Xem tại đây](#mã-lỗi-thường-gặp) |

# 

3. ## Lấy thông tin qua phân trang \- Hóa đơn qua Máy tính tiền {#lấy-thông-tin-qua-phân-trang---hóa-đơn-qua-máy-tính-tiền}

| Thông tin | Diễn giải |
| ----- | ----- |
| URL | {[API\_AIO\_Url](#thông-tin-chi-tiết-để-kết-nối)}/webapp/paging/calculating |
| Method | Post |
| Header | TaxCode: {taxcode} Authorization: Bearer {token} |
| Params | invoiceWithCode: true/false (hóa đơn có mã/không mã) |
| Body | {     "Start": 0, (bắt đầu)     "Length": 100, (số lượng lấy, mặc đinh 2, tối đa 100\)     "Sort": "InvDate", (thông tin sắp xếp)     "FromDate": "2025-07-01", (từ ngày)     "ToDate": "2025-12-30", (đến ngày)     "PublishStatus": "0" (trạng thái phát hành hóa đơn) }  PublishStatus:  0: chưa phát hành,  4: chờ cấp mã,  6: đã cấp mã,  7: từ chối cấp mã |
| Response | {     "success": true,     "errorCode": null,     "descriptionErrorCode": null,     "errors": \[\],     "data": "**List [InvoiceData](#invoicedata-\(dữ-liệu-hóa-đơn\))**",     "customData": null } |
| Mã lỗi thường gặp | [Xem tại đây](#mã-lỗi-thường-gặp) |

# 

# 

# 

8. # **Mô tả đối tượng của hóa đơn** {#mô-tả-đối-tượng-của-hóa-đơn}

   1. ## Template (mẫu hóa đơn) {#template-(mẫu-hóa-đơn)}

| Tên trường | Kiểu dữ liệu | Diễn giải |
| ----- | ----- | ----- |
| IPTemplateID | string | ID mẫu hóa đơn |
| CompanyID | integer | ID của công ty |
| TemplateName | string | Tên mẫu hóa đơn |
| InvTemplateNo | string | Mẫu số hóa đơn |
| InvSeries | string | Ký hiệu hóa đơn |
| OrgInvSeries | string | Ký hiệu hóa đơn rút gọn |
| TemplateType | integer | Loại mẫu hóa đơn |
| CreatedDate | string | Ngày tạo |
| ModifiedDate | string | Ngày sửa đổi |
| Inactive | boolean | Trạng thái không hoạt động |
| IsInheritFromOldTemplate | boolean | Xác định có kế thừa từ mẫu cũ không |
| IsSendSummary | boolean | Xác định có gửi bảng tổng hợp không |
| IsTemplatePetrol | boolean | Xác định có phải là mẫu hóa đơn xăng dầu không |
| IsMoreVATRate | boolean | Xác định có nhiều mức thuế VAT không |

   2. ## Ticket (mẫu vé) {#ticket-(mẫu-vé)}

| Tên trường | Kiểu dữ liệu | Diễn giải |
| ----- | ----- | ----- |
| TicketTemplateID | string | ID mẫu hóa đơn (trong JSON) |
| TemplateName | string | Tên mẫu hóa đơn |
| InvSeries | string | Ký hiệu hóa đơn |
| Inactive | boolean | Trạng thái không hoạt động |
| TicketType | integer | Loại vé (trong JSON) |
| TemplateType | integer | Loại mẫu hóa đơn |
| ServiceName | string | Tên dịch vụ (trong JSON, không có trong bảng mẫu) |
| IsInheritFromOldTemplate | boolean | Xác định có kế thừa từ mẫu cũ không |
| IsTicketCode | boolean | (trong JSON, không có trong bảng mẫu) |

3. ## InvoiceData (Dữ liệu hóa đơn) {#invoicedata-(dữ-liệu-hóa-đơn)}

| Thông tin | Kiểu dữ lệu | Bắt buộc | Diễn giải |
| ----- | ----- | ----- | ----- |
| RefID | string (GUID) | x | Mã tham chiếu của hóa đơn (check trùng theo key này, lưu lại để xử lý nghiệp vụ về sau) \- Key tự định nghĩa |
| InvoiceTemplateID | string | x | Lấy từ [API 2.2 Lấy danh sách mẫu hóa đơn](https://docs.google.com/document/d/1-pIxKIhC9MOskBm2eYbyNNXzOTqf5juQ8XUe--y0zes/edit?tab=t.0#heading=h.idpvscmxk26j) **IPTemplateID** |
| InvSeries | string | x | Lấy từ [API 2.2 Lấy danh sách mẫu hóa đơn](https://docs.google.com/document/d/1-pIxKIhC9MOskBm2eYbyNNXzOTqf5juQ8XUe--y0zes/edit?tab=t.0#heading=h.idpvscmxk26j) **InvSeries** |
| InvDate | string | x | Ngày phát hành hóa đơn |
| InvNo | string |  | Trả về khi hóa đơn đã phát hành |
| AccountObjectTaxCode | string |  | Mã số thuế của khách hàng |
| AccountObjectName | string |  | Tên đơn vị |
| AccountObjectCode | string |  | Mã đơn vị |
| AccountObjectAddress | string |  | Địa chỉ đơn vị |
| AccountObjectBankAccount | string |  | Số tài khoản ngân hàng của đơn vị |
| AccountObjectBankName | string |  | Tên ngân hàng của đơn vị |
| CitizenIDNumber | string |  | Số định danh cá nhân Một chuỗi gồm 12 ký tự dạng số, không validate theo cấu trúc chi tiết , Không bắt buộc **(Chỉ sử dụng ở ND70)** |
| PassportNumber | string |  | Số hộ chiếu Cho phép nhập dạng chuỗi gồm 20 ký tự **(Chỉ sử dụng ở ND70)** |
| RelatedUnitCode | string |  | Mã số đơn vị có quan hệ với Ngân sách (gồm 7 ký tự) Khi nhập MSĐVCQHVNS người mua thì validate bắt buộc nhập Tên đơn vị, Địa chỉ **(Chỉ sử dụng ở ND70)** |
| SellerShopCode | string |  | Mã cửa hàng |
| SellerShopName | string |  | Tên cửa hàng |
| BuyerSalesChannel | string |  | Kênh bán hàng, tối đa 255 ký tự |
| BuyerShopName | string |  | Tên gian hàng, tối đa 255 ký tự |
| BuyerOrderCode | string |  | Mã đơn hàng, tối đa 255 ký tự |
| ContactName | string |  | Họ tên người mua hàng |
| ReceiverEmail | string |  | Email của người mua hàng |
| ReceiverName | string |  | Tên người nhận email |
| ReceiverMobile | string |  | Số điện thoại gửi SMS |
| PaymentMethod | string | x | Phương thức thanh toán (TM, CK …) |
| CurrencyCode | string | x | Mã tiền tệ (VND, USD …) |
| DiscountRate | decimal | x | Tỷ lệ chiết khấu |
| ExchangeRate | decimal | x | Tỷ giá |
| TotalSaleAmountOC | decimal | x | Tổng tiền hàng (trước chiết khấu, trước thuế) nguyên tệ |
| TotalSaleAmount | decimal | x | Tổng tiền hàng quy đổi \= TotalSaleAmountOC \* ExchangeRate |
| TotalDiscountAmountOC | decimal | x | Tổng tiền chiết khấu nguyên tệ |
| TotalDiscountAmount | decimal | x | Tổng tiền chiết khấu quy đổi \= TotalDiscountAmountOC \* ExchangeRate |
| TotalVATAmountOC | decimal | x | Tổng tiền VAT nguyên tệ |
| TotalVATAmount | decimal | x | Tổng tiền VAT quy đổi \= TotalVATAmountOC \* ExchangeRate |
| TotalAmountOC | decimal | x | Tổng tiền thanh toán (tổng tiền bao gồm thuế) nguyên tệ |
| TotalAmount | decimal | x | Tổng tiền thanh toán quy đổi \= TotalAmountOC \* ExchangeRate |
| CreatedDate | Datetime | x | Ngày tạo (lấy theo thời gian hiện tại) |
| CreatedBy | string |  | Người tạo |
| ModifiedDate | Datetime | x | Ngày chỉnh sửa  (theo thời gian hiện tại) |
| ModifiedBy | string |  | Người chỉnh sửa |
| InvoiceDetails | List\<[InvoiceDetails](https://docs.google.com/document/d/1-pIxKIhC9MOskBm2eYbyNNXzOTqf5juQ8XUe--y0zes/edit?tab=t.0#heading=h.7ttbureqr7ri)\> | x | Danh sách chi tiết hóa đơn |
| **Thông tin về hóa đơn điều chỉnh/thay thế** |  |  |  |
| EInvoiceStatus | string |  | Trạng thái hóa đơn1: hóa đơn gốc (mặc định)3: thay thế4: điều chỉnh |
| OrgRefID | string |  | ID của hóa đơn bị thay thế, hoặc điều chỉnh |
| OrgInvNo | string |  | Số của hóa đơn bị thay thế, hoặc điều chỉnh |
| OrgTransactionID | string |  | TransactionID của hóa đơn bị thay thế điều chỉnh |
| OrgInvTemplateNo | string |  | Mẫu số hóa đơn bị thay thế/điều chỉnh VD: Ký hiệu của hóa đơn theo ND123 bị thay thế là 1C24MAA thì OrgInvTemplateNo \= 1 (ký tự đầu tiên) |
| OrgInvSeries | string |  | Ký hiệu hóa đơn theo ND123 bị thay thế/điều chỉnh VD: Ký hiệu của hóa đơn bị thay thế là 1C24MAA thì OrgInvSeries \= C24MAA (6 ký tự cuối cùng) |
| OrgInvDate | DateTime |  | Ngày của hóa đơn bị thay thế, hoặc điều chỉnh |
| ChangeReason | string |  | Lý do thay thế, hoặc điều chỉnh |
| **Thông tin mở rộng** |  |  |  |
| CustomField | string |  | Hỗ trợ trường mở rộng từ CustomField1 \-\> CustomField10 |

   

   4. ## InvoiceDetail (dữ liệu dòng hàng hóa, dịch vụ) {#invoicedetail-(dữ-liệu-dòng-hàng-hóa,-dịch-vụ)}

| Thông tin | Kiểu dữ lệu | Bắt buộc  | Diễn giải |
| ----- | ----- | ----- | ----- |
| InventoryItemType | integer | x | Tính chất HHDV 0: Hàng hóa/dịch vụ 2: Khuyến mại 3: Ghi chú/diễn giải 4: Chiết khấu thương mại (theo dòng) 6: Hàng hóa đặc thù |
| InventoryItemCode | string |  | Mã hàng hóa |
| Description | string | x | Mô tả hàng hóa/ Tên hàng hóa |
| SortOrderView | integer | x | Thứ tự sắp xếp của dòng hàng *Null nếu InventoryItemType \= 3 hoặc 4* (bắt đầu từ 1\) |
| SortOrder | integer | x | Thứ tự hiển thị của dòng hàng (bắt đầu từ 1\) |
| UnitName | string | x | Đơn vị tính |
| Quantity | decimal | x | Số lượng |
| UnitPrice | decimal | x | Đơn giá trước thuế, trước chiết khấu |
| AmountOC | decimal | x | Thành tiền hàng nguyên tệ (trước thuế, trước chiết khấu) |
| Amount | decimal | x | Thành tiền hàng quy đổi |
| DiscountRate | decimal |  | Tỷ lệ chiết khấu |
| DiscountAmountOC | decimal |  | Tiền chiết khấu nguyên tệ |
| DiscountAmount | decimal |  | Tiền chiết khấu quy đổi |
| VATRate | int? | x (HD VAT) | Tỷ lệ thuế suất \-1: Không chịu thuế \-3: Không kê khai nộp thuế 0: Thuế 0% 5: Thuế 5% 8: Thuế 8% 10: Thuế 10% KHAC (truyền **null**, app sẽ *tự tính lại % theo VATAmountOC*) |
| VATAmountOC | decimal | x (HD VAT) | Tiền thuế nguyên tệ |
| VATAmount | decimal | x (HD VAT) | Tiền thuế quy đổi |
| InWards | decimal | x (PXK) | Thực xuất |
| WageAmountOC | decimal |  | Tiền công nguyên tệ |
| WageAmount | decimal |  | Tiền công quy đổi |
| TaxReduction43AmountOC | decimal |  | Số tiền giảm thuế nguyên tệ theo NQ218 |
| TaxReduction43Amount | decimal |  | Số tiền giảm thuế quy đổi theo NQ218 |
| ExciseTaxRate | decimal |  | Thuế suất TTDB |
| ExciseTaxAmountOC | decimal |  | Tiền thuế TTDB nguyên tệ |
| ExciseTaxAmount | decimal |  | Tiền thuế TTDB quy đổi |
| ServiceFeeRate | decimal |  | Phí dịch vụ (%) |
| ServiceAmountOC | decimal |  | Tiền phí dịch vụ nguyên tệ |
| ServiceAmount | decimal |  | Tiền phí dịch vụ quy đổi |
| LotNo | string |  | Số lô |
| ExpireDate | DateTime |  | Hạn dùng |
| LicensePlate | string |  | Biển kiểm soát |
| EngineNumber | string |  | Số máy |
| ChassisNumber | string |  | Số khung |
| **Thông tin mở rộng** |  |  |  |
| CustomField**x**Detail | string |  | Hỗ trợ trường mở rộng từ CustomField1Detail \-\> CustomField10Detail |

      ## 

## 

9. # **Công thức tính toán trên hóa đơn** {#công-thức-tính-toán-trên-hóa-đơn}

   1. ## Công thức trên Detail {#công-thức-trên-detail}

| Dữ liệu | Công thức |
| ----- | ----- |
| AmountOC | Quantity \* UnitPrice |
| Amount | AmountOC \* ExchangeRate |
| DiscountAmountOC | AmountOC \* DiscountRate/100 |
| DiscountAmount | DiscountAmountOC \* ExchangeRate |
| ServiceAmountOC (chỉ áp dụng với hóa đơn ngành nghề Khách sạn) | ((AmountOC \- DiscountAmountOC) \* ServiceFeeRate)/100 |
| ServiceAmount (chỉ áp dụng với hóa đơn ngành nghề Khách sạn) | ServiceAmountOC \* ExchangeRate |
| ExciseTaxAmountOC (chỉ áp dụng với hóa đơn ngành nghề Khách sạn) | ((AmountOC \- DiscountAmountOC \+ ServiceAmountOC) \* ExciseTaxRate)/100 |
| ExciseTaxAmount (chỉ áp dụng với hóa đơn ngành nghề Khách sạn) | ExciseTaxAmountOC\* ExchangeRate |
| VATAmountOC | (AmountOC \- DiscountAmountOC \+ ServiceAmountOC \+ ExciseTaxAmountOC) \* VatRate/100 |
| VATAmount | VATAmountOC  \* ExchangeRate |

2. ## Công thức trên Master (dựa trên dữ liệu của dòng hàng hóa) {#công-thức-trên-master-(dựa-trên-dữ-liệu-của-dòng-hàng-hóa)}

| Dữ liệu | Công thức |
| ----- | ----- |
| TotalSaleAmountOC | Sum(AmountOC, InventoryItemType \= 0\)  \-  Sum(AmountOC, InventoryItemType \= 4\) |
| TotalSaleAmount | TotalSaleAmountOC  \* ExchangeRate |
| TotalDiscountAmountOC | Sum(DiscountAmountOC, InventoryItemType \= 0\) |
| TotalDiscountAmount | TotalDiscountAmountOC  \* ExchangeRate |
| TotalVATAmountOC | Sum(VATAmountOC, InventoryItemType \= 0\) \-  Sum(VATAmountOC, InventoryItemType \= 4\) |
| TotalVATAmount | TotalVATAmountOC  \* ExchangeRate |
| ServiceAmountOC | Sum(ServiceAmountOC) |
| ServiceAmount | ServiceAmountOC \* ExchangeRate |
| ExciseTaxAmountOC | Sum(ExciseTaxAmountOC) |
| ExciseTaxAmount | ExciseTaxAmountOC  \* ExchangeRate |
| TotalAmountOC | TotalSaleAmountOC \- TotalDiscountAmountOC \+ TotalVATAmountOC \+ ServiceAmountOC \+ ExciseTaxAmountOC |
| TotalAmount | TotalAmountOC  \* ExchangeRate |

# 

   # 

10. # **Mã lỗi thường gặp** {#mã-lỗi-thường-gặp}

| Mã lỗi | Mô tả | Cách xử lý |
| ----- | ----- | ----- |
| InvalidAppID | Sai thông tin AppID | Liên hệ MISA để nhận AppID |
| InActiveAppID | Ứng dụng ngừng theo dõi | Liên hệ MISA để nhận AppID |
| UnAuthorize | Sai mã token | Đăng nhập lại để lấy token mới |
| TokenExpiredCode | Token hết hạn | Cần gọi hàm RefreshToken |
| InvalidInvoiceData | Sai tham số đầu vào | Kiểm tra lại kiểu dữ liệu của đối tượng |
| InvalidTokenCode | Token lỗi cần đăng nhập lại | Đăng nhập lại để lấy token mới |
| DuplicateTemplateName | Lỗi trùng tên mẫu | Thay đổi tên mẫu hóa đơn |
| DuplicateTemplateNo | Lỗi trùng ký hiệu | Thay đổi ký hiệu mẫu hóa đơn |
| InvoiceTemplateNotExist | Mẫu hóa đơn không tồn tại | Tạo mẫu hóa đơn/ kiểm tra lại thông tin InvSeries |
| CreateInvoiceDataError | Tạo XML hóa đơn lỗi không xác định |  |
| InvoiceDetail\_{0} | Nếu mã lỗi bắt đầu bằng InvoiceDetail\_{0} thì thông tin có tên trường dữ liệu phía sau không hợp lệ \- Không thuộc loại được cho phép \- Giá trị bắt buộc \- không được trống \- Vượt quá giới hạn MaxLength cho phép | Lỗi thông tin nào kiểm tra thông tin đó, kiểm tra về kiểu dữ liệu, số lượng ký tự, tính đúng đắn của dữ liệu |
| StockInTaxCode\_NotInfo\_{0}\_{1} | Nếu mã lỗi bắt đầu bằng StockInTaxCode\_NotInfo\_{0}\_{1} thì thông tin MST có nhưng thông tin đơn vị không có 0: Loại phiếu xuất kho 1: thông tin thiếu | Lỗi thông tin nào kiểm tra thông tin đó, kiểm tra về kiểu dữ liệu, số lượng ký tự, tính đúng đắn của dữ liệu |
| RequireError\_{0} | Nếu bắt đầu bằng RequireError\_{0} thì thông tin có tên trường dữ liệu phía sau không hợp lệ \- Không thuộc loại được cho phép \- Giá trị bắt buộc \- không được trống \- Vượt quá giới hạn MaxLength cho phép | Lỗi thông tin nào kiểm tra thông tin đó, kiểm tra về kiểu dữ liệu, số lượng ký tự, tính đúng đắn của dữ liệu |
| TaxRateInfo\_VATRateName | Tên loại thuế suất trong Bảng tổng hợp thuế suất của hóa đơn có dữ liệu không hợp lệ | Bổ sung thêm thông tin tổng hợp thuế suất TaxRateInfo |
| InvoiceQuantityTooLarge | Số lượng hóa đơn gửi lên trong 1 Request quá số lượng cho phép | Nếu dữ liệu của 1 hóa đơn lớn, nên gửi tối đa 30 hóa đơn/request |
| XMLTooLong | File XML quá dài | Nếu số lượng dòng hàng quá lớn, nên tách thành nhiều hóa đơn, mỗi hóa đơn \<200 dòng hàng |
| LicenseInfo\_NotBuy | Chưa mua tài nguyên | Liên hệ MISA để đăng ký/ mua dịch vụ |
| LicenseInfo\_OutOfInvoice | Số lượng tài nguyên còn lại không đủ để phát hành toàn bộ các hóa đơn gửi lên | Mua thêm tài nguyên phát hành hóa đơn |
| LicenseInfo\_Expired | Tài nguyên chưa thanh toán hoặc đã hết hạn | Liên hệ kinh doanh MISA để thanh toán/cấp tài nguyên |
| InvalidTransactionID | Mã tra cứu không hợp lệ | Mã tra cứu được hệ thống MISA cung cấp, KH không tự ý thay đổi |
| DuplicateTransactionID | Trùng Mã tra cứu | Thực hiện lại bước tạo hóa đơn để lấy mã tra cứu mới (thường gặp khi ký số qua tool) |
| SignatureEmpty | Chữ ký số bị bỏ trống | Thực hiện ký số (thường gặp khi ký số qua tool) |
| InvalidSignature | Chữ ký số không hợp lệ | Kiểm tra lại chữ ký số với chữ ký số đăng ký trên tờ khai được chấp thuận |
| CertRevocation | Chữ ký số đã bị thu hồi | Lỗi về CKS, liên hệ các bên liên quan |
| InvalidCertByRegistration | Chữ ký số không tồn tại trong tờ khai | Tạo tờ khai hoặc ký số hóa đơn đúng với CKS đã đăng ký ở tờ khai |
| HasRegistrationStopUseCert | Tồn tại tờ khai Ngừng sử dụng chứng thư số | Tạo tờ khai hoặc ký số hóa đơn đúng với CKS đã đăng ký ở tờ khai |
| SigningTimeNotInRegistration | Ngày ký không thuộc khoảng thời gian có hiệu lực của chứng thư số đã đăng ký với cơ quan thuế và được CQT chấp nhận | Tạo tờ khai hoặc ký số hóa đơn đúng với CKS đã đăng ký ở tờ khai |
| InvalidXMLData | XML không hợp lệ | Kiểm tra lại định dạng của XML |
| InvalidInvNo | Số hóa đơn không hợp lệ | Số hóa đơn thường có 8 ký tự, do hệ thống cấp tự động, người dùng k can thiệp vào dữ liệu của những field liên quan đến số hóa đơn |
| InvalidTaxCode | Mã số thuế không hợp lệ | Kiểm tra, tra cứu MST đang sử dụng |
| DuplicateInvoiceRefID | Trùng RefID của hóa đơn | \- gọi đầu API lấy trạng thái hóa đơn theo RefID \=\> cập nhật lại trạng thái xuống client (số hóa đơn, mã tra cứu, mã CQT cấp, ...) |
| **InvoiceNumberNotCotinuous** | Số hóa đơn không liên tục | \- PA chung là: gặp mã lỗi này thì retry gọi lại hàm phát hành \- có 2 TH mã lỗi này thường gặp: \- TH1: kỹ thuật đơn vị dùng vòng lặp for gửi request liên tục \=\> Xử lý: Báo kỹ thuật xử lý mỗi khi phát hành xong 1 request thì mới thực hiện tiếp request khác \- TH2: có nhiều điểm máy trạm phát hành đồng thời \=\> Xử lý: Tư vấn đơn vị mỗi 1 điểm máy trạm phát hành sẽ theo 1 ký hiệu (InvSeries) khác nhau |
| SignSoftDream78Exception | Lỗi ký số HSM | Liên hệ MISA |
| SignEsignHSMError | Lỗi ký số HSM | Liên hệ MISA |
| CallSignServiceFail | Lỗi ký số | Liên hệ MISA |
| **InvoiceDuplicated** | Trùng hóa đơn \- hóa đơn đã được phát hành | \- PA chung là: gặp mã lỗi này thì retry gọi lại hàm phát hành \=\> Xử lý: trường hợp retry vẫn bị thì liên hệ kỹ thuật MISA |
| Exception | Thực hiện bị Exception \- Không rõ nguyên nhân | Liên hệ kỹ thuật MISA để tìm hiểu nguyên nhân |
| DeclarationNotExist | Chưa tồn tại Tờ khai/Thay đổi thông tin | Lập tờ khai và chờ CQT chấp nhận thì thực hiện phát hành hóa đơn |
| InvalidDeclaration | Chưa tồn tại Tờ khai/Thay đổi thông tin có trạng thái CQT chấp nhận | Lập tờ khai và chờ CQT chấp nhận thì thực hiện phát hành hóa đơn |
| ExistDeclarationNotReceive | Tồn tại tờ khai có trạng thái: Đã gửi CQT/ CQT Tiếp nhận | Chờ CQT chấp nhận tờ khai |
| ExistsInvoiceNextYear | Tồn tại hóa đơn của năm tiếp theo | Lập hóa đơn/ gửi tờ khai mới với loại hóa đơn sử dụng (thường liên quan đến hóa đơn có mã/không mã) |
| InvoiceTemplateNotValidInDeclaration | Tờ khai/Thay đổi thông tin không chứa loại hóa đơn đang phát hành | Lập hóa đơn/ gửi tờ khai mới với loại hóa đơn sử dụng (thường liên quan đến hóa đơn có mã/không mã) |
| InvalidInvoiceDate | Nếu ngày hóa đơn không hợp lệ, nhỏ hơn ngày của hóa đơn cuối cùng đã phát hành | Ngày hóa đơn phải đáp ứng không được nhỏ hơn ngày của số hóa đơn lớn nhất theo ký hiệu đang sử dụng. |
| InvoiceCannotReplace | Không thể thay thế hóa đơn đã hủy | Sai về nghiệp vụ, không có phương án xử lý |
| InvoiceCannotAdjust | Không thể điều chỉnh hóa đơn đã hủy/thay thế | Sai về nghiệp vụ, không có phương án xử lý |
| TaxReductionDateInValid | Ngày hóa đơn giảm thuế không hợp lệ | Ngày phát hành hóa đơn giảm thuế không nằm trong thời gian giảm thuế theo Quyết định |
| X509SubjectName | Không có SubjectName trên file XML đã ký | Lỗi về CKS, liên hệ các bên liên quan |
| X509Certificate | Không có chứng thư số | Lỗi về CKS, liên hệ các bên liên quan |
| InvoiceCannotReplaceByStatusNew | Hóa đơn gốc chưa được hủy, không thể thay thế | Cần hủy hóa đơn gốc trước khi phát hành/điều chỉnh hóa đơn |
| HasAdjustmentInvoice | Hóa đơn đã được lập hóa đơn điều chỉnh, không thể thay thế | Đã tồn tại hóa đơn điều chỉnh, không thể thực hiện các nghiệp vụ điều chỉnh nữa |

[image1]: <data:image/png;base64,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>