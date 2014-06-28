/*
   Based on airstream-media-player (http://code.google.com/p/airstream-media-player)
   Copyright (C) 2011 Tom Thorpe

   This program is free software; you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation; either version 2 of the License, or (at your option) any later version.

   This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details. You should have received a copy of the GNU General Public License along with this program; if not, write to the Free Software Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MediaPortal.GUI.Library;
using ZeroconfService;
using System.Diagnostics;
using System.Drawing;
using MediaPortal.Player;
using System.IO;
using System.Drawing.Imaging;

using MediaPortal.Dialogs;



namespace AirPortal
{
    public class AirPortal : ISetupForm, IPlugin
    {
        public const string PLUGIN_NAME = "AirPortal";
        public const string LOG_PREFIX = "[AIRPORTAL] ";
        public const int DISPLAY_WINDOW_ID = 1514;

        private const string domain = "local";
        private const string type = "_airplay._tcp";
        private string name = AirPortal.GetServiceName();
        private const int port = 7000;

        bool publishing = false;
        NetService publishService = null;
        Server theServer = null;

    

        /// <summary>
        /// Mediaportal log type
        /// </summary>
        public enum LogType 
        {
            Debug,
            Info,
            Warn,
            Error
        }

        #region ISetupForm
        public string PluginName()
        {
            return PLUGIN_NAME;
        }

        public string Description()
        {
            return "AirPlay receiver - Plays audio, video and photos streamed from iOS";
        }

        public string Author()
        {
            return "Shukuyen";
        }

        public void ShowPlugin()
        {
            // Show setup form
        }

        public bool CanEnable()
        {
            return true;
        }

        public int GetWindowId()
        {
            //return WINDOW_ID;
            return -1;
        }

        public bool DefaultEnabled()
        {
            return true;
        }

        public bool HasSetup()
        {
            return true;
        }

        public bool GetHome(out string strButtonText, out string strButtonImage, out string strButtonImageFocus, out string strPictureImage)
        {
            strButtonText = null;
            strButtonImage = null;
            strButtonImageFocus = null;
            strPictureImage = null;
            return false;
        }


        #endregion

        #region IPlugin
        public void Start()
        {
            LogMessage("AirPortal starting up ...", LogType.Debug);

            //check if Bonjour is installed and exit app if it isn't
            if (checkBonjourInstalled())
            {
                //start the server to receive incoming connections from iOS devices
                theServer = new Server(port);
                theServer.Start();

                //add the delegate to do something when a client connects to the server
                theServer.clientConnected += new Server.clientConnectedHandler(theServer_clientConnected);

                //add the delegate to do something when the server sends a message to the client
                theServer.messageSent += new Server.messageSentHandler(theServer_messageSent);

                //add the delegate to do something when the client sends a play url request
                theServer.playURL += new Server.urlPlayMessageHandler(theServer_playURL);

                //add the delegate to do something when a playback event is received
                theServer.playbackEvent += new Server.playbackMessageHandler(theServer_playbackEvent);

                //add the delegate to do something when play image event is received
                theServer.playImage += new Server.imageMessageHandler(theServer_playImage);

                //add the delegate to do something when authorisation key is received from server
                theServer.authorisationRequest += new Server.authorisationRequestHandler(theServer_authorisationRequest);

                //start publishing the airplay service over Bonjour.
                DoPublish();
            }
        }

        public void Stop()
        {
            StopPublish();
            if (theServer != null)
            {
                theServer.Stop();
            }
        }
        #endregion

        #region AirPortal private methods

        private string getImageExtension(Image i)
        {
            try
            {
                foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageDecoders())
                {
                    if (codec.FormatID == i.RawFormat.Guid)
                    {
                        String extension = codec.FilenameExtension.Split(';')[0].ToLower();
                        extension = extension.Remove(0,1);
                        return extension;
                    }
                }
            }
            catch (Exception) { }

            return ".jpg";
        }

        private bool checkBonjourInstalled()
        {
            Version bonjourVersion = null;

            //check if Bonjour is installed by attempting to print it's version
            try
            {
                bonjourVersion = NetService.DaemonVersion;
                LogMessage(String.Format("Bonjour Version: {0}", NetService.DaemonVersion), LogType.Debug);
            }
            catch (Exception ex)
            {
                String message = ex is DNSServiceException ? "Could not find Bonjour. Do you have it installed?\nIf not, please download and install it from the Apple website.\nhttp://support.apple.com/kb/DL999" : ex.Message; //if you got an exception when you tried to print the version, Bonjour is not installed. Or you might get some other exception here, so show that too
                LogMessage(message, LogType.Error);
                return false;
            }

            //it seems sometimes the version still returns even when bonjour isn't installed, but returns version 0, so check that too.
            if (bonjourVersion == null || bonjourVersion.MajorRevision == 0)
            {
                LogMessage("Could not find Bonjour. Do you have it installed?\nIf not, please download and install it from the Apple website.\nhttp://support.apple.com/kb/DL999", LogType.Error);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Is called when the service is successfully published. 
        /// Conforms to the delegate specified by NetService's DidPublishService event.
        //  Writes a line to the Debug and changes the publishing variable value to true
        /// </summary>
        /// <param name="service"></param>
        private void publishService_DidPublishService(NetService service)
        {
            LogMessage("Published Bonjour Service: domain(" + service.Domain + ") type(" + service.Type + ") name(" + service.Name + ")", LogType.Debug);
            publishing = true;
        }

        /// <summary>
        /// Is called when the service attempted to be published but couldnt be for some reason.
        /// Conforms to the delegate specified by NetService's DidNotPublishService event.
        /// Displays error message and quits the application.
        /// </summary>
        /// <param name="service">The NetService that failed to successfully publish</param>
        /// <param name="exception">The exception that occured</param>
        private void publishService_DidNotPublishService(NetService service, DNSServiceException exception)
        {
            LogMessage(String.Format("A DNSServiceException occured: {0}", exception.Message), LogType.Error);
            Stop();
        }

        /// <summary>
        /// Stops publishing the airplay service over Bonjour.
        /// </summary>
        private void StopPublish()
        {
            //if the publish service is set up, stop it.
            if (publishService != null)
            {
                publishService.Stop();
                LogMessage("Stopped publishing", LogType.Debug);
            }

            publishing = false;
        }
        #endregion


        #region AirPortal public methods
                /// <summary>
        /// Log a message to the mediaportal log
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="type">Type of log (debug, info, error ...)</param>
        public static void LogMessage(string message, LogType type)
        {
            switch (type)
            {
                case LogType.Debug:
                    Log.Debug(String.Format("{0} {1}", LOG_PREFIX, message));
                    break;

                case LogType.Info:
                    Log.Info(String.Format("{0} {1}", LOG_PREFIX, message));
                    break;

                case LogType.Warn:
                    Log.Warn(String.Format("{0} {1}", LOG_PREFIX, message));
                    break;

                case LogType.Error:
                    Log.Error(String.Format("{0} {1}", LOG_PREFIX, message));
                    break;
            }
        }

        /// <summary>
        /// Get the machine name or a fallback
        /// </summary>
        /// <returns></returns>
        public static string GetServiceName()
        {
            try
            {
                return System.Environment.MachineName;
            }
            catch (InvalidOperationException)
            {
                return "MediaPortal AirPortal";
            }
        }


        /// <summary>
        /// Starts publishing the airplay service over Bonjour so that iOS devices can find it.
        /// This only advertises the service, Bonjour doesn't deal with the connections themselves. The connections are dealt with in the Server class.
        /// </summary>
        private void DoPublish()
        {
            publishService = new NetService(domain, type, name, port);

            // add delegates for success/false
            publishService.DidPublishService += publishService_DidPublishService;
            publishService.DidNotPublishService += publishService_DidNotPublishService;

            // add txtrecord, which gives details of the service. For now we'll just put the version number
            System.Collections.Hashtable dict = new System.Collections.Hashtable();
            dict.Add("txtvers", "1");
            publishService.TXTRecordData = NetService.DataFromTXTRecordDictionary(dict);

            publishService.Publish();
        }
        #endregion


        #region Server delegate
        /// <summary>
        /// Called when the Server instance sends a request for the application to play a URL.
        /// Conforms to the urlPlayMessageHandler delegate
        /// Sends the URL to the player object, and requests that the Server send the "loading" status message to the iOS device.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="url"></param>
        /// <param name="position">The position in the file to start from, a double between 0 and 1, where 0 is the beginning, 1 is the end, and 0.5 is halfway through the file. If a number not in this range is given, it will be ignored.</param>
        void theServer_playURL(object sender, string url, double position)
        {
            if (position < 0 || position > 1)
            {
                position = 0;
            }

            if (GUIWindowManager.ActiveWindow != DISPLAY_WINDOW_ID)
            {
                

                GUIGraphicsContext.ResetLastActivity();
                GUIMessage msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_GOTO_WINDOW, 0, 0, 0, DISPLAY_WINDOW_ID, 0, null);
                GUIWindowManager.SendThreadMessage(msg);
            }
            GUIMessage msg1 = new GUIMessage();
            msg1.Label = "video";
            msg1.Label2 = url;
            msg1.Label3 = position.ToString();
            msg1.SendToTargetWindow = true;
            msg1.TargetWindowId = DISPLAY_WINDOW_ID;
            msg1.Message = GUIMessage.MessageType.GUI_MSG_LABEL_ADD;

            GUIWindowManager.SendThreadMessage(msg1);
            //g_Player.Play(url, g_Player.MediaType.Video);
            AirPortal.LogMessage("AirPortal wants to display an video ...", LogType.Debug);
            //sendPlaybackEvent("playUrl", url, position.ToString());
            theServer.sendStatusMessage("loading"); 
        }
        /// <summary>
        /// Conforms to the delegate that the Server uses to sent the playImage event.
        /// Calls the setPictureBoxPicture() method to set the image in a thread-safe way. setPictureBoxPicture() also hides the video players and shows the picturebox
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="theImage">The image to display</param>
        void theServer_playImage(object sender, Image theImage)
        {
            AirPortal.LogMessage("AirPortal wants to display an image ...", LogType.Debug);

           

            // Create temp folder if it doesn't exist
            if (!Directory.Exists(Path.GetTempPath() + PLUGIN_NAME))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetTempPath() + PLUGIN_NAME);
                }
                catch (Exception e)
                {
                    AirPortal.LogMessage("Could not create temp path: " + e.Message, LogType.Error);
                    return;
                }
            }

            String tempFileName = String.Empty;

            try
            {
                tempFileName = Path.GetTempPath() + PLUGIN_NAME + Path.DirectorySeparatorChar +  Path.GetRandomFileName() + getImageExtension(theImage);
                AirPortal.LogMessage(tempFileName, LogType.Debug);
                theImage.Save(tempFileName, theImage.RawFormat);
            }
            catch (Exception e)
            {
                AirPortal.LogMessage("Error writing image to temp file: " + e.Message, LogType.Error);
                return;
            }

            GUIPropertyManager.SetProperty("#Airportal.Image.Available", "true");
            GUIPropertyManager.SetProperty("#Airportal.Image.Filename", tempFileName);
            GUIPropertyManager.SetProperty("#Airportal.Image.Width", theImage.Width.ToString());
            GUIPropertyManager.SetProperty("#Airportal.Image.Height", theImage.Height.ToString());
            
            // Activate AirPortalDisplay plugin
            if (GUIWindowManager.ActiveWindow != DISPLAY_WINDOW_ID)
            {
                // Pause player if playing video
                if (g_Player.Playing && (g_Player.IsVideo || g_Player.IsTVRecording || g_Player.IsDVD) && !g_Player.Paused)
                {
                    g_Player.Pause();
                }

                GUIGraphicsContext.ResetLastActivity();
                GUIMessage msg = new GUIMessage(GUIMessage.MessageType.GUI_MSG_GOTO_WINDOW, 0, 0, 0, DISPLAY_WINDOW_ID, 0, null);
                GUIWindowManager.SendThreadMessage(msg);
            }
            /*else{
                GUIMessage msg = new GUIMessage();
                msg.Label = tempFileName;
                msg.SendToTargetWindow = true;
                msg.TargetWindowId = DISPLAY_WINDOW_ID;
                msg.Message = GUIMessage.MessageType.GUI_MSG_LABEL_ADD;
               
                GUIWindowManager.SendThreadMessage(msg);
                }
             */
            
        }

        /// <summary>
        /// Is called when the Server instance receives a playback event from the iOS device (such as "play", "pause", "seek" etc).
        /// Conforms to the playbackMessageHandler delegate
        /// This could in theory be called from any random thread, as it will be triggered by the Server. So it does nothing but forward the data on to the sendPlaybackEvent() method, which will perform the action on the player object in a thread-safe way.
        /// </summary>
        /// <param name="sender">The object that sent the event</param>
        /// <param name="action">The action to perform</param>
        /// <param name="param">Any extra data that might go with that action (eg the seek position)</param>
        void theServer_playbackEvent(object sender, string action, string param)
        {
            AirPortal.LogMessage("Action: " + action + " Param: " + param, LogType.Debug);
            //sendPlaybackEvent(action, param); //pass it on to the thread-safe method.
            GUIMessage msg1 = new GUIMessage();
            msg1.Label = "action";
            msg1.Label2 = action;
            msg1.Label3 = param;
            msg1.SendToTargetWindow = true;
            msg1.TargetWindowId = DISPLAY_WINDOW_ID;
            msg1.Message = GUIMessage.MessageType.GUI_MSG_LABEL_ADD;

            GUIWindowManager.SendThreadMessage(msg1);
        }

        /// <summary>
        /// Is called when the Server instance receives a message from a client.
        /// Conforms to the clientConnectedHandler delegate
        /// Constructs a message to write to the debug box saying the message was received and what data was in it, then calls appendToMessagesBox() to write this info to the debug box in a thread-safe way (as theServer_clientConnected() will be called from one of the Server threads, not the GUI thread)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="message"></param>
        private void theServer_clientConnected(object sender, string message)
        {
            String text = "";
            text += "New message received.\r\n";
            text += "Data:\r\n";
            text += message;
            text += "\r\n---------------------------------------------------------------\r\n";
            LogMessage((text.Length > 100) ? text.Substring(0, 100) : text, LogType.Debug);
        }

        /// <summary>
        /// Is called when the Server instance sends a message to the client.
        /// Conforms to the MessageSentHandler delegate
        /// Constructs a message to write to the debug box saying the message was sent and what data was in it, then calls appendToMessagesBox() to write this info to the debug box in a thread-safe way (as theServer_messageSent() will be called from one of the Server threads, not the GUI thread)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="message"></param>
        void theServer_messageSent(object sender, string message)
        {
            String text = "";
            text += "Message sent to client.\r\n";
            text += "Data:\r\n";
            text += message;
            text += "\r\n---------------------------------------------------------------\r\n";
            LogMessage(text, LogType.Debug);
        }

        /// <summary>
        /// Displays a message box saying that DRM videos are not currently supported
        /// </summary>
        void theServer_authorisationRequest()
        {
            LogMessage("Sorry, DRM video playback from the iPod app is not currently supported", LogType.Info);
            GUIDialogNotify dlgNotify = (GUIDialogNotify)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_NOTIFY);
            if (dlgNotify != null)
            {
                dlgNotify.SetHeading("Sorry ...");
                dlgNotify.SetText("Playback of DRM protected material is not possible with AirPortal at the moment.");
                dlgNotify.TimeOut = 5000;
                dlgNotify.DoModal(GUIWindowManager.ActiveWindow);
            }
        }

        #endregion

    }
}
