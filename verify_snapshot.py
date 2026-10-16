"""Check the instrumented game's actual UI values after opening the phone app."""
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("snapshot", type=Path)
args = parser.parse_args()
data = json.loads(args.snapshot.read_text())
assert data["Open"], "Open the phone app before taking the snapshot"
assert data["ModIcons"] == 1, f"Duplicate/missing phone icons: {data['ModIcons']}"
assert data["NativeComputerPanel"], "The native computer panel was not mounted"
assert data["RefreshCount"] >= 2, "Wait for a live refresh"
if "BackgroundOpaque" in data:
    assert data["BackgroundOpaque"], "The phone background must be opaque"
if "ButtonHitTests" in data:
    assert len(data["ButtonHitTests"]) == 8, "Expected all four corners of both header buttons"
    assert all(probe["Passed"] for probe in data["ButtonHitTests"]), data["ButtonHitTests"]
for row in data["Rows"]:
    if row["RawAmount"] is None:
        assert row["Amount"] == "--" and row["Time"] == "Idle", row
        continue
    assert row["Amount"] == f"${row['RawAmount']:,.0f}", row
    remaining = max(0, row["DurationMinutes"] - row["ElapsedMinutes"])
    if remaining == 0:
        expected = "Finishing"
    elif remaining < 60:
        expected = f"{remaining}m"
    else:
        expected = f"{remaining // 60}h {remaining % 60:02d}m"
    assert row["Time"] == expected, (row, expected)
print(f"PASS: one phone icon, native computer panel, {len(data['Rows'])} live rows match game operation values")
