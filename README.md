# HttpGetUrl

**Hget**, short for **HttpGetUrl**, is a network download service that supports direct downloads via the HTTP/HTTPS protocols, analyse and download videos and playlists from Twitter (x.com) or YouTube.

## Usage

Simply paste the resource URL and submit. The task will be automatically queued.

## I. Installation on Ubuntu 24.04

### Preparing the Environment

1. **Install dotnetcore**
   ```sh
   apt-get update
   apt-get install -y aspnetcore-runtime-8.0
   ```

2. **Prepare folders**
   Assuming the installation directory is `/usr/local/hget`, prepare the following folders:
   - `/usr/local/hget/.hg` : Stores hget configurations, `Playwright.net` browser configurations, and the `yt-dlp` executable file.

### II. Compilation and Deployment

3. **Compile**
   Publish this project with the target framework `net8.0` and target runtime `linux-x64`.

4. **Upload binaries**
   Upload all compiled binaries to the `/usr/local/hget` folder. Compress the `.playwright` directory (which contains numerous JS files) before uploading if necessary. Then grant execute permissions.
   ```sh
   cd /usr/local/hget/.playwright/node/linux-x64
   chmod +x node
   ```

5. **Configuration file**
   Configure the production environment in `appsettings.Production`. This is an example, you need to modify parameters as your appropriate.
   ```json
   {
     "https_port": 443,
     "Kestrel": {
       "Endpoints": {
         "Http": {
           "Url": "http://*:80"
         },
         "HttpsInlineCertFile": {
           "Url": "https://*:443",
           "Certificate": {
             "Path": "/path/to/.crt",
             "KeyPath": "/path/to/.key"
           }
         }
       }
     },
     "Hget": {
       "Proxy": "socks5://127.0.0.1:1081",
       "MaxConcurrentDownloads": 3
     }
   }
   ```

6. **Register service**
   Copy `hget.service` from the code directory to `/etc/systemd/system/`, then run:
   ```sh
   systemctl enable hget
   ```

### III. Installing Components

7. **PowerShell (for installing Playwright.net headless browser)**
   ```sh
   apt-get update
   wget -q https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb
   dpkg -i packages-microsoft-prod.deb
   rm packages-microsoft-prod.deb
   apt-get update
   apt-get install -y powershell
   pwsh
   ```

8. **Playwright.net**
   ```sh
   cd /usr/local/hget
   pwsh playwright.ps1 install
   pwsh playwright.ps1 install-deps
   ```

9. **yt-dlp**
   ```sh
   cd /usr/local/hget/.hg
   wget -q https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp
   chmod +x yt-dlp
   ```

10. **ffmpeg**
    ```sh
    apt-get install -y ffmpeg
    ```

### IV. Testing

11. **Test run**
    ```sh
    cd /usr/local/hget/
    dotnet HttpGetUrl.dll
    ```

12. **Update token**
    Supports importing `cookie.json` files exported by the Firefox Cookie Manager plugin. Update cookies on the `/tokens.htm` page. Some resources require your login credentials to access them.

13. **Try download urls**
    ```txt
    # Single file download
    https://github.com/FFmpeg/FFmpeg/archive/refs/heads/master.zip
    # Twitter video download
    https://x.com/elonmusk/status/1851515326581916096
    # YouTube video and playlist download
    https://youtube.com/shorts/FUoB6xVTdE8
    https://youtube.com/playlist?list=PLOGi5-fAu8bGbCMgtuuNf8t-kV28ZhnGa
    ```

### V. Start service

14. **Everything is ready**
    ```sh
    systemctl start hget
    ```

## License

This project is licensed under the MIT License.
