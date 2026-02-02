# DataGateWin.UI

**DataGateWin** is a lightweight Windows desktop UI for **DataGate OpenVPN 3 engine**.

The application provides a modern Windows (WPF) interface to:
- authenticate the user via **Google Sign-In** (browser-based OAuth),
- launch `engine.exe` with the required parameters (including an `.ovpn` profile),
- display a simple connection status (connected / disconnected),
- serve as the foundation for future features (certificates, settings, etc.).

This project is intentionally **not** distributed via Microsoft Store. It is designed for classic desktop deployment (portable or installer).

---

## Why this project exists

OpenVPN 3 Core is powerful, but end users need a friendly UI.
This project separates responsibilities:

- **engine.exe** (C++): does the VPN heavy lifting (OpenVPN 3 Core integration, drivers, networking, etc.).
- **DataGateWin** (C# / WPF): handles user interaction, authentication, configuration, and starting/stopping the engine.

This split keeps the engine focused and testable, while the UI remains easy to iterate.

---

## How it works (high-level)

1. **Startup checks**
   - The app verifies it is running with **Administrator privileges** (required for VPN drivers, routing, etc.).
   - The app loads configuration from `appsettings.json` located next to the executable.

2. **Authentication**
   - The app shows a **Login window** first.
   - Clicking *Sign in with Google* opens the system browser.
   - A local loopback HTTP listener receives the OAuth redirect with the authorization code.
   - The code can be exchanged for tokens or sent to a backend (depending on your architecture).

3. **Main UI**
   - After successful authentication, the app opens the **Main window**.
   - The main window is prepared for pages like Home, Connection, Certificates, Settings.
   - Connection logic will start/stop `engine.exe` and show status.

---

## Tech stack

- **.NET (Windows Desktop)**
- **WPF**
- **WPF-UI** for Fluent design and theming
- **Google OAuth (browser loopback flow)**

---

## Configuration

The application requires an `appsettings.json` file next to the executable.

Example:

```json
{
  "GoogleAuth": {
    "ClientId": "YOUR_GOOGLE_OAUTH_CLIENT_ID",
    "RedirectPort": 51723
  }
}
