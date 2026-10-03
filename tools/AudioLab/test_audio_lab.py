"""Only checks this script's native wiring with muted synthetic audio."""
import ctypes as C
import json
from pathlib import Path
import tempfile
import time
import unittest
import wave

import audio_lab as lab


def wait_for(predicate):
    deadline = time.monotonic() + 12
    while time.monotonic() < deadline:
        if predicate():
            return
        time.sleep(.03)
    raise AssertionError("原生播放未达到预期状态")


class AudioLabTest(unittest.TestCase):
    def test_native_roles_reload_controls_and_release(self):
        original_settings, original_logs = lab.SETTINGS_FILE, lab.LOG_DIRECTORY
        with tempfile.TemporaryDirectory(prefix="nexus-audio-lab-") as temporary:
            root = Path(temporary)
            audio = root / "中文音频.wav"
            with wave.open(str(audio), "wb") as file:
                file.setnchannels(2); file.setsampwidth(2); file.setframerate(44100)
                file.writeframes(b"\x00\x00\x00\x00" * 44100 * 8)
            settings = root / "settings.py"
            lab.SETTINGS_FILE, lab.LOG_DIRECTORY = settings, root / "logs"
            native = Path(__file__).resolve().parents[2] / "artifacts/NexusExplorer-2.0.4-preview-win-x64/libvlc/win-x64"
            try:
                for role in ("music", "video", "none"):
                    # Rewrite the SAME file on every pass to verify edits are reread, not cached.
                    settings.write_text(
                        f"MEDIA_PATH = {str(audio)!r}\nVLC_DIRECTORY = {str(native)!r}\n"
                        f"VLC_OPTIONS = ['--no-osd']\nMEDIA_OPTIONS = []\nROLE = {role!r}\nVOLUME = 0\nRATE = 1.0\n",
                        encoding="utf-8")
                    lab.play()
                    wait_for(lambda: lab._active.snapshot()["state"] == "Playing" and lab._active.snapshot()["decoded_audio"] > 0)
                    self.assertEqual(role, lab._active.config["ROLE"])
                    lab.volume(0)
                    self.assertEqual(0, lab._active.snapshot()["volume"])
                    active = lab._active
                    with self.assertRaises(FileNotFoundError):
                        lab.play(path=root / "missing.wav")
                    self.assertIs(active, lab._active)
                    lab.pause()
                    wait_for(lambda: active.snapshot()["state"] == "Paused")
                    lab.resume()
                    wait_for(lambda: active.snapshot()["state"] == "Playing")
                    lab.seek(3)
                    wait_for(lambda: active.snapshot()["seconds"] >= 2.5)
                    log_path = active.log_path
                    lab.stop()
                    # CreateFileW with share=0 checks native handles were actually released.
                    kernel = C.WinDLL("kernel32", use_last_error=True)
                    kernel.CreateFileW.argtypes = [C.c_wchar_p, C.c_uint, C.c_uint, C.c_void_p, C.c_uint, C.c_uint, C.c_void_p]
                    kernel.CreateFileW.restype = C.c_void_p
                    kernel.CloseHandle.argtypes = [C.c_void_p]
                    kernel.CloseHandle.restype = C.c_int
                    handle = kernel.CreateFileW(str(audio), 0x80000000, 0, None, 3, 0, None)
                    self.assertNotEqual(C.c_void_p(-1).value, handle, f"仍被占用，错误 {C.get_last_error()}")
                    kernel.CloseHandle(handle)
                    records = [json.loads(line) for line in log_path.read_text(encoding="utf-8").splitlines()]
                    self.assertEqual(role, records[0]["config"]["ROLE"])
                    self.assertEqual("closed", records[-1]["event"])
            finally:
                lab.stop()
                lab.SETTINGS_FILE, lab.LOG_DIRECTORY = original_settings, original_logs


if __name__ == "__main__":
    unittest.main()
