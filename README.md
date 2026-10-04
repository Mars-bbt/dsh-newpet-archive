# dsh-newpet

给 **DeepSeek Harness** 用的鲸鱼娘桌面宠物插件。

## 版本

- **1.0**：修复前的初版，保留原立绘。
- **1.1**：保留同一套原立绘，修复后台唤起跳转网页、唤起后无法正常关闭到后台，以及继承 Electron/Node 模式变量导致唤起失效的问题。

这两个版本保存在私有仓库 `Mars-bbt/dsh-newpet-archive` 的 Releases 中，下载对应版本的 `.tgz` 后按下面的本地安装命令安装。

> 桌宠是**桌面上独立的透明置顶窗口**，不依赖 DSH 窗口——DSH 最小化后依然悬浮在所有窗口最前面，可以拖到屏幕任意位置。

## 功能特性

| 能力 | 说明 |
|---|---|
| **桌面悬浮桌宠** | `desktop-pet/WhaleOverlay.exe`（WPF 透明置顶窗口），插件加载时自动拉起、卸载时结束 |
| **跟随任务状态** | 只读本机会话状态文件：工作中切「坐着抱笔记本」立绘、任务结束弹可点击提示、出错切 failure |
| **可折叠任务气泡** | 气泡显示**我下达的任务** + 当前进度/工具/第几步/已工作时长，点一下折叠 |
| **完成提示** | `✅ 任务已完成` + **[知道了]**（只关闭）/ **[查看]**（跳到聊天页） |
| **悬停快捷条** | 鼠标悬停宠物 → 下方出现两个图标按钮：💻 呼出桌面端 / 💰 查看余额 |
| **余额查询** | 走插件宿主接口读本机凭据调 DeepSeek 官方接口，另有本进程直连兜底 |
| **称呼** | 设置面板可改「如何称呼我 / 她的自称」，写进 `desktop-pet/names.json` 供桌宠使用 |
| **贴边但不出屏** | 立绘本体可贴屏幕左右下三条边，但不会跑到桌面之外 |
| **单实例** | 内置互斥锁，重复拉起只会有一只桌宠 |

## 桌面桌宠用法

- **拖动**：在立绘或气泡上按住左键拖动；拖动时气泡自动隐藏，只剩立绘跟着鼠标
  - 立绘本体不会离开桌面工作区；松手自动记住位置
- **缩放**：右键 放大 / 缩小（每次 ×1.25 / ÷1.25，范围 0.15~3.0）/ 重置大小
- **单击**：抚摸 + 呼出 DeepSeek Harness 桌面端
- **双击**：庆祝 + 跳跃 + 粒子；**三击**：转圈
- **悬停**：宠物下方弹出快捷条（💻 呼出桌面端 / 💰 查看余额）
- **右键菜单**：放大 / 缩小 / 重置大小 / 回到默认位置 / 开机自启 / 呼出 DeepSeek Harness / 退出桌宠
- **动效**：呼吸（6px / 1.5s 往返）、摇摆（±2.6° / 6s）、换姿势交叉淡入、待机挤压、粒子特效、注视跟随

## 设置面板

**设置 → 看板娘** 下只有两项：

1. **桌面悬浮桌宠**：启动 / 关闭 按钮（含运行状态提示）
2. **称呼**：如何称呼我 / 她的自称（桌面桌宠说话时会用）

## 安装

```bash
dsh plugin --profile desktop add <本插件目录或 tgz>
```

或从 GitHub：

```bash
dsh plugin --profile desktop add github:Mars-bbt/dsh-newpet-archive
```

装完**重启 DeepSeek Harness**。插件加载时会自动拉起桌面悬浮桌宠，插件卸载时随之结束。

## 目录

```
dsh-newpet/
  lib/index.js        宿主半边:静态资源路由 + 拉起/结束桌面桌宠 + 余额接口 + 称呼接口
  lib/client.js       网页半边:设置面板(桌宠启停 / 称呼)
  assets/             立绘(webp)与表现层资源
  desktop-pet/
    WhaleOverlay.exe  桌面悬浮桌宠(WPF 透明置顶窗口)
    assets/*.png      带 alpha 的立绘 PNG(桌面桌宠用)
  cordis.patch.yml    bundle 层
```

## 实现要点

1. **立绘是带 alpha 的 webp（VP8L），但 WPF/WIC 的 webp 解码会丢 alpha** → 桌面桌宠改用 libwebp(sharp) 预先转好的 PNG，避免出现黑底方块。
2. **WPF 的 `ShaderEffect` / `OpacityMask` 在 `AllowsTransparency=true` 的透明分层窗口里会让整窗渲染成空白** → 一律不用，改为纯图片图层 + 原生动画。
3. **透明置顶窗口需要"置顶守护"**：定时用 `SetWindowPos(HWND_TOPMOST)` 重新提升，否则会被全屏窗口压到下面。
4. **呼出桌面端**：通过正在运行的桌面端程序的单实例入口通知已有进程唤起窗口，由 DeepSeek Harness 自身管理显示、恢复、聚焦和再次关闭到后台，不再跳转网页。唤起时清除从插件宿主继承的 Electron/Node 模式变量，确保启动桌面端而不是 Node 命令行。
5. **任务状态**来源是本地会话状态文件（`session_projcache/sessions/*.json` 的 `prompt` / `openStep` / `draft` / `pendingCalls`），只读文件、不调需要鉴权的接口。

## 桌宠源码与编译

`desktop-pet/WhaleOverlay.cs` 是桌宠源码。在 Windows 上用 .NET Framework 自带编译器构建：

```powershell
./desktop-pet/build.ps1
```

编译前在「设置 → 看板娘」关闭桌面悬浮桌宠，编译后重新启动桌宠即可加载新程序。

## 许可

本项目以 MIT 许可发布，见 [LICENSE](LICENSE)。
