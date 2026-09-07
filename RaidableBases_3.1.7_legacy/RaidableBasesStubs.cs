// Stub types for Harmony build (no Oxide). CUI types serialize to CommunityEntity JSON.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RaidableBases
{
    /// <summary>Stub for Oxide Timer (timer.Once returns this). Destroy() stops the timer without invoking the callback.</summary>
    public class Timer
    {
        public bool Destroyed { get; set; }
        public Action Callback { get; set; }
        public void Destroy() { if (!Destroyed) Destroyed = true; }
        public void Reset() { Destroyed = false; }
    }

    /// <summary>Stub for [HookMethod] - no-op attribute.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class HookMethodAttribute : Attribute
    {
        public HookMethodAttribute(string name) { }
    }

    /// <summary>Stub for [ConsoleCommand] - stores the console command name.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ConsoleCommandAttribute : Attribute
    {
        public string Command { get; }
        public ConsoleCommandAttribute(string name) { Command = name; }
    }

    /// <summary>Oxide-style CUI container that serializes to CommunityEntity AddUI JSON.</summary>
    public class CuiElementContainer : List<object>
    {
        public string Add(CuiPanel panel, string parent = "Overlay", string name = null, string destroy = null)
        {
            name ??= CuiHelperGetGuid();
            var el = new CuiElement { Name = name, Parent = parent, DestroyUi = destroy ?? name, FadeOut = panel?.FadeOut ?? 0f };
            if (panel != null)
            {
                if (panel.Image != null) el.Components.Add(MakeImage(panel.Image));
                if (panel.RawImage != null) el.Components.Add(MakeRawImage(panel.RawImage));
                if (panel.RectTransform != null) el.Components.Add(MakeRect(panel.RectTransform));
                if (panel.CursorEnabled) el.Components.Add(new JObject { ["type"] = "NeedsCursor" });
                if (panel.KeyboardEnabled) el.Components.Add(new JObject { ["type"] = "NeedsKeyboard" });
            }
            base.Add(el);
            return name;
        }

        public string Add(CuiButton button, string parent = "Overlay", string name = null, string destroy = null)
        {
            name ??= CuiHelperGetGuid();
            var el = new CuiElement { Name = name, Parent = parent, DestroyUi = destroy };
            if (button != null)
            {
                if (button.Button != null) el.Components.Add(MakeButton(button.Button));
                if (button.RectTransform != null) el.Components.Add(MakeRect(button.RectTransform));
                if (button.Text != null)
                {
                    // Text is a separate child element in Oxide; flatten as sibling text element.
                    var textEl = new CuiElement
                    {
                        Name = name + "_Text",
                        Parent = name,
                        Components =
                        {
                            MakeText(button.Text),
                            new JObject
                            {
                                ["type"] = "RectTransform",
                                ["anchormin"] = "0 0",
                                ["anchormax"] = "1 1",
                                ["offsetmin"] = "0 0",
                                ["offsetmax"] = "0 0"
                            }
                        }
                    };
                    base.Add(el);
                    base.Add(textEl);
                    return name;
                }
            }
            base.Add(el);
            return name;
        }

        public void Add(CuiElement element)
        {
            if (element == null) return;
            // Convert typed components to JObjects for JSON.
            var converted = new List<object>();
            foreach (var c in element.Components)
            {
                if (c is JObject) { converted.Add(c); continue; }
                if (c is CuiOutlineComponent outline)
                {
                    converted.Add(MakeOutline(outline));
                    continue;
                }
                if (c is CuiComponentData d)
                {
                    if (!string.IsNullOrEmpty(d.Command) || d.Color != null && d.Text == null && d.FontSize == 0 && d.Distance == null)
                        converted.Add(d.Command != null || HasButtonShape(d) ? MakeButton(d) : MakeImage(d));
                    else if (d.Text != null || d.FontSize > 0)
                        converted.Add(MakeText(d));
                    else if (d.AnchorMin != null || d.OffsetMin != null)
                        converted.Add(MakeRect(d));
                    else
                        converted.Add(MakeImage(d));
                    continue;
                }
                if (c is CuiNeedsCursorComponent) { converted.Add(new JObject { ["type"] = "NeedsCursor" }); continue; }
                if (c is CuiNeedsKeyboardComponent) { converted.Add(new JObject { ["type"] = "NeedsKeyboard" }); continue; }
                if (c is CuiDraggableComponent drag)
                {
                    converted.Add(MakeDraggable(drag));
                    continue;
                }
                converted.Add(c);
            }
            element.Components = converted;
            if (element.Text != null && element.Text.Text != null)
                element.Components.Add(MakeText(element.Text));
            if (element.RectTransform != null && (element.RectTransform.AnchorMin != null || element.RectTransform.OffsetMin != null))
                element.Components.Add(MakeRect(element.RectTransform));
            base.Add(element);
        }

        public void Add(string json) => base.Add(json);

        public string ToJson()
        {
            var arr = new JArray();
            foreach (var item in this)
            {
                if (item is string s)
                {
                    try { arr.Add(JToken.Parse(s)); } catch { }
                    continue;
                }
                if (item is CuiElement el)
                {
                    var obj = new JObject
                    {
                        ["name"] = el.Name,
                        ["parent"] = el.Parent
                    };
                    if (!string.IsNullOrEmpty(el.DestroyUi)) obj["destroyUi"] = el.DestroyUi;
                    if (el.FadeOut > 0f) obj["fadeOut"] = el.FadeOut;
                    var comps = new JArray();
                    foreach (var c in el.Components)
                    {
                        if (c is JObject jo) comps.Add(jo);
                        else if (c != null) comps.Add(JObject.FromObject(c));
                    }
                    obj["components"] = comps;
                    arr.Add(obj);
                }
            }
            return arr.ToString(Formatting.None);
        }

        private static bool HasButtonShape(CuiComponentData d) =>
            !string.IsNullOrEmpty(d.Command) || d.Align != default;

        private static JObject MakeImage(CuiComponentData d) => new JObject
        {
            ["type"] = "UnityEngine.UI.Image",
            ["color"] = d.Color ?? "1 1 1 1"
        };

        private static JObject MakeOutline(CuiOutlineComponent d) => new JObject
        {
            ["type"] = "UnityEngine.UI.Outline",
            ["color"] = d.Color ?? "0 0 0 1",
            ["distance"] = string.IsNullOrEmpty(d.Distance) ? "1.0 -1.0" : d.Distance
        };

        private static JObject MakeDraggable(CuiDraggableComponent drag)
        {
            var o = new JObject
            {
                ["type"] = "UnityEngine.UI.Draggable",
                ["limitToParent"] = drag.LimitToParent,
                ["maxDistance"] = drag.MaxDistance,
                ["allowSwapping"] = drag.AllowSwapping,
                ["dropAnywhere"] = drag.DropAnywhere,
                ["dragAlpha"] = drag.DragAlpha,
                ["parentLimitIndex"] = drag.ParentLimitIndex,
                ["filter"] = drag.Filter ?? "",
                ["parentPadding"] = drag.ParentPadding ?? "0 0",
                ["anchorOffset"] = drag.AnchorOffset ?? "0 0",
                ["keepOnTop"] = drag.KeepOnTop,
                ["positionRPC"] = (int)drag.PositionRPC
            };
            return o;
        }

        private static JObject MakeRawImage(CuiComponentData d)
        {
            var o = new JObject { ["type"] = "UnityEngine.UI.RawImage", ["color"] = d.Color ?? "1 1 1 1" };
            if (!string.IsNullOrEmpty(d.Text)) o["url"] = d.Text;
            return o;
        }

        private static JObject MakeButton(CuiComponentData d) => new JObject
        {
            ["type"] = "UnityEngine.UI.Button",
            ["command"] = d.Command ?? "",
            ["color"] = d.Color ?? "1 1 1 1"
        };

        private static JObject MakeText(CuiComponentData d) => new JObject
        {
            ["type"] = "UnityEngine.UI.Text",
            ["text"] = d.Text ?? "",
            ["font"] = string.IsNullOrEmpty(d.Font) ? "robotocondensed-regular.ttf" : d.Font,
            ["fontSize"] = d.FontSize > 0 ? d.FontSize : 14,
            ["align"] = d.Align.ToString(),
            ["color"] = d.Color ?? "1 1 1 1"
        };

        private static JObject MakeRect(CuiComponentData d) => new JObject
        {
            ["type"] = "RectTransform",
            ["anchormin"] = d.AnchorMin ?? "0 0",
            ["anchormax"] = d.AnchorMax ?? "1 1",
            ["offsetmin"] = d.OffsetMin ?? "0 0",
            ["offsetmax"] = d.OffsetMax ?? "0 0"
        };

        private static string CuiHelperGetGuid() => Guid.NewGuid().ToString("N");
    }

    public class CuiPanel
    {
        public bool CursorEnabled;
        public bool KeyboardEnabled;
        public float FadeOut;
        public CuiComponentData Image = new CuiComponentData();
        public CuiComponentData RawImage;
        public CuiComponentData RectTransform = new CuiComponentData();
    }

    public class CuiButton
    {
        public CuiComponentData Button = new CuiComponentData();
        public CuiComponentData Text = new CuiComponentData();
        public CuiComponentData RectTransform = new CuiComponentData();
    }

    public class CuiElement
    {
        public string DestroyUi;
        public string Name;
        public string Parent;
        public float FadeOut;
        public List<object> Components = new List<object>();
        public CuiComponentData Text = new CuiComponentData();
        public CuiComponentData RectTransform = new CuiComponentData();
    }

    public class CuiTextComponent : CuiComponentData { }
    public class CuiOutlineComponent : CuiComponentData { }
    public class CuiRectTransformComponent : CuiComponentData { }
    public class CuiNeedsCursorComponent { }
    public class CuiNeedsKeyboardComponent { }
    public class CuiImageComponent : CuiComponentData { }
    public class CuiRawImageComponent : CuiComponentData { }

    public class CuiDraggableComponent
    {
        public bool LimitToParent;
        public float MaxDistance;
        public bool AllowSwapping;
        public bool DropAnywhere;
        public float DragAlpha;
        public int ParentLimitIndex;
        public string Filter;
        public string ParentPadding;
        public string AnchorOffset;
        public bool KeepOnTop;
        public CommunityEntity.DraggablePositionSendType PositionRPC;
    }

    public class CuiComponentData
    {
        public string Color;
        public string Text;
        public string Font;
        public int FontSize;
        public UnityEngine.TextAnchor Align;
        public string Command;
        public string AnchorMin;
        public string AnchorMax;
        public string OffsetMin;
        public string OffsetMax;
        public string Distance;
    }
}
