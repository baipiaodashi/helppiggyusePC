# 小猪形象

原创小猪：粗描边、圆胖身体、大鼻子、头顶一撮小卷毛。

- `gen.py` 生成每个动作的 SVG（`svgs.json`）
- `render.js` 用 Playwright 把 SVG 渲染成透明 PNG：`node render.js svgs.json ../../src/PiggyHelper/Assets/pig 400`

| 动作 | 用在哪 |
| --- | --- |
| wave 招手 | 侧边栏 |
| cheer 欢呼 | 首页、清理完成 |
| broom 扫帚 | 一键清理 |
| think 思考 | AI 问答 |
| search 放大镜 | 搜索没结果、大文件整理 |
| key 举着键帽 | 快捷键指南 |
| face 脑袋 | 头像、图标、提示卡 |
| sleep 睡觉 | 空状态 |
