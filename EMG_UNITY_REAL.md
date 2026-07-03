# EMG → Unity 3D Runner (Production / Delivery)

## For end users (testers)

1. BITalino on + Bluetooth paired
2. Run the game `.exe`
3. Calibrate (REST → MVC) → **Start Game**

**No Python, no pip, no terminal.** The bundled `emg_backend.exe` ships inside the game.

## For developers (one-time before shipping)

### 1. Build the bundled EMG backend

Unity menu: **EMG → Build Bundled EMG Backend (no Python for users)**

Or PowerShell:

```powershell
cd path\to\projectGame-Bitalino
.\tools\build_emg_backend.ps1
```

This creates `3DRunner-main/Assets/StreamingAssets/EMG/emg_backend.exe` (+ `_internal` support files).

Requires Python on **your** PC only: `pip install -r requirements.txt pyinstaller`

After changing `Python Code/emg_core.py`, rebuild the bundle.

### 2. Unity settings

- **GameManager** → **EMG Process Launcher** → **Bitalino Port** (e.g. COM6)
- **Backend Path Override**: leave **empty**
- **EMG → Setup EMG UI** if panels are missing
- **EMG → Clean Scene (Remove Duplicate EMG UI)** if duplicate Start panels exist

### 3. Unity build

**File → Build Settings** → Windows → Build

Ship the **entire** build folder (`exe` + `_Data` + `StreamingAssets/EMG/emg_backend.exe`).

## How it starts

| Environment | What runs |
|-------------|-----------|
| Built game | `StreamingAssets/EMG/emg_backend.exe` |
| Unity Editor | `emg_backend.exe` (Editor fallback: `python code.py` only if exe missing) |

## Flow

```
Play → emg_backend.exe → UDP JSON :5055 → EMGInputBridge
  → Calibration UI (CALIB_* packets)
  → CALIB_DONE → Start Game panel (GameManager)
  → Start Game → lane steps + in-game debug graphs
```

## UDP JSON protocol (port 5055)

| Phase | `command` values |
|-------|------------------|
| Connection | `STATUS` |
| Calibration | `CALIB_PREP`, `CALIB_REST`, `CALIB_MVC`, `CALIB_PHASE_DONE`, `CALIB_DONE` |
| Gameplay | `LEFT`, `RIGHT`, `NONE`, `STOP` |

Calibration completes only on `CALIB_DONE` (not on gameplay packets).

## What users still need

- BITalino device + Windows Bluetooth/COM driver (cannot be bundled)
- Correct COM port set **before** you build the game
