# HttpGetUrl

**Hget**, short for **HttpGetUrl**, is a network download service that supports direct downloads via the HTTP/HTTPS protocols, analyse and download videos and playlists from Twitter (x.com) or YouTube.

## Usage

Simply paste the resource URL and submit. The task will be automatically queued.

## I. Installation on Ubuntu 24.04 / Debian 12

### Preparing the Environment

1. **Add the Microsoft package signing key**

   ```sh
   # for ubuntu 24.04, required by powershell
   wget -q https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb
   # for debian 12, required by netcore-runtime and powershell
   wget -q https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb

   dpkg -i packages-microsoft-prod.deb
   rm packages-microsoft-prod.deb
   ```

2. **Install dotnetcore**

   - aspnetcore-runtime-9.0: ASP.NET Core runtime
   - powershell: for installing Playwright.net headless browser
   - python3: as yt-dlp runtime
   - ffmpeg: as codecs
   - libmsquic: for turn on HTTP/3(QUIC)
   ```sh
   apt-get update
   apt-get install -y aspnetcore-runtime-9.0 powershell python3 ffmpeg libmsquic
   ```

3. **Prepare folders**
   Assuming the installation directory is `/usr/local/hget`, prepare the following folders:
   - `/usr/local/hget/.hg` : Stores hget configurations, `Playwright.net` browser configurations, and the `yt-dlp` executable file.

### II. Compilation and Deployment

4. **Compile**
   Publish this project with the target framework `net9.0` and target runtime `linux-x64`.

5. **Upload binaries**
   Upload all compiled binaries to the `/usr/local/hget` folder. Compress the `.playwright` directory (which contains numerous JS files) before uploading if necessary. Then grant execute permissions.
   ```sh
   cd /usr/local/hget/.playwright/node/linux-x64
   chmod +x node
   ```

6. **Configuration file**
   Configure the production environment in `appsettings.Production`. This is an example, you need to modify parameters as your appropriate.
   ```json
   {
     "https_port": 443,
     "Kestrel": {
       "EndpointDefaults": {
         "Protocols": "Http1AndHttp2AndHttp3"
       },
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
       // a multiple lines CIDR format (e.g., 1.2.4.0/24, 2001:250::/35) file,
       // support empty line and comment line start with #
       // put in directory `.hg`.
       "ByPassList": ["bypass.txt"],
       "MaxConcurrentDownloads": 3,
       "MaxRetry": 5
     }
   }
   ```

7. **Register service**
   Copy `hget.service` from the code directory to `/etc/systemd/system/`, then run:
   ```sh
   systemctl enable hget
   ```

### III. Installing Components

8. **Playwright.net**
   ```sh
   cd /usr/local/hget
   pwsh playwright.ps1 install --with-deps
   ```

9. **yt-dlp**
   ```sh
   cd /usr/local/hget/.hg
   wget -q https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp
   chmod +x yt-dlp
   ```
   If you're having parsing issues with yt-dlp, try updating `yt-dlp` via `yt-dlp -U`

### IV. Testing

10. **Test run**
    ```sh
    cd /usr/local/hget/
    dotnet HttpGetUrl.dll
    ```

11. **Update token**
    Supports importing `cookie.json` files exported by the Firefox Cookie Manager plugin. Update cookies on the `/tokens.htm` page. Some resources require your login credentials to access them.

12. **Try download urls**
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

13. **Everything is ready**
    ```sh
    systemctl start hget
    ```

## License

This project is licensed under the MIT License.
