# 蓝图偏移调节器 (DspBlueprintOffsetMod)

戴森球计划 (Dyson Sphere Program) 的 BepInEx 模组：在游戏内以图形窗口调节蓝图的偏移数据，
所有偏移坐标统一量化到 **小数点后 4 位（0.0001）**。

## 功能

- 从系统剪贴板读取游戏蓝图（游戏内复制蓝图后剪贴板中的 `BLUEPRINT:1,...` 数据）。
- 输入 X / Y / Z 偏移量，对蓝图内**所有建筑**施加平移：
  - 普通建筑：`localOffset_x/y/z`；
  - 分拣器第二端点、太阳帆发射器 / 射线接收站第二节点：`localOffset_x2/y2/z2` 同步偏移。
- 每个坐标值经 `Math.Round(v, 4)`（四位小数、四舍五入）后写回。
- 一键写回剪贴板，直接粘贴使用；或直接应用到当前打开的"蓝图粘贴预览"。
- 内置 ↑↓←→升降 0.1000 微调按钮、应用前自动备份、一键撤销。

## 快捷键

| 按键 | 功能 |
|---|---|
| `Ctrl + Shift + F9` | 打开 / 关闭调节窗口 |

## 使用流程

1. 游戏内用复制工具复制一个蓝图（剪贴板中已包含蓝图数据）。
2. 按 `Ctrl + Shift + F9` 打开窗口，点【读取剪贴板蓝图】。
3. 输入偏移量（支持小数点后 4 位，如 `0.1234`），点【应用偏移 → 写回剪贴板】。
4. 回到游戏直接粘贴即可；Y 轴为高度方向。
5. 误操作可点【撤销本次偏移】恢复。

## 安装

依赖 [BepInEx 5.x](https://github.com/BepInEx/BepInEx)（游戏目录已有 `BepInEx` 文件夹即可）。

将 `DspBlueprintOffsetMod.dll` 放入：

```
<DSP 游戏目录>\BepInEx\plugins\
```

本机已自动部署完成，启动游戏即可。

## 构建

需要 .NET SDK 与本机 DSP 游戏安装（csproj 中 `GameDir` 属性指向游戏目录，可自行修改）：

```
dotnet build DspBlueprintOffsetMod.csproj -c Release
```

产物位于 `bin\Release\DspBlueprintOffsetMod.dll`。

## 技术说明

- 蓝图解析/导出直接调用游戏自身的 `BlueprintData.CreateNew(string)` / `ToBase64String()`,
  与游戏版本的原生蓝图格式（头部字符串 + Base64）完全兼容。
- 粘贴预览中的实时蓝图位于 `PlayerAction_Build.blueprintClipboard`，通过
  `GameMain.mainPlayer.controller.actionBuild` 访问。
