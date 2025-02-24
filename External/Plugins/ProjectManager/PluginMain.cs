using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PluginCore;
using PluginCore.Bridge;
using PluginCore.Controls;
using PluginCore.Helpers;
using PluginCore.Localization;
using PluginCore.Managers;
using PluginCore.Utilities;
using ProjectManager.Actions;
using ProjectManager.Controls;
using ProjectManager.Controls.AS2;
using ProjectManager.Controls.TreeView;
using ProjectManager.Helpers;
using ProjectManager.Projects;
using WeifenLuo.WinFormsUI.Docking;

namespace ProjectManager
{
    public static class ProjectManagerCommands
    {
        public const string NewProject = "ProjectManager.NewProject";
        public const string OpenFolder = "ProjectManager.OpenFolder";
        public const string OpenProject = "ProjectManager.OpenProject";
        public const string SendProject = "ProjectManager.SendProject";
        public const string BuildProject = "ProjectManager.BuildProject";
        public const string PlayOutput = "ProjectManager.PlayOutput";
        public const string TestMovie = "ProjectManager.TestMovie";
        public const string CompileWithFlexShell = "ProjectManager.CompileWithFlexShell";
        public const string RestartFlexShell = "ProjectManager.RestartFlexShell";
        public const string SetConfiguration = "ProjectManager.SetConfiguration";
        public const string InstalledSDKsChanged = "ProjectManager.InstalledSDKsChanged";
        public const string LineEntryDialog = "ProjectManager.LineEntryDialog";
        public const string HotBuild = "ProjectManager.HotBuild";
        public const string RefreshTree = "ProjectManager.RefreshTree";
    }

    public static class ProjectManagerEvents
    {
        public const string Menu = "ProjectManager.Menu";
        public const string ToolBar = "ProjectManager.ToolBar";
        public const string Project = "ProjectManager.Project";
        public const string ProjectSetUp = "ProjectManager.ProjectSetUp";
        public const string CleanProject = "ProjectManager.CleanProject";
        public const string TestProject = "ProjectManager.TestingProject";
        public const string BuildProject = "ProjectManager.BuildingProject";
        public const string BuildComplete = "ProjectManager.BuildComplete";
        public const string BuildFailed = "ProjectManager.BuildFailed";
        public const string RunCustomCommand = "ProjectManager.RunCustomCommand";
        public const string FileMapping = "ProjectManager.FileMapping";
        public const string TreeSelectionChanged = "ProjectManager.TreeSelectionChanged";
        public const string OpenProjectProperties = "ProjectManager.OpenProjectProperties";
        public const string OpenVirtualFile = "ProjectManager.OpenVirtualFile";
        public const string CreateProject = "ProjectManager.CreateProject";
        public const string ProjectCreated = "ProjectManager.ProjectCreated";
        public const string FileMoved = "ProjectManager.FileMoved";
        public const string FilePasted = "ProjectManager.FilePasted";
        public const string UserRefreshTree = "ProjectManager.UserRefreshTree";
        public const string BeforeSave = "ProjectManager.BeforeSave";
    }

    public class PluginMain : IPlugin
    {
        FDMenus menus;
        FileActions fileActions;
        BuildActions buildActions;
        ProjectActions projectActions;
        Queue<string> openFileQueue;
        DockContent pluginPanel;
        PluginUI pluginUI;
        Project activeProject;
        OpenResourceForm projectResources;
        bool runOutput;
        bool buildingAll;
        Queue<string> buildQueue;
        Timer buildTimer;
        bool listenToPathChange;
        ProjectManagerUIStatus uiStatus = ProjectManagerUIStatus.NotBuilding;
        bool firstRun;

        ProjectTreeView Tree => pluginUI.Tree;
        public static IMainForm MainForm => PluginBase.MainForm;

        public static ProjectManagerSettings Settings;

        const EventType eventMask = EventType.UIStarted | EventType.UIClosing | EventType.FileOpening
            | EventType.FileOpen | EventType.FileSave | EventType.FileSwitch | EventType.ProcessStart | EventType.ProcessEnd
            | EventType.ProcessArgs | EventType.Command | EventType.Keys | EventType.ApplySettings | EventType.FolderOpen;

        #region Load/Save Settings

        static string SettingsDir => Path.Combine(PathHelper.DataDir, nameof(ProjectManager));
        static string SettingsPath => Path.Combine(SettingsDir, "Settings.fdb");

        public void LoadSettings()
        {
            Settings = new ProjectManagerSettings();
            if (!Directory.Exists(SettingsDir)) Directory.CreateDirectory(SettingsDir);
            if (!File.Exists(SettingsPath))
            {
                SaveSettings();
                firstRun = true;
            }
            else
            {
                Settings = (ProjectManagerSettings)ObjectSerializer.Deserialize(SettingsPath, Settings);
                PatchSettings();
            }
            // set manually to avoid dependency in FDBuild
            FileInspector.ExecutableFileTypes = Settings.ExecutableFileTypes;
            Settings.Changed += SettingChanged;
        }

        void PatchSettings()
        {
            if (Settings.WebserverPort == 0)
            {
                Settings.WebserverPort = 2000;
            }
            // remove 'obj' from the excluded directory names - now /obj a hidden directory
            if (Settings.ExcludedDirectories.Length > 0 && Settings.ExcludedDirectories[0] == "obj")
            {
                Settings.ExcludedDirectories = Settings.ExcludedDirectories.Skip(1).ToArray();
                SaveSettings();
            }
            // add new filtered types if user has old settings
            if (!Settings.ExcludedDirectories.Contains("node_modules"))
            {
                var list = new List<string>(Settings.ExcludedDirectories) {"node_modules"};
                Settings.ExcludedDirectories = list.ToArray();
                SaveSettings();
            }
        }

        public void SaveSettings()
        {
            Settings.Changed -= SettingChanged;
            ObjectSerializer.Serialize(SettingsPath, Settings);
        }

        #endregion

        #region Plugin MetaData

        public int Api => 1;

        public string Name => nameof(ProjectManager);

        public string Guid => "30018864-fadd-1122-b2a5-779832cbbf23";

        public string Author => "FlashDevelop Team";

        public string Description { get; private set; } = "Adds project management and building to FlashDevelop.";

        public string Help => "www.flashdevelop.org/community/";

        [Browsable(false)] // explicit implementation so we can reuse the "Settings" var name
        object IPlugin.Settings => Settings;

        #endregion
        
        #region Initialize/Dispose
        
        public void Initialize()
        {
            LoadSettings();
            Description = TextHelper.GetString("Info.Description");
            openFileQueue = new Queue<string>();

            Icons.Initialize(MainForm);
            EventManager.AddEventHandler(this, eventMask);

            #region Actions and Event Listeners

            menus = new FDMenus(MainForm);
            menus.ProjectMenu.ProjectItemsEnabled = false;
            menus.TestMovie.Enabled = false;
            menus.TestMovie.Click += delegate { TestMovie(); };
            menus.BuildProject.Enabled = false;
            menus.BuildProject.Click += BuildProjectClick;
            menus.View.Click += delegate { OpenPanel(); };
            menus.GlobalClasspaths.Click += delegate { OpenGlobalClasspaths(); };
            menus.ConfigurationSelector.FlatCombo.SelectedIndexChanged += delegate 
            {
                var isDebug = menus.ConfigurationSelector.Text == TextHelper.GetString("Info.Debug");
                FlexCompilerShell.Cleanup();
                pluginUI.IsTraceDisabled = !isDebug;
                var project = activeProject;
                if (project != null) project.TraceEnabled = isDebug;
            };
            menus.TargetBuildSelector.KeyDown += TargetBuildSelector_KeyDown;
            menus.TargetBuildSelector.FlatCombo.SelectedIndexChanged += delegate { ApplyTargetBuild(); };
            menus.TargetBuildSelector.LostFocus += delegate { ApplyTargetBuild(); };
            
            menus.ProjectMenu.NewProject.Click += delegate { NewProject(); };
            menus.ProjectMenu.OpenProject.Click += delegate { OpenProject(); };
            menus.ProjectMenu.OpenFolder.Click += delegate { OpenFolder(); };
            menus.ProjectMenu.ImportProject.Click += ImportProject;
            menus.ProjectMenu.CloseProject.Click += delegate { CloseProject(false); };
            menus.ProjectMenu.OpenResource.Click += delegate { OpenResource(); };
            menus.ProjectMenu.TestMovie.Click += delegate { TestMovie(); };
            menus.ProjectMenu.RunProject.Click += delegate { RunProject(); };
            menus.ProjectMenu.BuildProject.Click += BuildProjectClick;
            menus.ProjectMenu.CleanProject.Click += delegate { CleanProject(); };
            menus.ProjectMenu.Properties.Click += delegate { OpenProjectProperties(); };
            menus.RecentProjects.ProjectSelected += OpenProjectSilent;

            buildActions = new BuildActions(MainForm, this);
            buildActions.BuildComplete += BuildComplete;
            buildActions.BuildFailed += BuildFailed;

            fileActions = new FileActions(MainForm);
            fileActions.OpenFile += OpenFile;
            fileActions.FileDeleted += FileDeleted;
            fileActions.FileMoved += FileMoved;
            fileActions.FileCopied += FilePasted;

            projectActions = new ProjectActions(pluginUI);

            pluginUI = new PluginUI(menus, fileActions, projectActions);
            pluginUI.NewProject += delegate { NewProject(); };
            pluginUI.OpenFolder += delegate { OpenFolder(); };
            pluginUI.OpenProject += delegate { OpenProject(); };
            pluginUI.ImportProject += ImportProject;
            pluginUI.Rename += fileActions.Rename;
            pluginUI.TreeBar.ShowHidden.Click += delegate { ToggleShowHidden(); };
            pluginUI.TreeBar.Synchronize.Click += delegate { TreeSyncToCurrentFile(); };
            pluginUI.TreeBar.SynchronizeMain.Click += delegate { TreeSyncToMainFile(); };
            pluginUI.TreeBar.CollapseAll.Click += delegate { CollapseAll(); };
            pluginUI.TreeBar.ProjectProperties.Click += delegate { OpenProjectProperties(); };
            pluginUI.TreeBar.RefreshSelected.Click += delegate { TreeRefreshSelectedNode(); };
            pluginUI.TreeBar.ProjectTypes.Click += delegate 
            {
                DataEvent de = new DataEvent(EventType.Command, "ASCompletion.TypesExplorer", null);
                EventManager.DispatchEvent(this, de);
            };

            pluginUI.Menu.Open.Click += delegate { TreeOpenItems(); };
            pluginUI.Menu.Execute.Click += delegate { TreeExecuteItems(); };
            pluginUI.Menu.Insert.Click += delegate { TreeInsertItem(); };
            pluginUI.Menu.AddLibrary.Click += delegate { TreeAddLibraryItems(); };
            pluginUI.Menu.AlwaysCompile.Click += delegate { TreeAlwaysCompileItems(); };
            pluginUI.Menu.SetDocumentClass.Click += delegate { TreeDocumentClass(); };
            pluginUI.Menu.DocumentClass.Click += delegate { TreeDocumentClass(); };
            pluginUI.Menu.Browse.Click += delegate { TreeBrowseItem(); };
            pluginUI.Menu.Cut.Click += delegate { TreeCutItems(); };
            pluginUI.Menu.Copy.Click += delegate { TreeCopyItems(); };
            pluginUI.Menu.Paste.Click += delegate { TreePasteItems(); };
            pluginUI.Menu.Delete.Click += delegate { TreeDeleteItems(); };
            pluginUI.Menu.LibraryOptions.Click += delegate { TreeLibraryOptions(); };
            pluginUI.Menu.HideItem.Click += delegate { TreeHideItems(); };
            pluginUI.Menu.ShowHidden.Click += delegate { ToggleShowHidden(); };
            pluginUI.Menu.AddFileFromTemplate += TreeAddFileFromTemplate;
            pluginUI.Menu.AddNewFolder.Click += delegate { TreeAddFolder(); };
            pluginUI.Menu.AddLibraryAsset.Click += delegate { TreeAddAsset(); };
            pluginUI.Menu.AddExistingFile.Click += delegate { TreeAddExistingFile(); };
            pluginUI.Menu.TestMovie.Click += delegate { TestMovie(); };
            pluginUI.Menu.RunProject.Click += delegate { RunProject(); };
            pluginUI.Menu.BuildProject.Click += BuildProjectClick;
            pluginUI.Menu.CleanProject.Click += delegate { CleanProject(); };
            pluginUI.Menu.CloseProject.Click += delegate { CloseProject(false); };
            pluginUI.Menu.Properties.Click += delegate { OpenProjectProperties(); };
            if (Win32.ShouldUseWin32()) pluginUI.Menu.ShellMenu.Click += delegate { TreeShowShellMenu(); };
            pluginUI.Menu.CommandPrompt.Click += delegate { TreeShowCommandPrompt(); };
            pluginUI.Menu.BuildProjectFile.Click += delegate { BackgroundBuild(); };
            pluginUI.Menu.BuildProjectFiles.Click += delegate { BackgroundBuild(); };
            pluginUI.Menu.BuildAllProjects.Click += delegate { FullBuild(); };
            pluginUI.Menu.TestAllProjects.Click += delegate { TestBuild(); };
            pluginUI.Menu.FindAndReplace.Click += delegate { FindAndReplace(); };
            pluginUI.Menu.FindInFiles.Click += delegate { FindInFiles(); };
            pluginUI.Menu.CopyClassName.Click += delegate { CopyClassName(); };
            pluginUI.Menu.AddSourcePath.Click += delegate { AddSourcePath(); };
            pluginUI.Menu.RemoveSourcePath.Click += delegate { RemoveSourcePath(); };
            pluginUI.Menu.Opening += MenuOpening;

            Tree.MovePath += fileActions.Move;
            Tree.CopyPath += fileActions.Copy;
            Tree.DoubleClick += delegate { TreeDoubleClick(); };

            #endregion

            pluginPanel = MainForm.CreateDockablePanel(pluginUI, Guid, Icons.Project.Img, DockState.DockRight);
            buildQueue = new Queue<string>();
            buildTimer = new Timer();
            buildTimer.Interval = 500;
            buildTimer.Tick += OnBuildTimerTick;
            buildingAll = false;
            runOutput = false;
        }

        void BuildProjectClick(object sender, EventArgs e)
        {
            if (uiStatus == ProjectManagerUIStatus.NotBuilding)
                BuildProject();
            else if (uiStatus == ProjectManagerUIStatus.Building)
            {
                string title = " " + TextHelper.GetString("FlashDevelop.Title.ConfirmDialog");
                string message = TextHelper.GetString("Info.AreYouSureToStopBuild");

                DialogResult result = MessageBox.Show(PluginBase.MainForm, message,
                    title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result == DialogResult.Yes)
                    MainForm.KillProcess();
            }
        }

        void ApplyTargetBuild()
        {
            var project = activeProject;
            if (project is null) return;
            var target = menus.TargetBuildSelector.Text;
            if (project.TargetBuild == target) return;
            menus.AddTargetBuild(target);
            FlexCompilerShell.Cleanup();
            project.TargetBuild = menus.TargetBuildSelector.Text;
            project.UpdateVars(false);
            projectActions.UpdateASCompletion(MainForm, project);
        }

        void TargetBuildSelector_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) // leave target build input field to apply
                PluginBase.MainForm.CurrentDocument.Activate();
        }
        
        public void Dispose()
        {
            // we have to fiddle this a little since we only get once change to save our settings!
            // (further saves will be ignored by FD design)
            var project = activeProject; 
            var lastProject = (project != null) ? project.ProjectPath : "";
            CloseProject(true);
            Settings.LastProject = lastProject;
            FlexCompilerShell.Cleanup(); // in case it was used
            SaveSettings();
        }
        
        #endregion

        #region Plugin Events

        public void HandleEvent(object sender, NotifyEvent e, HandlingPriority priority)
        {
            TextEvent te = e as TextEvent;
            DataEvent de = e as DataEvent;
            Project project;
            switch (e.Type)
            {
                case EventType.UIStarted:
                    // for some reason we have to do this on the next message loop for the tree
                    // state to be restored properly.
                    pluginUI.BeginInvoke((MethodInvoker)(() =>
                    {
                        BroadcastMenuInfo();
                        BroadcastToolBarInfo();
                        OpenLastProject();
                        if (firstRun) pluginPanel.Show();
                    }));
                    break;

                case EventType.UIClosing:
                    // save project session, documents have not been closed yet
                    SaveProjectSession();
                    break;

                // replace $(SomeVariable) type stuff with things we know about
                case EventType.ProcessArgs:
                    project = activeProject; // replace arguments using active project data

                    if (!ProjectCreator.IsRunning)
                    {
                        if (project != null && te.Value.Contains('$'))
                        {
                            // steal macro names and values from the very useful BuildEvent macros
                            BuildEventVars vars = new BuildEventVars(project);

                            vars.AddVar("CompilerConfiguration", menus.ConfigurationSelector.Text);
                            vars.AddVar("BuildIPC", buildActions.IPCName);

                            foreach (BuildEventInfo info in vars.GetVars())
                                te.Value = te.Value.Replace(info.FormattedName, info.Value);

                            // give the FileActions class an opportunity to process arguments
                            // it may know about (if it was responsible for creating the file)
                            te.Value = fileActions.ProcessArgs(project, te.Value);
                        }
                        else
                        {
                            BuildEventVars vars = new BuildEventVars(null);
                            vars.AddVar("ProjectDir", PluginBase.MainForm.WorkingDirectory);
                            foreach (BuildEventInfo info in vars.GetVars())
                                te.Value = te.Value.Replace(info.FormattedName, info.Value);
                        }
                    }
                    break;

                case EventType.FolderOpen:
                    if (Directory.Exists(te.Value))
                    {
                        te.Handled = true;
                        OpenFolderSilent(te.Value);
                    }
                    break;

                case EventType.FileOpening:
                    // if this is a project file, we can handle it ourselves
                    if (FileInspector.IsProject(te.Value) || ProjectCreator.IsKnownProject(Path.GetExtension(te.Value).ToLower()))
                    {
                        te.Handled = true;
                        OpenProjectSilent(te.Value);
                    }
                    else if (te.Value.EndsWithOrdinal(".swf"))
                    {
                        te.Handled = true;
                        OpenSwf(te.Value);
                    }
                    break;

                case EventType.FileOpen:
                    SetDocumentIcon(MainForm.CurrentDocument);
                    OpenNextFile(); // it's safe to open any other files on the queue
                    break;

                case EventType.FileSave:
                    // refresh the tree to update any included <mx:Script> tags
                    string path = MainForm.CurrentDocument.FileName;
                    if (Settings.EnableMxmlMapping && FileInspector.IsMxml(path, Path.GetExtension(path).ToLower()) && Tree.NodeMap.ContainsKey(path))
                    {
                        Tree.RefreshNode(Tree.NodeMap[path]);
                    }
                    TabColors.UpdateTabColors(Settings);
                    break;

                case EventType.FileSwitch:
                    TabColors.UpdateTabColors(Settings);
                    if (Settings.TrackActiveDocument) TreeSyncToCurrentFile();
                    break;

                case EventType.ProcessStart:
                    buildActions.NotifyBuildStarted();
                    break;

                case EventType.ProcessEnd:
                    buildActions.NotifyBuildEnded(te.Value);
                    break;

                case EventType.ApplySettings:
                    TabColors.UpdateTabColors(Settings);
                    break;

                case EventType.Command:
                    if (de.Action.StartsWithOrdinal("ProjectManager."))
                    {
                        if (de.Action == ProjectManagerCommands.NewProject)
                        {
                            NewProject();
                            e.Handled = true;
                        }
                        else if (de.Action == ProjectManagerCommands.OpenFolder)
                        {
                            if (de.Data != null && Directory.Exists((string)de.Data))
                            {
                                OpenFolderSilent((string)de.Data);
                            }
                            else OpenFolder();
                            e.Handled = true;
                        }
                        else if (de.Action == ProjectManagerCommands.OpenProject)
                        {
                            if (de.Data != null && File.Exists((string)de.Data))
                            {
                                OpenProjectSilent((string)de.Data);
                            }
                            else OpenProject();
                            e.Handled = true;
                        }
                        else if (de.Action == ProjectManagerCommands.SendProject)
                        {
                            BroadcastProjectInfo(activeProject);
                            e.Handled = true;
                        }
                        else if (de.Action == ProjectManagerCommands.InstalledSDKsChanged)
                        {
                            project = activeProject; // TODO refresh SDK for all projects
                            BuildActions.GetCompilerPath(project); // refresh project's SDK
                            e.Handled = true;
                        }
                        else if (de.Action == ProjectManagerCommands.BuildProject)
                        {
                            if (Tree.Projects.Count > 0)
                            {
                                AutoSelectConfiguration((string)de.Data);
                                BuildProject();
                                e.Handled = true;
                            }
                        }
                        else if (de.Action == ProjectManagerCommands.TestMovie)
                        {
                            project = activeProject; // TODO we need a "runnable" project
                            if (project != null)
                            {
                                AutoSelectConfiguration((string)de.Data);
                                TestMovie();
                                e.Handled = true;
                            }
                        }
                        else if (de.Action == ProjectManagerCommands.PlayOutput)
                        {
                            if (activeProject != null || de.Data != null)
                            {
                                OpenSwf((string)de.Data);
                                de.Handled = true;
                            }
                        }
                        else if (de.Action == ProjectManagerCommands.RestartFlexShell)
                        {
                            FlexCompilerShell.Cleanup();
                        }
                        else if (de.Action == ProjectManagerCommands.SetConfiguration)
                        {
                            AutoSelectConfiguration((string)de.Data);
                        }
                        else if (de.Action == ProjectManagerCommands.HotBuild)
                        {
                            if (activeProject != null)
                            {
                                AutoSelectConfiguration((string)de.Data);
                                TestMovie();
                                e.Handled = true;
                            }
                        }
                        else if (de.Action == ProjectManagerCommands.RefreshTree)
                        {
                            TreeRefreshSelectedNode();
                        }
                        else if (de.Action == ProjectManagerCommands.LineEntryDialog)
                        {
                            var info = (Hashtable)de.Data;
                            using var askName = new LineEntryDialog((string)info["title"], (string)info["label"], (string)info["suggestion"]);
                            var choice = askName.ShowDialog();
                            if (choice == DialogResult.OK && askName.Line.Trim().Length > 0 && askName.Line.Trim() != (string)info["suggestion"])
                            {
                                info["suggestion"] = askName.Line.Trim();
                            }
                            if (choice == DialogResult.OK)
                            {
                                e.Handled = true;
                            }
                        }
                    }
                    else if (de.Action == "FlashDebugger.Running")
                    {
                        menus.TestMovie.Enabled = false;
                        menus.ProjectMenu.TestMovie.Enabled = false;
                        menus.ProjectMenu.RunProject.Enabled = false;
                        pluginUI.Menu.TestMovie.Enabled = false;
                        pluginUI.Menu.RunProject.Enabled = false;
                    }
                    else if (de.Action == "FlashDebugger.Stopped") UpdateUIStatus(ProjectManagerUIStatus.NotBuilding);
                    break;

                case EventType.Keys:
                    e.Handled = HandleKeyEvent(e as KeyEvent);
                    break;
            }
        }

        void AutoSelectConfiguration(string configuration)
        {
            if (configuration != null)
            {
                int newIdx = menus.ConfigurationSelector.Items.IndexOf(configuration);
                if (newIdx >= 0) menus.ConfigurationSelector.SelectedIndex = newIdx;
            }
        }

        bool HandleKeyEvent(KeyEvent ke)
        {
            if (activeProject is null) return false;

            var shortcutId = PluginBase.MainForm.GetShortcutItemId(ke.Value);
            if (shortcutId == "ProjectMenu.ConfigurationSelector")
            {
                pluginUI.menus.ConfigurationSelector.Focus();
            }
            else if (shortcutId == "ProjectMenu.ConfigurationSelectorToggle")
            {
                pluginUI.menus.ToggleDebugRelease();
            }
            else if (shortcutId == "ProjectMenu.TargetBuildSelector")
            {
                pluginUI.menus.TargetBuildSelector.Focus();
            }
            else if (shortcutId == "ProjectTree.LocateActiveFile")
            {
                pluginPanel.Show();
                TreeSyncToCurrentFile();
            }
            // Handle tree-level simple shortcuts like copy/paste/del
            else if (Tree.Focused && !pluginUI.IsEditingLabel)
            {
                if (ke.Value == (Keys.Control | Keys.C) && pluginUI.Menu.Contains(pluginUI.Menu.Copy)) TreeCopyItems();
                else if (ke.Value == (Keys.Control | Keys.X) && pluginUI.Menu.Contains(pluginUI.Menu.Cut)) TreeCutItems();
                else if (ke.Value == (Keys.Control | Keys.V) && pluginUI.Menu.Contains(pluginUI.Menu.Paste)) TreePasteItems();
                else if (ke.Value == Keys.Delete && pluginUI.Menu.Contains(pluginUI.Menu.Delete)) TreeDeleteItems();
                else if (ke.Value == Keys.Enter && pluginUI.Menu.Contains(pluginUI.Menu.Open)) TreeOpenItems();
                else if (ke.Value == Keys.Enter && pluginUI.Menu.Contains(pluginUI.Menu.Insert)) TreeInsertItem();
                else return false;
            }
            else return false;
            return true;
        }
        
        #endregion

        #region Custom Methods

        void RestoreProjectSession(IProject project)
        {
            if (project is null || !Settings.UseProjectSessions) return;
            var hash = HashCalculator.CalculateSHA1(project.ProjectPath.ToLower());
            var sessionFile = Path.Combine(SettingsDir, "Sessions", hash + ".fdb");
            if (File.Exists(sessionFile)) PluginBase.MainForm.CallCommand("RestoreSession", sessionFile);
        }

        void SaveProjectSession()
        {
            Project project = Tree.Projects.Count > 0 ? Tree.Projects[0] : null; // TODO we need a main project/solution
            if (project is null || !Settings.UseProjectSessions) return;
            string hash = HashCalculator.CalculateSHA1(project.ProjectPath.ToLower());
            string sessionDir = Path.Combine(SettingsDir, "Sessions");
            if (!Directory.Exists(sessionDir)) Directory.CreateDirectory(sessionDir);
            string sessionFile = Path.Combine(sessionDir, hash + ".fdb");
            PluginBase.MainForm.CallCommand("SaveSession", sessionFile);
        }

        void SetProject(Project project) => SetProject(project, true, false);

        void SetProject(Project project, bool stealFocus) => SetProject(project, stealFocus, false);

        void SetProject(Project project, bool stealFocus, bool internalOpening)
        {
            if (project is null || Tree.Projects.Contains(project)) return;
            if (activeProject != null) CloseProject(true);

            // configure
            var prefs = Settings.GetPrefs(project);
            project.TraceEnabled = prefs.DebugMode;
            project.TargetBuild = prefs.TargetBuild;
            project.UpdateVars(true);

            SetActiveProject(project);

            // events
            project.ClasspathChanged += ProjectClasspathsChanged;
            project.BeforeSave += ProjectBeforeSave;
            listenToPathChange = true;

            // activate
            if (!internalOpening || !PluginBase.Settings.RestoreFileSession)
            {
                RestoreProjectSession(project);
            }

            // track active file
            if (Settings.TrackActiveDocument) TreeSyncToCurrentFile();

            if (stealFocus)
            {
                OpenPanel();
                pluginUI.Focus();
            }
            TabColors.UpdateTabColors(Settings);
            UpdateUIStatus(ProjectManagerUIStatus.NotBuilding);
        }

        void SetActiveProject(Project project)
        {
            activeProject = project;

            // init
            Environment.CurrentDirectory = project.Directory;
            Settings.LastProject = project.ProjectPath;
            Settings.Language = project.Language;

            // notify
            PluginBase.CurrentProject = project;
            PluginBase.MainForm.RefreshUI();

            BuildActions.GetCompilerPath(project); // detect project's SDK
            BroadcastProjectInfo(project);
            projectActions.UpdateASCompletion(MainForm, project);
            BroadcastProjectSetUp(project);

            // ui
            pluginUI.SetProject(project);
            menus.SetProject(project); // TODO this should reflect the "solution"
            pluginUI.NotifyIssues();
        }

        void CloseProject(bool internalClosing)
        {
            var project = Tree.Projects.Count > 0 ? Tree.Projects[0] : null; // TODO we need a main project/solution
            if (project is null) return; // already closed
            listenToPathChange = false;

            // save project prefs
            var prefs = Settings.GetPrefs(project);
            prefs.ExpandedPaths = Tree.ExpandedPaths;
            prefs.DebugMode = project.TraceEnabled;
            prefs.TargetBuild = project.TargetBuild;

            if (!PluginBase.MainForm.ClosingEntirely)
            {
                SaveProjectSession();
                menus.CloseProject();
            }

            activeProject = null;
            if (projectResources != null)
            {
                projectResources.Close();
                projectResources = null;
            }
            FlexCompilerShell.Cleanup(); // clear compile cache for this project
            Webserver.KillServer();

            if (!internalClosing)
            {
                pluginUI.SetProject(null);
                Settings.LastProject = "";
                UpdateUIStatus(ProjectManagerUIStatus.Disabled);
                
                PluginBase.CurrentSolution = null;
                PluginBase.CurrentProject = null;
                PluginBase.CurrentSDK = null;
                PluginBase.MainForm.RefreshUI();

                BroadcastProjectInfo(null);
                projectActions.UpdateASCompletion(MainForm, null);
            }
            TabColors.UpdateTabColors(Settings);
        }
        
        public void OpenPanel() => pluginPanel.Show();

        public void OpenLastProject()
        {
            // try to open the last opened project
            string lastProject = Settings.LastProject;
            if (!string.IsNullOrEmpty(lastProject) && File.Exists(lastProject))
            {
                SetProject(projectActions.OpenProjectSilent(lastProject), false, true);
            }
        }

        void OpenGlobalClasspaths()
        {
            using var dialog = new ClasspathDialog(Settings) {Language = "as2"};
            var project = activeProject;
            if (project != null && project.Language != "*")
                dialog.Language = project.Language;
            dialog.ShowDialog(pluginUI);
        }

        void OpenProjectProperties()
        {
            var project = activeProject;
            var de = new DataEvent(EventType.Command, ProjectManagerEvents.OpenProjectProperties, project);
            EventManager.DispatchEvent(this, de);
            using var dialog = project.CreatePropertiesDialog();
            project.UpdateVars(false);
            dialog.SetProject(project);
            dialog.OpenGlobalClasspaths += (sender, args) => OpenGlobalClasspaths();
            dialog.ShowDialog(pluginUI);

            if (dialog.ClasspathsChanged || dialog.AssetsChanged)
                Tree.RebuildTree();

            if (dialog.PropertiesChanged)
            {
                project.PropertiesChanged();
                project.UpdateVars(true);
                BroadcastProjectInfo(project);
                project.Save();
                menus.ProjectChanged(project);
            }
            else projectActions.UpdateASCompletion(MainForm, project);
        }

        public void OpenFile(string path)
        {
            if (FileInspector.ShouldUseShellExecute(path)) ShellOpenFile(path);
            else if (FileInspector.IsSwf(path, Path.GetExtension(path).ToLower())) PlaySwf(path);
            else if (path.IndexOfOrdinal("::") > 0)
            {
                var de = new DataEvent(EventType.Command, ProjectManagerEvents.OpenVirtualFile, path);
                EventManager.DispatchEvent(this, de);
            }
            else MainForm.OpenEditableDocument(path);
        }

        void SetDocumentIcon(ITabbedDocument doc)
        {
            Bitmap bitmap = null;

            // try to open with the same icon that the treeview is using
            if (doc.FileName != null)
            {
                if (Tree.NodeMap.ContainsKey(doc.FileName))
                    bitmap = Tree.ImageList.Images[Tree.NodeMap[doc.FileName].ImageIndex] as Bitmap;
                else
                    bitmap = Icons.GetImageForFile(doc.FileName).Img as Bitmap;
            }
            if (bitmap != null)
            {
                doc.UseCustomIcon = true;
                doc.Icon = Icon.FromHandle(bitmap.GetHicon());
            }
        }

        void PlaySwf(string path)
        {
            // Let FlashViewer handle it..
            var de = new DataEvent(EventType.Command, "FlashViewer.Default", path);
            EventManager.DispatchEvent(this, de);
        }

        void OpenSwf(string path)
        {
            DataEvent de;
            Webserver.Port = Settings.WebserverPort;

            var project = activeProject; // TODO this should be the runnable project
            if (path is null)
            {
                if (project is null) return;
                path = project.OutputPathAbsolute;
            }
            if (project is null) // use default player
            {
                de = new DataEvent(EventType.Command, "FlashViewer.Default", path);
                EventManager.DispatchEvent(this, de);
                return;
            }

            if (path.StartsWithOrdinal(project.Directory)) 
                path = project.FixDebugReleasePath(path);

            if (project.TestMovieBehavior == TestMovieBehavior.NewTab)
            {
                de = new DataEvent(EventType.Command, "FlashViewer.Document", path);
                EventManager.DispatchEvent(this, de);
            }
            else if (project.TestMovieBehavior == TestMovieBehavior.NewWindow)
            {
                de = new DataEvent(EventType.Command, "FlashViewer.Popup", $"{path},{project.MovieOptions.Width},{project.MovieOptions.Height}");
                EventManager.DispatchEvent(this, de);
            }
            else if (project.TestMovieBehavior == TestMovieBehavior.ExternalPlayer)
            {
                de = new DataEvent(EventType.Command, "FlashViewer.External", path);
                EventManager.DispatchEvent(this, de);
            }
            else if (project.TestMovieBehavior == TestMovieBehavior.OpenDocument)
            {
                if (!string.IsNullOrEmpty(project.TestMovieCommand))
                {
                    if (project.TraceEnabled && project.EnableInteractiveDebugger)
                    {
                        de = new DataEvent(EventType.Command, "AS3Context.StartProfiler", null);
                        EventManager.DispatchEvent(this, de);
                        de = new DataEvent(EventType.Command, "AS3Context.StartDebugger", null);
                        EventManager.DispatchEvent(this, de);
                    }
                    var doc = project.TestMovieCommand;
                    try
                    {
                        doc = project.GetAbsolutePath(doc);
                        doc = project.FixDebugReleasePath(doc);
                    }
                    catch
                    {
                        // ignored
                    }
                    var psi = new ProcessStartInfo(doc) {WorkingDirectory = project.Directory};
                    ProcessHelper.StartAsync(psi);
                }
            }
            else if (project.TestMovieBehavior == TestMovieBehavior.Webserver)
            {
                if (project.TraceEnabled && project.EnableInteractiveDebugger)
                {
                    de = new DataEvent(EventType.Command, "AS3Context.StartProfiler", null);
                    EventManager.DispatchEvent(this, de);
                    de = new DataEvent(EventType.Command, "AS3Context.StartDebugger", null);
                    EventManager.DispatchEvent(this, de);
                }
                var doc = project.TestMovieCommand;
                try
                {
                    if (string.IsNullOrEmpty(doc) || doc == "/" || doc == "\\")
                    {
                        doc = project.OutputPathAbsolute;
                        if (File.Exists(doc)) doc = Path.GetDirectoryName(doc);
                    }
                    doc = project.GetAbsolutePath(doc);
                    doc = project.FixDebugReleasePath(doc);
                    Webserver.StartServer(doc);
                }
                catch
                {
                    // ignored
                }
            }
            else if (project.TestMovieBehavior == TestMovieBehavior.Custom)
            {
                if (!string.IsNullOrEmpty(project.TestMovieCommand))
                {
                    if (project.TraceEnabled && project.EnableInteractiveDebugger)
                    {
                        de = new DataEvent(EventType.Command, "AS3Context.StartProfiler", null);
                        EventManager.DispatchEvent(this, de);
                        de = new DataEvent(EventType.Command, "AS3Context.StartDebugger", null);
                        EventManager.DispatchEvent(this, de);
                    }

                    var cmd = MainForm.ProcessArgString(project.TestMovieCommand).Trim();
                    cmd = project.FixDebugReleasePath(cmd);

                    // let plugins handle the command
                    de = new DataEvent(EventType.Command, ProjectManagerEvents.RunCustomCommand, cmd);
                    EventManager.DispatchEvent(this, de);
                    if (de.Handled) return;

                    // shell execute
                    var semi = cmd.IndexOf(';');
                    if (semi < 0) semi = cmd.IndexOf(' ');
                    var args = semi > 0 ? cmd.Substring(semi + 1) : "";
                    cmd = semi > 0 ? cmd.Substring(0, semi) : cmd;

                    var psi = new ProcessStartInfo(cmd, args);
                    psi.UseShellExecute = true;
                    psi.WorkingDirectory = project.Directory;
                    ProcessHelper.StartAsync(psi);
                }
                else
                {
                    // let plugins handle the command
                    de = new DataEvent(EventType.Command, ProjectManagerEvents.RunCustomCommand, "");
                    EventManager.DispatchEvent(this, de);
                }
            }
            else
            {
                // Default: Let FlashViewer handle it..
                de = new DataEvent(EventType.Command, "FlashViewer.Default", $"{path},{project.MovieOptions.Width},{project.MovieOptions.Height}");
                EventManager.DispatchEvent(this, de);
            }
        }
        
        #endregion

        #region Event Handlers

        void BuildComplete(IProject project, bool runOutput)
        {
            if (project != null) BroadcastBuildComplete(project);
            if (buildQueue.Count > 0) ProcessBuildQueue();
            else if (buildingAll)
            {
                buildingAll = false;
                buildTimer.Tag = "buildAll";
                buildTimer.Start();
            }
            else if (runOutput)
            {
                OpenSwf(project.OutputPathAbsolute);
            }
        }

        void BuildFailed(IProject project, bool runOutput)
        {
            buildQueue.Clear();
            this.runOutput = false;
            buildingAll = false;
            BroadcastBuildFailed(project);
        }

        bool DisabledForBuild
        {
            set => menus.DisabledForBuild = pluginUI.Menu.DisabledForBuild = value;
        }

        public void UpdateUIStatus(ProjectManagerUIStatus status)
        {
            if (activeProject != null && activeProject.IsFolderProject())
            {
                status = ProjectManagerUIStatus.Disabled;
            }
            var contextMenuItem = pluginUI.Menu.BuildProject;
            var menuItem = menus.ProjectMenu.BuildProject;
            var menuButton = menus.BuildProject;

            uiStatus = status;

            switch (status)
            {
                case ProjectManagerUIStatus.Disabled:
                    DisabledForBuild = true;
                    break;

                case ProjectManagerUIStatus.NotBuilding:
                    DisabledForBuild = false;
                    menuButton.ToolTipText = menuItem.Text = contextMenuItem.Text =
                        TextHelper.GetStringWithoutMnemonics("Label.BuildProject");
                    PluginBase.MainForm.ApplySecondaryShortcut(menuButton);
                    menuButton.Image = menuItem.Image = contextMenuItem.Image = Icons.Gear.Img;
                    break;

                case ProjectManagerUIStatus.Building:
                    DisabledForBuild = true;
                    menuButton.Enabled = menuItem.Enabled = contextMenuItem.Enabled = true;
                    menuButton.ToolTipText = menuItem.Text = contextMenuItem.Text = TextHelper.GetString("Label.StopBuild");
                    PluginBase.MainForm.ApplySecondaryShortcut(menuButton);
                    menuButton.Image = menuItem.Image = contextMenuItem.Image = Icons.X.Img;
                    break;
            }
        }

        bool ProjectBeforeSave(Project project, string fileName)
        {
            var de = new DataEvent(EventType.Command, ProjectManagerEvents.BeforeSave, fileName);
            EventManager.DispatchEvent(project, de);
            return !de.Handled; // saving handled or not allowed
        }

        void ProjectClasspathsChanged(Project project)
        {
            if (!listenToPathChange) return;
            listenToPathChange = false;
            projectActions.UpdateASCompletion(MainForm, project);
            pluginUI.NotifyIssues();
            FlexCompilerShell.Cleanup(); // clear compile cache for this project
            Tree.RebuildTree();
            listenToPathChange = true;
        }

        void NewProject()
        {
            var project = projectActions.NewProject();
            if (project != null) SetProject(project);
        }

        void OpenProject()
        {
            var project = projectActions.OpenProject();
            if (project != null) SetProject(project);
        }

        void OpenFolder()
        {
            var project = projectActions.OpenFolder();
            if (project != null) SetProject(project);
        }

        void OpenFolderSilent(string path)
        {
            var project = projectActions.OpenFolderSilent(path);
            SetProject(project);
        }

        void ImportProject(object sender, EventArgs eventArgs)
        {
            string importFrom = null;
            if (eventArgs is LinkLabelLinkClickedEventArgs args && args.Link.LinkData is string data)
            {
                var strings = data.Split('|');
                if (strings.Length > 1) importFrom = strings[1];
            }
            var project = projectActions.ImportProject(importFrom);
            if (project != null) OpenProjectSilent(project);
        }

        void OpenProjectSilent(string projectPath)
        {
            if (!Path.IsPathRooted(projectPath)) projectPath = Path.GetFullPath(projectPath);
            var project = projectActions.OpenProjectSilent(projectPath);
            if (project != null) SetProject(project);
        }

        void TestMovie()
        {
            var project = activeProject; // TODO we need a runnable project
            var noTrace = pluginUI.IsTraceDisabled;
            var de = new DataEvent(EventType.Command, ProjectManagerEvents.TestProject, (noTrace) ? "Release" : "Debug");
            EventManager.DispatchEvent(this, de);
            if (de.Handled) return;
            if (!buildActions.Build(project, true, noTrace))
            {
                BroadcastBuildFailed(project);
            }
        }

        void RunProject()
        {
            var de = new DataEvent(EventType.Command, ProjectManagerCommands.PlayOutput, null);
            EventManager.DispatchEvent(this, de);
        }

        void BuildProject() 
        {
            var project = activeProject; // TODO build all projects
            var noTrace = pluginUI.IsTraceDisabled;
            var de = new DataEvent(EventType.Command, ProjectManagerEvents.BuildProject, (noTrace) ? "Release" : "Debug");
            EventManager.DispatchEvent(this, de);
            if (de.Handled) return;
            if (!buildActions.Build(project, false, noTrace))
            {
                BroadcastBuildFailed(project);
            }
        }

        void CleanProject()
        {
            try
            {
                var disableWatchers = new DataEvent(EventType.Command, ProjectFileActionsEvents.FileDisableWatchers, null);
                EventManager.DispatchEvent(this, disableWatchers);

                var project = activeProject; // TODO clean all projects

                var de = new DataEvent(EventType.Command, ProjectManagerEvents.CleanProject, project);
                EventManager.DispatchEvent(this, de);
                if (de.Handled)
                    return;

                FlexCompilerShell.Cleanup();
                if (!project.Clean())
                    ErrorManager.ShowInfo(TextHelper.GetString("Info.UnableToCleanProject"));
            }
            finally
            {
                var enableWatchers = new DataEvent(EventType.Command, ProjectFileActionsEvents.FileEnableWatchers, null);
                EventManager.DispatchEvent(this, enableWatchers);
            }
        }

        void FileDeleted(string path)
        {
            DocumentManager.CloseDocuments(path);
            var project = Tree.ProjectOf(path);
            if (project != null)
            {
                projectActions.RemoveAllReferences(project, path);
                project.Save();
            }
            pluginUI.WatchParentOf(path);
        }

        void FileMoved(string fromPath, string toPath)
        {
            var project = Tree.ProjectOf(fromPath);
            var projectTo = Tree.ProjectOf(toPath);
            if (project != null && project != projectTo)
            {
                if (project.IsCompileTarget(fromPath) || project.IsLibraryAsset(fromPath))
                {
                    ErrorManager.ShowInfo("The project maintains a reference on this file preventing this modification.");
                    return;
                }
            }

            DocumentManager.MoveDocuments(fromPath, toPath);
            if (project != null)
            {
                projectActions.MoveReferences(project, fromPath, toPath);
                project.Save();
            }
            pluginUI.WatchParentOf(fromPath);
            pluginUI.WatchParentOf(toPath);

            var data = new Hashtable {["fromPath"] = fromPath, ["toPath"] = toPath};
            var de = new DataEvent(EventType.Command, ProjectManagerEvents.FileMoved, data);
            EventManager.DispatchEvent(this, de);
        }

        void FilePasted(string fromPath, string toPath)
        {
            var data = new Hashtable {["fromPath"] = fromPath, ["toPath"] = toPath};
            var de = new DataEvent(EventType.Command, ProjectManagerEvents.FilePasted, data);
            EventManager.DispatchEvent(this, de);
        }

        public void PropertiesClick(object sender, EventArgs e) => OpenProjectProperties();

        void SettingChanged(string setting)
        {
            if (setting == "ExcludedFileTypes" || setting == "ExcludedDirectories" || setting == "ShowProjectClasspaths" || setting == "ShowGlobalClasspaths" || setting == "GlobalClasspath" || setting == "ShowExternalLibraries")
            {
                Tree.RebuildTree();
            }
            else if (setting == "ExecutableFileTypes")
            {
                FileInspector.ExecutableFileTypes = Settings.ExecutableFileTypes;
            }
            else if (setting == "GlobalClasspath")
            {
                FlexCompilerShell.Cleanup(); // clear compile cache for all projects
            }
        }

        #endregion

        #region Event Broadcasting

        void BroadcastMenuInfo()
        {
            var de = new DataEvent(EventType.Command, ProjectManagerEvents.Menu, menus.ProjectMenu);
            EventManager.DispatchEvent(this, de);
        }

        void BroadcastToolBarInfo()
        {
            var de = new DataEvent(EventType.Command, ProjectManagerEvents.ToolBar, pluginUI.TreeBar);
            EventManager.DispatchEvent(this, de);
        }

        void BroadcastProjectInfo(Project project)
        {
            var de = new DataEvent(EventType.Command, ProjectManagerEvents.Project, project);
            EventManager.DispatchEvent(this, de);
        }

        void BroadcastProjectSetUp(Project project)
        {
            var de = new DataEvent(EventType.Command, ProjectManagerEvents.ProjectSetUp, project);
            EventManager.DispatchEvent(this, de);
        }

        void BroadcastBuildComplete(IProject project)
        {
            var de = new DataEvent(EventType.Command, ProjectManagerEvents.BuildComplete, project);
            EventManager.DispatchEvent(this, de);
        }

        void BroadcastBuildFailed(IProject project)
        {
            var de = new DataEvent(EventType.Command, ProjectManagerEvents.BuildFailed, project);
            EventManager.DispatchEvent(this, de);
        }

        #endregion

        #region Project Tree Event Handling

        void MenuOpening(object sender, CancelEventArgs e)
        {
            if (Control.ModifierKeys == Keys.Control)
            {
                TreeShowShellMenu();
            }
        }

        void TreeDoubleClick()
        {
            if (pluginUI.Menu.Contains(pluginUI.Menu.Open)) TreeOpenItems();
            else if (pluginUI.Menu.Contains(pluginUI.Menu.Insert)) TreeInsertItem();
        }

        void TreeOpenItems()
        {
            foreach (var path in Tree.SelectedPaths)
            {
                openFileQueue.Enqueue(path);
            }
            OpenNextFile();
        }

        void OpenNextFile()
        {
            if (openFileQueue.Count > 0)
            {
                var file = openFileQueue.Dequeue();
                if (File.Exists(file)) OpenFile(file);
                if (file.IndexOfOrdinal("::") > 0 && File.Exists(file.Substring(0, file.IndexOfOrdinal("::")))) // virtual files
                {
                    OpenFile(file);
                }
            }
        }

        void TreeExecuteItems()
        {
            foreach (var path in Tree.SelectedPaths)
                ShellOpenFile(path);
        }

        void ShellOpenFile(string path)
        {
            if (BridgeManager.Active && BridgeManager.IsRemote(path) && !BridgeManager.AlwaysOpenLocal(path))
            {
                BridgeManager.RemoteOpen(path);
                return;
            }
            var psi = new ProcessStartInfo(path) {WorkingDirectory = Path.GetDirectoryName(path)};
            ProcessHelper.StartAsync(psi);
        }

        void TreeInsertItem()
        {
            // special behavior if this is a fake export node inside a SWF file
            var node = Tree.SelectedNode as ExportNode;
            var path = (node != null) ? node.ContainingSwfPath : Tree.SelectedPath;
            var project = Tree.ProjectOf(path) ?? Tree.ProjectOf(Tree.SelectedNode);
            if (project != null) projectActions.InsertFile(MainForm, project, path, node);
            // TODO better handling / report invalid action
        }

        void TreeAddLibraryItems()
        {
            // we want to deselect all nodes when toggling library so you can see
            // them turn blue to get some feedback
            var selectedPaths = Tree.SelectedPaths;
            var project = Tree.ProjectOf(Tree.SelectedNode);
            Tree.SelectedNodes = null;
            if (project != null) projectActions.ToggleLibraryAsset(project, selectedPaths);
            // TODO report invalid action
        }

        void TreeAlwaysCompileItems()
        {
            var project = Tree.ProjectOf(Tree.SelectedNode);
            if (project != null)
                projectActions.ToggleAlwaysCompile(project, Tree.SelectedPaths);
            // TODO report invalid action
        }

        void TreeDocumentClass()
        {
            var project = Tree.ProjectOf(Tree.SelectedNode);
            if (project != null)
                projectActions.ToggleDocumentClass(project, Tree.SelectedPaths);
            // TODO report invalid action
        }

        void TreeBrowseItem()
        {
            var de = new DataEvent(EventType.Command, "FileExplorer.Explore", Tree.SelectedPath);
            EventManager.DispatchEvent(this, de);
        }

        void TreeCutItems() => fileActions.CutToClipboard(Tree.SelectedPaths);

        void TreeCopyItems() => fileActions.CopyToClipboard(Tree.SelectedPaths);

        void TreePasteItems() => fileActions.PasteFromClipboard(Tree.SelectedPath);

        void TreeDeleteItems() => fileActions.Delete(Tree.SelectedPaths);

        void TreeLibraryOptions()
        {
            var project = Tree.ProjectOf(Tree.SelectedNode);
            if (project is null) return;
            using var dialog = new LibraryAssetDialog( /*Tree.SelectedAsset*/project.GetAsset(Tree.SelectedPath), project);
            if (dialog.ShowDialog(pluginUI) != DialogResult.OK) return;
            Tree.SelectedNode.Refresh(false);
            project.Save();
        }

        void TreeAddFileFromTemplate(string templatePath, bool noName)
        {
            var project = Tree.ProjectOf(Tree.SelectedNode);
            if (project != null)
                fileActions.AddFileFromTemplate(project, Tree.SelectedPath, templatePath, noName);
        }

        void TreeAddFolder() => fileActions.AddFolder(Tree.SelectedPath);

        void TreeAddAsset()
        {
            if (Tree.ProjectOf(Tree.SelectedPath) is { } project)
                fileActions.AddLibraryAsset(project, Tree.SelectedPath);
        }

        void TreeAddExistingFile() => fileActions.AddExistingFile(Tree.SelectedPath);

        void TreeHideItems()
        {
            if (Tree.ProjectOf(Tree.SelectedNode) is { } project)
                projectActions.ToggleHidden(project, Tree.SelectedPaths);
        }

        public void ToggleShowHidden()
        {
            var project = activeProject; // TODO apply to all projects
            projectActions.ToggleShowHidden(project);
            pluginUI.ShowHiddenPaths(project.ShowHiddenPaths);
        }

        public void TreeRefreshSelectedNode()
        {
            var de = new DataEvent(EventType.Command, ProjectManagerEvents.UserRefreshTree, Tree);
            EventManager.DispatchEvent(this, de);

            var project = activeProject; // TODO apply to all projects?
            projectActions.UpdateASCompletion(MainForm, project);

            Tree.RefreshTree();
        }

        /// <summary>
        /// Shows the command prompt
        /// </summary>
        void TreeShowCommandPrompt()
        {
            var de = new DataEvent(EventType.Command, "FileExplorer.PromptHere", Tree.SelectedPath);
            EventManager.DispatchEvent(this, de);

            if (!de.Handled)
            {
                var psi = new ProcessStartInfo();
                psi.FileName = PluginBase.MainForm.CommandPromptExecutable;
                psi.WorkingDirectory = Tree.SelectedPath;
                Process.Start(psi);
            }
        }

        /// <summary>
        /// Shows the explorer shell menu
        /// </summary>
        void TreeShowShellMenu()
        {
            string parentDir = null;
            var scm = new ShellContextMenu();
            var selectedPathsAndFiles = new List<FileInfo>();
            foreach (var path in Tree.SelectedPaths)
            {
                // only select files in the same directory
                if (parentDir is null) parentDir = Path.GetDirectoryName(path);
                else if (Path.GetDirectoryName(path) != parentDir) continue;
                selectedPathsAndFiles.Add(new FileInfo(path));
            }
            pluginUI.Menu.Hide(); /* Hide default menu */
            var location = new Point(pluginUI.Menu.Bounds.Left, pluginUI.Menu.Bounds.Top);
            scm.ShowContextMenu(selectedPathsAndFiles.ToArray(), location);
        }

        void TestBuild()
        {
            runOutput = true;
            FullBuild();
        }

        void FullBuild()
        {
            buildingAll = true;
            foreach (GenericNode node in Tree.SelectedNode.Nodes)
            {
                if (IsBuildable(node.BackingPath) && !buildQueue.Contains(node.BackingPath))
                {
                    buildQueue.Enqueue(node.BackingPath);
                }
            }
            ProcessBuildQueue();
        }

        void BackgroundBuild()
        {
            foreach (string path in Tree.SelectedPaths)
            {
                if (IsBuildable(path) && !buildQueue.Contains(path))
                {
                    buildQueue.Enqueue(path);
                }
            }
            ProcessBuildQueue();
        }

        void ProcessBuildQueue()
        {
            if (buildQueue.Count > 0)
            {
                buildTimer.Start();
            }
        }

        void OnBuildTimerTick(object sender, EventArgs e)
        {
            buildTimer.Stop();
            if (buildTimer.Tag is null)
            {
                try
                {
                    var project = ProjectLoader.Load(buildQueue.Dequeue());
                    if (project is null) return;
                    var debugging = buildingAll ? !activeProject.TraceEnabled : !project.TraceEnabled;
                    buildActions.Build(project, false, debugging);
                }
                catch (Exception ex)
                {
                    TraceManager.AddAsync(ex.Message);
                    BuildComplete(null, false);
                }
            } 
            else
            {
                buildTimer.Tag = null;
                if (runOutput) TestMovie();
                else BuildProject();
                runOutput = false;
            }
        }

        bool IsBuildable(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            return FileInspector.IsAS2Project(path, ext)
                   || FileInspector.IsAS3Project(path, ext)
                   || FileInspector.IsHaxeProject(path, ext);
        }

        void AddSourcePath()
        {
            var path = Tree.SelectedPath;
            var project = Tree.ProjectOf(path);
            if (project is null) return;
            if (path.StartsWithOrdinal(project.Directory)) path = project.GetRelativePath(path);
            if (project.Classpaths.Count == 1 && project.Classpaths[0] == ".")
                project.Classpaths.Clear();
            project.Classpaths.Add(path);
            project.Save();
            project.OnClasspathChanged();
        }

        void RemoveSourcePath()
        {
            var path = Tree.SelectedPath;
            var project = Tree.ProjectOf(path);
            if (project is null) return;
            project.Classpaths.Remove(project.GetRelativePath(path));
            if (project.Classpaths.Count == 0) project.Classpaths.Add(".");
            project.Save();
            project.OnClasspathChanged();
        }

        void CopyClassName()
        {
            string path = Tree.SelectedPath;
            DataEvent copyCP = new DataEvent(EventType.Command, "ASCompletion.GetClassPath", path);
            EventManager.DispatchEvent(this, copyCP);
            if (copyCP.Handled) // UI needs refresh on clipboard change...
            {
                PluginBase.MainForm.RefreshUI();
            }
        }

        void FindAndReplace()
        {
            var path = Tree.SelectedPath;
            if (path != null && File.Exists(path))
            {
                PluginBase.MainForm.CallCommand("FindAndReplaceFrom", path);
            }
        }

        void FindInFiles()
        {
            if (Tree.SelectedPaths is null) return;
            var paths = new List<string>(Tree.SelectedPaths);
            paths.RemoveAll(p => !Directory.Exists(p));
            if (paths.Count > 0)
            {
                var path = string.Join(";", paths);
                PluginBase.MainForm.CallCommand("FindAndReplaceInFilesFrom", path);
            }
        }

        void TreeSyncToMainFile()
        {
            if (activeProject != null && activeProject.CompileTargets.Count > 0)
            {
                Tree.Select(activeProject.GetAbsolutePath(activeProject.CompileTargets[0]));
                Tree.SelectedNode.EnsureVisible();
            }
        }

        void CollapseAll()
        {
            foreach (TreeNode rootNode in Tree.Nodes)
            {
                foreach (TreeNode node in rootNode.Nodes)
                {
                    node.Collapse(false);
                }
            }
        }

        void TreeSyncToCurrentFile()
        {
            var doc = PluginBase.MainForm.CurrentDocument;
            if (activeProject is null || doc is null || !doc.IsEditable || doc.IsUntitled) return;
            var path = doc.FileName;
            if (Tree.SelectedNode != null && Tree.SelectedNode.BackingPath == path)
            {
                Tree.SelectedNode.EnsureVisible();
                Tree.PathToSelect = null;
                return;
            }
            Tree.Select(path);
            if (Tree.SelectedNode.BackingPath == path)
            {
                Tree.SelectedNode.EnsureVisible();
                Tree.PathToSelect = null;
            }
            else
                Tree.PathToSelect = path;
        }

        void OpenResource()
        {
            if (PluginBase.CurrentProject != null)
            {
                if (projectResources is null) projectResources = new OpenResourceForm(this);
                projectResources.ShowDialog(pluginUI);
            }
        }

        #endregion
    }

    public enum ProjectManagerUIStatus
    {
        Building,
        NotBuilding,
        Disabled
    }
}