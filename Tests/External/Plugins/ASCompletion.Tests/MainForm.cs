﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using FlashDevelop.Managers;
using FlashDevelop.Utilities;
using PluginCore;
using ScintillaNet;
using ScintillaNet.Configuration;
using WeifenLuo.WinFormsUI.Docking;

namespace FlashDevelop
{
    public class MainForm : Form, IMainForm
    {
        public void RefreshUI()
        {
            throw new NotImplementedException();
        }

        public void KillProcess()
        {
            throw new NotImplementedException();
        }

        public void RefreshSciConfig()
        {
            throw new NotImplementedException();
        }

        public void RestartRequired()
        {
            throw new NotImplementedException();
        }

        public void ThemeControls(object control)
        {
            // Not implemented
        }

        public void ClearTemporaryFiles(string file)
        {
            throw new NotImplementedException();
        }

        public void ShowSettingsDialog(string itemName)
        {
            throw new NotImplementedException();
        }

        public void ShowErrorDialog(object sender, Exception ex)
        {
            // Not implemented
        }

        public void ShowSettingsDialog(string itemName, string filter)
        {
            throw new NotImplementedException();
        }

        public void AutoUpdateMenuItem(ToolStripItem item, string action)
        {
            throw new NotImplementedException();
        }

        public void RegisterShortcutItem(string id, Keys keys)
        {
            throw new NotImplementedException();
        }

        public void RegisterShortcutItem(string id, ToolStripMenuItem item)
        {
            throw new NotImplementedException();
        }

        public void RegisterSecondaryItem(string id, ToolStripItem item)
        {
            throw new NotImplementedException();
        }

        public void ApplySecondaryShortcut(ToolStripItem item)
        {
            throw new NotImplementedException();
        }

        public void FileFromTemplate(string templatePath, string newFilePath)
        {
            throw new NotImplementedException();
        }

        public DockContent OpenEditableDocument(string file) => OpenEditableDocument(file, false);

        public DockContent OpenEditableDocument(string file, bool restoreFileState)
        {
            return new TabbedDocument(CurrentDocument);
        }

        public DockContent CreateCustomDocument(Control ctrl)
        {
            throw new NotImplementedException();
        }

        public DockContent CreateEditableDocument(string file, string text, int codepage)
        {
            throw new NotImplementedException();
        }

        public DockContent CreateDockablePanel(Control form, string guid, Image image, DockState defaultDockState)
        {
            throw new NotImplementedException();
        }

        public DockContent CreateDynamicPersistDockablePanel(Control ctrl, string guid, string id, Image image, DockState defaultDockState)
        {
            throw new NotImplementedException();
        }

        public bool CallCommand(string command, string arguments)
        {
            throw new NotImplementedException();
        }

        public List<ToolStripItem> FindMenuItems(string name)
        {
            throw new NotImplementedException();
        }

        public ToolStripItem FindMenuItem(string name)
        {
            throw new NotImplementedException();
        }

        public string ProcessArgString(string args)
        {
            return ArgsProcessor.ProcessString(args, true);
        }

        public Keys GetShortcutItemKeys(string id)
        {
            throw new NotImplementedException();
        }

        public string GetShortcutItemId(Keys keys)
        {
            throw new NotImplementedException();
        }

        public Dictionary<Keys, string> GetShortcutItemsByKeys()
        {
            throw new NotImplementedException();
        }

        public string GetThemeValue(string id) => string.Empty;

        public Color GetThemeColor(string id) => Color.Black;

        public bool GetThemeFlag(string id)
        {
            throw new NotImplementedException();
        }

        public T GetThemeValue<T>(string id) where T : struct
        {
            throw new NotImplementedException();
        }

        public string GetThemeValue(string id, string fallback)
        {
            if (id == "ScrollBar.UseCustom")
                return fallback;
            throw new NotImplementedException();
        }

        public int getThemeColorCount;
        public Color GetThemeColor(string id, Color fallback)
        {
            getThemeColorCount++;

            return fallback;
        }

        public bool GetThemeFlag(string id, bool fallback)
        {
            return fallback;
        }

        public T GetThemeValue<T>(string id, T fallback) where T : struct
        {
            throw new NotImplementedException();
        }

        public IPlugin FindPlugin(string guid)
        {
            throw new NotImplementedException();
        }

        public int imageSetAdjustCount;
        public Image ImageSetAdjust(Image image)
        {
            imageSetAdjustCount++;
            return image;
        }

        public Image GetAutoAdjustedImage(Image image)
        {
            throw new NotImplementedException();
        }

        public Image FindImage(string data)
        {
            return null;
        }

        public Image FindImage(string data, bool autoAdjust)
        {
            return null;
        }

        public Image FindImage16(string data)
        {
            throw new NotImplementedException();
        }

        public Image FindImage16(string data, bool autoAdjusted)
        {
            throw new NotImplementedException();
        }

        public Image FindImageAndSetAdjust(string data)
        {
            throw new NotImplementedException();
        }

        public int GetInstanceCount()
        {
            throw new NotImplementedException();
        }

        public void SetUseTheme(object parent, bool use)
        {
            // Not implemented
        }

        public ISettings Settings { get; set; }

        public ToolStrip ToolStrip => throw new NotImplementedException();

        public MenuStrip MenuStrip => throw new NotImplementedException();

        public Scintilla SciConfig => ScintillaManager.SciConfig;

        public DockPanel DockPanel => new DockPanel();

        public string[] StartArguments => throw new NotImplementedException();

        public List<Argument> CustomArguments => throw new NotImplementedException();

        public StatusStrip StatusStrip => throw new NotImplementedException();

        public string WorkingDirectory
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        public ToolStripPanel ToolStripPanel => throw new NotImplementedException();

        public ToolStripStatusLabel StatusLabel => throw new NotImplementedException();

        public ToolStripStatusLabel ProgressLabel => throw new NotImplementedException();

        public ToolStripProgressBar ProgressBar => throw new NotImplementedException();

        public ContextMenuStrip TabMenu => throw new NotImplementedException();

        public ContextMenuStrip EditorMenu => throw new NotImplementedException();

        private ITabbedDocument _currentDocument;
        public ITabbedDocument CurrentDocument
        {
            get => _currentDocument;
            set => _currentDocument = value;
        }

        ITabbedDocument[] tabbedDocuments;

        public ITabbedDocument[] Documents
        {
            get => tabbedDocuments;
            set => tabbedDocuments = value;
        }

        public bool HasModifiedDocuments => throw new NotImplementedException();

        public bool ClosingEntirely => false;

        public bool ProcessIsRunning => throw new NotImplementedException();

        public bool ReloadingDocument => throw new NotImplementedException();

        public bool ProcessingContents => throw new NotImplementedException();

        public bool RestoringContents => throw new NotImplementedException();

        public bool SavingMultiple => throw new NotImplementedException();

        public bool PanelIsActive => throw new NotImplementedException();

        public bool IsFullScreen => throw new NotImplementedException();

        private bool _standaloneMode;
        public bool StandaloneMode
        {
            get => _standaloneMode;
            set => _standaloneMode = value;
        }

        public bool MultiInstanceMode => throw new NotImplementedException();

        public bool IsFirstInstance => throw new NotImplementedException();

        public bool RestartRequested => throw new NotImplementedException();

        public bool RequiresRestart => throw new NotImplementedException();


        public bool RefreshConfig => throw new NotImplementedException();

        public List<Keys> IgnoredKeys => new List<Keys>();

        public string CommandPromptExecutable => throw new NotImplementedException();
    }
}

class TabbedDocument : DockContent, ITabbedDocument
{
    public string FileName { get; }
    public bool UseCustomIcon { get; set; }
    public SplitContainer SplitContainer { get; }
    public ScintillaControl SciControl { get; }
    public ScintillaControl SplitSci1 { get; }
    public ScintillaControl SplitSci2 { get; }
    public bool IsModified { get; set; }
    public bool IsSplitted { get; set; }
    public bool IsBrowsable { get; }
    public bool IsUntitled { get; }
    public bool IsEditable { get; }
    public bool HasBookmarks { get; }
    public bool IsAloneInPane { get; }

    public TabbedDocument(ITabbedDocument mock)
    {
        FileName = mock.FileName;
        SciControl = mock.SciControl;
    }

    public void RefreshTexts()
    {
        throw new NotImplementedException();
    }

    public void Reload(bool showQuestion)
    {
        throw new NotImplementedException();
    }

    public void Revert(bool showQuestion)
    {
        throw new NotImplementedException();
    }

    public void Save(string file)
    {
        // ignore
    }

    public void Save(string file, string reason)
    {
        // ignore
    }

    public void Save()
    {
        // ignore
    }
}