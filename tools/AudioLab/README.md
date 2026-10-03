# 可编辑音频实验脚本

用户当前采用此方式测试：普通 Python 源码，无 EXE 打包，无第三方 Python 依赖。使用现有 Windows x64 Python 与固定软件目录中同一份 LibVLC 3.0.21。

## 开始

双击 **start.cmd**，看到 `>>>` 后输入 `play()` 回车。默认播放 oldtest 中的 login.mp3，不自动发声。编辑 **audio_settings.py**，保存，再输入 `play()`：脚本立即重新读取文件、释放旧播放器、创建采用新参数的实例并从头播放，无须重开终端。

```python
play()                              # 按参数文件播放
play(role="video")                  # 临时只改变播放角色
play(role="none")                   # 临时使用 None 角色
play(vlc_options=["--no-osd", "--aout=directsound"])  # 临时改变输出模块
play(path=r"C:\音乐\测试.flac")      # 临时选择其他文件
pause()
resume()
volume(50)                          # 当前播放即时调音量
seek(30)                            # 跳到第 30 秒
info()                              # 状态、时间、实际音量/速度与解码统计
stop()
exit()                              # 释放资源后退出
```

`play(...)` 的参数只影响该次实验，不改写参数文件。下一次无参数 `play()` 恢复文件里的设置。参数文件语法、基本参数、路径或 VLC 初始化失败时先报告错误，不打断原来正在播放的文件。一次只改一个变量，对比同一段，并记录“原参数破音／修改后正常或仍破音”。

注意实际参数：当前正式软件在停止后、播放开始前设置音量。VLC 3 的音量调用此时可能返回 -1；脚本保留该顺序并记录返回值，会在终端提示。输入 `info()` 查看实际音量，播放开始后输入 `volume(50)` 可直接修改。这里不额外补一个隐含的音量调用，以免基线和正式软件不同。相关行为可核对 [VLC 3 播放器停止实现](https://github.com/videolan/vlc/blob/3.0.21/lib/media_player.c) 和 [音量 API 实现](https://github.com/videolan/vlc/blob/3.0.21/lib/audio.c)。

## 修改播放代码

播放代码在 **audio_lab.py** 的 `NativePlayback.start()`。直接编辑 Python 调用顺序或增加调用；修改这个文件后退出并重开 start.cmd，即运行保存后的源码，无须编译。API 的参数类型集中在 `_bind()` 中，来自随包 VLC 3 头文件；增加新 API 时先声明原生签名。

默认步骤对应生产播放器与音频有关的原生调用：`--no-osd` 初始化、停止清空上一媒体、以 UTF-8 文件 URI 创建媒体、设置 Music 角色、尝试设置音量 100、速度 1，再关联媒体并调用 play。Python 线程显式初始化 COM MTA，与正式播放器创建时所在的 CLR 后台线程环境对应。MP4/MOV/M4V 使用相同的 avformat 选项。音频采用 VLC 的实际 Windows 音频输出，没有注册替代输出的采样回调。

这里没有 LibVLCSharp、WPF 控件、分类/数据库与主软件后台任务；每次 play 重建实例以使初始化参数生效，生产软件则复用实例。因此它是 VLC 调用层的实验环境，不能单凭脚本正常就认定正式软件修复。硬件视频解码设置不在此音频实验中模拟。原 C# 诊断工具保留，但本次实验使用这个脚本。

## 环境和记录

start.cmd 优先使用本机已存在的 Codex Python，找不到时使用 PATH 中的 Python。也可手动执行 `python -i tools/AudioLab/audio_lab.py`。不安装 Python 包，不替换 VLC，不改动主软件配置、DB 和 Storage。VLC_DIRECTORY、MEDIA_PATH 可在参数文件修改。

每次实验的参数、VLC 版本、暂停/音量/进度操作和结束状态存入 `artifacts/audio-lab/trial-*.jsonl`。退出等待原生停止并释放媒体引用。要查看原生 VLC 模块日志，可临时在 VLC_OPTIONS 添加 `--verbose=2`；详细日志显示在终端，JSONL 记录不会自动包含它。

最小自检：`python tools/AudioLab/test_audio_lab.py`。它仅在临时目录生成全零静音 WAV，检查实际 Music/Video/None 播放、播放后设音量 0、暂停/恢复/跳转、参数文件重读及退出后独占读取；不验证真实听感，不运行主软件或完整测试套件。
