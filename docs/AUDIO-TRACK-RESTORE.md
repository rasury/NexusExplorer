# 真正 Disable 与 WASAPI 音轨恢复：2026-10-04

音频与视频选择 Disable 再恢复 Track 1 时，用户听到短响后约一秒静音。官方 VLC 默认输出平滑等待约一秒；用户将官方 VLC 改成 DirectX 音频输出并重启后，也复现先响一下。用户要求保留真正的底层音轨切换，明确拒绝静音替代，并随后选择 WASAPI 输出。

## 已验证与未验证

- 原正式程序与 Python 最小宿主加载相同 LibVLC 3.0.21，DirectSound＋Speex 下取消音轨会销毁解码器和输出流，恢复后日志记录补约一秒静音。独立静音 WAV/MP3 对比见 artifacts/audio-track-probe.txt、audio-track-mp3-probe.txt。
- 官方应用使用同一输出也复现，说明短响现象不是 NexusExplorer 独有。DirectSound [创建缓冲代码](https://github.com/videolan/vlc/blob/3.0.21/modules/audio_output/directsound.c)会清零新缓冲；未播放时的 TimeGet 会失败。VLC [音频同步](https://github.com/videolan/vlc/blob/3.0.21/src/audio_output/dec.c)在不能获取时间时跳过校正，后续发现过早才补静音。这支持“初始新数据过早输出”的解释，不能把短响直接定性为旧数据未 Flush。
- 同一 SDK 改用 MMDevice/WASAPI＋Speex 的静音 MP3 对比已实际选中 mmdevice、wasapi、speex_resampler，记录正常补静音对齐；见 artifacts/audio-track-mmdevice-probe.txt。静音素材、状态与日志都不能证明真正听到的波形没有短响，也不能替代持续音质验收。

## 最终实现

1. 撤销 56a94ef 中的静音替代：Disable 直接 SetAudioTrack(-1)，真正取消选择、销毁音频解码器。正值直接选择对应音轨；没有静音、音量门控、固定等一秒、seek 或重启媒体来替代底层选择。
2. 菜单读取实际原生音轨。调用记录返回结果并等待对应 ESSelected/实际音轨确认，五秒仅为失败超时，不是正常切换延迟；订阅在 finally 中解除。保留串行命令和媒体请求版本保护。
3. 用户选择 WASAPI，音频与视频共享 `--aout=mmdevice`、`--mmdevice-backend=wasapi`、`--audio-resampler=speex_resampler`。VLC 3 的 [MMDevice 定义](https://github.com/videolan/vlc/blob/3.0.21/modules/audio_output/mmdevice.c)提供 audio output，加载 backend；[WASAPI 定义](https://github.com/videolan/vlc/blob/3.0.21/modules/audio_output/wasapi.c)是 aout stream，不能只用 `--aout=wasapi` 假定成功。日志提升实际 aout stream 选择，测试核对真实模块，避免只看传入参数。
4. 未接管 PCM 输出，也没有通过私有指针或二进制补丁调用 SDK 内部 Flush。WASAPI 自身的 Flush 使用 IAudioClient Stop/Reset，但不把正常音轨重建延迟当成应用应伪造的静音期。
5. 插件缓存生成/验证使用同一组新参数，缓存独立进程确认 MMDevice 可用且没有扫描加载全部 DLL；实际 WASAPI 后端在真实播放检查中确认。Storage、DB、配置及用户实验参数保留。

## 最小验证与人工验收

四项 C# 原生检查：音频 WAV 与带音轨 AVI 确认 mmdevice、wasapi、speex_resampler 实际选中，控制及释放正常；两类文件各验证原生音轨为 -1、解码器销毁、静音状态未被打开、音频解码统计停止、恢复时 WASAPI 流/重采样真实重建、音量保持、暂停状态保持、无效 ID 报错与文件释放。输出 artifacts/audio-track-wasapi-min-tests.txt。

Python 仅运行新增一项原生切轨检查，覆盖 tracks() 的链表读取及释放、实际 ID、取消与恢复、无效 ID 不改变当前选择；见 artifacts/audio-track-lab-min-tests.txt。C# 四项与 Python 一项均通过，无失败、无跳过。未运行完整测试。

以上使用静音素材。实际破音/电流声、切轨短响与视频同步仍需用户在新版本复验。正确的预期是恢复可无声等待正常管线对齐，而非取消解码工作来伪装立即恢复。
