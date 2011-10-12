using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using MediaPortal.GUI.Library;
using MediaPortal.Dialogs;
using MediaPortal.Player;
using System.Text.RegularExpressions;
using System.Drawing;



namespace AirPortalDisplay
{
    public class AirPortalDisplay : GUIWindow, ISetupForm
    {
        public const string PLUGIN_NAME = "AirPortal Display";
        public const string LOG_PREFIX = "[AIRPORTAL_DISPLAY] ";
        public const int WINDOW_ID = 1514;

     /*   int xres;
        WebBrowser PlayNowWindow;
      */

        bool bplaying = false;

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
            bplaying = false;
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
            bplaying = false;
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
        private void GUIWindowManager_OnNewMessage(GUIMessage message)
        {
            Log.Debug(message.Message.ToString());
        }
        public override bool OnMessage(GUIMessage message)
        {
            
            if (message.Message == GUIMessage.MessageType.GUI_MSG_LABEL_ADD && message.Label == "video")
            {
                Log.Debug(message.Message.ToString());
                //MessageBox.Show(message.Label);
                //playvideo(message.Label2);
               
                if (g_Player.PlayVideoStream(message.Label2))
                {
                    bplaying = true;
                }
                else
                {

                    if (g_Player.Play(message.Label2, g_Player.MediaType.Video))
                    { 
                        bplaying = true; 
                    }

                }
                
                
                g_Player.ShowFullScreenWindow();
                return true;
            }
            else if (message.Message == GUIMessage.MessageType.GUI_MSG_LABEL_ADD && message.Label == "action")
            {
                if (bplaying) 
                {
                    if (message.Label2.Equals("pause"))
                    {

                        Log.Debug("pausing player");
                        g_Player.Pause();
                    }
                    else if (message.Label2.Equals("play"))
                    {
                        Log.Debug("playing player");
                        //g_Player.p
                    }
                    else if (message.Label2.Equals("stop"))
                    {
                        Log.Debug("stopping player");
                        g_Player.Stop();
                        bplaying = false;
                    }
                    else if (message.Label2.Equals("scrub"))
                    {
                        if (message.Label3.Length > 0)//if the scrub position exists in the paramters (should be first arg)...  
                        {
                        
                            //g_Player.CurrentPosition = Convert.ToDouble(message.Label3[0]);  
                            int ipercent = (int) Convert.ToDouble(message.Label3)*100;
                            g_Player.SeekAsolutePercentage(ipercent);
                        }
                    }
                }
                return true;
            }
            else
            {
                return base.OnMessage(message);
            }
                 
            
        }
        /* using a web browser to play the movie
        public void playvideo( string url)
        {
         PlayNowWindow = new WebBrowser();
            PlayNowWindow.Url = new Uri (url);
            PlayNowWindow.Name = "PlayNowWindow";
            PlayNowWindow.Size = new Size(800, 500);
            PlayNowWindow.Location = new Point(0, 145);
            GUIGraphicsContext.form.Controls.Add(PlayNowWindow);
            PlayNowWindow.Visible = true;

            PlayNowWindow.DocumentCompleted += new WebBrowserDocumentCompletedEventHandler(PlayNowWindow_DocumentCompleted);
            GUIGraphicsContext.form.Focus();


               
        }
        void PlayNowWindow_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        { }
         */
    }
}
