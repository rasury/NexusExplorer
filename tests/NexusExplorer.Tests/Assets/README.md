# 原生媒体回归素材

seek-h264-aac.mp4 是自行生成的 4 秒测试图案与 440 Hz 正弦音频，320×180、30 fps、H.264/AAC 双声道。没有用户数据或第三方画面。测试使用真实 WPF VideoView，在硬件与软件解码下反向跳转、检查音视频解码及停止后句柄释放；素材缺失直接失败。

生成命令（生成时使用 FFmpeg 9.0.2；运行测试不依赖 FFmpeg）：

```text
ffmpeg -f lavfi -i testsrc2=size=320x180:rate=30 -f lavfi -i sine=frequency=440:sample_rate=48000 -t 4 -c:v libx264 -preset fast -g 30 -pix_fmt yuv420p -c:a aac -ac 2 -movflags +faststart seek-h264-aac.mp4
```
