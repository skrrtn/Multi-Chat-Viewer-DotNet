# Multi Chat Viewer

A Windows app for viewing Twitch and Kick.com chat in real-time. No authentication required - just enter a channel name and start watching!

![Screenshot](Assets/screenshot.png)

## Features

### Chat Viewing
- **Multi-Platform**: Works with both Twitch and Kick.com
- **Real-time Messages**: See chat as it happens with auto-scroll
- **Dark Theme**: Easy on the eyes for long viewing sessions
- **No Login Required**: Connect anonymously to any public channel

### Multi-Channel Support
- **Monitor Multiple Channels**: Add channels from both platforms
- **Switch Between Channels**: Toggle which chat you're viewing
- **Background Logging**: Channels continue collecting messages even when not visible
- **Channel Statistics**: See message counts, connection status, and activity

### User Features
- **Click Usernames**: View message history for any user
- **@Mention Highlighting**: Mentions are highlighted in orange
- **User Blacklist**: Block annoying users from chat and logs
- **Search History**: Find messages from specific users

### Customization
- **Font Scaling**: Multiple size options (50%-200%)
- **Zoom with Ctrl+Scroll**: Fine-tune text size (6pt-36pt)
- **Message Database**: All messages saved locally for searching

## Installation

### Quick Start (Recommended)
1. Download `MultiChatViewer.exe` from [Releases](../../releases)
2. Double-click to run - that's it!

**No .NET installation required** - everything's included in the ~170MB download.

**Windows Security Warning**: Windows may show a "protected your PC" warning since the app isn't code-signed (certificates cost $$$). Just click "More info" → "Run anyway".

### Build from Source
Need the latest changes or want to contribute?

```powershell
git clone https://github.com/skrrtn/Twitch-Chat-Viewer-DotNet.git
cd Twitch-Chat-Viewer-DotNet
dotnet restore
dotnet build
dotnet run
```

## How to Use

1. **Launch**: Double-click `MultiChatViewer.exe` (or `dotnet run` if building from source)
2. **Add Channels**: Go to 'File' → 'Manage Channels'
3. **Enter Channel**: Type any Twitch or Kick channel name
4. **Select Platform**: Pick Twitch or Kick from the dropdown  
5. **Start Watching**: Click "Add" then "Enable" to view that channel's chat

### Tips
- **Switch Channels**: Use the "Enable" button to change which chat you're viewing
- **Multiple Channels**: Add several channels - they'll all log messages in the background
- **User History**: Click any username to see their message history
## Development

Built with C# and WPF using these libraries:
- **KickLib** (1.4.3) - Kick.com API integration  
- **Microsoft.Data.Sqlite** (9.0.6) - Message storage
- Plus standard .NET logging and DI packages

## License

MIT License - see the full text below or in the LICENSE file.

Special thanks to the [KickLib](https://github.com/Bukk94/KickLib) team for making Kick.com integration possible!

<details>
<summary>MIT License (click to expand)</summary>

```
MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

</details>
