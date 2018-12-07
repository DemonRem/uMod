extern alias References;

using System;
using System.IO;
using System.Net;

namespace uMod.Libraries.Net
{
    internal class WebClient
    {
        public static string FileName = "wcp.exe";
        public static string BinaryPath;
        private static int downloadRetries;
        internal static string validHash = "0";

        public static void CheckWebClientBinary()
        {
            BinaryPath = Path.Combine(Interface.uMod.RootDirectory, FileName);
            UpdateCheck();
        }

        private static void UpdateCheck()
        {
            try
            {
                // Create the web request
                WebRequest request = WebRequest.Create($"https://nyc3.digitaloceanspaces.com/umod-01/{FileName}");

                string filePath = Path.Combine(Interface.uMod.RootDirectory, FileName);
                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                HttpStatusCode statusCode = response.StatusCode;
                if (statusCode != HttpStatusCode.OK)
                {
                    Interface.uMod.LogWarning($"Status code from download location was not okay (code {statusCode})");
                }

                string remoteHash = response.Headers[HttpResponseHeader.ETag].Trim('"');
                string localHash = File.Exists(filePath) ? Utility.GetHash(filePath, Utilities.Algorithms.MD5) : "0";
                Interface.uMod.LogInfo($"Latest web client MD5: {remoteHash}");
                Interface.uMod.LogInfo($"Local web client MD5: {localHash}");
                if (remoteHash != localHash)
                {
                    Interface.uMod.LogInfo("Web client hashes did not match, downloading latest");
                    DownloadWebClient(response, remoteHash);
                } else
                {
                    validHash = localHash;
                }
            }
            catch (Exception ex)
            {
                Interface.uMod.LogError($"Couldn't check for update to {FileName}");
                Interface.uMod.LogError(ex.ToString());
            }
        }

        private static void DownloadWebClient(WebResponse response, string remoteHash)
        {
            try
            {
                Interface.uMod.LogInfo($"Downloading {FileName} for secure web channel protocols");

                Stream stream = response.GetResponseStream();
                FileStream fs = new FileStream(FileName, FileMode.Create, FileAccess.Write, FileShare.None);
                int bufferSize = 10000;
                byte[] buffer = new byte[bufferSize];

                while (true)
                {
                    int result = stream.Read(buffer, 0, bufferSize);
                    if (result == -1 || result == 0)
                    {
                        break;
                    }

                    fs.Write(buffer, 0, result);
                }
                fs.Flush();
                fs.Close();
                stream.Close();
                response.Close();

                if (downloadRetries >= 3)
                {
                    Interface.uMod.LogInfo($"Couldn't download {FileName}! Please download manually from: https://github.com/theumod/Compiler/releases/download/latest/{FileName}");
                    return;
                }

                string localHash = File.Exists(BinaryPath) ? Utility.GetHash(BinaryPath, Utilities.Algorithms.MD5) : "0";
                if (remoteHash != localHash)
                {
                    Interface.uMod.LogInfo($"Local hash did not match remote hash for {FileName}, attempting download again");
                    UpdateCheck();
                    downloadRetries++;
                    return;
                }

                validHash = localHash;

                Interface.uMod.LogInfo($"Download of {FileName} completed successfully");
            }
            catch (Exception ex)
            {
                Interface.uMod.LogError($"Couldn't download {FileName}! Please download manually from: https://github.com/theumod/Compiler/releases/download/latest/{FileName}");
                Interface.uMod.LogError(ex.Message);
            }
        }

        internal static bool IsHashValid()
        {
            string localHash = File.Exists(BinaryPath) ? Utility.GetHash(BinaryPath, Utilities.Algorithms.MD5) : "0";
            return localHash == validHash;
        }
    }
}
