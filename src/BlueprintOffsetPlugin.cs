using System;
using System.Globalization;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace DspBlueprintOffsetMod
{
    /// <summary>
    /// 戴森球计划 —— 蓝图偏移调节器
    ///
    /// 功能：
    ///  1. 从系统剪贴板读取游戏蓝图（复制蓝图后剪贴板中的 "BLUEPRINT:1,..." 字符串）；
    ///  2. 在游戏内窗口输入 X/Y/Z 偏移量，对所有建筑（含分拣器第二端点、太阳帆发射器/射线接收站的第二节点）
    ///     的 localOffset 施加平移；
    ///  3. 所有偏移坐标统一量化到小数点后 4 位（Math.Round(v, 4)）；
    ///  4. 写回剪贴板，直接粘贴使用；也可以直接应用到当前“蓝图粘贴预览”中的蓝图。
    ///
    /// 快捷键：Ctrl + Shift + F9 开关窗口。
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class BlueprintOffsetPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "dsp.mod.blueprintOffset";
        public const string PluginName = "蓝图偏移调节器 (Blueprint Offset Adjuster)";
        public const string PluginVersion = "1.1.0";

        private const float QuantizeStep = 0.0001f; // 小数点后 4 位
        private static readonly Vector2 WindowSize = new Vector2(420f, 330f);

        private ManualLogSource _log;
        private Rect _windowRect = new Rect(60f, 60f, WindowSize.x, WindowSize.y);
        private bool _showWindow;

        private string _offX = "0";
        private string _offY = "0";
        private string _offZ = "0";

        // 线性变换缩放系数：x' = scaleX * x + offX
        private string _scaleX = "1.0000";
        private string _scaleY = "1.0000";
        private string _scaleZ = "1.0000";

        private BlueprintData _loaded;      // 从剪贴板载入的蓝图
        private BlueprintData _backup;      // 应用前的备份，用于“撤销”
        private string _status = "请先在游戏里复制一个蓝图（剪贴板），再点【读取剪贴板蓝图】。";

        private void Awake()
        {
            _log = Logger;
            _log.LogInfo($"{PluginName} v{PluginVersion} 已加载。按 Ctrl+Shift+F9 打开窗口。");
        }

        private void Update()
        {
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKey(KeyCode.LeftShift) &&
                Input.GetKeyDown(KeyCode.F9))
            {
                _showWindow = !_showWindow;
            }
        }

        private void OnGUI()
        {
            if (!_showWindow) return;
            _windowRect = GUILayout.Window(0x4F53, _windowRect, DrawWindow, PluginName);
        }

        // ------------------------- 窗口 UI -------------------------

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            // 蓝图信息
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(_loaded == null ? "当前蓝图：未载入" :
                $"当前蓝图：{_loaded.buildings.Length} 个建筑，区域 {_loaded.areas.Length} 个");
            GUILayout.Label(_status);
            GUILayout.EndVertical();

            // 偏移输入
            GUILayout.Space(6f);
            GUILayout.Label("偏移量（沿行星本地坐标，单位：格，精确到小数点后 4 位）：");
            GUILayout.BeginHorizontal();
            GUILayout.Label("X", GUILayout.Width(20f));
            _offX = GUILayout.TextField(_offX);
            GUILayout.Label("Y(高)", GUILayout.Width(42f));
            _offY = GUILayout.TextField(_offY);
            GUILayout.Label("Z", GUILayout.Width(20f));
            _offZ = GUILayout.TextField(_offZ);
            GUILayout.EndHorizontal();

            // 线性变换缩放系数
            GUILayout.Space(6f);
            GUILayout.Label("缩放系数（线性变换 x' = 缩放 × x + 偏移，精确到小数点后 4 位）：");
            GUILayout.BeginHorizontal();
            GUILayout.Label("X", GUILayout.Width(20f));
            _scaleX = GUILayout.TextField(_scaleX);
            GUILayout.Label("Y(高)", GUILayout.Width(42f));
            _scaleY = GUILayout.TextField(_scaleY);
            GUILayout.Label("Z", GUILayout.Width(20f));
            _scaleZ = GUILayout.TextField(_scaleZ);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("整体放大 1.1000")) SetScaleDelta(0.1f);
            if (GUILayout.Button("整体缩小 0.9000")) SetScaleDelta(-0.1f);
            if (GUILayout.Button("缩放重置 1.0000")) { _scaleX = _scaleY = _scaleZ = "1.0000"; }
            GUILayout.EndHorizontal();

            // 常用快捷偏移
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("↑ 0.1000")) SetOffsetDelta(0f, 0f, -1f);
            if (GUILayout.Button("↓ 0.1000")) SetOffsetDelta(0f, 0f, 1f);
            if (GUILayout.Button("← 0.1000")) SetOffsetDelta(-1f, 0f, 0f);
            if (GUILayout.Button("→ 0.1000")) SetOffsetDelta(1f, 0f, 0f);
            if (GUILayout.Button("升 0.1000")) SetOffsetDelta(0f, 1f, 0f);
            if (GUILayout.Button("降 0.1000")) SetOffsetDelta(0f, -1f, 0f);
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);

            // 主操作
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("读取剪贴板蓝图", GUILayout.Height(30f)))
                ReadClipboard();
            GUI.enabled = _loaded != null;
            if (GUILayout.Button("应用偏移 → 写回剪贴板", GUILayout.Height(30f)))
                ApplyAndCopyBack();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = _backup != null;
            if (GUILayout.Button("撤销本次偏移"))
                UndoApply();
            GUI.enabled = _loaded != null;
            if (GUILayout.Button("应用到当前粘贴预览"))
                ApplyToLiveClipboard();
            GUI.enabled = true;
            if (GUILayout.Button("清空"))
                ResetAll();
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.Label("提示：先用游戏内复制工具复制蓝图，再读取；应用后直接粘贴即可。\n坐标一律量化到 0.0001（小数点后 4 位）。");

            GUILayout.EndVertical();

            GUI.DragWindow(new Rect(0f, 0f, float.MaxValue, 24f));
        }

        private void SetScaleDelta(float d)
        {
            _scaleX = Quantize(ParseFloat(_scaleX) + d).ToString("0.0000", CultureInfo.InvariantCulture);
            _scaleY = Quantize(ParseFloat(_scaleY) + d).ToString("0.0000", CultureInfo.InvariantCulture);
            _scaleZ = Quantize(ParseFloat(_scaleZ) + d).ToString("0.0000", CultureInfo.InvariantCulture);
        }

        private void SetOffsetDelta(float dx, float dy, float dz)
        {
            float x = ParseFloat(_offX), y = ParseFloat(_offY), z = ParseFloat(_offZ);
            _offX = Quantize(x + dx * 0.1f).ToString("0.0000", CultureInfo.InvariantCulture);
            _offY = Quantize(y + dy * 0.1f).ToString("0.0000", CultureInfo.InvariantCulture);
            _offZ = Quantize(z + dz * 0.1f).ToString("0.0000", CultureInfo.InvariantCulture);
        }

        // ------------------------- 核心逻辑 -------------------------

        /// <summary>量化到小数点后 4 位。</summary>
        private static float Quantize(float v) => (float)Math.Round(v, 4, MidpointRounding.AwayFromZero);

        private static float ParseFloat(string s)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
        }

        private void ReadClipboard()
        {
            try
            {
                string clip = GUIUtility.systemCopyBuffer;
                if (string.IsNullOrEmpty(clip) || !clip.TrimStart().StartsWith("BLUEPRINT:1,"))
                {
                    _status = "✗ 剪贴板里没有蓝图数据（请先在游戏里复制蓝图）。";
                    return;
                }

                var bp = BlueprintData.CreateNew(clip.Trim());
                if (bp == null || !bp.isValid)
                {
                    _status = "✗ 蓝图解析失败（数据无效）。";
                    return;
                }

                _loaded = bp;
                _backup = null;
                _status = $"✓ 已载入蓝图：{bp.buildings.Length} 个建筑。";
                _log.LogInfo($"载入蓝图成功，建筑数 {bp.buildings.Length}");
            }
            catch (Exception e)
            {
                _status = "✗ 读取失败：" + e.Message;
                _log.LogError(e);
            }
        }

        private void ApplyAndCopyBack()
        {
            if (_loaded == null) return;
            try
            {
                if (_backup == null) _backup = CloneBlueprint(_loaded);

                var t = ReadTransform();
                TransformBlueprint(_loaded, t);

                GUIUtility.systemCopyBuffer = _loaded.ToBase64String();
                _status = $"✓ 已应用线性变换并写回剪贴板，可直接粘贴。{Describe(t)}";
            }
            catch (Exception e)
            {
                _status = "✗ 应用失败：" + e.Message;
                _log.LogError(e);
            }
        }

        /// <summary>读取 UI 中的线性变换参数（各坐标均量化到小数点后 4 位）。</summary>
        private LinearTransform ReadTransform()
        {
            return new LinearTransform
            {
                scaleX = Quantize(ParseFloat(_scaleX)),
                scaleY = Quantize(ParseFloat(_scaleY)),
                scaleZ = Quantize(ParseFloat(_scaleZ)),
                offX = Quantize(ParseFloat(_offX)),
                offY = Quantize(ParseFloat(_offY)),
                offZ = Quantize(ParseFloat(_offZ)),
            };
        }

        private static string Describe(LinearTransform t)
        {
            return $"缩放 ({t.scaleX:0.0000}, {t.scaleY:0.0000}, {t.scaleZ:0.0000})，" +
                   $"偏移 ({t.offX:0.0000}, {t.offY:0.0000}, {t.offZ:0.0000})";
        }

        /// <summary>对蓝图内所有建筑应用线性变换 x' = s·x + o，坐标四舍五入到小数点后 4 位。</summary>
        private static void TransformBlueprint(BlueprintData bp, LinearTransform t)
        {
            if (bp.buildings == null) return;
            foreach (var b in bp.buildings)
            {
                b.localOffset_x = Quantize(b.localOffset_x * t.scaleX + t.offX);
                b.localOffset_y = Quantize(b.localOffset_y * t.scaleY + t.offY);
                b.localOffset_z = Quantize(b.localOffset_z * t.scaleZ + t.offZ);

                // 第二端点（分拣器另一头 / 太阳帆发射器与射线接收站节点）
                if (b.itemId > 2000 && b.itemId < 2030)
                {
                    b.localOffset_x2 = Quantize(b.localOffset_x2 * t.scaleX + t.offX);
                    b.localOffset_y2 = Quantize(b.localOffset_y2 * t.scaleY + t.offY);
                    b.localOffset_z2 = Quantize(b.localOffset_z2 * t.scaleZ + t.offZ);
                }
            }
        }

        private struct LinearTransform
        {
            public float scaleX, scaleY, scaleZ;
            public float offX, offY, offZ;
        }

        private void ApplyToLiveClipboard()
        {
            try
            {
                var live = GetLiveBlueprint();
                if (live == null || !live.isValid)
                {
                    _status = "✗ 当前没有打开中的蓝图粘贴（先打开粘贴预览再试）。";
                    return;
                }

                if (_backup == null) _backup = CloneBlueprint(live);

                var t = ReadTransform();
                TransformBlueprint(live, t);
                _status = $"✓ 已对当前粘贴中的蓝图应用线性变换。{Describe(t)}";
            }
            catch (Exception e)
            {
                _status = "✗ 应用到粘贴预览失败：" + e.Message;
                _log.LogError(e);
            }
        }

        private void UndoApply()
        {
            try
            {
                if (_backup == null) return;
                if (_loaded != null) RestoreInto(_backup, _loaded);
                var live = GetLiveBlueprint();
                if (live != null && live.isValid) RestoreInto(_backup, live);

                GUIUtility.systemCopyBuffer = _loaded != null
                    ? _loaded.ToBase64String()
                    : GUIUtility.systemCopyBuffer;

                _backup = null;
                _status = "✓ 已撤销本次偏移。";
            }
            catch (Exception e)
            {
                _status = "✗ 撤销失败：" + e.Message;
                _log.LogError(e);
            }
        }

        /// <summary>获取当前“蓝图粘贴”中的蓝图（位于 PlayerAction_Build）。</summary>
        private static BlueprintData GetLiveBlueprint()
        {
            var mainPlayer = GameMain.mainPlayer;
            var controller = mainPlayer != null ? mainPlayer.controller : null;
            var actionBuild = controller != null ? controller.actionBuild : null;
            return actionBuild != null ? actionBuild.blueprintClipboard : null;
        }

        /// <summary>深拷贝蓝图（通过导出/导入字符串，最稳妥）。</summary>
        private static BlueprintData CloneBlueprint(BlueprintData src)
        {
            return BlueprintData.CreateNew(src.ToBase64String());
        }

        /// <summary>把 src 的建筑偏移值复制回 dst（只还原位置相关字段，其余不动）。</summary>
        private static void RestoreInto(BlueprintData src, BlueprintData dst)
        {
            if (src?.buildings == null || dst?.buildings == null) return;
            int n = Math.Min(src.buildings.Length, dst.buildings.Length);
            for (int i = 0; i < n; i++)
            {
                var s = src.buildings[i];
                var d = dst.buildings[i];
                d.localOffset_x = s.localOffset_x;
                d.localOffset_y = s.localOffset_y;
                d.localOffset_z = s.localOffset_z;
                d.localOffset_x2 = s.localOffset_x2;
                d.localOffset_y2 = s.localOffset_y2;
                d.localOffset_z2 = s.localOffset_z2;
            }
        }

        private void ResetAll()
        {
            _loaded = null;
            _backup = null;
            _offX = _offY = _offZ = "0";
            _scaleX = _scaleY = _scaleZ = "1.0000";
            _status = "已清空。请先在游戏里复制一个蓝图。";
        }
    }
}
