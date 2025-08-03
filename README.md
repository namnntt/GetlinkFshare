Cài đặt acc Fshare VIP
-> Xóa các phiên đăng nhập
-> Tự động tải xuống khi truy cập liên kết tệp tin
<img width="2517" height="1107" alt="image" src="https://github.com/user-attachments/assets/b1c9a8ab-8069-4073-a095-78fdb822cc31" />
<img width="1322" height="514" alt="image" src="https://github.com/user-attachments/assets/d8a0a9a9-d0dd-47c8-85e3-3a018e95cb38" />


Đây là hướng dẫn chi tiết deploy ứng dụng cho Linux ->
Xem thêm file Programs và file appsettings.Production để biết chi tiết comment:

Tổng quan kiến trúc
●	Kestrel: Web server của ASP.NET Core sẽ chạy ứng dụng của chúng ta, nhưng nó sẽ chỉ lắng nghe các kết nối từ bên trong server (ví dụ: http://localhost:5000).
●	Nginx: Sẽ đóng vai trò là "người gác cổng". Nó sẽ nhận tất cả các yêu cầu từ internet (port 80 và 443), xử lý HTTPS, và sau đó chuyển tiếp các yêu cầu đó đến cho Kestrel.
●	Systemd: Sẽ quản lý ứng dụng của chúng ta như một dịch vụ, đảm bảo nó luôn chạy nền và tự khởi động lại khi cần.
●	Certbot: Công cụ tự động hóa việc lấy và gia hạn chứng chỉ SSL/TLS từ Let's Encrypt.
Bước 0: Chuẩn bị
1.	Server Ubuntu 22.04: Một server vật lý hoặc VPS đã được cài đặt Ubuntu 22.04 và bạn có quyền truy cập sudo.
2.	Domain: Domain getlinkfsharevoz.ddns.net đã trỏ đến địa chỉ IP public của server Ubuntu.
3.	No-IP Client: Đảm bảo No-IP DUC client đã được cài đặt và cấu hình trên server Ubuntu để IP luôn được cập nhật.
Bước 1: Cài đặt các thành phần cần thiết trên Ubuntu
Kết nối vào server của bạn qua SSH và chạy các lệnh sau:
1.0. Cài đặt các thư viện phụ thuộc cho Trình duyệt Headless (QUAN TRỌNG)
PuppeteerSharp cần các thư viện này để có thể chạy trình duyệt Chromium.
sudo apt-get update && \
  sudo apt-get install -y \
  ca-certificates \
  fonts-liberation \
  libasound2 \
  libatk-bridge2.0-0 \
  libatk1.0-0 \
  libc6 \
  libcairo2 \
  libcups2 \
  libdbus-1-3 \
  libexpat1 \
  libfontconfig1 \
  libgbm1 \
  libgcc1 \
  libgconf-2-4 \
  libgdk-pixbuf2.0-0 \
  libglib2.0-0 \
  libgtk-3-0 \
  libnspr4 \
  libnss3 \
  libpango-1.0-0 \
  libpangocairo-1.0-0 \
  libstdc++6 \
  libx11-6 \
  libx11-xcb1 \
  libxcb1 \
  libxcomposite1 \
  libxcursor1 \
  libxdamage1 \
  libxext6 \
  libxfixes3 \
  libxi6 \
  libxrandr2 \
  libxrender1 \
  libxss1 \
  libxtst6 \
  lsb-release \
  wget \
  xdg-utils

1.1. Cài đặt ASP.NET Core Runtime
# Đăng ký khóa và nguồn cấp dữ liệu gói của Microsoft
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# Cài đặt ASP.NET Core Runtime (chúng ta đang dùng .NET 8)
sudo apt-get install -y aspnetcore-runtime-8.0

1.2. Cài đặt Nginx
sudo apt-get install -y nginx

1.3. Cấu hình Tường lửa (UFW)
# Cho phép Nginx Full (bao gồm cả port 80 cho HTTP và 443 cho HTTPS)
sudo ufw allow 'Nginx Full'

# Kích hoạt tường lửa nếu nó chưa chạy
sudo ufw enable

Bước 2: Chuẩn bị và Publish ứng dụng
2.1. Chỉnh sửa cấu hình Kestrel
●	Mở file appsettings.json trong project của bạn.
●	Xóa toàn bộ phần cấu hình "Kestrel".
2.2. Publish ứng dụng cho Linux
1.	Mở terminal trong thư mục gốc của project API.
2.	Chạy lệnh:
dotnet publish -c Release -r linux-x64 --self-contained false -o ./publish_linux

Bước 3: Deploy ứng dụng lên Server
1.	Tạo thư mục trên server:
sudo mkdir /var/www/fshare_api

2.	Copy file đã publish lên server:
scp -r ./publish_linux/* your_username@your_server_ip:/var/www/fshare_api/

3.	Cấp quyền sở hữu và thực thi:
sudo chown -R www-data:www-data /var/www/fshare_api
sudo chmod +x /var/www/fshare_api/GetlinkFshare

Bước 4: Tạo Systemd Service File
1.	Tạo và mở file service bằng nano:
sudo nano /etc/systemd/system/kestrel-fshareapi.service

2.	Dán toàn bộ nội dung sau vào file:
[Unit]
Description=Fshare Getlink API Service

[Service]
WorkingDirectory=/var/www/fshare_api
ExecStart=/var/www/fshare_api/GetlinkFshare
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=fshare-api
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
Environment=HOME=/var/www/fshare_api

[Install]
WantedBy=multi-user.target

3.	Lưu file và thoát nano (Ctrl + X, Y, Enter).
4.	Kích hoạt và khởi động dịch vụ:
sudo systemctl daemon-reload
sudo systemctl enable kestrel-fshareapi.service
sudo systemctl start kestrel-fshareapi.service
sudo systemctl status kestrel-fshareapi.service

Nếu thấy dòng chữ Active: active (running) màu xanh lá, xin chúc mừng, API của bạn đã chạy thành công!
Bước 4.1: Gỡ lỗi Dịch vụ không chạy được
Nếu dịch vụ vẫn không chạy được, hãy sử dụng các lệnh sau để xem log:
●	Để xem log trong thời gian thực:
sudo journalctl -u kestrel-fshareapi.service -f --no-pager

●	Để xem toàn bộ lịch sử log:
sudo journalctl -u kestrel-fshareapi.service --no-pager

Bước 5: Cấu hình Nginx làm Reverse Proxy (ĐÃ CẬP NHẬT)
1.	Tạo file cấu hình Nginx:
sudo nano /etc/nginx/sites-available/getlinkfsharevoz.ddns.net

2.	Dán nội dung sau vào file. Lưu ý dòng proxy_buffering off; đã được thêm vào.
server {
    listen 80;
    server_name getlinkfsharevoz.ddns.net;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # *** ĐÃ THÊM: Tắt tính năng buffering để stream trực tiếp ***
        proxy_buffering off;
    }
}

3.	Kích hoạt cấu hình:
sudo ln -s /etc/nginx/sites-available/getlinkfsharevoz.ddns.net /etc/nginx/sites-enabled/

4.	Kiểm tra và khởi động lại Nginx:
sudo nginx -t
sudo systemctl restart nginx

Bước 6: Cài đặt HTTPS với Let's Encrypt
1.	Cài đặt Certbot:
sudo apt-get install -y certbot python3-certbot-nginx

2.	Chạy Certbot:
sudo certbot --nginx -d getlinkfsharevoz.ddns.net

○	Nó sẽ hỏi email và yêu cầu đồng ý điều khoản.
○	Khi được hỏi có muốn tự động chuyển hướng HTTP sang HTTPS không, hãy chọn 2 (Redirect). Certbot sẽ tự động cập nhật file cấu hình Nginx của bạn.
Hoàn tất!
Bây giờ bạn có thể truy cập https://getlinkfsharevoz.ddns.net và ứng dụng của bạn đã được deploy một cách hoàn chỉnh, an toàn và chuyên nghiệp trên Ubuntu!
