using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using MediaPortal.GUI.Library;
using MediaPortal.Dialogs;
using MediaPortal.Player;
using System.Text.RegularExpressions;

namespace AirPortalDisplay
{
    public class AirPortalDisplay : GUIWindow, ISetupForm
    {
        public const string PLUGIN_NAME = "AirPortal Display";
        public const string LOG_PREFIX = "[AIRPORTAL_DISPLAY] ";
        public const int WINDOW_ID = 1514;



        public string PluginName()
        {
            return PLUGIN_NAME;
        }

        public string Description()
        {
            return "Display component for AirPortal - an Apple AirPlay client";
        }

        public string Author()
        {
            return "Shukuyen";
        }

        public void ShowPlugin()
        {
            MessageBox.Show("Not yet implemented, sorry!");
        }

        public bool CanEnable()
        {
            return true;
        }

        public int GetWindowId()
        {
            return WINDOW_ID;
        }

        public bool DefaultEnabled()
        {
            return true;
        }

        public bool HasSetup()
        {
            return false;
        }

        public bool GetHome(out string strButtonText, out string strButtonImage, out string strButtonImageFocus, out string strPictureImage)
        {
            strButtonText = String.Empty;
            strButtonImage = String.Empty;
            strButtonImageFocus = String.Empty;
            strPictureImage = String.Empty;

            return false;
        }

        public override int GetID
        {
            get
            {
              return WINDOW_ID;
            }

            set {}
        }

        // Plugin initialised, load skin file
        public override bool Init()
        {
          return Load(GUIGraphicsContext.Skin+@"\airportaldisplay.xml");
        }

        // Plugin was deactivated
        protected override void OnPageDestroy(int new_windowId)
        {
            GUIPropertyManager.SetProperty("#Airportal.Image.Available", "false");
            if (g_Player.Playing && (g_Player.IsVideo || g_Player.IsTVRecording || g_Player.IsDVD) && g_Player.Paused)
            {
                g_Player.Pause();
            }
            base.OnPageDestroy(new_windowId);
        }
    }
}
