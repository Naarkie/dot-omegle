/*Copyright (C) 2011 Naarkie (naarkie@gmail.com)

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace dotOmegle
{
    /// <summary>
    /// Allows a user to interact with the Omegle website
    /// </summary>
    public class Omegle
    {
        /// <summary>
        /// Array of servers currently online on the Omegle service. Discussions are NSFW.
        /// </summary>
        /// <value>
        /// Sub-domains of each server.
        /// </value>
        public string[] serverList;

        /// <summary>
        /// Array of servers currently online on the Omegle service. 
        /// </summary>
        /// <value>
        /// Sub-domains of each server.
        /// </value>
        public string[] antiNudeServerList;

        public List<string> Interests = new List<string>();

        protected Timer updateTimer;

        /// <summary>
        /// Raised when a message from a stranger is received.
        /// </summary>
        public event MessageReceivedEvent MessageReceived;

        /// <summary>
        /// Raised when the Stranger disconnects.
        /// </summary>
        public event EventHandler StrangerDisconnected;

        /// <summary>
        /// Raised when the stranger is typing a message.
        /// </summary>
        public event EventHandler StrangerTyping;

        /// <summary>
        /// Raised a stranger is connected.
        /// </summary>
        public event EventHandler Connected;

        /// <summary>
        /// Raised when the stranger stops typing a message.
        /// </summary>
        public event EventHandler StrangerStoppedTyping;

        /// <summary>
        /// Raised when a user count message is received.
        /// </summary>
        public event EventHandler Count;

        /// <summary>
        /// Raised when the background event processor encounters an exception.
        /// </summary>
        public event WebExceptionEvent WebException;

        /// <summary>
        /// Raised when an unknown or unhandled event is received.
        /// </summary>
        public event UnhandledResponseEvent UnhandledResponse;

        /// <summary>
        /// Raised when the server asks for a captcha response.
        /// </summary>
        public event CaptchaRequiredEvent CaptchaRequired;

        /// <summary>
        /// Raised when the server refuses a captcha.
        /// </summary>
        public event EventHandler CaptchaRefused;

        /// <summary>
        /// Raised when the application is still looking for a partner to connect to.
        /// </summary>
        public event EventHandler WaitingForPartner;

        /// <summary>
        /// Raised when Omegle returns a set of interest tags the stranger has in common with you.
        /// </summary>
        public event SharedInterestsFoundEvent SharedInterestsFound;

        /// <summary>
        /// The applications stranger Id.
        /// </summary>
        /// <value>
        /// The user's id.
        /// </value>
        public string Id { get; protected set; }

        /// <summary>
        /// How long (in milliseconds) should we wait between updates?
        /// </summary>
        /// <value>
        /// The check interval.
        /// </value>
        public int checkInterval { get; set; }

        /// <summary>
        /// What server to connect to?
        /// </summary>
        /// <value>
        /// Catp server.
        /// </value>
        public string Server { get; set; }

        /// <summary>
        /// Which language to use?
        /// </summary>
        /// <value>
        /// en, nl, de, etc.
        /// </value>
        public string Language { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="Omegle"/> throws 
        /// exceptions or passes them through the <see cref="WebException"/> event handler. 
        /// (except for exceptions from <see cref="Listen()"/>, which are always passed through.)
        /// </summary>
        /// <value><c>true</c> if throws; otherwise, <c>false</c>.</value>
        public bool Throws { get; set; }

        /// <summary>
        /// Event processor status.
        /// </summary>
        public enum status
        {
            Stopped,
            Started
        }

        /// <summary>
        /// Gets the current status.
        /// </summary>
        /// <value>
        /// The status.
        /// </value>
        public status Status { get; protected set; }

        /// <summary>
        /// Gets a boolean specifying whether or not there's an active connection.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance is connected; otherwise, <c>false</c>.
        /// </value>
        public bool IsConnected { get; protected set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Omegle"/> class.
        /// </summary>
        public Omegle()
        {
            GetServers();
            Id = null;
            checkInterval = 1000;
            updateTimer = new Timer(TimerCallBack);
            Status = status.Stopped;
            IsConnected = false;
            Throws = true;
            Server = serverList[0];
        }

        /// <summary>
        /// Connects to the Omegle network and starts processing events.
        /// </summary>
        public void Connect()
        {
            GetID();
            Start();
            IsConnected = true;
        }

        /// <summary>
        /// Starts event processing.
        /// </summary>
        public void Start()
        {
            Status = status.Started;
            updateTimer.Change(0, Timeout.Infinite);
        }

        /// <summary>
        /// Stops event processing.
        /// </summary>
        public void Stop()
        {
            Status = status.Stopped;
            updateTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Disconnects from the server.
        /// </summary>
        public void Disconnect()
        {
            Stop();
            SendDisconnect();
            Id = null;
            IsConnected = false;
        }

        /// <summary>
        /// Reconnects and begins a new conversation.
        /// </summary>
        public void Reconnect()
        {
            Disconnect();
            Connect();
        }

        /// <summary>
        /// Gets a new Id from the Omegle service.
        /// </summary>
        public void GetID()
        {
            PostSubmitter sendPost = new PostSubmitter();
            sendPost.Url = String.Format("http://{0}.omegle.com/start?rcs=1{1}{2}", Server, 
                this.Interests.Count > 0    ? "&topics="    + GetTopicPostString() : "", // Adding topics outside of the URL doesn't work 
                this.Language != null       ? "&lang="      + this.Language : "");
            sendPost.Type = PostSubmitter.PostTypeEnum.Post;

            if (!Throws)
                sendPost.WebExceptionEvent += WebException;

            Id = sendPost.Post();
            Id = Id.Trim('"'); //gets rid of " at the start and end
        }

        /// <summary>
        /// Gets the servers that are currently online.
        /// </summary>
        private void GetServers()
        {
            PostSubmitter sendPost = new PostSubmitter();
            sendPost.Url = String.Format("http://omegle.com/status");
            sendPost.Type = PostSubmitter.PostTypeEnum.Get;

            var strStatus = sendPost.Post();
            var dictStatus = JsonConvert.DeserializeObject<dynamic>(strStatus);
            var listServers = new List<String>();
            var listAntiNudeServers = new List<String>();

            foreach (String server in dictStatus.servers)
                listServers.Add(server.Split('.')[0]);

            foreach (String server in dictStatus.antinudeservers)
                listAntiNudeServers.Add(server.Split('.')[0]);

            serverList = listServers.ToArray<String>();
            antiNudeServerList = listAntiNudeServers.ToArray<String>();
        }

        private string GetTopicPostString()
        {
            int numTopics = this.Interests.Count;
            var sbTopics = new StringBuilder();
            for (int i = 0; i < numTopics; i++)
            {
                sbTopics.AppendFormat("\"{0}\"", this.Interests[i]);
                if (i < numTopics - 1)
                {
                    sbTopics.Append(",");
                }
            }
            return "[" + sbTopics.ToString() + "]";
        }

        /// <summary>
        /// Sends a message to the connected stranger.
        /// </summary>
        /// <param name="message">The message to be sent</param>
        /// <returns>
        /// The stranger's response
        /// </returns>
        public string SendMessage(string message)
        {
            message = Uri.EscapeDataString(message); //URL escape it first

            return SendMessageRaw(message);
        }

        /// <summary>
        /// Sends a message to the connected stranger. This version does not html-encode the message.
        /// </summary>
        /// <param name="message">The message to be sent</param>
        /// <returns></returns>
        public string SendMessageRaw(string message)
        {
            //Send Message format: [url]http://bajor.omegle.com/send?id=Id&msg=MSG[/url]

            PostSubmitter sendPost = new PostSubmitter();
            sendPost.Url = String.Format("http://{0}.omegle.com/send", Server);
            sendPost.PostItems.Add("id", Id);
            sendPost.PostItems.Add("msg", message);
            sendPost.Type = PostSubmitter.PostTypeEnum.Post;

            if (!Throws)
                sendPost.WebExceptionEvent += WebException;

            return sendPost.Post();
        }

        /// <summary>
        /// Sends a captcha response to the Server.
        /// </summary>
        /// <param name="challenge">The challenge value</param>
        /// <param name="response">The response string</param>
        /// <returns></returns>
        public string SendCaptcha(string challenge, string response)
        {
            PostSubmitter sendPost = new PostSubmitter();
            sendPost.Url = String.Format("http://{0}.omegle.com/recaptcha", Server);
            sendPost.PostItems.Add("id", Id);
            sendPost.PostItems.Add("challenge", challenge);
            sendPost.PostItems.Add("response", response);
            sendPost.Type = PostSubmitter.PostTypeEnum.Post;

            if (!Throws)
                sendPost.WebExceptionEvent += WebException;

            return sendPost.Post();
        }

        /// <summary>
        /// Notifies the Server that the user has started typing.
        /// </summary>
        public void StartTyping()
        {
            PostSubmitter sendPost = new PostSubmitter();
            sendPost.Url = String.Format("http://{0}.omegle.com/typing", Server);
            sendPost.PostItems.Add("id", Id);
            sendPost.Type = PostSubmitter.PostTypeEnum.Post;
            


            if (!Throws)
                sendPost.WebExceptionEvent += WebException;

            sendPost.Post();
        }

        /// <summary>
        /// Notifies the Server that the user has finished typing.
        /// </summary>
        public void StopTyping()
        {
            PostSubmitter sendPost = new PostSubmitter();
            sendPost.Url = String.Format("http://{0}.omegle.com/stoppedtyping", Server);
            sendPost.PostItems.Add("id", Id);
            sendPost.Type = PostSubmitter.PostTypeEnum.Post;

            if (!Throws)
                sendPost.WebExceptionEvent += WebException;

            sendPost.Post();
        }

        /// <summary>
        /// Sends a message from the specified Id.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="ownID">The ID.</param>
        /// <returns></returns>
        public string SendMessageAsID(string message, string ownID)
        {
            //This method could potentially be used to send messages from another user.
            //One would have to acquire said users Id first.
            //TODO: Find a way to get a strangers Id
            message = Uri.EscapeDataString(message);

            PostSubmitter sendPost = new PostSubmitter();
            sendPost.Url = String.Format("http://{0}.omegle.com/send", Server);
            sendPost.PostItems.Add("id", ownID);
            sendPost.PostItems.Add("msg", message);
            sendPost.Type = PostSubmitter.PostTypeEnum.Post;

            if (!Throws)
                sendPost.WebExceptionEvent += WebException;

            return sendPost.Post();
        }

        /// <summary>
        /// Sends a disconnect message to the server, ending the conversation.
        /// </summary>
        /// <returns></returns>
        public string SendDisconnect()
        {
            PostSubmitter sendPost = new PostSubmitter();
            sendPost.Url = String.Format("http://{0}.omegle.com/disconnect", Server);
            sendPost.PostItems.Add("id", Id);
            sendPost.Type = PostSubmitter.PostTypeEnum.Post;

            if (!Throws)
                sendPost.WebExceptionEvent += WebException;

            return sendPost.Post();
        }

        /// <summary>
        /// Internal event parsing.
        /// </summary>
        /// <param name="response">The response.</param>
        private void Parse(string response)
        {
            if (response == null) return;
            JArray events;
            try
            {
                events = JsonConvert.DeserializeObject<JArray>(response);
            }
            catch
            {
                return;
            }

            if (events != null)
            {
                foreach (JToken ev in events)
                {
                    string event_ = ev[0].ToString();
                    switch (event_)
                    {
                        //we need to prefix and suffix each one with a literal " character
                        case "connected":
                            IsConnected = true;
                            if (this.Connected != null)
                                this.Connected(this, new EventArgs());
                            break;
                        case "strangerDisconnected":
                            {
                                if (this.StrangerDisconnected != null)
                                    this.StrangerDisconnected(this, new EventArgs());

                                break;
                            }
                        case "gotMessage":
                            if (this.MessageReceived != null)
                            {
                                this.MessageReceived(this, new MessageReceivedArgs(ev[1].ToString().TrimStart('"').TrimEnd('"')));
                            }
                            break;
                        case "waiting":
                            if (this.WaitingForPartner != null)
                                this.WaitingForPartner(this, new EventArgs());
                            break;
                        case "typing":
                            if (this.StrangerTyping != null)
                                this.StrangerTyping(this, new EventArgs());
                            break;
                        case "stoppedTyping":
                            if (this.StrangerStoppedTyping != null)
                                this.StrangerStoppedTyping(this, new EventArgs());
                            break;
                        case "count":
                            if (this.Count != null)
                                this.Count(this, new EventArgs()); //I'm a cheapskate, ev[1] holds user count though.
                            break;
                        case "recaptchaRequired":
                            if (this.CaptchaRequired != null)
                                this.CaptchaRequired(this, new CaptchaRequiredArgs(ev[1].ToString()));
                            break;
                        case "recaptchaRejected":
                            if (this.CaptchaRefused != null)
                                this.CaptchaRefused(this, new EventArgs());
                            break;
                        case "commonLikes":
                            if (this.SharedInterestsFound != null)
                                this.SharedInterestsFound(this, new SharedInterestEventArgs(ev[1].Select<JToken, string>((t) => t.ToString()).ToArray()));
                            break;
                        case "suggestSpyee":
                        case "error": // should probably handle this one
                        case "spyMessage":
                        case "spyTyping":
                        case "spyStoppedTyping":
                        case "spyDisconnected":
                        case "question":
                        default:
                            if (this.UnhandledResponse != null)
                                this.UnhandledResponse(this, new UnhandledResponseEventArgs(ev.ToString()));
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Checks the Server for a new response.
        /// </summary>
        private void Listen()
        {
            PostSubmitter eventlisten = new PostSubmitter();
            eventlisten.Url = String.Format("http://{0}.omegle.com/events", Server);
            eventlisten.PostItems.Add("id", Id);
            eventlisten.Type = PostSubmitter.PostTypeEnum.Post;

            eventlisten.WebExceptionEvent += WebException;

            Parse(eventlisten.Post());
        }

        /// <summary>
        /// Internal timer callback used to process events on a different thread.
        /// </summary>
        /// <param name="info">Unused</param>
        protected void TimerCallBack(object info)
        {
            updateTimer.Change(Timeout.Infinite, Timeout.Infinite);

            Listen();

            if (Status == status.Started)
                updateTimer.Change(checkInterval, checkInterval);
        }
    }
}