> **Attribution.** This document is MISA JSC's MISA eSign RemoteSigning integration guide,
> reproduced in this repository (downloaded as Markdown from MISA's published
> Google Doc) as a wire-format reference for contributors — per Constitution
> Principle IV, DTOs must mirror MISA's published shapes. All copyright
> remains with MISA JSC. The MisaConnect project does not claim ownership of
> this content; it is included for developer reference only and is not
> covered by the project's MIT license.
>
> **Original source:** [Tài liệu tích hợp API eSign RemoteSigning - V2 (Google Docs)](https://docs.google.com/document/d/1Zv4HmYx3tiokrEjZ9wYZn83AOvocWQlpg3TBa6i4pkw/edit?tab=t.0#heading=h.nmjgclh47jnm) — always defer to the upstream document if it diverges from this snapshot.
>
> If you are MISA JSC and would prefer a link instead of a snapshot, please
> open an issue.

---
# **Tài liệu hướng dẫn tích hợp** 

# **MISA eSign RemoteSigning**

[**1\. Giới thiệu	2**](#giới-thiệu)

[1.1. Các khái niệm	2](#các-khái-niệm)

[1.2. Thông tin cần trước khi kết nối	2](#thông-tin-cần-trước-khi-kết-nối)

[1.3. Thông tin chi tiết để kết nối	3](#thông-tin-chi-tiết-để-kết-nối)

[1.4. Mô tả cấu trúc của API Esign	3](#mô-tả-cấu-trúc-của-api-esign)

[1.5. Hướng dẫn xử lý kết quả của API (Response)	4](#hướng-dẫn-xử-lý-kết-quả-của-api-\(response\))

[1.6. Các bước thực hiện	5](#các-bước-thực-hiện)

[**2\. Workflow	6**](#workflow)

[2.1. Luồng đăng nhập	6](#luồng-đăng-nhập)

[2.2. Luồng refresh token	7](#luồng-refresh-token)

[2.3. Luồng ký	8](#luồng-ký)

[2.4. Luồng kết nối chứng thư số	9](#luồng-kết-nối-chứng-thư-số)

[**3\. Mô tả về API	10**](#mô-tả-về-api)

[3.1. Đăng nhập	10](#đăng-nhập)

[3.1.1. Lấy access token	10](#lấy-access-token)

[3.1.2. Làm mới mã AccessToken	11](#làm-mới-mã-accesstoken)

[3.2. Xác thực tài khoản	12](#xác-thực-tài-khoản)

[3.2.1. Xác thực hai lớp	12](#xác-thực-hai-lớp)

[3.2.2. Gửi lại mã OTP	13](#gửi-lại-mã-otp)

[3.3. Lấy thông tin CTS	14](#lấy-thông-tin-cts)

[3.3.1. Lấy danh sách chứng thư số	14](#lấy-danh-sách-chứng-thư-số)

[3.3.2. Lấy thông tin chi tiết của một CTS	15](#lấy-thông-tin-chi-tiết-của-một-cts)

[3.4. Tạo hash của file	16](#tạo-hash-của-file)

[3.5. Ký hash	17](#ký-hash)

[3.6. Lấy trạng thái ký tài liệu	18](#lấy-trạng-thái-ký-tài-liệu)

[3.7. Gắn chữ ký vào file	19](#gắn-chữ-ký-vào-file)

[3.8. Webhook nhận trạng thái ký số	20](#webhook-nhận-trạng-thái-ký-số)

[**4\. Mô tả đối tượng của eSign	21**](#mô-tả-đối-tượng-của-esign)

[4.1. Thông tin has file	21](#thông-tin-has-file)

[4.1.1. Thông tin Pdf, Docs, Excel hash file	21](#thông-tin-pdf,-docs,-excel-hash-file)

[4.1.2. Thông tin XML hash file	21](#thông-tin-xml-hash-file)

[4.2. Thông tin SignatureInfo	21](#thông-tin-signatureinfo)

[4.3. Thông tin SignatureDescription	22](#thông-tin-signaturedescription)

[4.4. Thông tin Sign hash	22](#thông-tin-sign-hash)

[4.5. Thông tin Document	22](#thông-tin-document)

[4.6. Thông tin Attachment	23](#thông-tin-attachment)

[4.7. Thông tin Doc\_Attackment	23](#thông-tin-doc_attackment)

[4.8. Thông tin SignaturePosInfos	23](#thông-tin-signatureposinfos)

[4.9. Thông tin eSign-login	24](#thông-tin-esign-login)

[4.10. Thông tin Status	24](#thông-tin-status)

[4.11. Thông tin data\_token	24](#thông-tin-data_token)

[4.12. Thông tin User	24](#thông-tin-user)

[4.13. Thông tin ResponseError	25](#thông-tin-responseerror)

[4.14. Thông tin Cert	25](#thông-tin-cert)

[4.15. Thông tin đầu ra của API hash file	26](#thông-tin-đầu-ra-của-api-hash-file)

[4.15.1. Thông tin PdfDocs	26](#thông-tin-pdfdocs)

[4.15.2. Thông tin XmlDocs	26](#thông-tin-xmldocs)

[4.15.3. Thông tin ExcelDocs	26](#thông-tin-exceldocs)

[4.15.4. Thông tin WordDocs	27](#thông-tin-worddocs)

[4.16. Thông tin Sign\_status	27](#thông-tin-sign_status)

[4.17. Thông tin Signature	27](#thông-tin-signature)

[4.18. Thông tin Attachmented	27](#thông-tin-attachmented)

History

| Thông tin | Diễn giải |
| ----- | ----- |
| 27/10/2025 | Khởi tạo tài liệu |

1. # **Giới thiệu** {#giới-thiệu}

Tài liệu hướng dẫn tích hợp ứng dụng với dịch vụ chữ ký số từ xa MISA eSign remoteSigning.

1. ## Các khái niệm {#các-khái-niệm}

| Thông tin | Diễn giải |
| ----- | ----- |
| MISA-CA | Nhà cung cấp dịch vụ chữ ký số công cộng |
| Ứng dụng tích hợp (ERP) | Là ứng dụng muốn tích hợp giải pháp ký số điện tử của MISA-CA. |
| Ứng dụng xác thực ký | MISA eSign App (AppStore / CH Play) – là ứng dụng mobile app của MISA-CA dành cho người dùng cuối xác thực các yêu cầu ký do Ứng dụng tích hợp gửi |
| MISA eSign RemoteSigning (hoặc MISA eSign) | là server xử lý các nghiệp vụ liên quan đến ký số điện tử của MISA-CA |
| Access token | Mã kết nối sau khi thực hiện luồng đăng nhập MISA eSign |
| CTS | Chứng thư số |
| CKS | Chữ ký số |

   2. ## Thông tin cần trước khi kết nối {#thông-tin-cần-trước-khi-kết-nối}

| Thông tin | Giá trị | Diễn giải |
| ----- | ----- | ----- |
| Đối tác/KH phải sử dụng dịch vụ MISA eSign |  | Liên hệ KD MISA. |
| Đăng ký sử dụng dịch vụ API |  | Nhận về key clientId và clientKey và tài liệu mô tả về API |
| Đăng ký nhận kết quả (webhook)  |  | Nếu cần |
| x-clientid | ClientId | Id được MISA cung cấp |
| x-clientkey | ClientKey | Key được MISA cung cấp |
| APIUrl | https://esignapp.misa.vn/ | Đường dẫn API của eSign |
| userName | userName | Tài khoản MISAID |
| password | password | Mật khẩu tài khoản |

      ## 

   3. ## Thông tin chi tiết để kết nối {#thông-tin-chi-tiết-để-kết-nối}

| Thông tin | Diễn giải |
| ----- | ----- |
| APIUrl | Product: https://esignapp.misa.vn/ |
| Token url (lấy access token) | api/auth/api/v1/auth/login-api |
| Xác thực 2 lớp | api/auth/api/v1/auth/two-factor-auth |
| Gửi lại OTP | api/auth/api/v1/auth/resend-otp-auth |
| Làm mới token | api/auth/api/v1/auth/refreshtoken |
| Lấy danh sách CTS | external/esrm/service/general/api/v1/Certificates/by-userId |
| Lấy chi tiết CTS theo key | external/esrm/service/general/api/v1/Certificates/by-certId |
| Hash file tài liệu trước khi ký | external/esrm/service/document/api/v1/documents/hash |
| Ký file tài liệu đã hash | external/esrm/service/signing/api/v1/Signing/hash |
| Lấy trạng thái ký của tài liệu | external/esrm/service/signing/api/v1/Signing/status |
| Găn cks vào file | external/esrm/service/document/api/v1/documents/attachment |

## 

   4. ## Mô tả cấu trúc của API Esign {#mô-tả-cấu-trúc-của-api-esign}

| Thông tin | Diễn giải |
| ----- | ----- |
| Url | Theo từng API cụ thể |
| Method | Get/Post (Theo từng API cụ thể) |
| Params | Theo từng API cụ thể |
| Header | x-clientId: {clientId} x-clientKey: {clientKey} AuthorizationRM: {access token} |
| Body | Content-Type: application/json (Đối tượng theo từng API cụ thể) |
| Response | Content-Type: application/json (Đối tượng theo từng API cụ thể) |

## 

5. ## Hướng dẫn xử lý kết quả của API (Response) {#hướng-dẫn-xử-lý-kết-quả-của-api-(response)}

| Response thành công | Được mô tả ở mỗi API |
| :---- | :---- |
| Response lỗi | Trả về cùng 1 định dạng là ResponseError  { 	"error": "thông tin lỗi", 	"errorCode": "mã lỗi", 	"devMsg": "mô tả thông tin lỗi cho người phát triển", 	"userMsg": "mô tả thông tin lỗi cho người dùng" } |

6. ## Các bước thực hiện {#các-bước-thực-hiện}

| Đăng ký sử dụng dịch vụ  |  |
| :---- | :---- |
| Đăng ký sử dụng API | Đăng ký sử dụng API, liên hệ NVKD của MISA Nhận về clientId và clientKey (dùng cho cả sandbox và product). Nếu đối tác chưa có CTS thì có thể đăng ký CTS test và nhận về Tài khoản eSgin Mật khẩu eSgin Khi thực hiện golive, lấy các thông tin từ đơn vị **Tích hợp ký số chỉ sử dụng được với các CTS từ xa của MISA eSign (không hỗ trợ CTS dạng USB token)** |
| Thực hiện tải ứng dụng xác thực MISA eSign | Tải ứng dụng từ app store/CH Play |
| Đăng nhập thông tin ký | Thực hiện đăng nhập thông tin ký trên ứng dụng mới tải về (nếu ở site test,  giữ logo eSign khoảng 5s để đổi môi trường) |
| **Thao thác với API** |  |
| Bước 1: Thực hiện gọi API lấy access token | \- [Xem mô tả](#đăng-nhập) **Lưu ý:** \- Token có hạn được trả về qua thông tin data.expiresIn (dữ liệu trả về kiểu long) |
| Bước 2: Thực hiện xác thực nếu cần | \- [Xem mô tả](#xác-thực-tài-khoản) |
| Bước 3: Lấy thông tin CTS | \- [Xem mô tả](#lấy-thông-tin-cts) |
| Bước 4: Tạo hash file | \- [Xem mô tả](#tạo-hash-của-file) |
| Bước 5: Ký hash file (gửi trình ký) | \- [Xem mô tả](#ký-hash) |
| Bước 6: Lấy trạng thái ký của tài liệu | \- [Xem mô tả](#lấy-trạng-thái-ký-tài-liệu) |
| Bước 7: Gắn cks | \- [Xem mô tả](#gắn-chữ-ký-vào-file) |
| **Postman tham khảo** | \- [Tải về](https://drive.usercontent.google.com/u/0/uc?id=1E4GNoFrsU5UDModXx10lzm8c4jegggHn&export=download) |

   ## 

2. # **Workflow** {#workflow}

   1. ## Luồng đăng nhập {#luồng-đăng-nhập}

![][image1]  
***Mô tả luồng:***

* Bước 1: Khi người dùng nhập tên đăng nhập và mật khẩu, ứng dụng tích hợp gọi đến API đăng nhập của eSign lấy token.   
* Bước 2\. Kiểm tra response trả về.   
  - Nếu có mã lỗi 122 chuyển bước 3 (xác thực 2 lớp \- 2FA).   
  - Ngược lại chuyển bước 5\.   
* Bước 3\. Gọi API xác thực hai lớp lấy token.   
* Bước 4\. Nếu quá hạn dùng mã OTP hoặc người dùng nhập sai mã OTP. Người dùng chọn “Gửi lại mã” thì ứng dụng tích hợp gọi API Gửi lại mã OTP.   
* Bước 5\. Sử dụng mã **remoteSigningAccessToken** trong response của API đăng nhập hoặc API xác thực hai lớp để làm điều kiện xác thực cho các API sau.   
* Bước 6: Ngay sau khi KH thực hiện đăng nhập xong. Ứng dụng tích hợp thực hiện gọi API lấy danh sách chứng thư số theo tài khoản của người dùng vừa đăng nhập (Xử lý theo [flow kết nối chứng thư số](#luồng-kết-nối-chứng-thư-số))


  2. ## Luồng refresh token {#luồng-refresh-token}

![][image2]  
***Thực hiện:***  
Khi gọi API mà response trả về mã HTTP StatusCode là 401 (hết hạn JWT Token). Thì ứng dụng tích hợp gọi đến API refresh token để lấy lại mã **remoteSigningAccessToken**.   
Nếu có lỗi xảy ra trong API này, ứng dụng tích hợp gọi lại API đăng nhập.

3. ## Luồng ký {#luồng-ký}

![][image3]  
***Các bước thực hiện:***

* Bước 1: Lấy content của file ký (dạng base64)/xml .  
* Bước 2\. Hash dữ liệu.  
* Bước 3\. Ký số từ xa dữ liệu đã hash thông qua MISA eSign.  
* Bước 4\. Ứng dụng tích hợp gọi API lấy trạng thái ký theo transactionId  
* Bước 5\. Gắn chữ ký số.  
* Bước 6: Thông báo kết quả

4. ## Luồng kết nối chứng thư số {#luồng-kết-nối-chứng-thư-số}

![][image4]  
***Các bước thực hiện:***

* Bước 1: Ứng dụng tích hợp gọi API lấy danh sách CTS theo người dùng.  
* Bước 2\. MISA eSign trả về danh sách CTS .  
* Bước 3\. Ứng dụng tích hợp thực hiện kiểm tra danh sách CTS với trạng thái đang hoạt động (keyStatus \= ACTIVE)  
  - Nếu không có: Vui lòng thông báo là người dùng chưa có chứng thư số, cần liên hệ quản trị để được cấp CTS mới hoặc mua mới CKS trên [https://esign.misa.vn/](https://esign.misa.vn/)  
  - Nếu có 1 CTS: thực hiện lưu trữ thông tin CTS đó để phục vụ cho việc gọi ký.  
  - Nếu có từ 2 CTS: Hiển thị giao diện danh sách CTS để người dùng lựa chọn kết nối.

# 

3. # **Mô tả về API** {#mô-tả-về-api}

   1. ## Đăng nhập {#đăng-nhập}

      1. ### Lấy access token {#lấy-access-token}

| Thông tin | Diễn giải |
| ----- | ----- |
| URL | {[APIUrl](#mô-tả-về-api)}/api/auth/api/v1/auth/login-api |
| Method | Post |
| Body request | {     "userName": "userName",     "password": "password" } |
| Response | { 	"status": { 		"type": "type", 		"code": 200, 		"message": "message", 		"error": response\_error, 		"errorCode": error\_status\_code, 		"devMsg": "dev\_msg", 		"userMsg": "user\_msg" 	}, 	"data": { 		"accessToken": "access\_token", 		"remoteSigningAccessToken": "eSign\_access\_token", 		"tokenType": "Bearer", 		"expiresIn": 3600, 		"refreshToken": "refresh\_token", 		"user": { 			"id": "user\_id", 			"email": "user\_email", 			"phoneNumber": "user\_phone\_number", 			"firstName": "user\_first\_name", 			"lastName": "user\_last\_name", 			"username": "user\_username" 		}, 		"default": { 			"email": false, 			"phoneNumber": false, 			"appAuthenticator": true 		}, "verifyUser": {             "emailsVerify": false,             "phoneNumberIsVerify": true,             "isChangePassword": true  }, 	} } |
| Mô tả đối tượng | **Tham số đầu ra ([eSign-login](#thông-tin-esign-login))** |
| Lưu ý | Những thông tin này sẽ dùng trong suốt quá trình call API **Token\_eSign** \= {tokenType} {eSign\_access\_token} (có khoảng trắng giữa 2 key).  Ví dụ: Bearer eyJhbGciOiJSUzI1NiItpZCI6… **Token\_eSign\_refresh** \= refresh\_token **RefreshToken** \= refresh\_token **UserID** \= user\_id |

## 

2. ### Làm mới mã AccessToken {#làm-mới-mã-accesstoken}

| Thông tin | Diễn giải |
| ----- | ----- |
| URL | {[APIUrl](#mô-tả-về-api)}/webdev/api/auth/api/v1/auth/refreshtoken |
| Method | Post |
| Body request | {     "refreshToken": "RefreshToken", } RefreshToken được lấy ở bước [4.3. Đăng nhập tài khoản](#đăng-nhập) |
| Response | { 	"remoteSigningAccessToken": "eSign\_access\_token", 	"accessToken": "access\_token", 	"refreshToken": "refresh\_token", 	"expiresIn": 3600 } |
| Mô tả đầu ra | **Tham số đầu ra ([eSign-login](#thông-tin-esign-login))** |
| Lưu ý | Lưu lại các thông tin tương tự mục [4.3. Đăng nhập tài khoản](#đăng-nhập) |

## 

2. ## Xác thực tài khoản {#xác-thực-tài-khoản}

   1. ### Xác thực hai lớp {#xác-thực-hai-lớp}

| Thông tin | Diễn giải |
| ----- | ----- |
| URL | {[APIUrl](#mô-tả-về-api)}/api/auth/api/v1/auth/two-factor-auth |
| Method | Post |
| Header | clientId: ClientId clientKey: ClientKey |
| Body request | {     "userName": "userName",     "code": "otp\_code", //mã otp     "otpType": 1, //0: nhận từ SDT/Email; 1: nhận từ ứng dụng     "remember": true/false, //ghi nhớ thiết bị } |
| Response | { 	"status": { 		"type": "response\_type", 		"code": 200, 		"message": "message", 		"error": response\_error, 		"errorCode": error\_status\_code, 		"devMsg": "dev\_msg", 		"userMsg": "user\_msg" 	}, 	"data": { 		"accessToken": "access\_token", 		"remoteSigningAccessToken": "eSign\_access\_token", 		"tokenType": "Bearer", 		"expiresIn": 3600, 		"refreshToken": "refresh\_token", 		"user": { 			"id": "user\_id", 			"email": "user\_email", 			"phoneNumber": "user\_phone\_number", 			"firstName": "user\_first\_name", 			"lastName": "user\_last\_name", 			"username": "user\_username" 		}, 		"default": { 			"email": false, 			"phoneNumber": false, 			"appAuthenticator": true 		} 	} } |
| Mô tả đầu ra | **Tham số đầu ra ([eSign-login](#thông-tin-esign-login))** |
| Lưu ý | Lưu lại các thông tin tương tự mục [4.3. Đăng nhập tài khoản](#đăng-nhập) |

      ## 

      2. ### Gửi lại mã OTP {#gửi-lại-mã-otp}

| Thông tin | Diễn giải |
| ----- | ----- |
| URL | {[APIUrl](#mô-tả-về-api)}/webdev/api/auth/api/v1/auth/resend-otp-auth |
| Method | Post |
| Body request | {     "userName": "userName",     "language": "language" //ngôn ngữ gửi thông báo: en-US } |
| Response | { 	"status": { 		"type": "response\_type", 		"code": 200, 		"message": "message", 		"error": response\_error, 		"errorCode": error\_status\_code 	}, 	"data": { 		"user": { 			"username": "user\_username" 		} 	} } |
| Mô tả đầu ra | **Tham số đầu ra ([eSign-login](#thông-tin-esign-login))** |

      ## 

   3. ## Lấy thông tin CTS {#lấy-thông-tin-cts}

      1. ### Lấy danh sách chứng thư số {#lấy-danh-sách-chứng-thư-số}

| Thông tin | Diễn giải |
| ----- | ----- |
| URL | {[APIUrl](#mô-tả-về-api)}/external/esrm/service/general/api/v1/Certificates/by-userId |
| Method | Get |
| Header | x-clientId: ClientId x-clientKey: ClientKey AuthorizationRM: **Token\_eSign** |
| Response | \[{ 	"userId": "user\_id", 	"keyAlias": "cert\_id", 	"appName": "app\_name", 	"keyStatus": "key\_status", 	"certificate": "cert\_data", 	"certiticateChain": \[                      "chain\_data\_1",                      "chain\_data\_2",                      "chain\_data\_3", }\] chain\_data\_1: là chứng thư ký chain\_data\_2: là intermediate cert của MISA CA, chain\_data\_3: là cert root do NEAC cấp. |
| Mô tả đầu ra | **Tham số đầu ra ([Cert](#thông-tin-cert))** |
| Lưu ý | Lưu lại đối tượng **Cert** để sử dụng cho việc ký |

      ## 

      2. ### Lấy thông tin chi tiết của một CTS {#lấy-thông-tin-chi-tiết-của-một-cts}

| Thông tin | Diễn giải |
| ----- | ----- |
| URL | {[APIUrl](#mô-tả-về-api)}/external/esrm/service/general/api/v1/Certificates/by-certId |
| Method | Get |
| Header | x-clientId: ClientId x-clientKey: ClientKey AuthorizationRM: **Token\_eSign** |
| Params | certAlias: keyAlias keyAlias lấy ở bước [4.7. Lấy danh sách chứng thư của người dùng](#lấy-danh-sách-chứng-thư-số) |
| Response | { 	"userId": "user\_id", 	"keyAlias": "cert\_id", 	"appName": "app\_name", 	"keyStatus": "key\_status", 	"certificate": "cert\_data", 	"certiticateChain": \[                      "chain\_data\_1",                      "chain\_data\_2",                      "chain\_data\_3", } chain\_data\_1: là chứng thư ký chain\_data\_2: là intermediate cert của MISA CA, chain\_data\_3: là cert root do NEAC cấp. |
| Mô tả đầu ra | **Tham số đầu ra ([Cert](#thông-tin-cert))** |
| Lưu ý | Lưu lại đối tượng **Cert** để sử dụng cho việc ký |

      ## 

   4. ## Tạo hash của file  {#tạo-hash-của-file}

(Dùng khi ngôn ngữ của đối tác không hỗ trợ băm file như PHP, NodeJS,…,trường hợp tự hash file thì cũng cần tự attach cts vào tài liệu)  
**HashAlgorithm \= SHA256**

| Thông tin | Diễn giải |
| ----- | ----- |
| URL | {[APIUrl](#mô-tả-về-api)}/external/esrm/service/document/api/v1/documents/hash |
| Method | POST |
| Header | x-clientId: ClientId x-clientKey: ClientKey |
| Body | { "certificate": "{[certificate](#lấy-thông-tin-chi-tiết-của-một-cts)}", 	"certificateChain": "{[certiticateChain](#lấy-thông-tin-chi-tiết-của-một-cts)}", 	"pdfDocs": \[PDF hash file\], 	"xmlDocs": \[XML hash file\], 	"wordDocs": \[Docs hash file\], 	"excelDocs": \[Excel hash file\] } |
| Response | { 	"pdfDocs": \[pdfDocs\], 	"xmlDocs": \[xmlDocs\], 	"wordDocs": \[wordDocs\], 	"excelDocs": \[excelDocs\] } |
| Mô tả tham số | Xem tại Sheet: **Tham số [đầu vào của từng đối tượng](#thông-tin-has-file)** Xem tại Sheet: **Tham số [đầu ra của từng đối tượng](#thông-tin-đầu-ra-của-api-hash-file)** |
| Lưu ý | Dùng trường **digest** trả ra bởi api để gọi lên api ký hash. Lưu lưu lại response để gọi lên api gắn chữ ký vào file khi có chữ ký |

## 

5. ## Ký hash {#ký-hash}

| Thông tin | Diễn giải |
| ----- | ----- |
| URL | {[APIUrl](#mô-tả-về-api)}/external/esrm/service/signing/api/v1/Signing/hash |
| Method | Post |
| Header | x-clientId: ClientId x-clientKey: ClientKey AuthorizationRM: **Token\_eSign** |
| Body | { 	"DataToBeDisplayed": "Lời nhắn ký số", 	"UserId": "UserID", //thông tin lấy ở api access\_token 	"CertAlias": "cert\_id", 	"Documents": \[                {     "DocumentId": "document\_id",                      "FileToSign": “digest", //thông tin lấy ở bước [Hash file](#tạo-hash-của-file)                      "DocumentName": "document\_name",     }\] } |
| Response | {    "transactionId": "transaction\_id" //id của lượt ký } |
| Lưu ý | Để gọi được api ký hash này, người dùng cần phải được thiết lập kết nối tài khoản CKS từ xa MISA eSign trước.  Nếu người dùng chưa thực hiện kết nối, ứng dụng tích hợp cần phải có thông báo và hướng dẫn để người dùng thực hiện. Thiết lập [tại đây](https://misajsc.amis.vn/wesign/setting/individual/connectesign) Lưu lại Response để thực hiện việc lấy trạng thái ký |

   ## 

   6. ## Lấy trạng thái ký tài liệu {#lấy-trạng-thái-ký-tài-liệu}

| Thông tin | Diễn giải |
| ----- | ----- |
| URL | {[APIUrl](#mô-tả-về-api)}/external/esrm/service/signing/api/v1/Signing/status/**transaction\_id** transaction\_id: id lượt ký ở bước [Ký hash](#ký-hash) |
| Method | Get |
| Header | x-clientId: ClientId x-clientKey: ClientKey AuthorizationRM: **Token\_eSign** |
| Response | { "status": "status\_sign", 	"errorCode": "error\_code", 	"errorDescription": "error\_description", 	"signatures": \[                    {                      "signature": "signature\_data"                    }                 \] } |
| Mô tả đầu ra | **Tham số đầu ra ([Sign\_status](#thông-tin-sign_status))** |
| Lưu ý | Danh sách dữ liệu chữ ký signature\_data sẽ trả về theo đúng thứ tự mà danh sách file ký truyền lên tại API ký hash Chỉ trả về dữ liệu chữ ký khi tài liệu được ký thành công (status \= SUCCESS) Ứng dụng tích hợp sẽ sử dụng dữ liệu chữ ký (signatures.signature) và gắn vào file để trả về cho người dùng cuối  |

      ## 

   7. ## Gắn chữ ký vào file  {#gắn-chữ-ký-vào-file}

| Thông tin | Diễn giải |
| :---- | :---- |
| URL | {[APIUrl](#mô-tả-về-api)}/external/esrm/service/document/api/v1/documents/attachment |
| Method | POST |
| Header | x-clientId: ClientId x-clientKey: ClientKey AuthorizationRM: **Token\_eSign** |
| Body | { 	"certificate": "{[certificate](#lấy-thông-tin-chi-tiết-của-một-cts)}", 	"certificateChain": "{[certiticateChain](#lấy-thông-tin-chi-tiết-của-một-cts)}", 	"pdfDocs": \[Attachment\], 	"xmlDocs": \[Attachment\], 	"wordDocs": \[Attachment\], 	"excelDocs": \[Attachment\] } |
| Response | { 	"pdfDocs": \[Attachmented\], 	"xmlDocs": \[Attachmented\], 	"wordDocs": \[Attachmented\], 	"excelDocs": \[Attachmented\] } |
| Mô tả tham số | Xem tại Sheet: Tham số đầu vào ([Attachment](#thông-tin-attachment)) Xem tại Sheet: Tham số đầu ra ([Attachmented](#thông-tin-attachmented)) |
| Lưu ý | Request body là những gì api [4.10. Lấy hash của file](#tạo-hash-của-file) trả về đi kèm với trường signature chính là chữ ký lấy được từ API [4.8. Lấy trạng thái ký tài liệu](#lấy-trạng-thái-ký-tài-liệu) (signature\_data). Hoặc có thể lấy từ kết quả của Webhook [4.9. Webhook nhận trạng thái ký số](#webhook-nhận-trạng-thái-ký-số) |

8. ## Webhook nhận trạng thái ký số {#webhook-nhận-trạng-thái-ký-số}

Đối tác xây dựng cổng nhận webhook theo cấu trúc

| Thông tin | Diễn giải |
| :---- | :---- |
| URL | URL mà KH đăng ký nhận thông tin từ MISA |
| Method | Post |
| Body | {     "messageId": "message\_id",     "clientId": "client\_id",     "extraData": {},     "status": "status",     "errorCode": "error\_code",     "transactionId": "transaction\_id",     "signatures":      \[          {              "documentId": "document\_id",              "signature": "signature",          }     \] } |
| Response | {     "errorCode": "status\_sign",     "devMsg": "error\_code",     "userMsg": "error\_description" } |
| Lưu ý | Danh sách dữ liệu chữ ký signature sẽ trả về theo đúng thứ tự mà danh sách file ký truyền lên tại API ký hash. Chỉ trả về dữ liệu chữ ký signatures khi tất cả tài liệu được ký thành công. Ứng dụng tích hợp sẽ sử dụng dữ liệu chữ ký này và gắn vào file để trả về cho người dùng cuối |

# 

4. # Mô tả đối tượng của eSign {#mô-tả-đối-tượng-của-esign}

   1. ## Thông tin has file {#thông-tin-has-file}

      1. ### Thông tin Pdf, Docs, Excel hash file {#thông-tin-pdf,-docs,-excel-hash-file}

| Tên trường | Kiểu dữ liệu | Bắt buộc | Mô tả |
| ----- | ----- | ----- | ----- |
| DocumentId | String | x | ID của tài liệu ký |
| FileToSign | String | x | Content của file đã được base64 |
| SignatureInfo | [SignatureInfo](#thông-tin-signatureinfo) | x | Thông tin chữ số trên file |

      2. ### Thông tin XML hash file {#thông-tin-xml-hash-file}

| Tên trường | Kiểu dữ liệu | Bắt buộc | Mô tả |
| ----- | ----- | ----- | ----- |
| DocumentId | String | x | ID của tài liệu ký |
| FileToSign | String | x | Content của file XML |
| SignatureInfo | [SignatureInfo](#thông-tin-signatureinfo) | x | Thông tin chữ số trên file |

   2. ## Thông tin SignatureInfo {#thông-tin-signatureinfo}

| Tên trường | Kiểu dữ liệu | Bắt buộc | Mô tả |
| ----- | ----- | ----- | ----- |
| TextColor | Integer |  | Mã màu của văn bản |
| PositionX | Integer |  | Tọa độ X |
| PositionY | Integer |  | Tọa độ Y |
| Width | Integer |  | Độ rộng |
| Height | Integer |  | Độ cao |
| FontSize | Integer |  | Kích thước văn bản |
| FontData | String |  | Fontfamy của văn bản, dạng base64 font file |
| SignatureImage | String |  | Ảnh chữ ký của tài khoản, dạng base64 (sẽ được resize fit theo khung ký, người dùng tự tạo ảnh này) |
| Page | Integer |  | Trang hiển thị chữ ký |
| SignatureName | String | x | Tên người ký |
| HashAlgorithm | String | x | Thuật toán băm file: SHA256 |
| LogoImage | String | x | Ảnh logo, dang base64 |
| SignatureDescription | [SignatureDescription](#thông-tin-signaturedescription) | x | Diễn giải ký số |
| RenderingMode | Integer | x | Chế độ hiển thị (logo và ảnh ký) khi hash 0: Chỉ hiện thị diễn giải 1: Hiển thị cả diễn giải và ảnh 2: Chỉ hiển thị ảnh |
| SignaturePosInfos | List\<[SignaturePosInfos](#thông-tin-signatureposinfos)\> |  | Danh sách chữ ký hiển thị ở trang khác |

   3. ## Thông tin SignatureDescription {#thông-tin-signaturedescription}

| Tên trường | Kiểu dữ liệu | Bắt buộc | Mô tả |
| ----- | ----- | ----- | ----- |
| SignedBy | String | x | Ký bởi |
| ShowSignedDate | Boolean |  | Hiển thị ngày ký |
| Location | String | x | Vị trí, nơi ký, VD: Hà Nội |
| Reason | String | x | Lý do/diễn giải việc ký |
| Contact | String | x | Liên hệ ký |
| DisplayText | String |  | Thông tin ký tự custom, xuống dòng bằng ký tự ‘\\n’ |

   4. ## Thông tin Sign hash {#thông-tin-sign-hash}

| Tên trường | Kiểu dữ liệu | Bắt buộc | Mô tả |
| ----- | ----- | ----- | ----- |
| DataToBeDisplayed | String | x | Dữ liệu hiển thị trên app mobile khi xác thực ký (có thể truyền lên dạng HTML) |
| UserId | String | x | Id người dùng, lấy ở **Cert.userId** |
| CertAlias | String | x | Id chứng thư số, lấy ở **Cert.keyAlias** |
| Documents | [Document](#thông-tin-document) | x | Danh sách tài liệu muốn hash |

   5. ## Thông tin Document {#thông-tin-document}

| Tên trường | Kiểu dữ liệu | Bắt buộc | Mô tả |
| ----- | ----- | ----- | ----- |
| DocumentId | String | x | Id tài liệu \- lấy từ bước hash file **Tối đa 36 ký tự** |
| FileToSign | String | x | Content của file đã được hash, là **digest** ở API Hash file |
| DocumentName | String | x | Tên văn bản **Tối đa 100 ký tự** |

      ## 

   6. ## Thông tin Attachment {#thông-tin-attachment}

Thực hiện ký loại tài liệu nào thì loại đó bắt buộc

| Tên trường | Kiểu dữ liệu | Bắt buộc | Mô tả |
| ----- | ----- | ----- | ----- |
| PdfDocs | List\<[Doc\_Attackment](#thông-tin-doc_attackment)\> |  | Thông tin ký số của loại văn bản PDF |
| XmlDocs | List\<[Doc\_Attackment](#thông-tin-doc_attackment)\> |  | Thông tin ký số của loại văn bản XML |
| ExcelDocs | List\<[Doc\_Attackment](#thông-tin-doc_attackment)\> |  | Thông tin ký số của loại văn bản Excel |
| WordDocs | List\<[Doc\_Attackment](#thông-tin-doc_attackment)\> |  | Thông tin ký số của loại văn bản Word |

7. ## Thông tin Doc\_Attackment {#thông-tin-doc_attackment}

| Tên trường | Kiểu dữ liệu | Bắt buộc | Mô tả |
| ----- | ----- | ----- | ----- |
| signature | String | x | Lấy từ API Status (signatures.signature) |
| documentId | String | x | Lấy từ API hash file (documentId) |
| documentBytes | String | x | Lấy từ API hash file (documentBytes) |
| digest | String | x | Lấy từ API hash file (digest) |
| mainDom | String | x | Lấy từ API hash file (documentId) (bắt buộc với Excel và Word) |
| signatureName | String | x | Lấy từ API hash file (SignatureName) |
| sh | String | x | Lấy từ API hash file (sh) |
| signatureId | String | x | Lấy từ API hash file (signatureId) (trừ pdf) |
| documentHash | String | x | Lấy từ API hash file (documentHash) |

   8. ## Thông tin SignaturePosInfos {#thông-tin-signatureposinfos}

| Tên trường | Kiểu dữ liệu | Bắt buộc | Mô tả |
| ----- | ----- | ----- | ----- |
| positionX | Integer | x | Tọa độ X |
| positionY | Integer | x | Tọa độ Y |
| width | Integer | x | Độ rộng |
| height | Integer | x | Độ cao |
| page | Integer | x | Trang hiển thị chữ ký |

   9. ## Thông tin eSign-login {#thông-tin-esign-login}

| Tên trường | Kiểu dữ liệu | Mô tả |
| ----- | ----- | ----- |
| status | [status](#thông-tin-status) | Trạng thái kết quả của việc call API |
| data | [data\_token](#thông-tin-data_token) | Dữ liệu của dữ liệu trả về khi đăng nhập thành công |

   10. ## Thông tin Status {#thông-tin-status}

| Tên trường | Kiểu dữ liệu | Mô tả |
| ----- | ----- | ----- |
| type | String | Trạng thái response (success/ fail) |
| code | Integer | Status code của Request |
| message | String | Thông báo |
| error | Boolean | Đánh dấu có lỗi |
| errorCode | Integer | Mã lỗi (xảy ra khi error \= true) |

   11. ## Thông tin data\_token {#thông-tin-data_token}

| Tên trường | Kiểu dữ liệu | Mô tả |
| ----- | ----- | ----- |
| accessToken | String | Mã accessToken của hệ thống MISAID |
| remoteSigningAccessToken | String | Mã accessToken của ứng dụng eSign (hạn dùng 60p) |
| tokenType | String | Kiểu của token |
| expiresIn | Integer | Thời hạn của token |
| refreshToken | String | Mã làm mới Token, dùng khi remoteSigningAccessToken hết hạn |
| user | [User](#thông-tin-user) | Thông tin tài khoản |

   12. ## Thông tin User {#thông-tin-user}

| Tên trường | Kiểu dữ liệu | Mô tả |
| ----- | ----- | ----- |
| id | String | ID người dùng (ID đăng nhập) |
| email | String | Email người dùng |
| phoneNumber | String | Số điện thoại người dùng |
| firstName | String | Họ đệm |
| lastName | String | Tên người dùng |
| username | String | Tên đăng nhập tài khoản |

   13. ## Thông tin ResponseError {#thông-tin-responseerror}

| Tên trường | Kiểu dữ liệu | Mô tả |
| ----- | ----- | ----- |
| error | String | Mô tả lỗi |
| errorCode | String | Mã lỗi |
| devMsg | String | Thông tin lỗi cho dev |
| userMsg | String | Thông tin lỗi cho người dùng |

   14. ## Thông tin Cert {#thông-tin-cert}

| Tên trường | Kiểu dữ liệu | Mô tả |
| ----- | ----- | ----- |
| userId | Guid | Id người dùng |
| keyAlias | Guid | Id chữ ký số (là CertAlias của api Ký, là SignatureId của api attack) |
| appName | String | Tên ứng dụng |
| keyStatus | String | Mã trạng thái (ACTIVE, INACTIVE) |
| certificate | String | Raw data của chứng thư (dạng base64) |
| certiticateChain | List\<String\> | Chuỗi chứng thư, gồm 3 chuỗi ký tự chain\_data\_1: là chứng thư ký chain\_data\_2: là intermediate cert của MISA CA, chain\_data\_3: là cert root do NEAC cấp. |
| certStatus | String | Mã trạng thái (ACTIVE, INACTIVE) |
| effectiveDate | DateTime | Ngày bắt đầu sử dụng |
| expirationDate | DateTime | Ngày kết thúc sử dụng |
| emailName | String | email tài khoản |
| isAutoSign | Boolean | Tự động ký |

       ## 

   15. ## Thông tin đầu ra của API hash file {#thông-tin-đầu-ra-của-api-hash-file}

       1. ### Thông tin PdfDocs {#thông-tin-pdfdocs}

| Tên trường | Kiểu dữ liệu | Mô tả |
| ----- | ----- | ----- |
| documentId | String | ID của tài liệu ký |
| documentBytes | String | Dữ liệu của file dạng byte |
| documentHash | String | Dữ liệu của file được Hash |
| sh | String | Dữ liệu của tài liệu được Hash, sử dụng làm đầu vào của API gắn CTS |
| signatureName | String | Tên người ký |
| digest | String | Dữ liệu của tài liệu được Hash, sử dụng làm đầu vào của API ký Hash |

       2. ### Thông tin XmlDocs {#thông-tin-xmldocs}

| Tên trường | Kiểu dữ liệu | Mô tả |
| ----- | ----- | ----- |
| documentId | String | ID của tài liệu ký |
| document | String | Dữ liệu của file dạng text |
| signatureId | String | Id người ký |
| digest | String | Dữ liệu của tài liệu được Hash, sử dụng làm đầu vào của API ký Hash |
| sh | String | Dữ liệu của tài liệu được Hash, sử dụng làm đầu vào của API gắn CTS |

       3. ### Thông tin ExcelDocs {#thông-tin-exceldocs}

| Tên trường | Kiểu dữ liệu | Mô tả |
| ----- | ----- | ----- |
| documentId | String | ID của tài liệu ký |
| documentBytes | String | Dữ liệu của file dạng byte |
| signatureId | String | Id người ký |
| digest | String | Dữ liệu của tài liệu được Hash, sử dụng làm đầu vào của API ký Hash |
| mainDom | String | Dữ liệu mainDon của tài liệu, sử dụng làm đầu vào của API gắn CTS |

          ### 

       4. ### Thông tin WordDocs {#thông-tin-worddocs}

| Tên trường | Kiểu dữ liệu | Mô tả |
| ----- | ----- | ----- |
| documentId | String | ID của tài liệu ký |
| documentBytes | String | Dữ liệu của file dạng byte |
| signatureId | String | Id người ký |
| digest | String | Dữ liệu của tài liệu được Hash, sử dụng làm đầu vào của API ký Hash |
| mainDom | String | Dữ liệu mainDon của tài liệu, sử dụng làm đầu vào của API gắn CTS |

   16. ## Thông tin Sign\_status {#thông-tin-sign_status}

| Tên trường | Kiểu dữ liệu | Mô tả |
| ----- | ----- | ----- |
| status | String | \- Trạng thái ký (PENDING, SUCCESS, FAILED). \- Trong trường hợp ký bó, ký lô. Trạng thái SUCCESS sẽ được trả về khi tất cả các file đã được ký thành công. |
| errorCode | String | Mã lỗi |
| errorDescription | String | Mô tả chi tiết lỗi |
| transactionId | String | Id lượt ký |
| signatures | List\<[signature](#thông-tin-signature)\> | Danh sách thông tin ký số |

   17. ## Thông tin Signature {#thông-tin-signature}

| Tên trường | Kiểu dữ liệu | Mô tả |
| ----- | ----- | ----- |
| documentId | String | Id của file ký |
| signature | String | Dữ liệu chữ ký trên tài liệu |

   18. ## Thông tin Attachmented {#thông-tin-attachmented}

| Tên trường | Kiểu dữ liệu | Mô tả |
| ----- | ----- | ----- |
| documentId | String | ID của tài liệu ký đã được ký |
| document | String | File đã được gắn CKS (dạng base64 string) |

       ## 

[image1]: <data:image/png;base64,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n+yn7FOkIBW+l62ClJtHrQtSus/FtrYMUhLW5Gt1P3arSIUIUsDh22ZE2Wi7gQkYhq3aa3Dujh2UT935QzYUSeXBD5p2gA8H2WBg1SAlYchHCbsumUcHa7tuP2gXh/bmZZDS4KXbKCo5uv3g+/qhvTDobD60Z1zlLV/X6KT6HpU7LOcO+xUBzq9f5m8LUtMTFy7DfdLn5b+F+9lU968apCqH9mSazO9frwcpPYdJpu8SpPTfZFqvxvmqZL3trA1SYXVyjfo6AaQlSg/mgwApOUt71TBwyOx5RfWJZ1JW9/YqCMN90MO5m3CyOZC27iNK4CwDE7BvtFcAQCxRRhQGJqSE9oohoSIFpC3KiMLAhJTQXjEktEcgbVF6MB8ESAntFX157LHH6pMAJC7KiMLAhJTQXtGXixcvmmvXrtUnA0hYlBGFgQkpob2iL1KRetGLXmTboASqmzdv0h6BxEXpwXwQICXaXm/dumX+8pe/mGeffdb84Q9/MM8884z59a9/bX71q1/ZAe7nP/+5+elPf2p+8pOfmB/96Efme9/7nnniiSfM448/br7xjW+Yr33ta+arX/2q+cpXvmI+//nPm8985jPmE5/4hHn44YfNhz70IfP+97/fvPvd7zbvete7zDve8Q7z1re+1bz5zW82b3zjG82DDz5orl69al73uteZ17zmNeaBBx4wL3vZy8x9991n7rnnHnPnnXeaO+64w1y4cMFcvnzZvOQlLzEvfelLzSte8Qrz6le/2rz2ta81V65cMa9//evNG97wBvOmN73JvOUtbzFvf/vbzTvf+U4zHo/N+973PvPBD37QfOQjHzEf//jHzac//Wnzuc99znz5y1+2Jzg/+uij5utf/7od3L/97W+b7373u+aHP/yh+fGPf2yefPJJ87Of/cz84he/ML/85S/NU089ZZ5++mnz+9//3vzxj380f/7zn81f//pX8/e//90899xz5p///Kf597//bf7zn//UftoINQUpTjYX5fXY5BIj/spoay/DoRfFlWuG7WrdMpWLsRZXyp+ZbMvLWeD4tLemHRCkkBLaK/rCob12NkBNyous6i2B9KK08r1ctFbp8/IG0vKav+iqXBx24i7oWlxkNbiWWXHBVj8t3FZTkArnt1f5b7kfJY5TlBGFgQkpob2iL00nm1ORKoV3CbDfSSDyN57W5zqHXs1e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>

[image2]: <data:image/png;base64,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>

[image3]: <data:image/png;base64,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>

[image4]: <data:image/png;base64,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>