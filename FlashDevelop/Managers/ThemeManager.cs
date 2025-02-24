﻿using PluginCore;
using PluginCore.Helpers;
using PluginCore.Managers;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace FlashDevelop.Managers
{
    class ThemeManager
    {
        /// <summary>
        /// Dictionary containing the loaded theme values
        /// </summary>
        private static readonly Dictionary<string, string> valueMap = new Dictionary<string, string>();

        /// <summary>
        /// Gets a value entry from the config.
        /// </summary>
        public static string GetThemeValue(string id)
        {
            return valueMap.TryGetValue(id, out var result) ? result : null;
        }

        /// <summary>
        /// Gets a color entry from the config.
        /// </summary>
        public static Color GetThemeColor(string id)
        {
            try { return ColorTranslator.FromHtml(GetThemeValue(id)); }
            catch { return Color.Empty; }
        }

        /// <summary>
        /// Loads and applies the theme to MainForm.
        /// </summary>
        public static void LoadTheme(string file)
        {
            try
            {
                if (File.Exists(file))
                {
                    valueMap.Clear();
                    string[] lines = File.ReadAllLines(file);
                    foreach (string rawLine in lines)
                    {
                        string line = rawLine.Trim();
                        if (line.Length < 2 || line.StartsWith('#')) continue;
                        string[] entry = line.Split(new[] { '=' }, 2);
                        if (entry.Length < 2) continue;
                        valueMap[entry[0]] = entry[1];
                    }
                    string currentFile = Path.Combine(PathHelper.ThemesDir, "CURRENT");
                    if (file != currentFile) File.Copy(file, currentFile, true);
                }
            }
            catch (Exception ex)
            {
                ErrorManager.ShowError(ex);
            }
        }

        /// <summary>
        /// Sets the use theme setting also to children
        /// </summary>
        public static void SetUseTheme(object obj, bool use)
        {
            try
            {
                if (obj is ListView parent1)
                {
                    foreach (ListViewItem item in parent1.Items)
                    {
                        SetUseTheme(item, use);
                    }
                }
                else if (obj is TreeView parent2)
                {
                    foreach (TreeNode item in parent2.Nodes)
                    {
                        SetUseTheme(item, use);
                    }
                }
                else if (obj is MenuStrip parent3)
                {
                    foreach (ToolStripItem item in parent3.Items)
                    {
                        SetUseTheme(item, use);
                    }
                }
                else if (obj is ToolStripMenuItem parent4)
                {
                    foreach (ToolStripItem item in parent4.DropDownItems)
                    {
                        SetUseTheme(item, use);
                    }
                }
                else if (obj is Control parent)
                {
                    foreach (Control item in parent.Controls)
                    {
                        SetUseTheme(item, use);
                    }
                }
                PropertyInfo info = obj.GetType().GetProperty("UseTheme");
                if (info != null && info.CanWrite)
                {
                    info.SetValue(obj, use, null);
                }
            }
            catch (Exception ex)
            {
                ErrorManager.ShowError(ex);
            }
        }

        /// <summary>
        /// Walks the control tree down and themes all controls.
        /// </summary>
        public static void WalkControls(object obj)
        {
            try
            {
                if (obj is ListView parent1)
                {
                    foreach (ListViewItem item in parent1.Items)
                    {
                        WalkControls(item);
                    }
                }
                else if (obj is TreeView parent2)
                {
                    foreach (TreeNode item in parent2.Nodes)
                    {
                        WalkControls(item);
                    }
                }
                else if (obj is MenuStrip parent3)
                {
                    foreach (ToolStripItem item in parent3.Items)
                    {
                        WalkControls(item);
                    }
                }
                else if (obj is ToolStripMenuItem parent4)
                {
                    foreach (ToolStripItem item in parent4.DropDownItems)
                    {
                        WalkControls(item);
                    }
                }
                else if (obj is Control parent)
                {
                    foreach (Control item in parent.Controls)
                    {
                        WalkControls(item);
                    }
                }
                ThemeControl(obj);
                if (obj is IThemeHandler th)
                {
                    th.AfterTheming();
                }
                if (obj is MainForm)
                {
                    NotifyEvent ne = new NotifyEvent(EventType.ApplyTheme);
                    EventManager.DispatchEvent(Globals.MainForm, ne);
                    Globals.MainForm.AdjustAllImages();
                    Globals.MainForm.Refresh();
                }
            }
            catch (Exception ex)
            {
                ErrorManager.ShowError(ex);
            }
        }

        /// <summary>
        /// Applies the theme colors to the control.
        /// </summary>
        public static void ThemeControl(object obj) => ThemeControl(obj, obj.GetType());

        /// <summary>
        /// Applies theme colors to the control based on type.
        /// </summary>
        private static void ThemeControl(object obj, Type type)
        {
            try
            {
                dynamic cast = obj;
                // Apply colors of base type before applying for this type
                bool useIn = GetThemeValue("ThemeManager.UseInheritance") == "True";
                if (useIn && type.BaseType != null) ThemeControl(obj, type.BaseType);
                string name = ThemeHelper.GetFilteredTypeName(type);
                // Apply all basic style settings
                ApplyPropColor(obj, name + ".BackColor");
                ApplyPropColor(obj, name + ".ForeColor");
                ApplyPropColor(obj, name + ".BackgroundColor");
                ApplyPropColor(obj, name + ".ActiveLinkColor");
                ApplyPropColor(obj, name + ".DisabledLinkColor");
                ApplyPropColor(obj, name + ".LinkColor");
                ApplyPropColor(obj, name + ".BorderColor");
                ApplyPropColor(obj, name + ".ActiveForeColor");
                ApplyPropColor(obj, name + ".DisabledTextColor");
                ApplyPropColor(obj, name + ".DisabledBorderColor");
                ApplyPropColor(obj, name + ".CurrentPositionColor");
                ApplyPropColor(obj, name + ".DisabledBackColor");
                ApplyPropColor(obj, name + ".GridLineColor");
                ApplyPropColor(obj, name + ".HotForeColor");
                ApplyPropColor(obj, name + ".HotArrowColor");
                ApplyPropColor(obj, name + ".ActiveArrowColor");
                ApplyPropColor(obj, name + ".ArrowColor");
                // Set border style from border style key
                PropertyInfo bstyle = type.GetProperty("BorderStyle");
                bool force = GetThemeValue("ThemeManager.ForceBorderStyle") == "True";
                if (bstyle != null && bstyle.CanWrite && (force || cast.BorderStyle != BorderStyle.None))
                {
                    string key = name + ".BorderStyle";
                    string style = GetThemeValue(key);
                    switch (style)
                    {
                        case "None":
                            bstyle.SetValue(obj, BorderStyle.None, null);
                            break;
                        case "Fixed3D":
                            bstyle.SetValue(obj, BorderStyle.Fixed3D, null);
                            break;
                        case "FixedSingle":
                            bstyle.SetValue(obj, BorderStyle.FixedSingle, null);
                            break;
                    }
                }
                // Set flat style from flat style key
                PropertyInfo fstyle = type.GetProperty("FlatStyle");
                if (fstyle != null && fstyle.CanWrite)
                {
                    string key = name + ".FlatStyle";
                    string style = GetThemeValue(key);
                    switch (style)
                    {
                        case "Flat":
                            fstyle.SetValue(obj, FlatStyle.Flat, null);
                            break;
                        case "Popup":
                            fstyle.SetValue(obj, FlatStyle.Popup, null);
                            break;
                        case "System":
                            fstyle.SetValue(obj, FlatStyle.System, null);
                            break;
                        case "Standard":
                            fstyle.SetValue(obj, FlatStyle.Standard, null);
                            break;
                    }
                }
                // Control specific style assignments
                if (obj is Button parent1)
                {
                    Color color = Color.Empty;
                    bool flat = GetThemeValue("Button.FlatStyle") == "Flat";
                    if (flat)
                    {
                        color = GetThemeColor("Button.BorderColor");
                        if (color != Color.Empty) parent1.FlatAppearance.BorderColor = color;
                        color = GetThemeColor("Button.CheckedBackColor");
                        if (color != Color.Empty) parent1.FlatAppearance.CheckedBackColor = color;
                        color = GetThemeColor("Button.MouseDownBackColor");
                        if (color != Color.Empty) parent1.FlatAppearance.MouseDownBackColor = color;
                        color = GetThemeColor("Button.MouseOverBackColor");
                        if (color != Color.Empty) parent1.FlatAppearance.MouseOverBackColor = color;
                    }
                }
                else if (obj is CheckBox parent)
                {
                    Color color = Color.Empty;
                    bool flat = GetThemeValue("CheckBox.FlatStyle") == "Flat";
                    if (flat)
                    {
                        color = GetThemeColor("CheckBox.BorderColor");
                        if (color != Color.Empty) parent.FlatAppearance.BorderColor = color;
                        color = GetThemeColor("CheckBox.CheckedBackColor");
                        if (color != Color.Empty) parent.FlatAppearance.CheckedBackColor = color;
                        color = GetThemeColor("CheckBox.MouseDownBackColor");
                        if (color != Color.Empty) parent.FlatAppearance.MouseDownBackColor = color;
                        color = GetThemeColor("CheckBox.MouseOverBackColor");
                        if (color != Color.Empty) parent.FlatAppearance.MouseOverBackColor = color;
                    }
                }
                else if (obj is PropertyGrid grid)
                {
                    ApplyPropColor(grid, "PropertyGrid.ViewBackColor");
                    ApplyPropColor(grid, "PropertyGrid.ViewForeColor");
                    ApplyPropColor(grid, "PropertyGrid.ViewBorderColor");
                    ApplyPropColor(grid, "PropertyGrid.HelpBackColor");
                    ApplyPropColor(grid, "PropertyGrid.HelpForeColor");
                    ApplyPropColor(grid, "PropertyGrid.HelpBorderColor");
                    ApplyPropColor(grid, "PropertyGrid.CategoryForeColor");
                    ApplyPropColor(grid, "PropertyGrid.CategorySplitterColor");
                    ApplyPropColor(grid, "PropertyGrid.CommandsBackColor");
                    ApplyPropColor(grid, "PropertyGrid.CommandsActiveLinkColor");
                    ApplyPropColor(grid, "PropertyGrid.CommandsDisabledLinkColor");
                    ApplyPropColor(grid, "PropertyGrid.CommandsForeColor");
                    ApplyPropColor(grid, "PropertyGrid.CommandsLinkColor");
                    ApplyPropColor(grid, "PropertyGrid.LineColor");
                }
            }
            catch (Exception ex)
            {
                ErrorManager.ShowError(ex);
            }
        }

        /// <summary>
        /// Apply property color if defined and property is available
        /// </summary>
        private static void ApplyPropColor(object targObj, string propId)
        {
            Color color = GetThemeColor(propId);
            PropertyInfo prop = targObj.GetType().GetProperty(propId.Split('.')[1]);
            if (prop != null && prop.CanWrite && color != Color.Empty)
            {
                prop.SetValue(targObj, color, null);
            }
        }

    }

}

