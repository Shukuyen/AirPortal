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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Drawing;
using System.IO;
using MediaPortal.Player;

namespace AirPortal
{
    /// <remarks>
    /// Class that deals with the communication between the application and the device. Loads up separate threads to deal with concurrent communication from the device. 
    /// iOS devices seem to open two connections to the server which remain open and do not close after every request (it seems):
    /// - The first connection starts by sending the "POST /reverse HTTP/1.1" header, indicating this connection will be for two way communication. The application can send requests to the iOS device (which it will reply to), and the iOS device can send requests to the application (which the application will need to reply to). This connection seems to be used for the iOS device to send playback requests to the application (such as "play this URL", "seek to this position", "pause" or "stop), and also for the application to send playback events to the iOS device (such as "the file is loading", "playback has started", "playback is paused" etc. The events are sent as XML/plist files)
    /// - The second connection seems to be used for one way communication. "GET /scrub HTTP/1.1" requests are sent from the iOS device to get the current playback position from the application, to update the track progress on the iOS device. It seems that when a file has finished playing this connection is closed by the iOS device, and reopened when needed. Unlike the first connection which seems to constantly remain open.
    /// </remarks>
    class Server
    {
        int port { get; set; }
        TcpListener tcpListener;
        Thread listenerThread;

        NetworkStream twoWayStream = null; //this is where the two way stream will be stored (use it to send data back to the iOS device). The first stream is the twowaystream, this is used for comms like requesting to play/pause/stop, then for the server to send back events like loading/playing/paused/stop. The second stream is one way and seems to be only used for GET requests to see what position the video is at (the GET /scrub HTTP/1.1 requests)
        Dictionary<int, TcpClient> activeConnections = null; //this will store the active connections, ready to be closed when the application is closed. The key will be the thread ID, and the value will be the TcpClient.

        /// <summary>
        /// Constructor for the Server class. Creates everything needed to make the server, but does not start the server.
        /// </summary>
        /// <param name="port">The port that the server should listen on. Normally this will be 7000, but it can be overriden if desired</param>
        public Server(int port = 7000)
        {
            this.port = port;


            //create a network socket
            this.tcpListener = new TcpListener(IPAddress.Any, this.port);

            //create a new thread
            this.listenerThread = new Thread(new ThreadStart(ListenForClients));
            listenerThread.IsBackground = true;

            //create the dictionary to store the active connections
            activeConnections = new Dictionary<int, TcpClient>();
        }

        /// <summary>
        /// Starts the server listening for connections
        /// </summary>
        public void Start()
        {
            this.listenerThread.Start();
        }

        /// <summary>
        /// Closes all the active connections and stops listening
        /// </summary>
        public void Stop()
        {
            AirPortal.LogMessage("Server stopping", AirPortal.LogType.Debug);

            //close all the active connections
            foreach (TcpClient theClient in activeConnections.Values)
            {
                if (theClient.Connected)
                {
                    theClient.Close();
                }
            }
            tcpListener.Stop();
        }


        /// <summary>
        /// This method is called by the thread that's set up in the constructor and started in Start()
        /// It will constantly loop, listening for connections
        /// On receiving a connection, it will spawn a new thread to deal with the connection (and pass that on to HandleClientComm() to deal with), and continue listening for more connections
        /// </summary>
        private void ListenForClients()
        {
            //start the TCP socket
            try
            {
                this.tcpListener.Start();
            }
            catch (SocketException e)
            {
                AirPortal.LogMessage("Could not start server, the message given was: " + e.Message, AirPortal.LogType.Error);
                this.Stop();
                return;
            }

            //debug log to say server started
           AirPortal.LogMessage("Server started", AirPortal.LogType.Debug);

            //listen for connections
            AirPortal.LogMessage("Waiting for client...", AirPortal.LogType.Debug);
            tcpListener.BeginAcceptTcpClient(new AsyncCallback(DoAcceptTcpClientCallback), tcpListener);
        }


        /// <summary>
        /// Accepts the client connection asyncronously
        /// TODO: make the read operation asyncronous too
        /// </summary>
        /// <param name="ar"></param>
        private void DoAcceptTcpClientCallback(IAsyncResult ar)
        {
            //when you get into this method, a client must have connected.

            try
            {
                // Get the listener that handles the client request.
                TcpListener listener = (TcpListener)ar.AsyncState;
                //get the client
                TcpClient client = listener.EndAcceptTcpClient(ar);

                //deal with the connection.
                AirPortal.LogMessage("Client connected on port " + client.Client.RemoteEndPoint.ToString(), AirPortal.LogType.Debug);
                //create a new thread to deal with the new connection
                Thread clientThread = new Thread(new ParameterizedThreadStart(HandleClientComm)); //this thread is parameterized, which will allow us to call the HandleClientComm() methid with a parameter
                clientThread.IsBackground = true;
                clientThread.Start(client); //calls HandleClientComm(client) in the new thread.

                //add the client to the active connections, indexed by the thread it's running in.
                activeConnections.Add(clientThread.ManagedThreadId, client);

                //listen for another connection
                tcpListener.BeginAcceptTcpClient(new AsyncCallback(DoAcceptTcpClientCallback), tcpListener);
            }
            catch (ObjectDisposedException)
            {
                AirPortal.LogMessage("Accepting client cancelled due to object disposed exception", AirPortal.LogType.Debug);
            }

        }


        /// <summary>
        /// Handles an individual client connection, as received from ListenForClients()
        /// Gets the TcpClient object (client), and for as long as there is data available in the client's network stream it will call readStream() to read the data and deal with it.
        /// </summary>
        /// <param name="client">The client that connected, should be a TcpClient object. Will fail otherwise.</param>
        private void HandleClientComm(object client)
        {
            //we know that client must be of type tcpClient, but couldnt know this in the method signature (as it was called by parameterizedthreadstart), but in our implementation it will always be a TcpClient
            TcpClient tcpClient = (TcpClient)client;

            Thread.CurrentThread.Name = tcpClient.Client.RemoteEndPoint.ToString();

            //get the client stream to read data from.
            NetworkStream clientStream = tcpClient.GetStream();


            readStream(tcpClient, clientStream);

            if (tcpClient.Connected)
            {
                AirPortal.LogMessage(tcpClient.Client.RemoteEndPoint.ToString() + " connection closing", AirPortal.LogType.Debug);
                tcpClient.Close();
            }

            //the client was closed, so remove it from the active connections
            activeConnections.Remove(Thread.CurrentThread.ManagedThreadId);
        }


        /// <summary>
        /// Reads as much data from a TcpClient and NetworkStream as is available
        /// Extracts it into an array of HTTP messages (requests or responses), and passes each individual message to handleMessageReceived() as well as firing a clientConnected() event
        /// </summary>
        /// <param name="tcpClient">The client that the stream should be read from</param>
        /// <param name="clientStream">The client's NetworkStream</param>
        private void readStream(TcpClient tcpClient, NetworkStream clientStream)
        {
            AirPortal.LogMessage(Thread.CurrentThread.Name + " entered readStream method", AirPortal.LogType.Debug);
            //begin getting the message that the client sent
            if (tcpClient.Connected && clientStream.CanRead)
            {
                List<byte> rawData = new List<byte>(); //raw data is to be used for photos (as the string caused problms). NOTE its not safe to use for the other requests, as for videos it may be affected by the problem of multiple requests being received at once. The string is split by a regex to deal with this, but the byte array isn't!
                byte[] myReadBuffer = new byte[1024];
                StringBuilder myCompleteMessage = new StringBuilder();
                int numberOfBytesRead = 0;

                //incoming message might be bigger than the buffer
                do
                {
                    try
                    {
                        numberOfBytesRead = clientStream.Read(myReadBuffer, 0, myReadBuffer.Length);
                        myCompleteMessage.Append(Encoding.ASCII.GetString(myReadBuffer, 0, numberOfBytesRead));
                        rawData.AddRange(myReadBuffer.Take(numberOfBytesRead));
                        Thread.Sleep(10);//let the iOS device catch up sending data
                    }
                    catch (System.IO.IOException) { }
                } while (tcpClient.Connected && clientStream.DataAvailable); //check if it's connected before checking for data available, as maybe the program might get quit and the sockets closed halfway through a read

                //make sure the socket is still connected (if it closed half way through we don't really care about the message any more, and want to ignore it because we can't send any replies!)
                if (tcpClient.Connected)
                {
                    //make the string object (instead of StringBuilder object)
                    string message = myCompleteMessage.ToString();
                    AirPortal.LogMessage(Thread.CurrentThread.Name + " received message", AirPortal.LogType.Debug);

                    //IPAddress
                    string IPAddress = tcpClient.Client.RemoteEndPoint.ToString();
                    string[] temp = IPAddress.Split(':');
                    IPAddress = temp[0];


                    //because of the persistent connection we might receive more than one request at a time if the time between each was particularly short. 
                    //find matches in the string where "HTTP(chars)", "GET(space)(chars)" or "POST(space)(chars)" is the beginning of the line (should be the only two ways a request can start?) to split up the requests and deal with each one.
                    Regex r = new Regex("^HTTP|^GET [.]*|^POST [.]*|^PUT [.]*", RegexOptions.Multiline);
                    MatchCollection m = r.Matches(message);

                    //each match is a new http request (begins with GET or POST), split the string into a substring starting at the match point, and ending at the next match's start point (or the end of the string if there are no more matches)
                    string[] requests = new string[m.Count];
                    for (int i = 0; i < m.Count; i++)
                    {
                        if (i + 1 < m.Count)
                        {
                            requests[i] = message.Substring(m[i].Index, m[i + 1].Index - m[i].Index); //there is another match after this, so work out how long this substring should be by getting the next match's start point minus the current match's start point. This gives you the substring length. Then supply the current match's start point and the calculated length.
                        }
                        else
                        {
                            requests[i] = message.Substring(m[i].Index); //no more matches after this, just continue on for the rest of the string.
                        }
                    }

                    //requests now contains all of the requests separately, go through and handle each one.
                    foreach (string theMessage in requests)
                    {
                        //send an event to say the client connected and sent a message, the view can then deal with this.
                        clientConnected(this, theMessage);
                        handleMessageReceived(clientStream, theMessage, rawData.ToArray());
                    }

                    //read again to get the next message and keep the connection open! The iOS device doesnt issue each request on separate connections, it keeps the connection alive.
                    if (numberOfBytesRead != 0)
                    {
                        AirPortal.LogMessage(Thread.CurrentThread.Name + " is Listening again", AirPortal.LogType.Debug);
                        readStream(tcpClient, clientStream);
                    }
                }
            }
        }


        /// <summary>
        /// Handles an individual HTTP message
        /// Does the required action if it is a request, and sends the required response
        /// If the message is a response from the iOS device it does nothing
        /// </summary>
        /// <param name="clientStream">The stream that the message came from</param>
        /// <param name="message">The message text</param>
        private void handleMessageReceived(NetworkStream clientStream, string message, byte[] rawData)
        {
            message = message.Trim();
            if (message.StartsWith("POST /reverse HTTP/1.1")) //initial opening message, declares this connection to the the one that will be two-way
            {
                twoWayStream = clientStream; //this is the two way stream for comms use in future. Store it so we can use it for sending playback events later. If we try to use any other connection's stream, the iOS device will ignore it or refuse the connection.
                string response = "HTTP/1.1 101 Switching Protocols\r\n" +
                                  "Date: " + String.Format("{0:R}", DateTime.Now) + "\r\n" +
                                  "Upgrade: PTTH/1.0\r\n" +
                                  "Connection: Upgrade\r\n" +
                                  "\r\n";
                sendMessage(clientStream, response);
                return;
            }
            if (message.StartsWith("POST /scrub?position=")) //seek.
            {
                //regex to get position
                Regex regex = new Regex(@"POST /scrub\?position=([0-9\.]+) HTTP/1.1", RegexOptions.Multiline);
                Match match = regex.Match(message);

                if (match.Success)
                {
                    //scrub to position
                    string pos = match.Groups[1].ToString();
                    playbackEvent(this, "scrub", pos);
                }
                //reply with ok message
                sendHTTPOKMessage(clientStream);
                return;
            }
            if (message.StartsWith("POST /play")) //play.
            {
                //get the url out of the message and play it here
                //URL is immediately after "Content-Location: "
                string url;
                string[] array = Regex.Split(message, "Content-Location: ");
                if (array.Count() > 1)
                {
                    //split by URL and Start-location
                    array = Regex.Split(array[1], "Start-Position: ");
                    if (array.Count() > 1)
                    {
                        url = array[0].Trim();
                        AirPortal.LogMessage("Attempting to play URL" + url, AirPortal.LogType.Debug);

                        //get the start position
                        double start = 0;
                        Match m = Regex.Match(array[1].Trim(), @"(\d*\.?\d*)"); //sometimes some random stuff comes after the number, just want the number
                        if (m.Success)
                        {
                            try
                            {
                                start = Convert.ToDouble(m.Value);
                            }
                            catch (FormatException) { }
                        }


                        playURL(this, url, start);
                    }
                }
                //reply with postion message
                sendHTTPOKMessage(clientStream);
                return;
            }
            if (message.StartsWith("GET /scrub HTTP/1.1")) //this is a request from the iOS device to ask the application how far along the playback is so it can update its progress bar
            {
                //get the current duration
                int duration = (int)g_Player.Duration;
                int position = (int)g_Player.CurrentPosition;

                //get the response data string
                string responsedata = String.Format("duration: {0:0.000000}\nposition: {1:0.000000}", duration, position);

                //get the content length and add one for the newline at the end
                int contentLength = responsedata.Length + 1;

                //send the current playback position status
                string response = "HTTP/1.1 200 OK\r\n" +
                  "Date: " + String.Format("{0:R}", DateTime.Now) + "\r\n" +
                  "Content-Length: " + contentLength + "\r\n\r\n" +
                  responsedata + "" +
                  "\n";
                sendMessage(clientStream, response);
                return;
            }
            if (message.StartsWith("POST /rate?value=0.000000")) //this is how the iOS device requests the video should be paused
            {
                //pause
                playbackEvent(this, "pause", "");
                sendHTTPOKMessage(clientStream);
                return;
            }
            if (message.StartsWith("POST /rate?value=1.000000")) //this is how the iOS device requests the video should be played
            {
                //play
                playbackEvent(this, "play", "");
                sendHTTPOKMessage(clientStream);
                return;
            }
            if (message.StartsWith("POST /stop HTTP/1.1")) //stop
            {
                //stop the playback
                playbackEvent(this, "stop", "");
                sendHTTPOKMessage(clientStream);
                return;
            }
            if (message.StartsWith("PUT /photo HTTP/1.1")) //photo
            {
                //get the position in the rawdata where the image starts
                int index = message.IndexOf("\r\n\r\n");
                index += 4; //the four \r\n\r\n characters
                MemoryStream ms = new MemoryStream(rawData.Skip(index).ToArray());
                Image returnImage = Image.FromStream(ms);
                playImage(this, returnImage);

            }
            if (message.StartsWith("POST /authorize HTTP/1.1")) //attempt to play a DRM track from the device (probably from ipod app, not youtube). Not currently supported as I don't know how to pass the key to quicktime! If anyone knows how to pass it to the quicktime control, LMK.
            {
                authorisationRequest();//notify the GUI that a DRM track was requested
                playbackEvent(this, "stop");//we can't deal with the video for now, for the above reason.
                sendHTTPOKMessage(clientStream);
                return;
            }
            if (message.StartsWith("GET") || message.StartsWith("POST")) //unknown
            {
                //still a request of some sort (not a reply to a message sent to device), so reply with ok.
                sendHTTPOKMessage(clientStream);
                return;
            }
        }


        /// <summary>
        /// Sends a general HTTP 200 OK response message across the NetworkStream
        /// </summary>
        /// <param name="clientStream">The stream to send the response message down</param>
        private void sendHTTPOKMessage(NetworkStream clientStream)
        {
            //reply with ok message
            string response = "HTTP/1.1 200 OK\r\n" +
                              "Date: " + String.Format("{0:R}", DateTime.Now) + "\r\n" +
                              "Content-Length: 0\r\n" +
                              "\n";
            sendMessage(clientStream, response);
        }

        /// <summary>
        /// Sends a message across the NetworkStream
        /// </summary>
        /// <param name="clientStream">The stream to send the message down</param>
        /// <param name="message">The message to send</param>
        public void sendMessage(NetworkStream clientStream, string message)
        {
            byte[] buffer = new ASCIIEncoding().GetBytes(message);
            try
            {
                clientStream.Write(buffer, 0, buffer.Length);
                clientStream.Flush();
                messageSent(this, message);
            }
            catch (System.IO.IOException e)
            {
                AirPortal.LogMessage("IOException: " + e.Message, AirPortal.LogType.Warn);
            }
        }

        /// <summary>
        /// Sends a playback status message (eg "paused" "playing" "loading") to the iOS device.
        /// The iOS device seems to use XML pLists for to send the key of "state" and the value of the playback status.
        /// This method requires that the iOS device has already instatiated a connection and designated it as the two-way stream by sending the "POST /reverse HTTP/1.1..." message, meaning the twoWayStream class variable has been set. If it hasn't this method will fail.
        /// </summary>
        /// <param name="status"></param>
        public void sendStatusMessage(string status)
        {
            Debug.WriteLine("Sending status {0}", status);
            string content = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
                            + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
                            + "<plist version=\"1.0\">\n"
                            + "<dict>\n"
                            + "\t<key>state</key>\n"
                            + "\t<string>" + status + "</string>\n"
                            + "</dict>\n"
                            + "</plist>\n";
            int length = content.Length + 1;
            string message = "POST /event HTTP/1.1\r\n"
                            + "Content-Type: application/x-apple-plist\r\n"
                            + "Content-Length: " + length + "\r\n\r\n"
                            + content
                            + "\r\n";
            if (twoWayStream != null)
            {
                sendMessage(twoWayStream, message);
            }
        }

        /// <summary>
        /// Delegate for when a client connects.
        /// Is used by the clientConnected event.
        /// </summary>
        /// <param name="sender">The object that sent the event (usually "this")</param>
        /// <param name="message">The message that was sent from the connecting device (will be a HTTP request/response)</param>
        public delegate void clientConnectedHandler(object sender, string message);

        /// <summary>
        /// Anyone who wants to watch this event will make a method that conforms to the clientConnectedHandler delgate, and gets called when this event is triggered.
        /// Is triggered when a message is received from the client.
        /// In this application it's used for the debug screen, so we can watch what messages were received over the network by adding a method that conforms to the delegate, and printing the "message" string to the GUI's message box.
        /// </summary>
        public event clientConnectedHandler clientConnected;

        /// <summary>
        /// Delegate for the messageSent event, is called when a message is sent from the application to the iOS device.
        /// </summary>
        /// <param name="sender">The object that sent the event (usually "this")</param>
        /// <param name="message">The message that was sent from the application to the device.</param>
        public delegate void messageSentHandler(object sender, string message);
        /// <summary>
        /// Is called when the Server sends a message to the connected device.
        /// </summary>
        public event messageSentHandler messageSent;

        /// <summary>
        /// Delegate for the event when a url is sent from the client to be played on the server
        /// </summary>
        /// <param name="sender">The object that sent the event (usually "this")</param>
        /// <param name="url">The URL to be played</param>
        /// <param name="position">The position (between 0 and 1, where 0.5 is half way through the track) to start playback from</param>
        public delegate void urlPlayMessageHandler(object sender, string url, double position);
        /// <summary>
        /// This event is triggered when the iOS device sends a request that a URL should be played (when a video is started)
        /// </summary>
        public event urlPlayMessageHandler playURL;

        /// <summary>
        /// A delegate for the event when some sort of playback event is requested from the iOS device.
        /// For example, the device requesta that the application pause playback, stop playback, or seek to a certain position.
        /// </summary>
        /// <param name="sender">The object that sent the event (usually "this")</param>
        /// <param name="action">The action, eg "play", "pause", "stop", "seek"</param>
        /// <param name="param">If any extra information is needed to go with the action, it can be passed here. This is optional. Eg for "play" "pause" or "stop", no extra data is required, but for "seek" the desired seek position will be included here.</param>
        public delegate void playbackMessageHandler(object sender, string action, string param = "");
        /// <summary>
        /// An event for when some sort of playback request has been requested, e.g pause/play/stop
        /// </summary>
        public event playbackMessageHandler playbackEvent;


        /// <summary>
        /// Delegate for the event when an image is received to play
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="theImage">The Image file to play/display</param>
        public delegate void imageMessageHandler(object sender, System.Drawing.Image theImage);
        /// <summary>
        /// An event for when an image is received to play
        /// </summary>
        public event imageMessageHandler playImage;

        /// <summary>
        /// Delegate for the authorisation request event
        /// </summary>
        public delegate void authorisationRequestHandler();
        /// <summary>
        /// An event for when an authorisation key is supplied from the iDevice to the app, probably when playing a DRM video from the ipod app
        /// </summary>
        public event authorisationRequestHandler authorisationRequest;


    }
}
