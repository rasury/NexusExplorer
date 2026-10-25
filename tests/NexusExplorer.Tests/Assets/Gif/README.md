# GIF 动画素材

使用 Pillow 生成的 8×8 合成素材，不含用户图片。运行 C# 测试不需要 Python/Pillow。

- loop.gif：红／绿／蓝三帧，80/120/160ms，无限循环。
- finite.gif：红／蓝两帧，50/80ms，NETSCAPE repeat=1，即播放两轮后停止。
- single.gif：灰色单帧，无动画循环扩展。
- disposal.gif：透明背景四个局部色块，200ms 每帧，disposal=1/2/3/1，含背景恢复及前一画面恢复。disposal-0～3.png 是 Pillow 解码的独立合成参考。

通过真正的生产解码、计时器与 WPF 图片控件验证像素和生命周期；透明像素仅比较 alpha，避免把不可见 RGB 值误判为画面错误。
