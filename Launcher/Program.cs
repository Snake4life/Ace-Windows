using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Windows.Forms;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using Semver;
using System.Linq;

namespace Ace
{
    static class Program
    {
        static string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ace");
        static string injectJsPath = Path.Combine(dataDir, "inject.js");
        static string bundleJsPath = Path.Combine(dataDir, "bundle.js");
        static string bundle_devJsPath = Path.Combine(dataDir, "bundle_dev.js");
        static string injectorExePath = Path.Combine(dataDir, "injector.exe");
        static string payloadDllPath = Path.Combine(dataDir, "payload.dll");
        static string currentExe = System.Reflection.Assembly.GetExecutingAssembly().Location;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
            
            string lcuPath = GetLCUPath();
            if (lcuPath == null) return;
            string exe = Path.Combine(lcuPath, "LeagueClient.exe");
            string argLine = "";

            bool dev = false;
            bool setup = false;
            foreach (string arg in args)
            {
                switch (arg)
                {
                    case "--dev":
                        dev = true;
                        goto default;

                    case "--setup":
                        setup = true;
                        break;

                    default:
                        argLine += arg + " ";
                        break;
                }
            }

            Microsoft.Win32.RegistryKey key;
            if (setup)
            {
                key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey("Software\\Microsoft\\Windows NT\\CurrentVersion\\Image File Execution Options\\LeagueClientUx.exe");
                key.SetValue("debugger", injectorExePath);
                return;
            } else
            {
                key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("Software\\Microsoft\\Windows NT\\CurrentVersion\\Image File Execution Options\\LeagueClientUx.exe");
                if (key.GetValue("debugger") == null || key.GetValue("debugger").ToString() != injectorExePath)
                {
                    DialogResult dialogResult = MessageBox.Show(
                        "Ace requires Admin access to run for the first time.",
                        "Admin access required",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Information
                    );

                    if (dialogResult == DialogResult.Cancel) return;

                    ProcessStartInfo startInfo;
                    startInfo = new ProcessStartInfo { FileName = currentExe, UseShellExecute = true, Arguments = argLine + " --setup", Verb = "runas" };
                    var process = Process.Start(startInfo);
                    process.WaitForExit();
                }
            }
            
            if (Update())
            {
                ProcessStartInfo startInfo;
                startInfo = new ProcessStartInfo { FileName = currentExe, UseShellExecute = false, Arguments = argLine };
                var process = Process.Start(startInfo);
                return;
            }

            if (CheckLCU())
            {
                DialogResult promptResult = MessageBox.Show(
                    "Ace has detected that the LCU is already running. Do you want to stop it?",
                    "Ace",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1
                );

                if (promptResult == DialogResult.Yes)
                    KillLCU();
                else
                    return;
            }

            LaunchLCU(exe, argLine, dev ? bundle_devJsPath : bundleJsPath, injectJsPath);
        }

        // Launches the LCU with the provided path, arguments and payloads.
        static void LaunchLCU(string path, string argLine, string initialPayload, string loadPayload)
        {
            File.WriteAllBytes(bundleJsPath, Properties.Resources.bundle);
            File.WriteAllBytes(bundle_devJsPath, Properties.Resources.bundle_dev);
            File.WriteAllBytes(injectJsPath, Properties.Resources.inject);
            File.WriteAllBytes(injectorExePath, Properties.Resources.injector);
            File.WriteAllBytes(payloadDllPath, Properties.Resources.payload);

            ProcessStartInfo startInfo;

            // Start LeagueClient.exe
            startInfo = new ProcessStartInfo { FileName = path, UseShellExecute = false, Arguments = argLine };
            startInfo.EnvironmentVariables["ACE_INITIAL_PAYLOAD"] = initialPayload;
            startInfo.EnvironmentVariables["ACE_LOAD_PAYLOAD"] = loadPayload;
            Process.Start(startInfo);
        }

        // Either gets the LCU path from the saved properties, or by prompting the user.
        static string GetLCUPath()
        {
            string configPath = Path.Combine(dataDir, "lcuPath");
            string path = File.Exists(configPath) ? File.ReadAllText(configPath) : "C:/Riot Games/League of Legends/";
            bool valid = IsPathValid(path);

            while (!valid)
            {
                // Notify that the path is invalid.
                MessageBox.Show(
                    "Ace could not find League of Legends. Please select your 'League of Legends' folder.",
                    "LCU not found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Exclamation
                );

                // Ask for new path.
                CommonOpenFileDialog dialog = new CommonOpenFileDialog()
                {
                    Title = "Select 'League of Legends' folder.",
                    InitialDirectory = "C:\\Riot Games\\",
                    EnsurePathExists = true,
                    IsFolderPicker = true
                };

                if (dialog.ShowDialog() == CommonFileDialogResult.Cancel)
                {
                    // User wants to cancel. Exit
                    return null;
                }

                path = dialog.FileName;
                valid = IsPathValid(path);
            }

            // Store choice so we don't have to ask for it again.
            File.WriteAllText(configPath, path);

            return path;
        }

        static bool Update()
        {
            try
            {
                if (File.Exists(currentExe + ".old"))
                {
                    File.Delete(currentExe + ".old");
                    return false;
                }

                string json = Encoding.UTF8.GetString(RequestURL("https://api.github.com/repos/zombiewizzard/ace-windows/releases").ToArray());
                JsonArray data = SimpleJson.DeserializeObject<JsonArray>(json);
                if (data.Count < 1) return false;

                JsonObject latest = (JsonObject)data[0];
                string release = (string)latest["tag_name"];
                if (release == null) return false;

                SemVersion newVer;
                //If the semver isn't valid or if we are already on the newest version.
                if (!SemVersion.TryParse(release, out newVer) || newVer <= GetBundleVersion()) return false;

                DialogResult promptResult = MessageBox.Show(
                    "Ace has detected a new version is available, would you like to update Ace?",
                    "Update",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (promptResult != DialogResult.Yes) return false;

                JsonArray assets = (JsonArray)latest["assets"];
                if (assets == null) return false;
                if (assets.Count < 1) return false;

                JsonObject asset = (JsonObject)assets.Find(x => ((string)((JsonObject)x)["name"]) == "Ace.exe");
                if (asset == null) return false;

                MemoryStream newData = RequestURL((string)asset["browser_download_url"]);
                if (newData == null) return false;

                File.Move(currentExe, currentExe + ".old");
                File.WriteAllBytes(currentExe, newData.ToArray());

                using (FileStream fileWriter = File.OpenWrite(currentExe))
                {
                    newData.CopyTo(fileWriter);
                }

                return true;
            } catch (Exception ex) {
                Console.WriteLine("error: " + ex);
                return false;
            }
        }
        
        // Tries to find the current version from the currently installed bundle.
        static string GetBundleVersion()
        {
            string bundleJsPath = Path.Combine(dataDir, "bundle.js");

            string contents = File.ReadAllText(bundleJsPath);
            Match match = Regex.Match(contents, "window\\.ACE_VERSION\\s?=\\s?\"(.*?)\"");
            return match.Groups[1].ToString();
        }

        // Makes a synchronous request to the provided URL.
        static MemoryStream RequestURL(string url)
        {
            HttpWebRequest request = (HttpWebRequest) WebRequest.Create(url);
            request.UserAgent = "Ace"; // Somehow the response is malformed if we don't send a user agent. See http://stackoverflow.com/questions/2482715/the-server-committed-a-protocol-violation-section-responsestatusline-error
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            using (WebResponse response = request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            {
                MemoryStream ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms;
            }
        }

        // Checks if there is a running LCU instance.
        static bool CheckLCU()
        {
            Process[] lcuCandidates = Process.GetProcessesByName("LeagueClient");
            lcuCandidates = lcuCandidates.Concat(Process.GetProcessesByName("LeagueClientUx")).ToArray();
            lcuCandidates = lcuCandidates.Concat(Process.GetProcessesByName("LeagueClientUxRender")).ToArray();

            return lcuCandidates.Length > 0;
        }

        // Kills the running LCU instance, if applicable.
        static void KillLCU()
        {
            Process[] lcuCandidates = Process.GetProcessesByName("LeagueClient");
            lcuCandidates = lcuCandidates.Concat(Process.GetProcessesByName("LeagueClientUx")).ToArray();
            lcuCandidates = lcuCandidates.Concat(Process.GetProcessesByName("LeagueClientUxRender")).ToArray();
            foreach (Process lcu in lcuCandidates)
            {
                lcu.Refresh();
                if (!lcu.HasExited)
                {
                    lcu.Kill();
                    lcu.WaitForExit();
                }
            }
        }

        // Checks if the provided path is most likely a path where the LCU is installed.
        static bool IsPathValid(string path)
        {
            return Directory.Exists(path) && Directory.Exists(Path.Combine(path, "RADS")) && Directory.Exists(Path.Combine(path, "RADS\\projects\\league_client"));
        }

        // Finds the newest league_client release and returns the path to that release.
        static string GetClientProjectPath(string path)
        {
            string p = Path.Combine(path, "RADS/projects/league_client/releases");
            return Directory.GetDirectories(p).Select(x => {
                try
                {
                    // Convert 0.0.0.29 to 29.
                    return new { Path = x, Version = int.Parse(Path.GetFileName(x).Replace(".", "")) };
                }
                catch (FormatException)
                {
                    // Invalid path, -1.
                    return new { Path = x, Version = -1 };
                }
            }).OrderBy(x => x.Version).Last().Path;
        }
    }
}
