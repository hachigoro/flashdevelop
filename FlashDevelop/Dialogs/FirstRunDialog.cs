using System;
using System.IO;
using System.Collections.Generic;
using PluginCore.Localization;
using System.ComponentModel;
using System.Windows.Forms;
using FlashDevelop.Managers;
using FlashDevelop.Helpers;
using FlashDevelop.Settings;
using PluginCore.Utilities;
using PluginCore.Managers;
using PluginCore.Helpers;
using PluginCore;

namespace FlashDevelop.Dialogs
{
    public class FirstRunDialog : Form
    {
        private FlashDevelop.Dialogs.Commands commands;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.ComponentModel.BackgroundWorker worker;
        private System.Windows.Forms.PictureBox pictureBox;
        private System.Windows.Forms.Label infoLabel;

        public FirstRunDialog()
        {
            this.Font = Globals.Settings.DefaultFont;
            this.InitializeComponent();
            this.InitializeExternals();
        }

        #region Windows Form Designer Generated Code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.infoLabel = new System.Windows.Forms.Label();
            this.progressBar = new System.Windows.Forms.ProgressBarEx();
            this.pictureBox = new System.Windows.Forms.PictureBox();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox)).BeginInit();
            this.SuspendLayout();
            // 
            // infoLabel
            //
            this.infoLabel.Anchor = (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right;
            this.infoLabel.BackColor = System.Drawing.SystemColors.Control;
            this.infoLabel.FlatStyle = System.Windows.Forms.FlatStyle.System;
            this.infoLabel.Location = new System.Drawing.Point(13, 36);
            this.infoLabel.Name = "infoLabel";
            this.infoLabel.Size = new System.Drawing.Size(361, 16);
            this.infoLabel.TabIndex = 0;
            this.infoLabel.Text = DistroConfig.DISTRIBUTION_NAME + " is initializing. Please wait...";
            this.infoLabel.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            // 
            // progressBar
            // 
            this.progressBar.Location = new System.Drawing.Point(13, 84);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(361, 14);
            this.progressBar.TabIndex = 0;
            // 
            // pictureBox
            // 
            this.pictureBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pictureBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pictureBox.Location = new System.Drawing.Point(0, 0);
            this.pictureBox.Name = "pictureBox";
            this.pictureBox.Size = new System.Drawing.Size(386, 110);
            this.pictureBox.TabIndex = 2;
            this.pictureBox.TabStop = false;
            // 
            // FirstRunDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(386, 110);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.infoLabel);
            this.Controls.Add(this.pictureBox);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.MaximumSize = new System.Drawing.Size(386, 110);
            this.MinimumSize = new System.Drawing.Size(386, 110);
            this.Name = "FirstRunDialog";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = " Initializing...";
            this.Load += this.FirstRunDialogLoad;
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        #region Methods And Event Handlers

        /// <summary>
        /// Initializes the external images and texts
        /// </summary>
        private void InitializeExternals()
        {
            this.infoLabel.Text = TextHelper.GetString("Info.Initializing"); 
        }

        /// <summary>
        /// Handles the load event
        /// </summary>
        private void FirstRunDialogLoad(object sender, EventArgs e)
        {
            this.LoadCommandsFile();
            this.worker = new BackgroundWorker();
            this.worker.WorkerReportsProgress = true;
            this.worker.WorkerSupportsCancellation = true;
            this.worker.DoWork += this.ProcessCommands;
            this.worker.ProgressChanged += this.ProgressChanged;
            this.worker.RunWorkerCompleted += this.WorkerCompleted;
            this.worker.RunWorkerAsync();
        }

        /// <summary>
        /// Forces the application to close
        /// </summary>
        private void FirstRunDialogClick(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Abort;
            this.Close();
        }

        /// <summary>
        /// Updates the progress
        /// </summary>
        private void ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            this.progressBar.Value = e.ProgressPercentage;
        }

        /// <summary>
        /// Handles the finish of the work
        /// </summary>
        private void WorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (((bool)e.Result))
            {
                if (!File.Exists(FileNameHelper.SettingData))
                {
                    SettingObject settings = SettingObject.GetDefaultSettings();
                    Globals.MainForm.AppSettings = settings;
                }
                Globals.Settings.LatestCommand = this.commands.LatestCommand;
                this.Close();
            }
            else
            {
                this.infoLabel.Text = TextHelper.GetString("Info.InitFailed");
                this.pictureBox.Click += this.FirstRunDialogClick;
                this.progressBar.Click += this.FirstRunDialogClick;
                this.infoLabel.Click += this.FirstRunDialogClick;
            }
        }

        /// <summary>
        /// Processes the specified commands
        /// </summary>
        private void ProcessCommands(object sender, DoWorkEventArgs e)
        {
            try
            {
                int count = 0;
                int total = this.commands.Entries.Count;
                foreach (Command command in this.commands.Entries)
                {
                    if (command.Number > Globals.Settings.LatestCommand)
                    {
                        string data = this.ProcessArguments(command.Data);
                        if (command.Action.ToLower() == "copy")
                        {
                            string[] args = data.Split(';');
                            if (Directory.Exists(args[0])) FolderHelper.CopyFolder(args[0], args[1]);
                            else
                            {
                                if (File.Exists(args[0]) && args.Length == 3 && args[2] == "keep-old") File.Copy(args[0], args[1], false);
                                else if (File.Exists(args[0])) File.Copy(args[0], args[1], true);
                            }
                        }
                        else if (command.Action.ToLower() == "move")
                        {
                            string[] args = data.Split(';');
                            if (Directory.Exists(args[0]))
                            {
                                FolderHelper.CopyFolder(args[0], args[1]);
                                Directory.Delete(args[0], true);
                            }
                            else if (File.Exists(args[0]))
                            {
                                File.Copy(args[0], args[1], true);
                                File.Delete(args[0]);
                            }
                        }
                        else if (command.Action.ToLower() == "delete")
                        {
                            if (Directory.Exists(data)) Directory.Delete(data, true);
                            else if (File.Exists(data)) File.Delete(data);
                        }
                        else if (command.Action.ToLower() == "syntax")
                        {
                            CleanupManager.RevertConfiguration(false);
                        }
                        else if (command.Action.ToLower() == "create")
                        {
                            Directory.CreateDirectory(data);
                        }
                        else if (command.Action.ToLower() == "appman")
                        {
                            string locale = Globals.Settings.LocaleVersion.ToString();
                            string appman = Path.Combine(PathHelper.ToolDir, "appman/AppMan.exe");
                            ProcessHelper.StartAsync(appman, "-locale=" + locale);
                        }
                    }
                    count++;
                    int percent = (100 * count) / total;
                    this.worker.ReportProgress(percent);
                }
                e.Result = true;
            }
            catch (Exception ex)
            {
                e.Result = false;
                ErrorManager.AddToLog("Init failed.", ex);
                this.worker.CancelAsync();
            }
        }

        /// <summary>
        /// Processes the default path arguments
        /// </summary>
        private void LoadCommandsFile()
        {
            this.commands = new Commands();
            string filename = Path.Combine(PathHelper.AppDir, "FirstRun.fdb");
            object obj = ObjectSerializer.Deserialize(filename, this.commands);
            this.commands = (Commands)obj;
        }

        /// <summary>
        /// Processes the default path arguments
        /// </summary>
        private string ProcessArguments(string text)
        {
            string result = text;
            if (result is null) return string.Empty;
            result = result.Replace("$(AppDir)", PathHelper.AppDir);
            result = result.Replace("$(UserAppDir)", PathHelper.UserAppDir);
            result = result.Replace("$(BaseDir)", PathHelper.BaseDir);
            return result;
        }

        /// <summary>
        /// Checks if we should process the commands
        /// </summary>
        public static bool ShouldProcessCommands()
        {
            Commands commands = new Commands();
            string filename = Path.Combine(PathHelper.AppDir, "FirstRun.fdb");
            if (File.Exists(filename))
            {
                commands = (Commands)ObjectSerializer.Deserialize(filename, commands);
                if (commands.LatestCommand > Globals.Settings.LatestCommand) return true;
                return false;
            }

            return false;
        }

        /// <summary>
        /// Shows the first run dialog
        /// </summary>
        public new static DialogResult Show()
        {
            using FirstRunDialog firstRunDialog = new FirstRunDialog();
            return firstRunDialog.ShowDialog();
        }

        #endregion

    }

    #region Command Classes

    [Serializable]
    public class Commands
    {
        public int LatestCommand = 0;
        public List<Command> Entries = new List<Command>();
    }

    [Serializable]
    public class Command
    {
        public int Number;
        public string Action;
        public string Data;

        public Command(){}
        public Command(int number, string action, string data)
        {
            this.Number = number;
            this.Action = action;
            this.Data = data;
        }

    }

    #endregion

}