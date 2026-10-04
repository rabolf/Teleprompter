# Teleprompter

A small, self-hosted web teleprompter built with ASP.NET Core. Type your script on your computer, open the viewer on your phone (or any device on the same network), and control the scrolling from your computer's keyboard over WebSockets.

No cloud, no accounts, no JavaScript build step: one .NET project and two static HTML pages.

## Features

- Enter your script, font size and scroll speed on a simple setup page
- Flip horizontally and/or vertically (for mirror or beam-splitter teleprompter rigs)
- Horizontal mode: a single line of text scrolling sideways
- Phone viewer with a fullscreen button, showing the text in the bottom half of the screen
- Scroll the viewer from the computer with the **Up** / **Down** arrow keys
- **Stop** button on the computer ends the session and returns to the setup page
- Late joiners and reconnecting devices pick up the current text and scroll position
- Settings are remembered in the browser between visits

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or change `TargetFramework` in `Teleprompter.csproj` to a version you have installed)
- A phone/tablet and a computer on the same network

## Getting started

```bash
git clone <your-repo-url>
cd Teleprompter
dotnet run
```

The console prints the addresses to use:

```
Teleprompter running.
  Computer: http://localhost:5000
  Phone:    http://192.168.1.3:5000/view
```

1. On the **computer**, open `http://localhost:5000`.
2. On the **phone**, open the `/view` address shown in the console. It shows "Waiting for the presenter..." until you start.
3. On the computer, paste your script, adjust the settings and press **Run**.
4. Use the **Up** / **Down** arrow keys on the computer to scroll. Hold a key to keep scrolling.
5. Press **Stop** on the computer when you're done.

If the phone can't connect, allow inbound connections on port 5000 in your firewall (on Windows, allow `dotnet`/`Teleprompter` through Windows Defender Firewall for private networks).

## Settings

| Setting | Description |
| --- | --- |
| Font size | Text size in pixels |
| Scroll speed | Distance moved per key press, 1-20 (a fraction of the font size, so all devices scroll the same amount of text) |
| Horizontal text | Render the script as one line that scrolls sideways |
| Flip horizontal | Mirror the text left-to-right |
| Flip vertical | Mirror the text top-to-bottom |

## Configuration

The server listens on `http://0.0.0.0:5000` by default. Override it with the `TELEPROMPTER_URLS` environment variable:

```bash
TELEPROMPTER_URLS=http://0.0.0.0:8080 dotnet run
```

(The addresses printed at startup assume port 5000; they are cosmetic and don't affect the actual listener.)

## How it works

- `Program.cs` serves static files and a WebSocket endpoint at `/ws`. A small in-memory hub relays messages between every connected browser and remembers the current session.
- `wwwroot/index.html` is the setup page (the computer).
- `wwwroot/prompter.html` is the teleprompter display. It is used by both the phone (`/view`) and the computer (`?role=controller`); only the controller shows the Stop button and sends key presses.

WebSocket messages (JSON):

| Message | Direction | Meaning |
| --- | --- | --- |
| `{"type":"start","config":{...}}` | client -> server | Start a session with the given settings |
| `{"type":"scroll","dir":"up"\|"down"}` | client -> server -> all | Scroll one step |
| `{"type":"stop"}` | client -> server | End the session |
| `{"type":"state","running":bool,"config":{...},"steps":n}` | server -> clients | Current session state, sent on connect, start and stop |

## Limitations

- There is no authentication: anyone on your network who can reach the server can view and control the teleprompter. Only run it on networks you trust.
- State is held in memory, so restarting the server ends the session.
- iPhone Safari doesn't support the Fullscreen API for web pages, so the Fullscreen button is hidden there. Use Share > Add to Home Screen for a full-screen view instead.
- Only one session is supported at a time.

## Contributing

Issues and pull requests are welcome. Keep changes small and focused, and make sure `dotnet build` succeeds before submitting.

## License

Add a license file (for example [MIT](https://choosealicense.com/licenses/mit/)) and reference it here.
