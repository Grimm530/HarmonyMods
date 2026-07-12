using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace InventoryShortcuts;

/// <summary>
/// Builds CUI for an in-game percentage grid overlay (0-1). Used by /gridlines (admin only).
/// Grid lines are drawn with normalized coordinates so you can read exact positions for UI layout.
/// </summary>
public static class UIGridOverlay
{
    public const string PanelName = "InventoryShortcuts.grid";

    private const int VerticalSteps = 20;   // lines at 0, 0.05, 0.10, ... 1.0
    private const int HorizontalSteps = 10; // lines at 0, 0.1, 0.2, ... 1.0
    private const float LineThickness = 0.002f;
    private const float ThinLineThickness = 0.001f; // for 0.01-interval lines (1% grid)
    private const float EdgeLineInset = 0.998f; // top/right lines start here so they're visible on screen
    private const string LineColor = "1 0.75 0.2 0.5"; // orange/yellow, semi-transparent
    private const string ThinLineColor = "1 0.9 0.6 0.38"; // lighter at every 0.01 (between the 0.1 boxes)
    private const string LabelColor = "1 0.9 0.5 0.95";
    private const int LabelFontSize = 14;

    /// <summary>
    /// Builds the full CUI JSON element list: full-screen overlay container, grid lines, and close button (X).
    /// Parent for overlay: Overlay. Close button command: cui.endtest INVSHORTCUTS GRIDCLOSE
    /// </summary>
    public static List<JObject> BuildGridElements()
    {
        var elements = new List<JObject>();

        // Full-screen container (Overlay so it stays on top and is visible everywhere)
        var container = new JObject
        {
            ["name"] = PanelName,
            ["parent"] = "Overlay",
            ["components"] = new JArray
            {
                new JObject
                {
                    ["type"] = "RectTransform",
                    ["anchormin"] = "0 0",
                    ["anchormax"] = "1 1",
                    ["offsetmin"] = "0 0",
                    ["offsetmax"] = "0 0"
                },
                new JObject { ["type"] = "NeedsCursor" }
            }
        };
        container["destroyUi"] = PanelName;
        elements.Add(container);

        // Vertical lines (x from 0 to 1 in steps); inset left/right edges so they're visible
        for (int i = 0; i <= VerticalSteps; i++)
        {
            float x = (float)i / VerticalSteps;
            float xMax = Mathf.Clamp01(x + LineThickness);
            if (i == 0) xMax = 0.002f;
            if (i == VerticalSteps) x = EdgeLineInset;
            string min = x.ToString("F4", CultureInfo.InvariantCulture) + " 0";
            string max = xMax.ToString("F4", CultureInfo.InvariantCulture) + " 1";
            elements.Add(GridLine(PanelName, "v" + i, min, max));
        }

        // Horizontal lines (y from 0 to 1); inset top/bottom so top and bottom lines are visible on screen
        for (int j = 0; j <= HorizontalSteps; j++)
        {
            float y = (float)j / HorizontalSteps;
            float yMax = Mathf.Clamp01(y + LineThickness);
            if (j == 0) yMax = 0.002f;
            if (j == HorizontalSteps) y = EdgeLineInset;
            string min = "0 " + y.ToString("F4", CultureInfo.InvariantCulture);
            string max = "1 " + yMax.ToString("F4", CultureInfo.InvariantCulture);
            elements.Add(GridLine(PanelName, "h" + j, min, max));
        }

        // Lighter lines at every 0.01 (1%) - fill the gaps between the 0.1 lines
        for (int i = 1; i <= 99; i++)
        {
            float x = (float)i / 100f;
            float xMax = Mathf.Clamp01(x + ThinLineThickness);
            string min = x.ToString("F4", CultureInfo.InvariantCulture) + " 0";
            string max = xMax.ToString("F4", CultureInfo.InvariantCulture) + " 1";
            elements.Add(GridLine(PanelName, "v01_" + i, min, max, ThinLineThickness, ThinLineColor));
        }
        for (int j = 1; j <= 99; j++)
        {
            float y = (float)j / 100f;
            float yMax = Mathf.Clamp01(y + ThinLineThickness);
            string min = "0 " + y.ToString("F4", CultureInfo.InvariantCulture);
            string max = "1 " + yMax.ToString("F4", CultureInfo.InvariantCulture);
            elements.Add(GridLine(PanelName, "h01_" + j, min, max, ThinLineThickness, ThinLineColor));
        }

        // X-axis positions including 0.25 and 0.75 (no extra lines - grid already has them)
        float[] xPositions = { 0f, 0.1f, 0.25f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.75f, 0.8f, 0.9f, 1f };

        // X-axis labels along bottom (0, 0.1, 0.25, 0.3, ... 0.75, ... 1.0)
        for (int i = 0; i < xPositions.Length; i++)
        {
            float x = xPositions[i];
            string text = FormatLabel(x, isOne: x >= 0.999f);
            float xMin = Mathf.Clamp(x - 0.02f, 0f, 0.98f);
            float xMax = Mathf.Clamp(x + 0.02f, 0.02f, 1f);
            elements.Add(Label(PanelName, "xlbl" + i, text, xMin.ToString("F3", CultureInfo.InvariantCulture) + " 0", xMax.ToString("F3", CultureInfo.InvariantCulture) + " 0.028"));
        }

        // Y-axis labels along left (0, 0.1, 0.25, 0.3, ... 0.75, ... 1.0)
        for (int j = 0; j < xPositions.Length; j++)
        {
            float y = xPositions[j];
            string text = FormatLabel(y, isOne: y >= 0.999f);
            float yMin = Mathf.Clamp(y - 0.02f, 0f, 0.98f);
            float yMax = Mathf.Clamp(y + 0.02f, 0.02f, 1f);
            elements.Add(Label(PanelName, "ylbl" + j, text, "0 " + yMin.ToString("F3", CultureInfo.InvariantCulture), "0.04 " + yMax.ToString("F3", CultureInfo.InvariantCulture)));
        }

        // X-axis labels across top
        for (int i = 0; i < xPositions.Length; i++)
        {
            float x = xPositions[i];
            string text = FormatLabel(x, isOne: x >= 0.999f);
            float xMin = Mathf.Clamp(x - 0.02f, 0f, 0.98f);
            float xMax = Mathf.Clamp(x + 0.02f, 0.02f, 1f);
            elements.Add(Label(PanelName, "xtop" + i, text, xMin.ToString("F3", CultureInfo.InvariantCulture) + " 0.972", xMax.ToString("F3", CultureInfo.InvariantCulture) + " 0.998"));
        }

        // Y-axis labels down the center (vertical midline)
        for (int j = 0; j < xPositions.Length; j++)
        {
            float y = xPositions[j];
            string text = FormatLabel(y, isOne: y >= 0.999f);
            float yMin = Mathf.Clamp(y - 0.02f, 0f, 0.98f);
            float yMax = Mathf.Clamp(y + 0.02f, 0.02f, 1f);
            elements.Add(Label(PanelName, "ycen" + j, text, "0.48 " + yMin.ToString("F3", CultureInfo.InvariantCulture), "0.52 " + yMax.ToString("F3", CultureInfo.InvariantCulture)));
        }

        // X-axis labels across the middle (horizontal midline) - skip 0.5 so it's only shown once (on the vertical center)
        for (int i = 0; i < xPositions.Length; i++)
        {
            float x = xPositions[i];
            if (Math.Abs(x - 0.5f) < 0.001f) continue; // one 0.5 at center (from ycen), no duplicate
            string text = FormatLabel(x, isOne: x >= 0.999f);
            float xMin = Mathf.Clamp(x - 0.02f, 0f, 0.98f);
            float xMax = Mathf.Clamp(x + 0.02f, 0.02f, 1f);
            elements.Add(Label(PanelName, "xcen" + i, text, xMin.ToString("F3", CultureInfo.InvariantCulture) + " 0.48", xMax.ToString("F3", CultureInfo.InvariantCulture) + " 0.52"));
        }

        // Close button (X) - top right
        const string closeName = PanelName + ".close";
        elements.Add(new JObject
        {
            ["name"] = closeName,
            ["parent"] = PanelName,
            ["components"] = new JArray
            {
                new JObject
                {
                    ["type"] = "UnityEngine.UI.Button",
                    ["command"] = "cui.endtest INVSHORTCUTS GRIDCLOSE",
                    ["color"] = "0.2 0.2 0.2 0.9",
                    ["sprite"] = "assets/content/ui/ui.background.tile.psd",
                    ["imagetype"] = "Simple",
                    ["normalColor"] = "1 1 1 1",
                    ["highlightedColor"] = "1 0.9 0.5 1",
                    ["pressedColor"] = "0.8 0.8 0.8 1"
                },
                new JObject
                {
                    ["type"] = "RectTransform",
                    ["anchormin"] = "0.92 0.92",
                    ["anchormax"] = "0.99 0.99",
                    ["offsetmin"] = "0 0",
                    ["offsetmax"] = "0 0"
                }
            }
        });
        elements.Add(new JObject
        {
            ["name"] = closeName + "_label",
            ["parent"] = closeName,
            ["components"] = new JArray
            {
                new JObject
                {
                    ["type"] = "UnityEngine.UI.Text",
                    ["text"] = "×",
                    ["fontSize"] = 28,
                    ["font"] = "RobotoCondensed-Bold.ttf",
                    ["color"] = "1 1 1 1",
                    ["align"] = "MiddleCenter"
                },
                new JObject
                {
                    ["type"] = "RectTransform",
                    ["anchormin"] = "0 0",
                    ["anchormax"] = "1 1"
                }
            }
        });

        return elements;
    }

    private static JObject GridLine(string parent, string name, string anchorMin, string anchorMax)
    {
        return GridLine(parent, name, anchorMin, anchorMax, LineThickness, LineColor);
    }

    private static JObject GridLine(string parent, string name, string anchorMin, string anchorMax, float thickness, string color)
    {
        return new JObject
        {
            ["name"] = PanelName + "." + name,
            ["parent"] = parent,
            ["components"] = new JArray
            {
                new JObject
                {
                    ["type"] = "UnityEngine.UI.Image",
                    ["color"] = color,
                    ["sprite"] = "assets/content/ui/ui.background.tile.psd",
                    ["imagetype"] = "Simple"
                },
                new JObject
                {
                    ["type"] = "RectTransform",
                    ["anchormin"] = anchorMin,
                    ["anchormax"] = anchorMax,
                    ["offsetmin"] = "0 0",
                    ["offsetmax"] = "0 0"
                }
            }
        };
    }

    private static string FormatLabel(float value, bool isOne)
    {
        if (isOne) return "1";
        if (value < 0.001f) return "0";
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static JObject Label(string parent, string name, string text, string anchorMin, string anchorMax)
    {
        return new JObject
        {
            ["name"] = PanelName + "." + name,
            ["parent"] = parent,
            ["components"] = new JArray
            {
                new JObject
                {
                    ["type"] = "UnityEngine.UI.Text",
                    ["text"] = text,
                    ["fontSize"] = LabelFontSize,
                    ["font"] = "RobotoCondensed-Bold.ttf",
                    ["color"] = LabelColor,
                    ["align"] = "MiddleCenter"
                },
                new JObject
                {
                    ["type"] = "RectTransform",
                    ["anchormin"] = anchorMin,
                    ["anchormax"] = anchorMax
                }
            }
        };
    }
}
