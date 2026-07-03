"""
BITalino EMG -> Unity (build package)
Started automatically when the game launches.
"""

import argparse
import socket
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

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
    parser = argparse.ArgumentParser(description="BITalino EMG -> Unity")
    parser.add_argument("--port", default=DEFAULT_PORT)
    parser.add_argument("--unity-ip", default=UNITY_IP)
    parser.add_argument("--unity-port", type=int, default=UNITY_PORT)
    parser.add_argument("--calibration", choices=("unity", "console"), default="unity")
    args = parser.parse_args()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    unity_addr = (args.unity_ip, args.unity_port)
    device = EMGDevice(port=args.port)

    send_status(sock, unity_addr, "EMG Software", "Connecting to Unity...", False)

    try:
        connect_device_with_status(device, sock, unity_addr, args.port)

        if args.calibration == "unity":
            device.calibrate_unity(sock, unity_addr)
        else:
            device.calibrate_console()

        run_game_stream(device, unity_addr)
    except Exception as exc:
        send_status(
            sock,
            unity_addr,
            "CALIBRATION ERROR",
            f"Calibration interrupted.\n{exc}\nClose and restart the game.",
            True,
        )
        device.close()
        sys.exit(1)


if __name__ == "__main__":
    main()
