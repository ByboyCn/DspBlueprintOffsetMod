# 蓝图偏移调节器 (DspBlueprintOffsetMod)

戴森球计划 (Dyson Sphere Program) 的 BepInEx 模组：在游戏内以图形窗口调节蓝图的偏移数据，
所有偏移坐标统一量化到 **小数点后 4 位（0.0001）**。

## 功能

- **一键读取蓝图**：蓝图检查器"复制"按钮旁注入了【读取蓝图】按钮，直接载入当前蓝图（无需经过剪贴板）；也支持从系统剪贴板读取。
- **线性变换**：`x' = 缩放 × x + 偏移`（X/Y/Z 三轴独立）。
- **垂直叠加**：设定层间距后，点【▲ 叠加一层】自动累加 Y 偏移，逐层堆叠多层工厂。
- **UI 缩放配置**：窗口内"设置"折叠区可实时调节 UI 缩放（0.5~2.0）与透明度，自动保存到 BepInEx 配置文件（`BepInEx/config/dsp.mod.blueprintOffset.cfg`）；窗口大小/位置始终被限制在屏幕内。
- 对蓝图内**所有建筑**施加变换：普通建筑 `localOffset_x/y/z`；分拣器第二端点、太阳帆发射器 / 射线接收站第二节点 `localOffset_x2/y2/z2` 同步变换。
- 每个坐标值经 `Math.Round(v, 4)`（四位小数、四舍五入）后写回。
- 应用前自动备份、一键撤销。

## 快捷键

| 按键 | 功能 |
|---|---|
| `Ctrl + Shift + F9` | 打开 / 关闭调节窗口 |

## 使用流程

1. 打开任意蓝图（蓝图浏览器 → 检查器），点"复制"旁的【读取蓝图】；或先在游戏里复制蓝图再点【读取剪贴板蓝图】。
2. 设置缩放 / 偏移；多层堆叠时设好层间距，每粘贴一层点【▲ 叠加一层】。
3. 点【应用线性变换 → 写回剪贴板】，回游戏直接粘贴即可。
4. 误操作可点【撤销本次变换】恢复。

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
