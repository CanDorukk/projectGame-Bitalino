"""
Generate a PDF session report from saved EMG session data (session.json + signals.csv).
Usage: python session_report.py <session_folder>
"""

import csv
import json
import sys
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.backends.backend_pdf import PdfPages


def load_session(session_dir: Path) -> dict:
    with open(session_dir / "session.json", encoding="utf-8-sig") as f:
        return json.load(f)


def load_signals(session_dir: Path):
    path = session_dir / "signals.csv"
    if not path.exists():
        return [], [], []

    times, left, right = [], [], []
    with open(path, encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            times.append(float(row["time_s"]))
            left.append(float(row["left_rms"]))
            right.append(float(row["right_rms"]))
    return times, left, right


def write_summary_page(pdf, metrics: dict):
    fig, ax = plt.subplots(figsize=(8.27, 11.69))
    ax.axis("off")

    lines = [
        "EMG Physiotherapy Session Report",
        "",
        f"Participant: {metrics.get('participantId', '')}",
        f"Session: {metrics.get('sessionId', '')}",
        f"Started (UTC): {metrics.get('startedAtUtc', '')}",
        f"Ended (UTC): {metrics.get('endedAtUtc', '')}",
        f"Duration: {metrics.get('durationSeconds', 0):.1f} s",
        "",
        "Calibration",
        f"  Baseline: {metrics.get('baseline', 0):.2f}",
        f"  MVC: {metrics.get('mvc', 0):.2f}",
        f"  Threshold: {metrics.get('emgThreshold', 0):.2f}",
        "",
        "Activation Metrics",
        f"  Left activation time: {metrics.get('leftActivationSeconds', 0):.1f} s",
        f"  Right activation time: {metrics.get('rightActivationSeconds', 0):.1f} s",
        f"  Left peak (% MVC): {metrics.get('leftPeakMvcPercent', 0):.1f}%",
        f"  Right peak (% MVC): {metrics.get('rightPeakMvcPercent', 0):.1f}%",
        f"  Left mean (% MVC): {metrics.get('leftMeanMvcPercent', 0):.1f}%",
        f"  Right mean (% MVC): {metrics.get('rightMeanMvcPercent', 0):.1f}%",
        f"  Left-right symmetry: {metrics.get('symmetryPercent', 0):.1f}%",
        "",
        "Gameplay",
        f"  Lane changes: {metrics.get('laneChangeCount', 0)}",
        f"  Score (coins): {metrics.get('gameScore', 0)}",
        f"  Signal samples: {metrics.get('signalSampleCount', 0)}",
    ]

    ax.text(
        0.05,
        0.95,
        "\n".join(lines),
        transform=ax.transAxes,
        fontsize=11,
        verticalalignment="top",
        family="monospace",
    )
    pdf.savefig(fig)
    plt.close(fig)


def write_signal_page(pdf, times, left, right, threshold: float):
    if not times:
        return

    fig, ax = plt.subplots(figsize=(8.27, 5.5))
    ax.plot(times, left, label="Left RMS", color="#4db8ff", linewidth=1.2)
    ax.plot(times, right, label="Right RMS", color="#ffc84d", linewidth=1.2)
    if threshold > 0:
        ax.axhline(threshold, color="red", linestyle="--", linewidth=1.0, label="Threshold")
    ax.set_xlabel("Time (s)")
    ax.set_ylabel("RMS")
    ax.set_title("EMG Signal — Session")
    ax.legend(loc="upper right")
    ax.grid(True, alpha=0.3)
    pdf.savefig(fig)
    plt.close(fig)


def main():
    if len(sys.argv) < 2:
        print("Usage: python session_report.py <session_folder>", file=sys.stderr)
        sys.exit(1)

    session_dir = Path(sys.argv[1]).resolve()
    if not session_dir.is_dir():
        print(f"Session folder not found: {session_dir}", file=sys.stderr)
        sys.exit(1)

    metrics = load_session(session_dir)
    times, left, right = load_signals(session_dir)
    pdf_path = session_dir / "session_report.pdf"

    with PdfPages(pdf_path) as pdf:
        write_summary_page(pdf, metrics)
        write_signal_page(pdf, times, left, right, float(metrics.get("emgThreshold", 0)))

    print(f"Report written: {pdf_path}")


if __name__ == "__main__":
    main()
