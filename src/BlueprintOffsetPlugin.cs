using System;
using System.Globalization;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DspBlueprintOffsetMod
{
    /// <summary>
    /// 戴森球计划 —— 蓝图偏移调节器
    ///
    /// 功能：
    ///  1. 从系统剪贴板读取游戏蓝图，或在蓝图检查器“复制”按钮旁一键【读取蓝图】；
    ///  2. 线性变换：x' = 缩放 × x + 偏移（三轴独立），坐标量化到小数点后 4 位；
    ///  3. 垂直叠加：按层间距逐层累加 Y 偏移，方便多层堆叠工厂连续粘贴；
    ///  4. 窗口大小受屏幕限制，UI 缩放可在配置界面（BepInEx cfg）与窗口内实时调节。
    ///
    /// 快捷键：Ctrl + Shift + F9 开关窗口。
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("DSPGAME.exe")]
    public class BlueprintOffsetPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "dsp.mod.blueprintOffset";
        public const string PluginName = "蓝图偏移调节器 (Blueprint Offset Adjuster)";
        public const string PluginVersion = "1.2.0";

        internal static BlueprintOffsetPlugin Instance;

        internal ManualLogSource _log;
        private Rect _windowRect = new Rect(60f, 60f, 460f, 480f);
        private bool _showWindow;
        private bool _showSettings;
        private int _layerCount;

        // 换行文本样式（信息框 / 状态栏），避免长文字把窗口横向撑出屏幕
        private GUIStyle _wrapStyle;

        // ---- BepInEx 配置 ----
        private ConfigEntry<float> _cfgUiScale;
        private ConfigEntry<float> _cfgUiOpacity;

        // ---- 输入状态 ----
        private string _offX = "0";
        private string _offY = "0";
        private string _offZ = "0";
        private string _scaleX = "1.0000";
        private string _scaleY = "1.0000";
        private string _scaleZ = "1.0000";
        private string _layerGap = "3.0000";

        private BlueprintData _loaded;
        private BlueprintData _backup;
        private string _status = "请先复制蓝图，或在蓝图窗口点【读取蓝图】。";

        private void Awake()
        {
            Instance = this;
            _log = Logger;

            _cfgUiScale = Config.Bind("UI", "UIScale", 1.0f,
                new ConfigDescription("模组 UI 缩放倍率", new AcceptableValueRange<float>(0.5f, 2.0f)));
            _cfgUiOpacity = Config.Bind("UI", "UIOpacity", 1.0f,
                new ConfigDescription("模组 UI 不透明度", new AcceptableValueRange<float>(0.3f, 1.0f)));

            var h = new Harmony(PluginGuid);
            h.PatchAll(typeof(UIBlueprintInspectorPatches));
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

        // ------------------------- 供注入按钮调用 -------------------------

        /// <summary>蓝图检查器【读取蓝图】按钮：直接载入检查器当前蓝图。</summary>
        internal void ReadFromInspector(BlueprintData bp)
        {
            if (bp == null || !bp.isValid)
            {
                _status = "✗ 检查器中当前没有蓝图。";
                _showWindow = true;
                return;
            }
            _loaded = CloneBlueprint(bp);
            _backup = null;
            _layerCount = 0;
            _showWindow = true;
            _status = $"✓ 已从蓝图窗口直接读取：{_loaded.buildings.Length} 个建筑。";
            _log.LogInfo($"直接读取蓝图成功，建筑数 {_loaded.buildings.Length}");
        }

        // ------------------------- 窗口 -------------------------

        private void OnGUI()
        {
            if (!_showWindow) return;

            float s = Mathf.Clamp(_cfgUiScale.Value, 0.5f, 2.0f);
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp(_cfgUiOpacity.Value, 0.3f, 1.0f));

            // 限制窗口大小：宽度固定，不随内容增长，绝不超过屏幕（缩放后的逻辑坐标）
            float maxW = Screen.width / s - 8f;
            float maxH = Screen.height / s - 8f;
            _windowRect.width = Mathf.Clamp(460f, 380f, Mathf.Min(560f, maxW));
            _windowRect.height = Mathf.Clamp(_windowRect.height, 360f, Mathf.Min(720f, maxH));

            _windowRect = GUILayout.Window(0x4F53, _windowRect, DrawWindow, PluginName + " v" + PluginVersion);

            // 限制窗口位置：始终留在屏幕内
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, maxW - 40f);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, maxH - 40f);

            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        private void DrawWindow(int id)
        {
            if (_wrapStyle == null)
            {
                _wrapStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            }

            GUILayout.BeginVertical();

            // 蓝图信息（自动换行，防止长状态文字撑宽窗口）
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(_loaded == null ? "当前蓝图：未载入" :
                $"当前蓝图：{_loaded.buildings.Length} 个建筑，区域 {_loaded.areas.Length} 个", _wrapStyle);
            GUILayout.Label(_status, _wrapStyle, GUILayout.Height(_wrapStyle.CalcHeight(
                new GUIContent(_status), _windowRect.width - 30f)));
            GUILayout.EndVertical();

            // 缩放系数（线性变换）
            GUILayout.Space(4f);
            GUILayout.Label("缩放系数（x' = 缩放 × x + 偏移，精确到小数点后 4 位）：");
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

            // 偏移输入
            GUILayout.Space(4f);
            GUILayout.Label("偏移量（单位：格，精确到小数点后 4 位）：");
            GUILayout.BeginHorizontal();
            GUILayout.Label("X", GUILayout.Width(20f));
            _offX = GUILayout.TextField(_offX);
            GUILayout.Label("Y(高)", GUILayout.Width(42f));
            _offY = GUILayout.TextField(_offY);
            GUILayout.Label("Z", GUILayout.Width(20f));
            _offZ = GUILayout.TextField(_offZ);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("← 0.1000")) SetOffsetDelta(-1f, 0f, 0f);
            if (GUILayout.Button("→ 0.1000")) SetOffsetDelta(1f, 0f, 0f);
            if (GUILayout.Button("↑ 0.1000")) SetOffsetDelta(0f, 0f, -1f);
            if (GUILayout.Button("↓ 0.1000")) SetOffsetDelta(0f, 0f, 1f);
            if (GUILayout.Button("升 0.1000")) SetOffsetDelta(0f, 1f, 0f);
            if (GUILayout.Button("降 0.1000")) SetOffsetDelta(0f, -1f, 0f);
            GUILayout.EndHorizontal();

            // 垂直叠加
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"垂直叠加（当前已叠加 {_layerCount} 层），层间距：", GUILayout.Width(200f));
            _layerGap = GUILayout.TextField(_layerGap);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("▲ 叠加一层")) { SetOffsetDelta(0f, 1f, 0f); _layerCount++; }
            if (GUILayout.Button("▼ 回退一层")) { SetOffsetDelta(0f, -1f, 0f); _layerCount = Math.Max(0, _layerCount - 1); }
            if (GUILayout.Button("层数归零")) _layerCount = 0;
            if (GUILayout.Button("Y 偏移归零")) { _offY = "0.0000"; _layerCount = 0; }
            GUILayout.EndHorizontal();
            GUILayout.Label("用法：粘贴一层 → 点【▲ 叠加一层】→ 再粘贴，逐层堆叠。");

            GUILayout.Space(4f);

            // 主操作
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("读取剪贴板蓝图", GUILayout.Height(30f)))
                ReadClipboard();
            GUI.enabled = _loaded != null;
            if (GUILayout.Button("应用线性变换 → 写回剪贴板", GUILayout.Height(30f)))
                ApplyAndCopyBack();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = _backup != null;
            if (GUILayout.Button("撤销本次变换"))
                UndoApply();
            GUI.enabled = _loaded != null;
            if (GUILayout.Button("应用到当前粘贴预览"))
                ApplyToLiveClipboard();
            GUI.enabled = true;
            if (GUILayout.Button("清空"))
                ResetAll();
            GUILayout.EndHorizontal();

            // 设置折叠区
            GUILayout.Space(4f);
            _showSettings = GUILayout.Toggle(_showSettings, "设置（UI 大小 / 透明度，自动保存到配置）");
            if (_showSettings)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                DrawConfigSlider("UI 缩放", ref _cfgUiScale, 0.5f, 2.0f, "0.00");
                DrawConfigSlider("UI 不透明度", ref _cfgUiOpacity, 0.3f, 1.0f, "0.00");
                GUILayout.Label("快捷键：Ctrl + Shift + F9 开关窗口");
                GUILayout.EndVertical();
            }

            GUILayout.EndVertical();

            GUI.DragWindow(new Rect(0f, 0f, float.MaxValue, 24f));
        }

        private void DrawConfigSlider(string label, ref ConfigEntry<float> entry, float min, float max, string fmt)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {entry.Value.ToString(fmt, CultureInfo.InvariantCulture)}", GUILayout.Width(150f));
            float v = GUILayout.HorizontalSlider(entry.Value, min, max);
            if (Math.Abs(v - entry.Value) > 0.001f)
                entry.Value = (float)Math.Round(v, 2);
            if (GUILayout.Button("重置", GUILayout.Width(44f)))
                entry.Value = (min + max) / 2f;
            GUILayout.EndHorizontal();
        }

        // ------------------------- 基础操作 -------------------------

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
                _layerCount = 0;
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
                _status = "✓ 已撤销本次变换。";
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
            _layerCount = 0;
            _offX = _offY = _offZ = "0";
            _scaleX = _scaleY = _scaleZ = "1.0000";
            _status = "已清空。请先复制蓝图或在蓝图窗口点【读取蓝图】。";
        }
    }

    /// <summary>
    /// 在蓝图检查器（UIBlueprintInspector）的“复制”按钮旁注入【读取蓝图】按钮，
    /// 点击后直接把检查器当前蓝图载入模组，无需经过系统剪贴板。
    /// </summary>
    [HarmonyPatch(typeof(UIBlueprintInspector), "_OnCreate")]
    internal static class UIBlueprintInspectorPatches
    {
        [HarmonyPostfix]
        private static void AfterCreate(UIBlueprintInspector __instance)
        {
            try
            {
                var copyBtn = AccessTools.Field(typeof(UIBlueprintInspector), "copyButton")
                    ?.GetValue(__instance) as Button;
                if (copyBtn == null) return;

                var parent = copyBtn.transform.parent;
                var go = UnityEngine.Object.Instantiate(copyBtn.gameObject, parent);
                go.name = "ModReadBlueprintButton";
                go.SetActive(true);

                // 放到“复制”按钮旁边
                var srcRt = copyBtn.transform as RectTransform;
                var rt = go.transform as RectTransform;
                if (srcRt != null && rt != null)
                {
                    rt.localScale = srcRt.localScale;
                    rt.anchorMin = srcRt.anchorMin;
                    rt.anchorMax = srcRt.anchorMax;
                    rt.sizeDelta = srcRt.sizeDelta;
                    rt.anchoredPosition = srcRt.anchoredPosition + new Vector2(srcRt.sizeDelta.x + 6f, 0f);
                }

                // 改文字（避免同名按钮事件串扰：先摘掉旧回调）
                var btn = go.GetComponent<Button>();
                if (btn == null) return;
                btn.onClick = new Button.ButtonClickedEvent();

                var txt = go.GetComponentInChildren<Text>(true);
                if (txt != null) txt.text = "读取蓝图";

                btn.onClick.AddListener(() =>
                {
                    var bp = AccessTools.Field(typeof(UIBlueprintInspector), "blueprint")
                        ?.GetValue(__instance) as BlueprintData;
                    BlueprintOffsetPlugin.Instance?.ReadFromInspector(bp);
                });
            }
            catch (Exception e)
            {
                BlueprintOffsetPlugin.Instance?._log.LogError($"注入【读取蓝图】按钮失败: {e}");
            }
        }
    }
}
