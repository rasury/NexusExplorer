# APNG 素材

8×8 的合成文件由 Pillow 生成，不包含用户媒体。测试运行不依赖 Pillow。

- loop.png：APNG 以 .png 保存，红／绿／蓝，80/120/160ms，无限循环。
- finite.apng：红／绿，60/90ms，播放两轮。
- poster.apng：不属于动画的紫色默认封面，动画帧为红／绿；封面不得显示或参与动画合成。
- disposal.apng：透明局部色块，200ms，disposal=NONE/BACKGROUND/PREVIOUS/NONE，blend=SOURCE/SOURCE/OVER/OVER。disposal-0～3.png 为 Pillow 的独立合成参考。
- blend.apng：不透明蓝色底图上覆盖 alpha=128 的红色，按 Porter-Duff OVER 计算为 RGBA(128,0,127,255)，参考图为 blend-reference.png。不用 Pillow 的 RGBA paste 结果代替标准 alpha 叠加；允许 1 级通道量化误差。
- static.png：普通静态 PNG。
