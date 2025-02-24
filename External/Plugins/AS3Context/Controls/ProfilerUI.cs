﻿using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using PluginCore;
using System.IO;
using PluginCore.Helpers;
using PluginCore.Localization;
using PluginCore.Managers;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using WeifenLuo.WinFormsUI.Docking;

namespace AS3Context.Controls
{
    public partial class ProfilerUI : DockPanelControl, IThemeHandler
    {
        static readonly byte[] RESULT_OK = Encoding.Default.GetBytes("<flashconnect status=\"0\"/>\0");
        static readonly byte[] RESULT_IGNORED = Encoding.Default.GetBytes("<flashconnect status=\"3\"/>\0");
        static readonly byte[] RESULT_GC = Encoding.Default.GetBytes("<flashconnect status=\"4\"/>\0");

        static ProfilerUI instance;
        static bool gcWanted;
        static byte[] snapshotWanted;
        public DockContent PanelRef;
        bool autoStart;
        bool running;
        string current;
        readonly List<string> previous = new List<string>();
        readonly ObjectRefsGrid objectRefsGrid;
        readonly ProfilerLiveObjectsView liveObjectsView;
        readonly ProfilerMemView memView;
        readonly ProfilerObjectsView objectRefsView;
        readonly Timer detectDisconnect;
        List<ToolStripMenuItem> profilerItems;
        string profilerItemsCheck;
        string profilerSWF;

        public bool AutoStart => autoStart;

        public static void HandleFlashConnect(object sender, object data)
        {
            Socket client = sender as Socket;

            if (instance is null || data is null || !instance.running)
            {
                if (client.Connected) client.Send(RESULT_IGNORED);
                return;
            }

            instance.OnProfileData((string)data);

            if (client.Connected) client.Send(RESULT_OK);

            if (gcWanted)
            {
                if (client.Connected) client.Send(RESULT_GC);
                gcWanted = false;
            }
            if (snapshotWanted != null)
            {
                if (client.Connected) client.Send(snapshotWanted);
                snapshotWanted = null;
            }
        }

        #region UI Init

        public ProfilerUI()
        {
            instance = this;
            AutoKeyHandling = true;

            InitializeComponent();
            objectRefsGrid = new ObjectRefsGrid();
            objectsPage.Controls.Add(objectRefsGrid);

            InitializeLocalization();

            toolStrip.ImageScalingSize = ScaleHelper.Scale(new Size(16, 16));
            toolStrip.Renderer = new DockPanelStripRenderer();
            runButton.Image = PluginBase.MainForm.FindImage("127");
            gcButton.Image = PluginBase.MainForm.FindImage("90");
            gcButton.Enabled = false;
            autoButton.Image = PluginBase.MainForm.FindImage("514");

            if (PluginMain.Settings.ProfilerTimeout == 0) PluginMain.Settings.ProfilerTimeout = 30;
            detectDisconnect = new Timer();
            detectDisconnect.Interval = Math.Max(5, PluginMain.Settings.ProfilerTimeout) * 1000;
            detectDisconnect.Tick += detectDisconnect_Tick;

            memView = new ProfilerMemView(memLabel, memStatsLabel, memScaleCombo, memoryPage);

            foreach (ColumnHeader column in listView.Columns)
                column.Width = ScaleHelper.Scale(column.Width);

            liveObjectsView = new ProfilerLiveObjectsView(listView);
            liveObjectsView.OnViewObject += liveObjectsView_OnViewObject;
            objectRefsView = new ProfilerObjectsView(objectRefsGrid);

            configureProfilerChooser();

            StopProfiling();
        }

        /// <summary>
        /// Lets do some theming fixes
        /// </summary>
        public void AfterTheming()
        {
            this.memStatsPanel.BackColor = Color.Empty;
            this.container.BackColor = PluginBase.MainForm.GetThemeColor("Form.BackColor", SystemColors.Control);
            this.memView.Graph.UpdateColors();
        }

        void liveObjectsView_OnViewObject(TypeItem item)
        {
            snapshotWanted = Encoding.Default.GetBytes("<flashconnect status=\"5\" qname=\"" + item.QName.Replace("<", "&#60;") + "\"/>\0");
            objectRefsView.Clear();
            tabControl.SelectedTab = objectsPage;
        }

        void InitializeLocalization()
        {
            this.labelTarget.Text = "";
            this.autoButton.Text = TextHelper.GetString("Label.AutoStartProfilerOFF");
            this.gcButton.Text = TextHelper.GetString("Label.RunGC");
            this.runButton.Text = TextHelper.GetString("Label.StartProfiler");
            this.typeColumn.Text = TextHelper.GetString("Column.Type");
            this.countColumn.Text = TextHelper.GetString("Column.Count");
            this.memColumn.Text = TextHelper.GetString("Column.Memory");
            this.pkgColumn.Text = TextHelper.GetString("Column.Package");
            this.maxColumn.Text = TextHelper.GetString("Column.Maximum");
            this.memScaleLabel.Text = TextHelper.GetString("Label.MemoryGraphScale");
            this.liveObjectsPage.Text = TextHelper.GetString("Label.LiveObjectsTab");
            this.objectsPage.Text = TextHelper.GetString("Label.ObjectsTab");
            this.memoryPage.Text = TextHelper.GetString("Label.MemoryTab");
            this.profilerChooser.Text = TextHelper.GetString("Label.ActiveProfiler");
            this.defaultToolStripMenuItem.Text = TextHelper.GetString("Label.FlashDevelopProfiler");
        }

        void detectDisconnect_Tick(object sender, EventArgs e)
        {
            detectDisconnect.Stop();
            StopProfiling();
        }

        public void Cleanup()
        {
            SetProfilerCfg(false);
        }

        void runButton_Click(object sender, EventArgs e)
        {
            if (running) StopProfiling();
            else StartProfiling();
        }

        void gcButton_Click(object sender, EventArgs e)
        {
            if (running && current != null) gcWanted = true;
        }

        public void StopProfiling()
        {
            running = false;
            runButton.Image = PluginBase.MainForm.FindImage("125");
            runButton.Text = TextHelper.GetString("Label.StartProfiler");
            gcButton.Enabled = false;

            SetProfilerCfg(false);
        }

        public void StartProfiling()
        {
            running = true;
            current = null;
            gcWanted = false;
            snapshotWanted = null;
            
            liveObjectsView.Clear();
            memView.Clear();

            if (tabControl.SelectedTab == objectsPage) 
                tabControl.SelectedTab = liveObjectsPage;
            runButton.Image = PluginBase.MainForm.FindImage("126");
            runButton.Text = TextHelper.GetString("Label.StopProfiler");
            gcButton.Enabled = false;

            if (!SetProfilerCfg(true)) StopProfiling();
            else if (autoStart)
            {
                detectDisconnect.Interval = 5000; // expecting connection before 5s
                detectDisconnect.Start();
            }
        }

        void autoButton_Click(object sender, EventArgs e)
        {
            autoStart = !autoStart;
            autoButton.Image = PluginBase.MainForm.FindImage(autoStart ? "510" : "514");
            autoButton.Text = TextHelper.GetString(autoStart ? "Label.AutoStartProfilerON" : "Label.AutoStartProfilerOFF");
        }

        #endregion

        #region Profiler selector

        void configureProfilerChooser()
        {
            profilerChooser.Image = PluginBase.MainForm.FindImage("274");
            profilerChooser.DropDownOpening += profilerChooser_DropDownOpening;

            profilerItems = new List<ToolStripMenuItem>();
            defaultToolStripMenuItem.Checked = true;
            defaultToolStripMenuItem.Click += changeProfiler_Click;

            profilerSWF = null; // default
            string active = Path.Combine(PathHelper.DataDir, "AS3Context", "activeProfiler.txt");
            if (File.Exists(active))
            {
                string src = File.ReadAllText(active).Trim();
                if (src.Length > 0 && File.Exists(src))
                    profilerSWF = src;
            }
        }

        void profilerChooser_DropDownOpening(object sender, EventArgs e)
        {
            var swfs = PluginMain.Settings.CustomProfilers;
            if (swfs.IsNullOrEmpty()) return;

            var check = swfs.Aggregate("", (current1, swf) => current1 + swf);
            if (check == profilerItemsCheck) return;
            profilerItemsCheck = check; 

            profilerItems.Clear();
            profilerItems.Add(defaultToolStripMenuItem);
            foreach (string swf in swfs)
            {
                string fileName = swf.Trim();
                if (fileName.Length > 0)
                {
                    ToolStripMenuItem item = new ToolStripMenuItem(Path.GetFileNameWithoutExtension(swf));
                    item.Tag = swf;
                    item.Click += changeProfiler_Click;
                    profilerItems.Add(item);
                }
            }
            profilerChooser.DropDownItems.Clear();
            profilerChooser.DropDownItems.AddRange(profilerItems.ToArray());
            defaultToolStripMenuItem.Checked = false;
            foreach (ToolStripMenuItem item in profilerItems)
                if (item.Tag as string == profilerSWF)
                {
                    item.Checked = true;
                    break;
                }
        }

        void changeProfiler_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            if (item is null || item.Checked) return;

            foreach (ToolStripMenuItem it in profilerItems)
                it.Checked = false;
            item.Checked = true;
            profilerSWF = item.Tag as string;

            string active = Path.Combine(PathHelper.DataDir, "AS3Context", "activeProfiler.txt");
            File.WriteAllText(active, profilerSWF ?? "");
        }

        #endregion

        #region Display profiling data

        /// <summary>
        /// Recieved profiling data
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        internal bool OnProfileData(string data)
        {
            // check sampler
            int p = data.IndexOf('|');
            string[] info = data.Substring(0, p).Split('/');

            // type snapshot
            if (info[0] == "stacks") 
            {
                objectRefsView.Display(info[1], data.Substring(p + 1).Split('|'));
                return true;
            }

            // live objects count
            if (current != info[0])
            {
                if (!previous.Contains(info[0]))
                {
                    current = info[0];
                    labelTarget.Text = info.Length > 2 ? info[2].Replace('\\', '/') : "";
                    previous.Add(current);
                    SetProfilerCfg(false);
                    gcButton.Enabled = true;
                    PanelRef.Show();
                }
                else return false;
            }

            detectDisconnect.Stop();
            detectDisconnect.Interval = Math.Max(5, PluginMain.Settings.ProfilerTimeout) * 1000;
            detectDisconnect.Start();

            memView.UpdateStats(info);
            liveObjectsView.UpdateTypeGrid(data.Substring(p + 1).Split('|'));
            return true;
        }

        #endregion

        #region MM configuration

        bool SetProfilerCfg(bool active)
        {
            try
            {
                string mmCfg = PathHelper.ResolveMMConfig();
                if (!File.Exists(mmCfg)) CreateDefaultCfg(mmCfg);

                string src = File.ReadAllText(mmCfg).Trim();
                src = Regex.Replace(src, "PreloadSwf=.*", "").Trim();
                if (active)
                {
                    src += AddCustomProfiler() ?? AddDefaultProfiler();
                }
                File.WriteAllText(mmCfg, src, Encoding.UTF8);
            }
            catch 
            {
                return false; // unable to set the profiler
            }
            return true;
        }

        string AddDefaultProfiler()
        {
            string swfPath = ResolvePath(CheckResource("Profiler5.swf", "Profiler.swf"));
            ASCompletion.Commands.CreateTrustFile.Run("FDProfiler.cfg", Path.GetDirectoryName(swfPath));
            FlashConnect.Settings settings = GetFlashConnectSettings();
            return "\r\nPreloadSwf=" + swfPath + "?host=" + settings.Host + "&port=" + settings.Port + "\r\n";
        }

        string AddCustomProfiler()
        {
            string swfPath = ResolvePath(profilerSWF);
            if (swfPath is null) return null;
            ASCompletion.Commands.CreateTrustFile.Run("FDProfiler.cfg", Path.GetDirectoryName(swfPath));
            return "\r\nPreloadSwf=" + swfPath + "\r\n";
        }

        string ResolvePath(string path)
        {
            return PluginBase.CurrentProject != null
                ? PathHelper.ResolvePath(path, Path.GetDirectoryName(PluginBase.CurrentProject.ProjectPath))
                : PathHelper.ResolvePath(path);
        }

        FlashConnect.Settings GetFlashConnectSettings()
        {
            IPlugin flashConnect = PluginBase.MainForm.FindPlugin("425ae753-fdc2-4fdf-8277-c47c39c2e26b");
            return flashConnect != null ? (FlashConnect.Settings)flashConnect.Settings : new FlashConnect.Settings();
        }

        static string CheckResource(string fileName, string resName)
        {
            var fullPath = Path.Combine(PathHelper.DataDir, "AS3Context", fileName);
            if (File.Exists(fullPath)) return fullPath;
            var id = "AS3Context.Resources." + resName;
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var br = new BinaryReader(assembly.GetManifestResourceStream(id));
            using var bw = File.Create(fullPath);
            var buffer = br.ReadBytes(1024);
            while (buffer.Length > 0)
            {
                bw.Write(buffer, 0, buffer.Length);
                buffer = br.ReadBytes(1024);
            }
            bw.Close();
            br.Close();
            return fullPath;
        }

        static void CreateDefaultCfg(string mmCfg)
        {
            try
            {
                const string contents = "PolicyFileLog=1\r\nPolicyFileLogAppend=0\r\nErrorReportingEnable=1\r\nTraceOutputFileEnable=1\r\n";
                FileHelper.WriteFile(mmCfg, contents, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                ErrorManager.ShowError(ex);
            }
        }

        #endregion

    }
}