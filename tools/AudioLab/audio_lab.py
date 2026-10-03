"""用 Python 标准库直接调用 LibVLC 3；python -i audio_lab.py 进入交互环境。

本文件是可编辑的播放代码，audio_settings.py 是每次 play() 重读的参数。
不导入主软件、不接触 DB/Storage 配置、不使用 PCM 回调代替系统音频输出。
"""
import atexit
import ctypes as C
import json
import math
import os
from pathlib import Path
import runpy
import struct
import time

HERE = Path(__file__).resolve().parent
SETTINGS_FILE = HERE / "audio_settings.py"
LOG_DIRECTORY = HERE.parents[1] / "artifacts" / "audio-lab"
ROLES = {"none": 0, "music": 1, "video": 2}
STATES = {0: "Idle", 1: "Opening", 2: "Buffering", 3: "Playing", 4: "Paused", 5: "Stopped", 6: "Ended", 7: "Error"}


class MediaStats(C.Structure):
    # Layout from the bundled VLC 3 libvlc_media.h; floats and ints are 32-bit.
    _fields_ = [(name, C.c_float if name.startswith("f_") else C.c_int) for name in (
        "i_read_bytes", "f_input_bitrate", "i_demux_read_bytes", "f_demux_bitrate",
        "i_demux_corrupted", "i_demux_discontinuity", "i_decoded_video", "i_decoded_audio",
        "i_displayed_pictures", "i_lost_pictures", "i_played_abuffers", "i_lost_abuffers",
        "i_sent_packets", "i_sent_bytes", "f_send_bitrate")]


class TrackDescription(C.Structure):
    pass


TrackDescription._fields_ = [('id', C.c_int), ('name', C.c_char_p),
                            ('next', C.POINTER(TrackDescription))]


def read_settings(**overrides):
    # run_path does not reuse import caches: the next play reads the saved file.
    values = runpy.run_path(str(SETTINGS_FILE))
    config = {key: values[key] for key in (
        "MEDIA_PATH", "VLC_DIRECTORY", "VLC_OPTIONS", "MEDIA_OPTIONS", "ROLE", "VOLUME", "RATE")}
    config.update({key.upper(): value for key, value in overrides.items() if value is not None})
    config["MEDIA_PATH"] = Path(config["MEDIA_PATH"]).expanduser().resolve()
    config["VLC_DIRECTORY"] = Path(config["VLC_DIRECTORY"]).expanduser().resolve()
    if not config["MEDIA_PATH"].is_file():
        raise FileNotFoundError(f"音频文件不存在：{config['MEDIA_PATH']}")
    if not (config["VLC_DIRECTORY"] / "libvlc.dll").is_file():
        raise FileNotFoundError(f"VLC 库不存在：{config['VLC_DIRECTORY']}")
    if config["ROLE"] not in ROLES:
        raise ValueError('ROLE 只能为 "music"、"video" 或 "none"。')
    if type(config["VOLUME"]) is not int or not 0 <= config["VOLUME"] <= 100:
        raise ValueError("VOLUME 必须为 0～100 的整数。")
    if not isinstance(config["RATE"], (int, float)) or not math.isfinite(config["RATE"]) or config["RATE"] <= 0:
        raise ValueError("RATE 必须为大于 0 的有限数值。")
    for key in ("VLC_OPTIONS", "MEDIA_OPTIONS"):
        if not isinstance(config[key], (list, tuple)) or not all(isinstance(option, str) and "\0" not in option for option in config[key]):
            raise ValueError(f"{key} 必须为不含空字符的字符串列表。")
        config[key] = list(config[key])
    return config


class NativePlayback:
    def __init__(self, config):
        if os.name != "nt" or struct.calcsize("P") != 8:
            raise RuntimeError("请使用 Windows x64 Python。")
        self.instance = self.player = self.media = None
        self.com_owned = False
        self.config = config
        self.directory_handle = os.add_dll_directory(str(config["VLC_DIRECTORY"]))
        try:
            # CLR thread-pool workers used by production are COM MTA threads.
            # Plain Python needs explicit COM initialization for MMDevice output.
            self.ole = C.WinDLL("ole32")
            self.ole.CoInitializeEx.argtypes, self.ole.CoInitializeEx.restype = [C.c_void_p, C.c_uint], C.c_long
            self.ole.CoUninitialize.argtypes, self.ole.CoUninitialize.restype = [], None
            com_result = self.ole.CoInitializeEx(None, 0)
            if com_result not in (0, 1):
                raise RuntimeError(f"COM MTA 初始化失败：0x{com_result & 0xffffffff:08X}")
            self.com_owned = True
            self.vlc = C.CDLL(str(config["VLC_DIRECTORY"] / "libvlc.dll"))
            self._bind()
            options = [option.encode("utf-8") for option in config["VLC_OPTIONS"]]
            argv = (C.c_char_p * len(options))(*options)
            self.instance = self.vlc.libvlc_new(len(options), argv)
            if not self.instance:
                raise RuntimeError("VLC 初始化失败，请检查 VLC_OPTIONS 和终端错误信息。")
            self.player = self.vlc.libvlc_media_player_new(self.instance)
            if not self.player:
                raise RuntimeError("VLC 播放器创建失败。")
            self.version = self.vlc.libvlc_get_version().decode("utf-8", "replace")
            if not self.version.startswith("3."):
                raise RuntimeError("此脚本只适用于 LibVLC 3。")
            LOG_DIRECTORY.mkdir(parents=True, exist_ok=True)
            self.log_path = LOG_DIRECTORY / f"trial-{time.time_ns()}.jsonl"
            self.record("created", config=config, version=self.version)
        except BaseException:
            self.close()
            raise

    def _bind(self):
        # Declare every ABI signature; undeclared pointers would be truncated on x64.
        signatures = {
            "libvlc_new": (C.c_void_p, [C.c_int, C.POINTER(C.c_char_p)]),
            "libvlc_release": (None, [C.c_void_p]),
            "libvlc_get_version": (C.c_char_p, []),
            "libvlc_media_player_new": (C.c_void_p, [C.c_void_p]),
            "libvlc_media_player_release": (None, [C.c_void_p]),
            "libvlc_media_new_location": (C.c_void_p, [C.c_void_p, C.c_char_p]),
            "libvlc_media_add_option": (None, [C.c_void_p, C.c_char_p]),
            "libvlc_media_release": (None, [C.c_void_p]),
            "libvlc_media_player_set_media": (None, [C.c_void_p, C.c_void_p]),
            "libvlc_media_player_set_role": (C.c_int, [C.c_void_p, C.c_uint]),
            "libvlc_audio_set_volume": (C.c_int, [C.c_void_p, C.c_int]),
            "libvlc_audio_get_volume": (C.c_int, [C.c_void_p]),
            "libvlc_audio_get_track": (C.c_int, [C.c_void_p]),
            "libvlc_audio_set_track": (C.c_int, [C.c_void_p, C.c_int]),
            "libvlc_audio_get_track_description": (C.POINTER(TrackDescription), [C.c_void_p]),
            "libvlc_track_description_list_release": (None, [C.POINTER(TrackDescription)]),
            "libvlc_media_player_set_rate": (C.c_int, [C.c_void_p, C.c_float]),
            "libvlc_media_player_get_rate": (C.c_float, [C.c_void_p]),
            "libvlc_media_player_play": (C.c_int, [C.c_void_p]),
            "libvlc_media_player_stop": (None, [C.c_void_p]),
            "libvlc_media_player_set_pause": (None, [C.c_void_p, C.c_int]),
            "libvlc_media_player_set_time": (None, [C.c_void_p, C.c_int64]),
            "libvlc_media_player_get_time": (C.c_int64, [C.c_void_p]),
            "libvlc_media_player_get_length": (C.c_int64, [C.c_void_p]),
            "libvlc_media_player_get_state": (C.c_int, [C.c_void_p]),
            "libvlc_media_get_stats": (C.c_int, [C.c_void_p, C.POINTER(MediaStats)]),
        }
        for name, (result, arguments) in signatures.items():
            function = getattr(self.vlc, name)
            function.restype, function.argtypes = result, arguments

    def record(self, event, **details):
        if not hasattr(self, "log_path"):
            return
        try:
            with self.log_path.open("a", encoding="utf-8") as file:
                file.write(json.dumps({"time": time.time(), "event": event, **details}, ensure_ascii=False, default=str) + "\n")
        except OSError as error:
            print(f"测试记录写入失败：{error}")

    def start(self):
        self.vlc.libvlc_media_player_stop(self.player)
        self.vlc.libvlc_media_player_set_media(self.player, None)

        # EDITABLE PLAYBACK CODE: mirrors the production engine's relevant calls.
        # Production opens a file URI, sets Music/Video role, volume/rate, then plays.
        self.media = self.vlc.libvlc_media_new_location(self.instance, self.config["MEDIA_PATH"].as_uri().encode("utf-8"))
        if not self.media:
            raise RuntimeError("VLC 媒体创建失败。")
        if self.config["MEDIA_PATH"].suffix.lower() in (".mp4", ".mov", ".m4v"):
            self.vlc.libvlc_media_add_option(self.media, b":demux=avformat")
        for option in self.config["MEDIA_OPTIONS"]:
            self.vlc.libvlc_media_add_option(self.media, option.encode("utf-8"))
        role_result = self.vlc.libvlc_media_player_set_role(self.player, ROLES[self.config["ROLE"]])
        volume_result = self.vlc.libvlc_audio_set_volume(self.player, self.config["VOLUME"])
        rate_result = self.vlc.libvlc_media_player_set_rate(self.player, self.config["RATE"])
        self.record("parameters", role_result=role_result, volume_result=volume_result, rate_result=rate_result)
        # Production currently ignores these return values; keep its call order
        # rather than silently adding a post-start volume call to this baseline.
        if volume_result:
            print(f"播放前音量设置返回 {volume_result}；开始后用 info() 查看实际音量，volume(数值) 可即时修改。")
        self.vlc.libvlc_media_player_set_media(self.player, self.media)
        if self.vlc.libvlc_media_player_play(self.player) != 0:
            raise RuntimeError("VLC 拒绝播放文件，请查看终端错误信息。")
        self.record("play_requested")

    def snapshot(self):
        stats = MediaStats()
        valid = bool(self.media and self.vlc.libvlc_media_get_stats(self.media, C.byref(stats)))
        return {
            "state": STATES.get(self.vlc.libvlc_media_player_get_state(self.player), "Unknown"),
            "seconds": self.vlc.libvlc_media_player_get_time(self.player) / 1000,
            "duration": self.vlc.libvlc_media_player_get_length(self.player) / 1000,
            "volume": self.vlc.libvlc_audio_get_volume(self.player),
            "rate": self.vlc.libvlc_media_player_get_rate(self.player),
            "audio_track": self.vlc.libvlc_audio_get_track(self.player),
            "decoded_audio": stats.i_decoded_audio if valid else None,
            "played_buffers": stats.i_played_abuffers if valid else None,
            "lost_buffers": stats.i_lost_abuffers if valid else None,
        }

    def close(self):
        if self.player:
            self.record("stopping", snapshot=self.snapshot())
            self.vlc.libvlc_media_player_stop(self.player)
            self.vlc.libvlc_media_player_set_media(self.player, None)
            self.vlc.libvlc_media_player_release(self.player)
            self.player = None
        if self.media:
            self.vlc.libvlc_media_release(self.media)
            self.media = None
        if self.instance:
            self.vlc.libvlc_release(self.instance)
            self.instance = None
        if self.com_owned:
            self.ole.CoUninitialize()
            self.com_owned = False
        if self.directory_handle:
            self.directory_handle.close()
            self.directory_handle = None
        self.record("closed")


_active = None


def stop():
    """等待停止，释放播放器、媒体与实例。"""
    global _active
    if _active:
        _active.close()
        _active = None


def play(path=None, role=None, volume=None, vlc_options=None, media_options=None, rate=None):
    """重读已保存的参数，从头播放；也可用关键字临时覆盖参数。"""
    global _active
    config = read_settings(media_path=path, role=role, volume=volume, vlc_options=vlc_options, media_options=media_options, rate=rate)
    candidate = NativePlayback(config)
    try:
        stop()
    except BaseException:
        candidate.close()
        raise
    _active = candidate
    try:
        _active.start()
    except BaseException:
        stop()
        raise
    print(f"播放：{config['MEDIA_PATH']}\n角色：{config['ROLE']}；请求音量：{config['VOLUME']}；VLC：{_active.version}\n记录：{_active.log_path}")


def _current():
    if not _active or not _active.player:
        raise RuntimeError("请先输入 play()。")
    return _active


def pause():
    lab = _current()
    lab.vlc.libvlc_media_player_set_pause(lab.player, 1)
    lab.record("pause")


def resume():
    lab = _current()
    lab.vlc.libvlc_media_player_set_pause(lab.player, 0)
    lab.record("resume")


def volume(value):
    if type(value) is not int or not 0 <= value <= 100:
        raise ValueError("音量必须为 0～100 的整数。")
    lab = _current()
    if lab.vlc.libvlc_audio_set_volume(lab.player, value) != 0:
        raise RuntimeError("VLC 拒绝设置音量。")
    lab.record("volume", value=value)


def seek(seconds):
    if not isinstance(seconds, (int, float)) or not math.isfinite(seconds) or seconds < 0:
        raise ValueError("秒数必须为大于等于 0 的有限数值。")
    lab = _current()
    duration = lab.vlc.libvlc_media_player_get_length(lab.player)
    if duration > 0 and seconds * 1000 > duration:
        raise ValueError("不能跳到文件时长之外。")
    lab.vlc.libvlc_media_player_set_time(lab.player, int(seconds * 1000))
    lab.record("seek", seconds=seconds)


def info():
    lab = _current()
    snapshot = lab.snapshot()
    lab.record("info", snapshot=snapshot)
    print(json.dumps(snapshot, ensure_ascii=False, indent=2))
    return snapshot


def tracks():
    """查询实际底层音轨 ID；不要假设 Track 1 的 ID 总是 1。"""
    lab = _current()
    head = lab.vlc.libvlc_audio_get_track_description(lab.player)
    result = []
    try:
        node = head
        while node:
            entry = node.contents
            result.append({'id': entry.id, 'name': (entry.name or b'').decode('utf-8', 'replace')})
            node = entry.next
    finally:
        if head:
            lab.vlc.libvlc_track_description_list_release(head)
    lab.record('audio_tracks', tracks=result)
    print(json.dumps(result, ensure_ascii=False))
    return result


def track(track_id):
    """真正选择底层音轨；-1 取消音轨，不使用静音替代。"""
    if type(track_id) is not int:
        raise ValueError('音轨 ID 必须为整数。')
    lab = _current()
    started = time.monotonic()
    result = lab.vlc.libvlc_audio_set_track(lab.player, track_id)
    lab.record('audio_track_requested', track=track_id, result=result,
               elapsed_ms=(time.monotonic() - started) * 1000)
    if result != 0:
        raise RuntimeError('VLC 拒绝切换该音轨。')


atexit.register(stop)
if __name__ == "__main__":
    print("音频代码实验室：不自动播放。\n编辑 audio_settings.py，保存后输入 play()。\n可用：play(role='video')、tracks()、track(-1)、track(实际ID)、pause()、resume()、volume(50)、seek(30)、info()、stop()、exit()。")
