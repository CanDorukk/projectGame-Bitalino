"""
BITalino EMG — reference BioStep game (docx):
  REST:  mean of (L+R)/2
  MVC:   max of (L+R)/2
  threshold = baseline + (mvc - baseline) * 0.40
  game:  level_l > threshold -> LEFT, level_r > threshold -> RIGHT
  channel 0 (data[:, -2]) = left, channel 1 (data[:, -1]) = right
"""

import json
import socket
import time

import numpy as np
from bitalino import BITalino
from scipy.signal import butter, lfilter, lfilter_zi

DEFAULT_PORT = "COM6"
SAMPLING_RATE = 1000
ACQ_CHANNELS = [0, 1]
N_SAMPLES = 12
CAL_PREP_SEC = 1.0
CAL_TIME = 4.0
CAL_PHASE_GAP_SEC = 0.25
CAL_DONE_SEC = 0.5
CAL_LOOP_INTERVAL = 1.0 / 60.0
CONNECT_RETRY_SEC = 0.15

THRESHOLD_RATIO = 0.40
MIN_MVC_SPAN = 6.0


def send_status(sock, addr, title, instruction, device_connected=False):
    payload = {
        "command": "STATUS",
        "phaseTitle": title,
        "phaseInstruction": instruction,
        "deviceConnected": device_connected,
        "isCalibrated": False,
    }
    sock.sendto(json.dumps(payload).encode("utf-8"), addr)


def connect_device_with_status(device, sock, addr, port, max_wait_sec=120):
    device.port = port
    deadline = time.time() + max_wait_sec
    while time.time() < deadline:
        send_status(
            sock,
            addr,
            "EMG Connection",
            f"Connecting to BITalino ({port})...",
            False,
        )
        try:
            device.connect()
            send_status(sock, addr, "EMG Ready", "Device connected. Starting calibration.", True)
            return
        except Exception as exc:
            send_status(
                sock,
                addr,
                "NO CONNECTION",
                f"Could not connect to device.\n{exc}\nCheck COM port and Bluetooth.",
                False,
            )
            time.sleep(CONNECT_RETRY_SEC)
    raise TimeoutError(f"Could not connect BITalino on {port} within {max_wait_sec}s.")


def _pace_loop(start_time, interval):
    elapsed = time.perf_counter() - start_time
    time.sleep(max(0.0, interval - elapsed))


def run_game_stream(device, unity_addr):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    print(f"\nGame stream -> {unity_addr[0]}:{unity_addr[1]}", flush=True)
    print(f"Threshold: {device.emg_threshold:.2f}", flush=True)
    last_cmd = None
    try:
        while True:
            level_l, level_r = device.read_levels()
            command, left_act, right_act = device.command_from_levels(level_l, level_r)
            payload = {
                "command": command,
                "leftActivation": round(left_act, 3),
                "rightActivation": round(right_act, 3),
                "leftRms": round(level_l, 2),
                "rightRms": round(level_r, 2),
                "emgThreshold": round(device.emg_threshold, 2),
                "baseline": round(device.baseline, 2),
                "mvc": round(device.mvc, 2),
                "isCalibrated": True,
            }
            sock.sendto(json.dumps(payload).encode("utf-8"), unity_addr)
            if command != last_cmd:
                print(
                    f"{command}  L={level_l:.1f} R={level_r:.1f}  thr={device.emg_threshold:.1f}",
                    flush=True,
                )
                last_cmd = command
    except KeyboardInterrupt:
        print("\nStopped.", flush=True)
    finally:
        sock.close()
        device.close()


b, a = butter(4, 20 / (SAMPLING_RATE / 2), btype="high")


class EMGDevice:
    def __init__(self, port=DEFAULT_PORT):
        self.port = port
        self.device = None
        self.baseline = 0.0
        self.mvc = 1.0
        self.emg_threshold = 20.0
        self.calibrated = False
        self._left_active = False
        self._right_active = False
        self._zi_l = lfilter_zi(b, a)
        self._zi_r = lfilter_zi(b, a)

    def connect(self):
        print(f"Connecting to BITalino: {self.port}", flush=True)
        self.device = BITalino(self.port)
        self.device.start(SAMPLING_RATE, ACQ_CHANNELS)
        self._zi_l = lfilter_zi(b, a)
        self._zi_r = lfilter_zi(b, a)
        print("Connection successful.", flush=True)

    def close(self):
        if self.device is not None:
            try:
                self.device.stop()
                self.device.close()
            except Exception:
                pass
            self.device = None

    def read_levels(self, retries=3):
        if self.device is None:
            raise RuntimeError("BITalino is not connected.")

        last_exc = None
        for _ in range(retries):
            try:
                data = self.device.read(N_SAMPLES)
                if data is None or len(data) < N_SAMPLES:
                    raise RuntimeError("Incomplete BITalino sample block.")

                raw_l = data[:, -2].astype(float) - 512
                raw_r = data[:, -1].astype(float) - 512
                l_f, self._zi_l = lfilter(b, a, raw_l, zi=self._zi_l)
                r_f, self._zi_r = lfilter(b, a, raw_r, zi=self._zi_r)
                level_l = float(np.sqrt(np.mean(l_f**2)))
                level_r = float(np.sqrt(np.mean(r_f**2)))
                if not np.isfinite(level_l):
                    level_l = 0.0
                if not np.isfinite(level_r):
                    level_r = 0.0
                return level_l, level_r
            except Exception as exc:
                last_exc = exc
                time.sleep(0.005)

        raise RuntimeError(f"EMG read failed after {retries} tries: {last_exc}")

    def _combined_level(self, level_l, level_r):
        return (level_l + level_r) / 2.0

    def _send_calib_udp(self, sock, addr, command, title, instruction, seconds_remaining, level_l=0.0, level_r=0.0):
        payload = {
            "command": command,
            "leftActivation": 0.0,
            "rightActivation": 0.0,
            "leftRms": round(level_l, 2),
            "rightRms": round(level_r, 2),
            "emgThreshold": round(self.emg_threshold, 2),
            "phaseTitle": title,
            "phaseInstruction": instruction,
            "secondsRemaining": max(-1.0, float(seconds_remaining)),
            "isCalibrated": command == "CALIB_DONE",
            "deviceConnected": True,
        }
        if self.calibrated:
            payload["baseline"] = round(self.baseline, 2)
            payload["mvc"] = round(self.mvc, 2)
        sock.sendto(json.dumps(payload).encode("utf-8"), addr)

    def _read_levels_safe(self):
        try:
            return self.read_levels()
        except Exception:
            return 0.0, 0.0

    def _collect_phase(self, sock, unity_addr, phase, is_rest):
        prep_end = time.time() + CAL_PREP_SEC
        while time.time() < prep_end:
            t0 = time.perf_counter()
            level_l, level_r = self._read_levels_safe()
            self._send_calib_udp(
                sock,
                unity_addr,
                "CALIB_PREP",
                phase["prep_title"],
                phase["instruction"],
                prep_end - time.time(),
                level_l,
                level_r,
            )
            _pace_loop(t0, CAL_LOOP_INTERVAL)

        collected = []
        start = time.time()
        cmd = "CALIB_REST" if is_rest else "CALIB_MVC"
        while time.time() - start < CAL_TIME:
            t0 = time.perf_counter()
            level_l, level_r = self._read_levels_safe()
            self._send_calib_udp(
                sock,
                unity_addr,
                cmd,
                phase["title"],
                phase["instruction"],
                CAL_TIME - (time.time() - start),
                level_l,
                level_r,
            )
            collected.append(self._combined_level(level_l, level_r))
            _pace_loop(t0, CAL_LOOP_INTERVAL)

        if is_rest:
            return float(np.mean(collected)) if collected else 0.0
        return float(np.max(collected)) if collected else 0.0

    def calibrate_unity(self, sock, unity_addr):
        rest_phase = {
            "prep_title": "Get ready: Rest",
            "title": "1. REST",
            "instruction": "Relax both arms completely...",
        }
        mvc_phase = {
            "prep_title": "Get ready: Maximum",
            "title": "2. MAXIMUM (MVC)",
            "instruction": "SQUEEZE BOTH ARMS AS HARD AS YOU CAN!",
        }

        baseline = self._collect_phase(sock, unity_addr, rest_phase, is_rest=True)
        self._send_calib_udp(
            sock, unity_addr, "CALIB_PHASE_DONE", "Rest complete", "Prepare for maximum squeeze...", -1
        )
        time.sleep(CAL_PHASE_GAP_SEC)

        mvc = self._collect_phase(sock, unity_addr, mvc_phase, is_rest=False)
        self._apply_calibration(baseline, mvc)

        done_end = time.time() + CAL_DONE_SEC
        while time.time() < done_end:
            t0 = time.perf_counter()
            level_l, level_r = self._read_levels_safe()
            self._send_calib_udp(
                sock,
                unity_addr,
                "CALIB_DONE",
                "Calibration complete",
                f"Threshold: {self.emg_threshold:.1f}\nClick Start Game",
                done_end - time.time(),
                level_l,
                level_r,
            )
            _pace_loop(t0, CAL_LOOP_INTERVAL)

        print(
            f"Calibration done. baseline={self.baseline:.2f} mvc={self.mvc:.2f} "
            f"threshold={self.emg_threshold:.2f}",
            flush=True,
        )

    def _apply_calibration(self, baseline, mvc):
        self.baseline = float(baseline)
        self.mvc = float(max(mvc, self.baseline + MIN_MVC_SPAN))
        self.emg_threshold = self.baseline + (self.mvc - self.baseline) * THRESHOLD_RATIO
        self.calibrated = True
        self._left_active = False
        self._right_active = False

    def _channel_active(self, level):
        """Active only above threshold; release as soon as at or below (matches red graph line)."""
        return level > self.emg_threshold

    def normalize(self, level):
        if level <= self.baseline:
            return 0.0
        span = max(self.mvc - self.baseline, MIN_MVC_SPAN)
        return float(np.clip((level - self.baseline) / span, 0.0, 1.0))

    def command_from_levels(self, level_l, level_r):
        """Reference BioStep: each channel vs same shared threshold."""
        if not self.calibrated:
            return "NONE", 0.0, 0.0

        thr = self.emg_threshold
        left_on = self._channel_active(level_l)
        right_on = self._channel_active(level_r)
        self._left_active = left_on
        self._right_active = right_on

        if left_on and right_on:
            if level_l > level_r:
                return "LEFT", self.normalize(level_l), 0.0
            if level_r > level_l:
                return "RIGHT", 0.0, self.normalize(level_r)
            return "NONE", 0.0, 0.0
        if left_on:
            return "LEFT", self.normalize(level_l), 0.0
        if right_on:
            return "RIGHT", 0.0, self.normalize(level_r)
        return "NONE", 0.0, 0.0
