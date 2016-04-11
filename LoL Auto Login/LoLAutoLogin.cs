﻿using System;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;
using System.IO;
using WindowsInput;
using WindowsInput.Native;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text;

// TODO: fix sloppy code (make more modular)
// TODO: fix the derpy bug that sometimes causes the launcher/client not to focus correctly

namespace LoLAutoLogin
{

    public partial class LoLAutoLogin : Form
    {

        const int patcherTimeout = 30000;
        const int launchTimeout = 30000;
        const int clientTimeout = 30000;
        const int passwordTimeout = 30000;

        public LoLAutoLogin()
        {

            InitializeComponent();

            // create notification icon context menu (so user can exit if program hangs)
            ContextMenu menu = new ContextMenu();
            MenuItem item = new MenuItem("&Exit", (sender, e) => Application.Exit());
            menu.MenuItems.Add(item);
            notifyIcon.ContextMenu = menu;

            // set accept button (will be activated when 'enter' key is pressed)
            this.AcceptButton = saveButton;

        }
        
        private void Form1_Load(object sender, EventArgs e)
        {

            Log.Info("Started LoL Auto Login v{0}", Assembly.GetEntryAssembly().GetName().Version);

            if(CheckLocation())
            {

                if(PasswordExists())
                    RunPatcher();
                else
                    Log.Info("Password file not found, prompting user to enter password...");

            }
            
        }

        private bool CheckLocation()
        {

            // check if program is in same directory as league of legends
            if (!File.Exists("lol.launcher.exe"))
            {

                Log.Fatal("\"lol.launcher.exe\" not found!");

                // show error message
                MessageBox.Show(this, "Please place LoL Auto Login in your League of Legends directory (beside the \"lol.launcher.exe\" file).", "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);

                // hide form so it doesn't flash on screen
                this.Opacity = 0.0F;

                // exit application
                Application.Exit();

                // return so no other commands are executed
                return false;

            }

            return true;

        }

        private bool PasswordExists()
        {

            if (File.Exists("password"))
            {

                using (StreamReader reader = new StreamReader("password"))
                {

                    if (Regex.IsMatch(reader.ReadToEnd(), @"^[a-zA-Z0-9\+\/]*={0,3}$"))
                    {

                        Log.Info("Password is old format, prompting user to enter password again...");
                        MessageBox.Show("Password encryption has been changed to DPAPI, a more secure encryption than the previously used AES. You will be prompted to enter your password once again.", "LoL Auto Login - Encryption method changed to DPAPI", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    }
                    else
                    {

                        return true;

                    }

                }

            }

            return false;

        }

        private bool CheckLeagueRunning()
        {

            if(Process.GetProcessesByName("LolClient").Length > 0 || Process.GetProcessesByName("LoLLauncher").Length > 0 || Process.GetProcessesByName("LoLPatcher").Length > 0)
            {

                Log.Warn("League of Legends is already running!");

                // prompt user to kill current league of legends process
                if (MessageBox.Show(this, "Another instance of League of Legends is currently running. Would you like to close it?", "League of Legends is already running!", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
                {

                    Log.Info("Attempting to kill all League of Legends instances...");

                    // kill all league of legends processes
                    KillProcessesByName("LolClient");
                    KillProcessesByName("LoLLauncher");
                    KillProcessesByName("LoLPatcher");

                    while (GetSingleWindowFromSize("LOLPATCHER", "LoL Patcher", 800, 600) != IntPtr.Zero)
                        Thread.Sleep(500);

                    return false;

                }
                else
                {

                    // exit if user says no
                    Application.Exit();
                    return true;

                }

            }

            return false;

        }

        private void RunPatcher()
        {
            
            // hide this window
            this.Hide();

            // start launch process
            Log.Info("Password file found!");

            // check if league of legends is already running
            if (!CheckLeagueRunning())
            {

                Log.Debug("Attempting to start thread...");

                Thread t = new Thread(PatcherLaunch);

                this.FormClosing += (s, args) =>
                {
                    if (t != null && t.IsAlive)
                    {
                        t.Abort();
                    }
                };

                t.IsBackground = true;
                t.Start();

            }

        }

        private bool StartClient()
        {

            // try launching league of legends
            try
            {

                Process.Start("lol.launcher.exe");

                return true;

            }
            catch (Exception ex)
            {

                // print error to log and show balloon tip to inform user of fatal error
                Log.Fatal("Could not start League of Legends!");
                Log.PrintStackTrace(ex.StackTrace);

                this.Invoke(new Action(() => {
                    notifyIcon.ShowBalloonTip(2500, "LoL Auto Login was unable to start League of Legends. Please check your logs for more information.", "LoL Auto Login has encountered a fatal error", ToolTipIcon.Error);
                }));

                // exit application
                Application.Exit();
                return false;

            }

        }

        private void PatcherLaunch()
        {

            if(StartClient())
            {

                // log
                Log.Info("Waiting {0} ms for League of Legends Patcher...", patcherTimeout);

                // create stopwatch for loading timeout
                Stopwatch patchersw = new Stopwatch();
                patchersw.Start();

                IntPtr patcherHwnd = IntPtr.Zero;

                // search for the patcher window for 30 seconds
                while (patchersw.ElapsedMilliseconds < patcherTimeout && (patcherHwnd = GetSingleWindowFromSize("LOLPATCHER", "LoL Patcher", 800, 600)) == IntPtr.Zero)
                    Thread.Sleep(500);

                // check if patcher window was found
                if (patcherHwnd != IntPtr.Zero)
                {

                    // get patcher rectangle (window pos and size)
                    RECT patcherRect;
                    NativeMethods.GetWindowRect(patcherHwnd, out patcherRect);

                    Log.Info("Found patcher after {0} ms {{Handle={1}, Rectangle={2}}}", patchersw.Elapsed.TotalMilliseconds, patcherHwnd, patcherRect);

                    // reset stopwatch so it restarts for launch button search
                    patchersw.Reset();
                    patchersw.Start();

                    Log.Info("Waiting for Launch button to enable...");

                    bool clicked = false;
                    bool sleepMode = false;

                    // check if the "Launch" button is there and can be clicked
                    while (!clicked && patcherHwnd != IntPtr.Zero)
                    {
                        
                        // get patcher image
                        Bitmap patcherImage = new Bitmap(ScreenCapture.CaptureWindow(patcherHwnd));

                        // check if the launch button is enabled
                        if (Pixels.LaunchButton.Match(patcherImage))
                        {

                            // get patcher rectangle and make patcher go to top
                            NativeMethods.GetWindowRect(patcherHwnd, out patcherRect);
                            NativeMethods.SetForegroundWindow(patcherHwnd);

                            Log.Info("Found Launch button after {0} ms. Initiating click.", patchersw.Elapsed.TotalMilliseconds);

                            // use new input simulator instance to click on "Launch" button.
                            InputSimulator sim = new InputSimulator();
                            sim.Mouse.LeftButtonUp();
                            Cursor.Position = new Point(patcherRect.Left + (int)(patcherRect.Width * 0.5), patcherRect.Top + (int)(patcherRect.Height * 0.025));
                            sim.Mouse.LeftButtonClick();

                            clicked = true;

                            patchersw.Stop();

                            EnterPassword();

                        }

                        // dispose of image
                        patcherImage.Dispose();

                        // force garbage collection
                        GC.Collect();

                        if(!sleepMode && patchersw.ElapsedMilliseconds >launchTimeout)
                        {

                            Log.Info("Launch button not enabling; going into sleep mode.");

                            sleepMode = true;

                        }

                        if(sleepMode)
                            Thread.Sleep(2000);
                        else
                            Thread.Sleep(500);

                        patcherHwnd = GetSingleWindowFromSize("LOLPATCHER", "LoL Patcher", 800, 600);

                    }

                }
                else
                {

                    // print error to log
                    Log.Error("Patcher not found after {0} ms. Aborting!", patcherTimeout);

                    // stop stopwatch
                    patchersw.Stop();

                }

            }
            
            // exit application
            Application.Exit();
            return;

        }

        private void EnterPassword()
        {
            
            // create new stopwatch for client searching timeout
            Stopwatch sw = new Stopwatch();
            sw.Start();

            // log
            Log.Info("Waiting {0} ms for League of Legends client...", clientTimeout);

            // try to find league of legends client for 30 seconds
            while (sw.ElapsedMilliseconds < clientTimeout && GetSingleWindowFromSize("ApolloRuntimeContentWindow", null, 800, 600) == IntPtr.Zero) Thread.Sleep(200);

            // check if client was found
            if (GetSingleWindowFromSize("ApolloRuntimeContentWindow", null, 800, 600) != IntPtr.Zero)
            {
                // get client window handle
                IntPtr hwnd = GetSingleWindowFromSize("ApolloRuntimeContentWindow", null, 800, 600);
                
                // get client window rectangle
                RECT rect;
                NativeMethods.GetWindowRect(hwnd, out rect);

                // log information found
                Log.Info("Found patcher after {0} ms {{Handle={1}, Rectangle={{Coordinates={2}, Size={3}}}}}", sw.Elapsed.TotalMilliseconds, hwnd, rect, rect.Size);
                Log.Info("Waiting 15 seconds for login form to appear...");

                // reset stopwatch for form loading
                sw.Reset();
                sw.Start();

                Bitmap clientImage = new Bitmap(ScreenCapture.CaptureWindow(hwnd));

                bool found = false;

                while (sw.ElapsedMilliseconds < passwordTimeout && !found && hwnd != IntPtr.Zero)
                {

                    Log.Verbose("{{Handle={0}, Rectangle={{Coordinates={1}, Size={2}}}}}", hwnd, rect, rect.Size);

                    NativeMethods.GetWindowRect(hwnd, out rect);

                    clientImage = new Bitmap(ScreenCapture.CaptureWindow(hwnd));

                    found = Pixels.PasswordBox.Match(clientImage);

                    clientImage.Dispose();

                    GC.Collect();

                    Thread.Sleep(500);

                    hwnd = GetSingleWindowFromSize("ApolloRuntimeContentWindow", null, 800, 600);

                }

                // check if password box was found
                if (found)
                {
                    // log information
                    Log.Info("Found password box after {0} ms. Reading & decrypting password from file...", sw.Elapsed.TotalMilliseconds);

                    NativeMethods.SetForegroundWindow(hwnd);

                    // create password string
                    string password;
                    
                    // try to read password from file
                    try
                    {

                        using (FileStream file = new FileStream("password", FileMode.Open, FileAccess.Read))
                        {

                            byte[] buffer = new byte[file.Length];
                            file.Read(buffer, 0, (int)file.Length);

                            password = Encryption.Decrypt(buffer);

                        }

                    }
                    catch(Exception ex)
                    {
                        // print exception & stacktrace to log
                        Log.Fatal("Password file could not be read!");
                        Log.PrintStackTrace(ex.StackTrace);

                        // show balloon tip to inform user of error
                        this.Invoke(new Action(() =>
                        {
                            notifyIcon.ShowBalloonTip(2500, "LoL Auto Login encountered a fatal error and will now exit. Please check your logs for more information.", "LoL Auto Login has encountered a fatal error", ToolTipIcon.Error);
                        }));

                        // exit application
                        Application.Exit();
                        return;
                    }
                    
                    // create character array from password
                    char[] passArray = password.ToCharArray();
                    
                    // log
                    Log.Info("Entering password...");

                    int i = 0;
                    
                    InputSimulator sim = new InputSimulator();

                    // enter password one character at a time
                    while (i <= passArray.Length && sw.Elapsed.Seconds < 30 && hwnd != IntPtr.Zero)
                    {
                        
                        // get window rectangle, in case it is resized or moved
                        NativeMethods.GetWindowRect(hwnd, out rect);
                        Log.Verbose("Client rectangle=" + rect.ToString());

                        // move cursor above password box
                        sim.Mouse.LeftButtonUp();
                        NativeMethods.SetForegroundWindow(hwnd);
                        Cursor.Position = new Point(rect.Left + (int)(rect.Width * 0.192), rect.Top + (int)(rect.Height * 0.480));
                        
                        // focus window & click on password box
                        sim.Mouse.LeftButtonClick();

                        if (NativeMethods.GetForegroundWindow() == hwnd)
                        {
                            // enter password character, press enter if complete
                            if (i != passArray.Length)
                            {
                                sim.Keyboard.KeyPress(VirtualKeyCode.END);
                                sim.Keyboard.TextEntry(passArray[i].ToString());
                            }
                            else
                                sim.Keyboard.KeyPress(VirtualKeyCode.RETURN);

                            i++;
                        }

                        hwnd = GetSingleWindowFromSize("ApolloRuntimeContentWindow", null, 800, 600);

                    }
                    
                }
                else
                {
                    // print error to log
                    Log.Error("Password box not found after 15 seconds. Aborting!");

                    // stop stopwatch
                    sw.Stop();

                    // exit application
                    Application.Exit();
                    return;
                }
            }
            else
            {
                // print error to log
                Log.Error("Client window not found after 15 seconds. Aborting!");

                // stop stopwatch
                sw.Stop();

                // exit application
                Application.Exit();
                return;
            }

            // log success message
            Log.Info("Success!");

            // close program
            Application.Exit();
        }

        private void saveButton_Click(object sender, EventArgs e)
        {
            // check if a password was inputted
            if (string.IsNullOrEmpty(passTextBox.Text))
            {
                MessageBox.Show(this, "You must enter a valid password!", "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // log
            Log.Info("Encrypting & saving password to file...");

            // try to write password to file
            try
            {

                using (FileStream file = new FileStream("password", FileMode.OpenOrCreate, FileAccess.Write))
                {

                    byte[] data = Encryption.Encrypt(passTextBox.Text);

                    file.Write(data, 0, data.Length);

                }

            }
            catch (Exception ex)
            {
                // show error message
                MessageBox.Show(this, "Something went wrong when trying to save your password:" + Environment.NewLine + Environment.NewLine + ex.StackTrace, "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);

                // print error message to log
                Log.Fatal("Could not save password to file!");
                Log.PrintStackTrace(ex.StackTrace);
            }

            // hide this window
            this.Opacity = 0.0F;
            this.Hide();

            // start launch process
            RunPatcher();
        }

        /// <summary>
        /// Gets a window with specified size using class name and window name.
        /// </summary>
        /// <param name="lpClassName">Class name</param>
        /// <param name="lpWindowName">Window name</param>
        /// <param name="width">Window minimum width</param>
        /// <param name="height">Window minimum height</param>
        /// <returns>The specified window's handle</returns>
        private IntPtr GetSingleWindowFromSize(string lpClassName, string lpWindowName, int width, int height)
        {
            // log what we are looking for
            Log.Verbose(string.Format("Trying to find window handle [ClassName={0},WindowName={1},Size={2}]", (lpWindowName != null ? lpWindowName : "null"), (lpClassName != null ? lpClassName : "null"), new Size(width, height).ToString()));
            
            // try to get window handle and rectangle using specified arguments
            IntPtr hwnd = NativeMethods.FindWindow(lpClassName, lpWindowName);
            RECT rect = new RECT();
            NativeMethods.GetWindowRect(hwnd, out rect);

            // check if handle is nothing
            if (hwnd == IntPtr.Zero)
            {
                // log that we didn't find a window
                Log.Verbose("Failed to find window with specified arguments!");

                return IntPtr.Zero;
            }

            // log what we found
            Log.Verbose(string.Format("Found window [Handle={0},Rectangle={1}]", hwnd.ToString(), rect.ToString()));

            if (rect.Size.Width >= width && rect.Size.Height >= height)
            {
                Log.Verbose("Correct window handle found!");
                
                return hwnd;
            }
            else
            {
                while(NativeMethods.FindWindowEx(IntPtr.Zero, hwnd, lpClassName, lpWindowName) != IntPtr.Zero)
                {
                    hwnd = NativeMethods.FindWindowEx(IntPtr.Zero, hwnd, lpClassName, lpWindowName);
                    NativeMethods.GetWindowRect(hwnd, out rect);

                    Log.Verbose(string.Format("Found window [Handle={0},Rectangle={1}]", hwnd.ToString(), rect.ToString()));

                    if (rect.Size.Width >= width && rect.Size.Height >= height)
                    {
                        Log.Verbose("Correct window handle found!");

                        return hwnd;
                    }
                }
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Kills every process with the specified name.
        /// </summary>
        /// <param name="pName">Name of process(es) to kill</param>
        public void KillProcessesByName(string pName)
        {

            Log.Verbose("Killing all " + pName + " processes.");

            foreach (Process p in Process.GetProcessesByName(pName)) p.Kill();

        }

        public new void Hide()
        {

            this.Opacity = 0.0f;
            this.ShowInTaskbar = false;
            base.Hide();

        }

        private void LoLAutoLogin_FormClosing(object sender, FormClosingEventArgs e)
        {
            
            notifyIcon.Dispose();

        }

    }

}
