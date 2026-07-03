# projectGame-Bitalino

EMG-controlled **3D Runner** prototype for paediatric physiotherapy. Muscle contractions acquired through a [BITalino](https://www.bitalino.com/) device are processed by a Python backend and sent to a Unity game over UDP/JSON. The player steers the runner by activating the left or right arm.

**Demonstration video:** [Google Drive](https://drive.google.com/file/d/1tnBmxs96qQ-lpmGkyMPE0hn4EoF1c6Wj/view?usp=drive_link)

---

## Features

- BITalino EMG acquisition (left / right arm, 1000 Hz)
- Guided calibration (REST → MVC) with real-time RMS graphs
- Lane-based game control via `LEFT` / `RIGHT` / `NONE` commands
- In-game intensity bars, threshold display, and debug UI
- Automatic session recording and PDF report generation
- Bundled `emg_backend.exe` for end users (no Python install required after build)

---

## Project structure

```
projectGame-Bitalino/
├── 3DRunner-main/              # Unity project (game + EMG integration)
│   ├── Assets/
│   │   ├── Scripts/            # C# game logic and EMG components
│   │   ├── Editor/             # Unity editor tools (EMG menu)
│   │   ├── Scenes/             # Game scenes
│   │   └── StreamingAssets/EMG/  # Backend bundle (built locally)
├── Python Code/
│   └── emg_core.py             # Signal processing and command generation
├── tools/
│   └── build_emg_backend.ps1   # Build script for emg_backend.exe
├── code.py                     # Python entry point (development)
├── emg_backend_entry.py        # PyInstaller entry point
├── emg_backend.spec            # PyInstaller spec
├── requirements.txt
└── EMG_UNITY_REAL.md           # Additional technical notes
```

### Main Unity components (author's EMG layer)

| Script | Role |
|--------|------|
| `EMGProcessLauncher` | Starts `emg_backend.exe` when the game launches |
| `EMGInputBridge` | Receives UDP/JSON packets on port 5055 |
| `EMGCalibrationUI` | Calibration screens and instructions |
| `EMGStartMenuUI` | Post-calibration start menu |
| `EMGRealtimeGraph` / `EMGInputDebugUI` | Real-time graphs and command monitoring |
| `EMGIntensityBarsUI` | Left/right muscular intensity bars |
| `EMGSessionRecorder` | Session data export and PDF report |
| `PlayerController` | Maps EMG commands to lane movement |

---

## Requirements

### End users (built `.exe`)

- Windows 10/11
- BITalino device paired via Bluetooth (virtual COM port, e.g. `COM6`)
- Game build folder (`exe` + `_Data` + `StreamingAssets/EMG/emg_backend.exe`)

### Developers

| Tool | Version |
|------|---------|
| Unity | **2021.3.26f1** |
| Python | 3.9+ recommended |
| OS | Windows (COM port + PyInstaller build) |

Python packages (see `requirements.txt`):

```
numpy
scipy
bitalino
matplotlib
pyinstaller   # build only
```

---

## Quick start (end users)

1. Turn on the BITalino and pair it in Windows Bluetooth settings.
2. Note the COM port in Device Manager (e.g. `COM6`).
3. Run the game `.exe`.
4. Follow on-screen calibration: **REST** → **MVC**.
5. Click **Start Game** and control the runner with arm contractions.

No Python or terminal is needed if `emg_backend.exe` is bundled inside `StreamingAssets/EMG/`.

---

## Developer setup

### 1. Clone the repository

```powershell
git clone https://github.com/CanDorukk/projectGame-Bitalino.git
cd projectGame-Bitalino
```

### 2. Install Python dependencies

```powershell
python -m pip install --upgrade pip
python -m pip install -r requirements.txt pyinstaller
```

### 3. Build the EMG backend bundle

**Option A — PowerShell script (recommended):**

```powershell
.\tools\build_emg_backend.ps1
```

**Option B — Unity menu:**

Open `3DRunner-main` in Unity → **EMG → Build Bundled EMG Backend (no Python for users)**

This produces:

```
3DRunner-main/Assets/StreamingAssets/EMG/emg_backend.exe
3DRunner-main/Assets/StreamingAssets/EMG/_internal/
```

> `emg_backend.exe` is not committed to Git (see `.gitignore`). You must build it locally before running or shipping the game.

### 4. Open the Unity project

1. Install [Unity Hub](https://unity.com/download) and editor **2021.3.26f1**.
2. **Add** → select the `3DRunner-main` folder.
3. Open the **Game** scene under `Assets/Scenes/`.

### 5. Configure the BITalino COM port

In the Unity Hierarchy, select **GameManager** (or the object with `EMGProcessLauncher`):

| Field | Value |
|-------|-------|
| **Bitalino Port** | Your COM port (e.g. `COM6`) |
| **Unity Port** | `5055` (default) |
| **Backend Path Override** | Leave **empty** |

Set the correct COM port **before** building the final game executable.

### 6. Unity editor helpers (if needed)

| Menu item | Purpose |
|-----------|---------|
| **EMG → Setup EMG UI** | Create missing calibration / UI panels |
| **EMG → Clean Scene (Remove Duplicate EMG UI)** | Remove duplicate Start panels |

### 7. Test in the Unity Editor

Press **Play**. The launcher starts `emg_backend.exe` (or falls back to `python code.py` in the Editor if the exe is missing).

### 8. Build the Windows game

**File → Build Settings → Windows → Build**

Ship the **entire** output folder:

```
YourGame.exe
YourGame_Data/
StreamingAssets/EMG/emg_backend.exe
StreamingAssets/EMG/_internal/
```

---

## How it works

```
BITalino (Bluetooth/COM)
        ↓
emg_backend.exe  (Python: filter, RMS, calibration, commands)
        ↓  UDP JSON :5055
EMGInputBridge  (Unity)
        ↓
Calibration UI → Start Menu → PlayerController (lane movement)
        ↓
EMGSessionRecorder → session JSON + CSV → PDF report
```

### UDP JSON protocol (port 5055)

| Phase | `command` values |
|-------|------------------|
| Connection | `STATUS` |
| Calibration | `CALIB_PREP`, `CALIB_REST`, `CALIB_MVC`, `CALIB_PHASE_DONE`, `CALIB_DONE` |
| Gameplay | `LEFT`, `RIGHT`, `NONE`, `STOP` |

Calibration completes only on `CALIB_DONE` (not on gameplay packets).

---

## Troubleshooting

| Problem | Check |
|---------|-------|
| **No connection** | BITalino on and paired; correct COM port in `EMGProcessLauncher` |
| **Backend does not start** | Run `.\tools\build_emg_backend.ps1` and confirm `emg_backend.exe` exists in `StreamingAssets/EMG/` |
| **Calibration fails** | Sensors placed on left/right arm; stay relaxed during REST, contract during MVC |
| **No lane movement** | Calibration finished (`CALIB_DONE`); threshold exceeded on target arm |
| **PDF report missing** | `matplotlib` installed when building backend; check `session_report.py` in `StreamingAssets/EMG/` |

---

## Development notes

- After changing `Python Code/emg_core.py`, rebuild the backend bundle.
- The team signal-processing module is consumed as a black box; Unity only reads the agreed UDP/JSON protocol.
- Session files are saved locally by `EMGSessionRecorder` (folder path configurable in the Inspector).

For more technical detail, see [EMG_UNITY_REAL.md](EMG_UNITY_REAL.md).

---

## Credits

- Base **3D Runner** game: [chiturca/3DRunner](https://github.com/chiturca/3DRunner)
- EMG integration, calibration UI, session recording, and backend packaging: **Semahattin Can Doruk** (UBI Project course)

## License

The original 3D Runner project is under the [MIT License](3DRunner-main/LICENSE.md). EMG integration components follow the same open-source spirit of the base project.
