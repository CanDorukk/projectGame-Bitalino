"""
PyInstaller entry point for the bundled EMG backend (no system Python required).
"""

import argparse
import socket
import sys
from pathlib import Path

if getattr(sys, "frozen", False):
    _ROOT = Path(sys._MEIPASS)
else:
    _ROOT = Path(__file__).resolve().parent
    sys.path.insert(0, str(_ROOT / "Python Code"))

sys.path.insert(0, str(_ROOT))

from emg_core import (  # noqa: E402
    DEFAULT_PORT,
    EMGDevice,
    connect_device_with_status,
    run_game_stream,
    send_status,
)

UNITY_IP = "127.0.0.1"
UNITY_PORT = 5055


def main():
    parser = argparse.ArgumentParser(description="BITalino EMG -> Unity (bundled)")
    parser.add_argument("--port", default=DEFAULT_PORT)
    parser.add_argument("--unity-ip", default=UNITY_IP)
    parser.add_argument("--unity-port", type=int, default=UNITY_PORT)
    args = parser.parse_args()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    unity_addr = (args.unity_ip, args.unity_port)
    device = EMGDevice(port=args.port)

    for _ in range(3):
        send_status(sock, unity_addr, "EMG Software", "Starting...", False)

    try:
        connect_device_with_status(device, sock, unity_addr, args.port)
        device.calibrate_unity(sock, unity_addr)
        run_game_stream(device, unity_addr)
    except Exception as exc:
        send_status(sock, unity_addr, "CONNECTION ERROR", str(exc), False)
        device.close()
        sys.exit(1)


if __name__ == "__main__":
    main()
