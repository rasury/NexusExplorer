# Disable 后恢复音轨：2026-10-04

用户反馈音频与视频都有“恢复后短响、静音约一秒”；官方 VLC 没有短响，但也要等约一秒。正式程序此前直接调用 `SetAudioTrack(-1)` 取消音轨，恢复时重新选择；日志记录 Speex 模块重建。

## 对比证据

Python 最小宿主加载固定目录相同 LibVLC 3.0.21，用相同 DirectSound＋Speex 参数比较取消音轨/恢复与静音/解除静音。素材是自动生成的静音 WAV、320 kbps / 48 kHz MP3，不绕过实际输出。前者重建解码器、DirectSound 流，日志出现 `playback way too early ... playing silence`，分别插入 52,752、50,976 个零采样，约 1.099、1.062 秒；仅静音没有重建和补静音记录。证据在 `artifacts/audio-track-probe.txt`、`artifacts/audio-track-mp3-probe.txt`。

VLC [同步实现](https://github.com/videolan/vlc/blob/3.0.21/src/audio_output/dec.c)在输出时钟过早时插入静音，支持避免重建的修正。尚未证明“先响一下”的全部内部机理；媒体统计周期更新，不能把 buffer 计数更新间隔等同于扬声器静音时长。

## 修正与生命周期

- Disable 关闭声音并保留音轨解码、时钟，因此静音期间仍有音频解码开销。恢复同一音轨只解除静音，不 seek、暂停或重启媒体。菜单选中状态与实际解码音轨分开；音量调整不解除静音，恢复保留音量与播放/暂停状态。选择其他实际音轨仍通过 VLC 切换，不能保证它没有解码等待。
- [原生输出实现](https://github.com/videolan/vlc/blob/3.0.21/src/audio_output/output.c)可能排队静音请求，即时 getter 可仍是旧值。监听 Muted/Unmuted 确认实际状态，结束解除订阅；两秒仅是失败超时，没有固定等待。
- [Stop](https://github.com/videolan/vlc/blob/3.0.21/lib/media_player.c)会终止输出，[静音 API](https://github.com/videolan/vlc/blob/3.0.21/lib/audio.c)此时不能更新静音。最小测试发现直接在停止后解除静音，会让下一文件继承无声。仅从 Disable 停播时，用公开 `SetAudioOutput("directsound")` 重建停止状态的输出对象，再确认解除静音、恢复音量；保留播放器和视频窗口，不发出旧媒体声音。
- 沿用串行命令和请求版本检查，不从原生回调调用 Stop；音频与视频共用修正，保留 DirectSound、Speex、插件缓存和既有解码配置。

## 最小验证

新增音频 WAV、含音轨 AVI 两项原生检查：连续三次关闭/恢复时保留原生音轨、确认实际静音状态；音量保持、静音期间解码推进；无额外输出/重采样重建或补静音日志；暂停时切换不恢复播放；无效音轨报错；关闭后停止再播放不继承静音；停止后文件可独占读取。另外两项必要关联检查验证实际 DirectSound＋Speex、seek、暂停恢复及释放。四项通过、0 失败、0 跳过，见 `artifacts/audio-track-min-tests.txt`，未运行完整套件。素材静音，不代替真实听感验收。

人工验收：音频和视频各连续选择 Disable → Track 1，确认没有短响后静音；关闭声音后调整音量、暂停、恢复音轨，应保留音量且仍暂停；关闭声音后播放下一文件，应恢复声音。更新固定目录继续保护 Storage、数据库、配置和日志。
