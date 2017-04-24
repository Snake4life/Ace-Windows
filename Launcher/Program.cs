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
        private static readonly string LauncherVersion = "2.0.0";

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
                if (key == null || key.GetValue("debugger") == null || key.GetValue("debugger").ToString() != injectorExePath)
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
                DialogResult dialogResult = MessageBox.Show(
                    "Ace has detected that the LCU is already running. Do you want to stop it?",
                    "Ace",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1
                );

                if (dialogResult == DialogResult.Yes)
                    KillLCU();
                else
                    return;
            }

            SemVersion bundleVer;
            if (!File.Exists(bundleJsPath) ||
                !SemVersion.TryParse(GetBundleVersion(Encoding.UTF8.GetString(Properties.Resources.bundle)), out bundleVer) ||
                GetBundleVersion(File.ReadAllText(bundleJsPath)) < bundleVer)
            {
                File.WriteAllBytes(bundleJsPath, Properties.Resources.bundle);
            }

            File.WriteAllBytes(bundle_devJsPath, Properties.Resources.bundle_dev);
            File.WriteAllBytes(injectJsPath, Properties.Resources.inject);
            File.WriteAllBytes(injectorExePath, Properties.Resources.injector);
            File.WriteAllBytes(payloadDllPath, Properties.Resources.payload);

            LaunchLCU(exe, argLine, dev ? bundle_devJsPath : bundleJsPath, injectJsPath);
        }

        // Launches the LCU with the provided path, arguments and payloads.
        static void LaunchLCU(string path, string argLine, string initialPayload, string loadPayload)
        {
            // Start LeagueClient.exe
            ProcessStartInfo startInfo = new ProcessStartInfo { FileName = path, UseShellExecute = false, Arguments = argLine };
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
                string[][] updaters = new string[][]{
                    //          { "GitHub Repo Name", "GitHub Asset Name", "File Path", "Local Version" }
                    new string[]{ "ace-windows", "Ace.exe", currentExe, LauncherVersion },
                    new string[]{ "ace", "bundle.js", bundleJsPath, File.Exists(bundleJsPath) ? GetBundleVersion(File.ReadAllText(bundleJsPath)) : "0.0.0" },
                };

                foreach (string[] updater in updaters)
                {
                    // Delete old file if it exists.
                    if (File.Exists(updater[2])) File.Delete(updater[2]);

                    string json = Encoding.UTF8.GetString(RequestURL($"https://api.github.com/repos/zombiewizzard/{updater[0]}/releases").ToArray());
                    JsonArray data = SimpleJson.DeserializeObject<JsonArray>(json);
                    if (data.Count < 1) continue;

                    JsonObject latest = (JsonObject)data[0];
                    string release = (string)latest["tag_name"];
                    if (release == null) continue;

                    SemVersion newVer;
                    // If the semver isn't vaalid or if we are already on the newsest version
                    if (!SemVersion.TryParse(release, out newVer) || newVer <= updater[3]) continue;

                    DialogResult dialogResult = MessageBox.Show(
                        "Ace has detected an update is available, would you like to install it now?",
                        "Update downloaded",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    );

                    // If user doesn't want to install update don't check for any more updates.
                    if (dialogResult == DialogResult.No) return false;

                    JsonArray assets = (JsonArray)latest["assets"];
                    if (assets == null) continue;
                    if (assets.Count < 1) continue;

                    JsonObject asset = (JsonObject)assets.Find(x => ((string)((JsonObject)x)["name"]) == updater[1]);
                    if (asset == null) continue;

                    MemoryStream newData = RequestURL((string)asset["browser_download_url"]);
                    if (newData == null) continue;

                    File.Move(updater[2], $"{updater[2]}.old");
                    using (FileStream fileWriter = File.OpenWrite(updater[2])) newData.CopyTo(fileWriter);

                    return true;
                }

                // No updates could be found.
                return false;
            } catch (Exception ex) {
                Console.WriteLine("Error: " + ex);
                return false;
            }
        }
        
        // Tries to find the current version from the provided bundle contents.
        static string GetBundleVersion(string contents)
        {
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
    }
}
