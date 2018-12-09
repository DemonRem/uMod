extern alias References;

using References::Newtonsoft.Json;
using References::Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Diagnostics;
using uMod.Plugins;

namespace uMod.Libraries
{
    /// <summary>
    /// Request methods for web requests
    /// </summary>
    public enum RequestMethod
    {
        /// <summary>
        /// Requsts deletion of the specified resource
        /// </summary>
        DELETE,

        /// <summary>
        /// Requests data from the specified resource
        /// </summary>
        GET,

        /// <summary>
        /// Applies partial modifications to the specified resource
        /// </summary>
        PATCH,

        /// <summary>
        /// Submits data to be processed to the specified resource
        /// </summary>
        POST,

        /// <summary>
        /// Uploads a representation of the specified URI
        /// </summary>
        PUT,

        /// <summary>
        /// Grabs only HTTP headers and no document body
        /// </summary>
        HEAD
    };

    /// <summary>
    /// The WebRequests library
    /// </summary>
    public class WebRequests : Library
    {
        private static readonly Universal.Universal universal = Interface.uMod.GetLibrary<Universal.Universal>();

        private readonly AutoResetEvent workevent = new AutoResetEvent(false);
        private readonly Queue<WebRequest> queue = new Queue<WebRequest>();
        private readonly Thread workerthread;
        private readonly int maxCompletionPortThreads;
        private readonly int maxWorkerThreads;
        private readonly object syncRoot = new object();

        private bool shutdown;

        /// <summary>
        /// Specifies the default HTTP request timeout in seconds
        /// </summary>
        public static float DefaultTimeout = 30f;

        /// <summary>
        /// Specifies the HTTP request decompression support
        /// </summary>
        public static bool AllowDecompression = false;

        /// <summary>
        /// Represents a single WebRequest instance
        /// </summary>
        public class WebRequest
        {
            /// <summary>
            /// Gets the callback delegate
            /// </summary>
            public Action<int, string> Callback { get; }

            /// <summary>
            /// Gets the v2 callback delegate
            /// </summary>
            public Action<WebResponse> CallbackV2 { get; }

            /// <summary>
            /// Gets or sets the web request synchronicity
            /// </summary>
            public bool Async { get; set; }

            /// <summary>
            /// Gets or sets the request timeout
            /// </summary>
            public float Timeout { get; set; }

            /// <summary>
            /// Gets or sets the web request method
            /// </summary>
            public string Method { get; set; } = "GET";

            /// <summary>
            /// Gets the destination URL
            /// </summary>
            public string Url { get; }

            /// <summary>
            /// Gets or sets the request body
            /// </summary>
            public string Body { get; set; }

            /// <summary>
            /// Gets or sets the request body
            /// </summary>
            public Dictionary<string, string> Cookies { get; set; }

            /// <summary>
            /// Gets the HTTP response code
            /// </summary>
            public int ResponseCode { get; protected set; }

            /// <summary>
            /// Gets the HTTP response text
            /// </summary>
            public string ResponseText { get; protected set; }

            /// <summary>
            /// Gets the HTTP response object
            /// </summary>
            public WebResponse Response { get; protected set; }

            /// <summary>
            /// Gets the plugin to which this web request belongs, if any
            /// </summary>
            public Plugin Owner { get; protected set; }

            /// <summary>
            /// Gets or sets the web request headers
            /// </summary>
            public Dictionary<string, string> RequestHeaders { get; set; }

            private Event.Callback<Plugin, PluginManager> removedFromManager;

            /// <summary>
            /// Initializes a new instance of the WebRequest class
            /// </summary>
            /// <param name="url"></param>
            /// <param name="callback"></param>
            /// <param name="owner"></param>
            public WebRequest(string url, Action<int, string> callback, Plugin owner)
            {
                Url = url;
                Callback = callback;
                Owner = owner;
                removedFromManager = Owner?.OnRemovedFromManager.Add(owner_OnRemovedFromManager);
            }

            /// <summary>
            /// Initializes a new instance of the WebRequest class
            /// </summary>
            /// <param name="url"></param>
            /// <param name="callback"></param>
            /// <param name="owner"></param>
            public WebRequest(string url, Action<WebResponse> callback, Plugin owner)
            {
                Url = url;
                CallbackV2 = callback;
                Owner = owner;
                removedFromManager = Owner?.OnRemovedFromManager.Add(owner_OnRemovedFromManager);
            }

            /// <summary>
            /// Gets timeout and ensures it is valid
            /// </summary>
            /// <returns></returns>
            int GetTimeout()
            {
                return (int)Math.Round((Timeout.Equals(0f) ? DefaultTimeout : Timeout));
            }

            /// <summary>
            /// Creates web client process
            /// </summary>
            /// <returns></returns>
            protected Process CreateProcess()
            {
                int timeout = GetTimeout();
                IPAddress address = universal.Server.LocalAddress ?? universal.Server.Address;

                Process process = new Process();
                process.StartInfo.FileName = Path.Combine(Interface.uMod.RootDirectory, "wcp.exe");
                string decompression = AllowDecompression ? "both" : "none";
                process.StartInfo.Arguments = $"--method={Method} --url=\"{Url}\" --timeout={timeout} --address=\"{address}\" --decompression={decompression}";
                if (RequestHeaders != null)
                {
                    foreach (KeyValuePair<string, string> kvp in RequestHeaders)
                    {
                        process.StartInfo.Arguments += $" --header=\"{kvp.Key}:{kvp.Value}\"";
                    }
                }

                if(Cookies != null && Cookies.Count > 0)
                {
                    foreach (KeyValuePair<string, string> kvp in Cookies)
                    {
                        process.StartInfo.Arguments += $" --cookie=\"{kvp.Key}:{kvp.Value}\"";
                    }
                }

                if (!string.IsNullOrEmpty(Body))
                {
                    process.StartInfo.Arguments += $" --body=\"{Body}\"";
                }

                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                return process;
            }

            /// <summary>
            /// Used by the worker thread to start the request
            /// </summary>
            public void Start()
            {
                if(!Net.WebClient.IsHashValid())
                {
                    Interface.Oxide.LogError($"Secure web channel potentially compromised, cancelling web request {Url}.");

                    Interface.uMod.NextTick(() =>
                    {
                        Net.WebClient.CheckWebClientBinary();
                    });
                    return;
                }

                Process process = CreateProcess();
                process.Start();

                string response = process.StandardOutput.ReadToEnd();

                if (string.IsNullOrEmpty(response))
                {
                    string errorText = process.StandardError.ReadToEnd();

                    if (!string.IsNullOrEmpty(errorText))
                    {
                        ResponseText = errorText;
                    }
                }
                else
                {
                    JObject jsonObject = JObject.Parse(response);
                    Response = jsonObject.ToObject<WebResponse>();
                    ResponseText = Response.ReadAsString();
                }

                bool exited = process.WaitForExit(GetTimeout() * 1000);
                if (!exited)
                {
                    process.Kill();
                    OnTimeout();
                }

                if ((ResponseCode = process.ExitCode) == -1)
                {
                    string message = $"Web request produced exception (Url: {Url})";
                    if (Owner)
                    {
                        message += $" in '{Owner.Name} v{Owner.Version}' plugin";
                    }

                    message += Environment.NewLine + ResponseText;

                    Interface.Oxide.LogError(message);
                }

                OnComplete();
            }

            private void OnTimeout()
            {
                if (Owner != null)
                {
                    Event.Remove(ref removedFromManager);
                    Owner = null;
                }
            }

            private void OnComplete()
            {
                Event.Remove(ref removedFromManager);
                Interface.uMod.NextTick(() =>
                {
                    Owner?.TrackStart();
                    try
                    {
                        if (Callback != null)
                        {
                            Callback.Invoke(ResponseCode, ResponseText);
                        }

                        if(CallbackV2 != null && Response != null)
                        {
                            CallbackV2?.Invoke(Response);
                        }
                    }
                    catch (Exception ex)
                    {
                        string message = "Web request callback raised an exception";
                        if (Owner && Owner != null)
                        {
                            message += $" in '{Owner.Name} v{Owner.Version}' plugin";
                        }

                        Interface.Oxide.LogException("Web request callback raised an exception", ex);
                    }

                    Owner?.TrackEnd();
                    Owner = null;
                });
            }

            /// <summary>
            /// Called when the owner plugin was unloaded
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="manager"></param>
            private void owner_OnRemovedFromManager(Plugin sender, PluginManager manager)
            {
                
            }
        }

        /// <summary>
        /// Represents a Response from a WebRequest
        /// </summary>
        public class WebResponse : IDisposable
        {
            /// <summary>
            /// The Headers from the response
            /// </summary>
            [JsonProperty("Headers")]
            public IDictionary<string, string> Headers { get; protected set; }

            /// <summary>
            /// The Content-Length returned from the response
            /// </summary>
            [JsonProperty("ContentLength")]
            public virtual long ContentLength { get; protected set; }

            /// <summary>
            /// Content-Type set by the Content-Type Header of the response
            /// </summary>
            [JsonProperty("ContentType")]
            public string ContentType { get; protected set; }

            /// <summary>
            /// Gets the Content-Encoding from the response
            /// </summary>
            [JsonProperty("ContentEncoding")]
            public string ContentEncoding { get; protected set; }

            /// <summary>
            /// Gets the status code returned from the responding server
            /// </summary>
            [JsonProperty("StatusCode")]
            public int StatusCode { get; protected set; }

            /// <summary>
            /// Gets information on the returned status code
            /// </summary>
            [JsonProperty("StatusDescription")]
            public string StatusDescription { get; protected set; }

            /// <summary>
            /// The original method used to get this response
            /// </summary>
            [JsonProperty("Method")]
            public RequestMethod Method { get; protected set; }

            /// <summary>
            /// Gets the Uri of the responding server
            /// </summary>
            [JsonProperty("ResponseUri")]
            public Uri ResponseUri { get; protected set; }

            /// <summary>
            /// Gets the HTTP protocol version
            /// </summary>
            [JsonProperty("ProtocolVersion")]
            public VersionNumber ProtocolVersion { get; protected set; }

            /// <summary>
            /// Gets the response body
            /// </summary>
            [JsonProperty("Body")]
            public byte[] Body { get; protected set; }

            /// <summary>
            /// Reads the Response in it's raw data
            /// </summary>
            /// <returns></returns>
            public byte[] ReadAsBytes() => ContentLength != 0 ? Body : new byte[0];

            /// <summary>
            /// Reads the response as a string
            /// </summary>
            /// <param name="encoding"></param>
            /// <returns></returns>
            public string ReadAsString(Encoding encoding = null) => ContentLength != 0 ? encoding?.GetString(ReadAsBytes()) ?? Encoding.UTF8.GetString(ReadAsBytes()) : null;

            /// <summary>
            /// Converts the response string from Json to a .NET Object
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="encoding"></param>
            /// <returns></returns>
            public T ConvertFromJson<T>(Encoding encoding = null) => ContentLength != 0 ? Utility.ConvertFromJson<T>(ReadAsString(encoding)) : default(T);

            /// <summary>
            /// Safely disposes this object
            /// </summary>
            public virtual void Dispose()
            {
                Headers?.Clear();
                Headers = null;

                ContentType = null;
                ContentEncoding = null;
                ResponseUri = null;
                StatusDescription = null;
                Body = null;
            }
        }

        /// <summary>
        /// Initializes a new instance of the WebRequests library
        /// </summary>
        public WebRequests()
        {
            ThreadPool.GetMaxThreads(out maxWorkerThreads, out maxCompletionPortThreads);
            maxCompletionPortThreads = (int)(maxCompletionPortThreads * 0.6);
            maxWorkerThreads = (int)(maxWorkerThreads * 0.75);

            // Start worker thread
            workerthread = new Thread(Worker);
            workerthread.Start();
        }

        /// <summary>
        /// Shuts down the worker thread
        /// </summary>
        public override void Shutdown()
        {
            if (!shutdown)
            {
                shutdown = true;
                workevent.Set();
                Thread.Sleep(250);
                workerthread.Abort();
            }
        }

        /// <summary>
        /// The worker thread method
        /// </summary>
        private void Worker()
        {
            try
            {
                while (!shutdown)
                {
                    int workerThreads, completionPortThreads;
                    ThreadPool.GetAvailableThreads(out workerThreads, out completionPortThreads);
                    if (workerThreads <= maxWorkerThreads || completionPortThreads <= maxCompletionPortThreads)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    WebRequest request = null;
                    lock (syncRoot)
                    {
                        if (queue.Count > 0)
                        {
                            request = queue.Dequeue();
                        }
                    }

                    if (request != null)
                    {
                        request.Start();
                    }
                    else
                    {
                        workevent.WaitOne();
                    }
                }
            }
            catch (Exception ex)
            {
                Interface.uMod.LogException("WebRequests worker: ", ex);
            }
        }

        /// <summary>
        /// Enqueues a DELETE, GET, PATCH, POST, HEAD, or PUT web request
        /// </summary>
        /// <param name="url"></param>
        /// <param name="body"></param>
        /// <param name="callback"></param>
        /// <param name="owner"></param>
        /// <param name="method"></param>
        /// <param name="headers"></param>
        /// <param name="timeout"></param>
        /// <param name="_async"></param>
        [LibraryFunction("Enqueue")]
        public void Enqueue(string url, string body, Action<int, string> callback, Plugin owner, RequestMethod method = RequestMethod.GET, Dictionary<string, string> headers = null, float timeout = 0f, bool _async = true)
        {
            WebRequest request = new WebRequest(url, callback, owner) { Method = method.ToString(), RequestHeaders = headers, Timeout = timeout, Body = body, Async = _async };
            Enqueue(request);
        }

        /// <summary>
        /// Enqueues a DELETE, GET, PATCH, POST, HEAD, or PUT web request
        /// </summary>
        /// <param name="url"></param>
        /// <param name="body"></param>
        /// <param name="callback"></param>
        /// <param name="owner"></param>
        /// <param name="method"></param>
        /// <param name="headers"></param>
        /// <param name="timeout"></param>
        public void Enqueue(string url, byte[] body, Action<int, string> callback, Plugin owner, RequestMethod method = RequestMethod.GET, Dictionary<string, string> headers = null, float timeout = 0f, bool _async = true)
        {
            
            WebRequest request = new WebRequest(url, callback, owner) { Method = method.ToString(), RequestHeaders = headers, Timeout = timeout, Body = Encoding.UTF8.GetString(body), Async = _async };
            Enqueue(request);
        }

        /// <summary>
        /// Enqueues a DELETE, GET, PATCH, POST, HEAD, or PUT web request
        /// </summary>
        /// <param name="url"></param>
        /// <param name="body"></param>
        /// <param name="callback"></param>
        /// <param name="owner"></param>
        /// <param name="method"></param>
        /// <param name="headers"></param>
        /// <param name="timeout"></param>
        [LibraryFunction("EnqueueV2")]
        public void Enqueue(string url, string body, Action<WebResponse> callback, Plugin owner, RequestMethod method = RequestMethod.GET, Dictionary<string, string> headers = null, float timeout = 0f, bool _async = true)
        {
            WebRequest request = new WebRequest(url, callback, owner) { Method = method.ToString(), RequestHeaders = headers, Timeout = timeout, Body = body, Async = _async };
            Enqueue(request);
        }

        /// <summary>
        /// Enqueues a DELETE, GET, PATCH, POST, HEAD, or PUT web request
        /// </summary>
        /// <param name="url"></param>
        /// <param name="body"></param>
        /// <param name="callback"></param>
        /// <param name="owner"></param>
        /// <param name="method"></param>
        /// <param name="headers"></param>
        /// <param name="timeout"></param>
        public void Enqueue(string url, byte[] body, Action<WebResponse> callback, Plugin owner, RequestMethod method = RequestMethod.GET, Dictionary<string, string> headers = null, float timeout = 0f, bool _async = true)
        {
            WebRequest request = new WebRequest(url, callback, owner) { Method = method.ToString(), RequestHeaders = headers, Timeout = timeout, Body = Encoding.UTF8.GetString(body), Async = _async };
            Enqueue(request);
        }

        /// <summary>
        /// Enqueues a web request
        /// </summary>
        /// <param name="request"></param>
        protected void Enqueue(WebRequest request)
        {
            if (request.Async)
            {
                lock (syncRoot)
                {
                    queue.Enqueue(request);
                }
                workevent.Set();
            }
            else
            {
                request.Start();
            }
        }

        /// <summary>
        /// Returns the current queue length
        /// </summary>
        /// <returns></returns>
        [LibraryFunction("GetQueueLength")]
        public int GetQueueLength() => queue.Count;
    }
}
