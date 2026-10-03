"""修改后保存，在终端再次输入 play() 即生效；每次只改一个变量。"""
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[2]

# 改为自己的音频路径，例如 r"C:\音乐\测试.flac"；原文件只读。
MEDIA_PATH = PROJECT / "oldtest" / "Storage" / "audio" / "login.mp3"

# 加载软件固定目录的同一份 VLC，不使用系统安装的 VLC。
VLC_DIRECTORY = PROJECT / "artifacts/NexusExplorer-2.0.4-preview-win-x64/libvlc/win-x64"

# 默认与正式软件初始化一致。要实验时一次只取消一条注释。
VLC_OPTIONS = [
    "--no-osd",
    # "--aout=directsound",           # 更换输出模块进行对比
    # "--audio-resampler=speex_resampler",  # 更换重采样模块进行对比
    # "--verbose=2",                 # 在终端显示详细 VLC 日志
]

# 与正式音频播放相同的默认值；ROLE 可改为 "video" 或 "none"。
ROLE = "music"
VOLUME = 100  # 0～100
RATE = 1.0

# 每个媒体的 VLC 参数；音频默认不添加额外选项。
MEDIA_OPTIONS = []
