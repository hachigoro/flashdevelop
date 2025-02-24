using System.Windows.Forms;
using CodeRefactor.Controls;
using PluginCore;

namespace CodeRefactor.Provider
{
    public static class UserInterfaceManager
    {
        private static ProgressDialog progressDialog;

        /// <summary>
        /// 
        /// </summary>
        private static Form Main => PluginBase.MainForm as Form;

        /// <summary>
        /// 
        /// </summary>
        public static ProgressDialog ProgressDialog
        {
            get
            {
                if (progressDialog is null)
                {
                    progressDialog = new ProgressDialog();
                    Main.AddOwnedForm(progressDialog);
                }
                return progressDialog;
            }
        }

    }

}