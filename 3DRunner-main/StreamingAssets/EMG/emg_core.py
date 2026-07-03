"""
BITalino EMG: per-channel calibration, auto left/right mapping, low-latency stream.
Calibration:
  1. REST (both relaxed) -> baseline per channel
  2. LEFT only -> verify channel + solo peak
  3. RIGHT only -> verify channel + solo peak
  4. MVC (both arms) -> per-channel MVC peak
  threshold_ch = baseline_ch + (mvc_ch - baseline_ch) * THRESHOLD_RATIO
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
N_SAMPLES = 10
CAL_PREP_SEC = 2.0
CAL_TIME = 4.0
CAL_SOLO_TIME = 3.0
CAL_PHASE_GAP_SEC = 0.5
CAL_DONE_SEC = 1.0
CAL_LOOP_INTERVAL = 1.0 / 60.0
GAME_LOOP_INTERVAL = 0.0

THRESHOLD_RATIO = 0.30
MIN_MVC_SPAN = 8.0
RELEASE_RATIO = 0.65
DOMINANCE_MARGIN = 0.08
SWAP_AUTO_DETECT = False


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
            time.sleep(1.0)
    raise TimeoutError(f"Could not connect BITalino on {port} within {max_wait_sec}s.")


def _pace_loop(start_time, interval):
    elapsed = time.perf_counter() - start_time
    time.sleep(max(0.0, interval - elapsed))


def run_game_stream(device, unity_addr):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    print(f"\nGame stream -> {unity_addr[0]}:{unity_addr[1]}")
    print(
        f"L thr={device.threshold_l:.1f}  R thr={device.threshold_r:.1f}  "
        f"swap={device.swap_channels}"
    )
    last_cmd = None
    try:
        while True:
            t0 = time.perf_counter()
            level_l, level_r = device.read_levels()
            command, left_act, right_act = device.command_from_levels(level_l, level_r)
            payload = {
                "command": command,
                "leftActivation": round(left_act, 3),
                "rightActivation": round(right_act, 3),
                "leftRms": round(level_l, 2),
                "rightRms": round(level_r, 2),
                "emgThreshold": round(device.threshold_l, 2),
                "isCalibrated": True,
            }
            sock.sendto(json.dumps(payload).encode("utf-8"), unity_addr)
            if command != last_cmd:
                print(
                    f"{command}  L={level_l:.1f} R={level_r:.1f}  "
                    f"thrL={device.threshold_l:.1f} thrR={device.threshold_r:.1f}",
                    flush=True,
                )
                last_cmd = command
            if GAME_LOOP_INTERVAL > 0.0:
                _pace_loop(t0, GAME_LOOP_INTERVAL)
    except KeyboardInterrupt:
        print("\nStopped.")
    finally:
        sock.close()
        device.close()


b, a = butter(4, 20 / (SAMPLING_RATE / 2), btype="high")


class EMGDevice:
    def __init__(self, port=DEFAULT_PORT):
        self.port = port
        self.device = None
        self.baseline_l = 0.0
        self.baseline_r = 0.0
        self.mvc_l = 1.0
        self.mvc_r = 1.0
        self.threshold_l = 20.0
        self.threshold_r = 20.0
        self.emg_threshold = 20.0
        self.calibrated = False
        self.swap_channels = False
        self._left_active = False
        self._right_active = False
        self._zi_l = lfilter_zi(b, a)
        self._zi_r = lfilter_zi(b, a)

    def connect(self):
        print(f"Connecting to BITalino: {self.port}")
        self.device = BITalino(self.port)
        self.device.start(SAMPLING_RATE, ACQ_CHANNELS)
        self._zi_l = lfilter_zi(b, a)
        self._zi_r = lfilter_zi(b, a)
        print("Connection successful.")

    def close(self):
        if self.device is not None:
            try:
                self.device.stop()
                self.device.close()
            except Exception:
                pass
            self.device = None

    def _apply_channel_map(self, level_l, level_r):
        if self.swap_channels:
            return level_r, level_l
        return level_l, level_r

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
                return self._apply_channel_map(level_l, level_r)
            except Exception as exc:
                last_exc = exc
                time.sleep(0.005)

        raise RuntimeError(f"EMG read failed after {retries} tries: {last_exc}")

    def _read_levels_safe(self):
        try:
            return self.read_levels()
        except Exception:
            return 0.0, 0.0

    def _bar_fill(self, level, baseline, mvc, threshold):
        if level <= threshold:
            return 0.0
        span = max(mvc - baseline, MIN_MVC_SPAN)
        return float(np.clip((level - baseline) / span, 0.0, 1.0))

    def _send_calib_udp(
        self,
        sock,
        addr,
        command,
        title,
        instruction,
        seconds_remaining,
        level_l=0.0,
        level_r=0.0,
    ):
        if self.calibrated:
            left_bar = self._bar_fill(level_l, self.baseline_l, self.mvc_l, self.threshold_l)
            right_bar = self._bar_fill(level_r, self.baseline_r, self.mvc_r, self.threshold_r)
        else:
            left_bar = float(np.clip(level_l / 50.0, 0.0, 1.0))
            right_bar = float(np.clip(level_r / 50.0, 0.0, 1.0))

        payload = {
            "command": command,
            "leftActivation": round(left_bar, 3),
            "rightActivation": round(right_bar, 3),
            "leftRms": round(level_l, 2),
            "rightRms": round(level_r, 2),
            "emgThreshold": round(self.threshold_l, 2),
            "phaseTitle": title,
            "phaseInstruction": instruction,
            "secondsRemaining": max(-1.0, float(seconds_remaining)),
            "isCalibrated": command == "CALIB_DONE",
            "deviceConnected": True,
        }
        sock.sendto(json.dumps(payload).encode("utf-8"), addr)

    def _run_timed_phase(self, sock, unity_addr, prep_title, title, instruction, duration, calib_cmd):
        prep_end = time.time() + CAL_PREP_SEC
        while time.time() < prep_end:
            t0 = time.perf_counter()
            level_l, level_r = self._read_levels_safe()
            self._send_calib_udp(
                sock,
                unity_addr,
                "CALIB_PREP",
                prep_title,
                instruction,
                prep_end - time.time(),
                level_l,
                level_r,
            )
            _pace_loop(t0, CAL_LOOP_INTERVAL)

        samples_l = []
        samples_r = []
        start = time.time()
        while time.time() - start < duration:
            t0 = time.perf_counter()
            level_l, level_r = self._read_levels_safe()
            self._send_calib_udp(
                sock,
                unity_addr,
                calib_cmd,
                title,
                instruction,
                duration - (time.time() - start),
                level_l,
                level_r,
            )
            samples_l.append(level_l)
            samples_r.append(level_r)
            _pace_loop(t0, CAL_LOOP_INTERVAL)

        return samples_l, samples_r

    def _detect_swap_from_solo(self, left_phase_l, left_phase_r, right_phase_l, right_phase_r):
        if not SWAP_AUTO_DETECT:
            return False

        left_score = float(np.mean(left_phase_l)) if left_phase_l else 0.0
        right_on_left = float(np.mean(left_phase_r)) if left_phase_r else 0.0
        right_score = float(np.mean(right_phase_r)) if right_phase_r else 0.0
        left_on_right = float(np.mean(right_phase_l)) if right_phase_l else 0.0

        left_wins_on_left_phase = left_score >= right_on_left * 1.15
        right_wins_on_right_phase = right_score >= left_on_right * 1.15
        swapped_pattern = (
            right_on_left > left_score * 1.15 and left_on_right > right_score * 1.15
        )

        if swapped_pattern and not (left_wins_on_left_phase and right_wins_on_right_phase):
            return True
        return False

    def calibrate_unity(self, sock, unity_addr):
        phases = [
            {
                "prep_title": "Get ready: Rest",
                "title": "1. REST",
                "instruction": "Relax BOTH arms completely...",
                "duration": CAL_TIME,
                "cmd": "CALIB_REST",
                "kind": "rest",
            },
            {
                "prep_title": "Get ready: Left arm",
                "title": "2. LEFT ARM ONLY",
                "instruction": "Squeeze your LEFT arm only.\nKeep the RIGHT arm relaxed.",
                "duration": CAL_SOLO_TIME,
                "cmd": "CALIB_LEFT",
                "kind": "left",
            },
            {
                "prep_title": "Get ready: Right arm",
                "title": "3. RIGHT ARM ONLY",
                "instruction": "Squeeze your RIGHT arm only.\nKeep the LEFT arm relaxed.",
                "duration": CAL_SOLO_TIME,
                "cmd": "CALIB_RIGHT",
                "kind": "right",
            },
            {
                "prep_title": "Get ready: Maximum",
                "title": "4. MAXIMUM (MVC)",
                "instruction": "SQUEEZE BOTH ARMS AS HARD AS YOU CAN!",
                "duration": CAL_TIME,
                "cmd": "CALIB_MVC",
                "kind": "mvc",
            },
        ]

        solo_left_l = solo_left_r = solo_right_l = solo_right_r = None

        for phase in phases:
            samples_l, samples_r = self._run_timed_phase(
                sock,
                unity_addr,
                phase["prep_title"],
                phase["title"],
                phase["instruction"],
                phase["duration"],
                phase["cmd"],
            )

            if phase["kind"] == "rest":
                self.baseline_l = float(np.mean(samples_l)) if samples_l else 0.0
                self.baseline_r = float(np.mean(samples_r)) if samples_r else 0.0
                self._send_calib_udp(
                    sock,
                    unity_addr,
                    "CALIB_PHASE_DONE",
                    "Rest complete",
                    f"Left rest: {self.baseline_l:.1f}  Right rest: {self.baseline_r:.1f}",
                    -1,
                )
            elif phase["kind"] == "left":
                solo_left_l, solo_left_r = samples_l, samples_r
            elif phase["kind"] == "right":
                solo_right_l, solo_right_r = samples_l, samples_r
                self.swap_channels = self._detect_swap_from_solo(
                    solo_left_l, solo_left_r, solo_right_l, solo_right_r
                )
                if self.swap_channels:
                    solo_left_l, solo_left_r = solo_left_r, solo_left_l
                    solo_right_l, solo_right_r = solo_right_r, solo_right_l
                    self.baseline_l, self.baseline_r = self.baseline_r, self.baseline_l
            elif phase["kind"] == "mvc":
                mvc_l_solo = float(np.max(solo_left_l)) if solo_left_l else 0.0
                mvc_r_solo = float(np.max(solo_right_r)) if solo_right_r else 0.0
                mvc_l_both = float(np.max(samples_l)) if samples_l else 0.0
                mvc_r_both = float(np.max(samples_r)) if samples_r else 0.0
                self.mvc_l = max(mvc_l_solo, mvc_l_both)
                self.mvc_r = max(mvc_r_solo, mvc_r_both)
                self._apply_calibration()

            if phase["kind"] != "mvc":
                time.sleep(CAL_PHASE_GAP_SEC)

        swap_note = " (channels auto-corrected)" if self.swap_channels else ""
        done_end = time.time() + CAL_DONE_SEC
        while time.time() < done_end:
            t0 = time.perf_counter()
            level_l, level_r = self._read_levels_safe()
            self._send_calib_udp(
                sock,
                unity_addr,
                "CALIB_DONE",
                "Calibration complete",
                (
                    f"Left threshold: {self.threshold_l:.1f}\n"
                    f"Right threshold: {self.threshold_r:.1f}{swap_note}\n"
                    "Click Start Game"
                ),
                done_end - time.time(),
                level_l,
                level_r,
            )
            _pace_loop(t0, CAL_LOOP_INTERVAL)

        print(
            f"Calibration done. swap={self.swap_channels} "
            f"L base={self.baseline_l:.1f} mvc={self.mvc_l:.1f} thr={self.threshold_l:.1f} | "
            f"R base={self.baseline_r:.1f} mvc={self.mvc_r:.1f} thr={self.threshold_r:.1f}"
        )

    def calibrate_console(self):
        print("\n=== EMG CALIBRATION (console) ===")
        for label, solo in [("REST (both relaxed)", None), ("LEFT only", "L"), ("RIGHT only", "R"), ("MVC both", "B")]:
            input(f"{label}. Press Enter when ready...")
            samples_l, samples_r = [], []
            end = time.time() + (CAL_SOLO_TIME if solo in ("L", "R") else CAL_TIME)
            while time.time() < end:
                level_l, level_r = self.read_levels()
                samples_l.append(level_l)
                samples_r.append(level_r)
            if solo is None:
                self.baseline_l = float(np.mean(samples_l))
                self.baseline_r = float(np.mean(samples_r))
            elif solo == "L":
                solo_left_l, solo_left_r = samples_l, samples_r
            elif solo == "R":
                solo_right_l, solo_right_r = samples_l, samples_r
                self.swap_channels = self._detect_swap_from_solo(
                    solo_left_l, solo_left_r, solo_right_l, solo_right_r
                )
            else:
                mvc_l = max(float(np.max(solo_left_l)), float(np.max(samples_l)))
                mvc_r = max(float(np.max(solo_right_r)), float(np.max(samples_r)))
                self.mvc_l, self.mvc_r = mvc_l, mvc_r
                self._apply_calibration()
        print(f"Thresholds L={self.threshold_l:.1f} R={self.threshold_r:.1f} swap={self.swap_channels}\n")

    def _apply_calibration(self):
        self.mvc_l = float(max(self.mvc_l, self.baseline_l + MIN_MVC_SPAN))
        self.mvc_r = float(max(self.mvc_r, self.baseline_r + MIN_MVC_SPAN))
        self.threshold_l = self.baseline_l + (self.mvc_l - self.baseline_l) * THRESHOLD_RATIO
        self.threshold_r = self.baseline_r + (self.mvc_r - self.baseline_r) * THRESHOLD_RATIO
        self.emg_threshold = (self.threshold_l + self.threshold_r) / 2.0
        self.calibrated = True
        self._left_active = False
        self._right_active = False

    def normalize(self, level, baseline, mvc):
        if level <= baseline:
            return 0.0
        span = max(mvc - baseline, MIN_MVC_SPAN)
        return float(np.clip((level - baseline) / span, 0.0, 1.0))

    def _channel_on(self, level, threshold, was_active):
        if was_active:
            return level > threshold * RELEASE_RATIO
        return level > threshold

    def _activation_excess(self, level, baseline, mvc, threshold):
        if level <= threshold:
            return 0.0
        span = max(mvc - baseline, MIN_MVC_SPAN)
        return (level - threshold) / span

    def command_from_levels(self, level_l, level_r):
        if not self.calibrated:
            return "NONE", 0.0, 0.0

        left_on = self._channel_on(level_l, self.threshold_l, self._left_active)
        right_on = self._channel_on(level_r, self.threshold_r, self._right_active)
        self._left_active = left_on
        self._right_active = right_on

        left_act = self.normalize(level_l, self.baseline_l, self.mvc_l) if left_on else 0.0
        right_act = self.normalize(level_r, self.baseline_r, self.mvc_r) if right_on else 0.0

        if left_on and right_on:
            left_excess = self._activation_excess(
                level_l, self.baseline_l, self.mvc_l, self.threshold_l
            )
            right_excess = self._activation_excess(
                level_r, self.baseline_r, self.mvc_r, self.threshold_r
            )
            if left_excess > right_excess + DOMINANCE_MARGIN:
                return "LEFT", left_act, 0.0
            if right_excess > left_excess + DOMINANCE_MARGIN:
                return "RIGHT", 0.0, right_act
            return "NONE", 0.0, 0.0

        if left_on:
            return "LEFT", left_act, 0.0
        if right_on:
            return "RIGHT", 0.0, right_act
        return "NONE", 0.0, 0.0
