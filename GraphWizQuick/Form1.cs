﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;
namespace GraphWizQuick
{
    public partial class Form1 : Form
    {
        const string DefaultBinLocation = @"C:\Program Files (x86)\Graphviz2.38\bin";
        //const string DebugDotFilePath = @"D:\UndertaleHacking\betteribttest\betteribttest";
        const string DebugDotFilePath = @"C:\Users\Paul Bruner\Documents\GitHub\GMdsam\betteribttest\bin\Debug";
        string dotExeFullFilePath;

        string graphFileName;
        string graphFilePath;
        string graphFullFilePath;
        string tempFileNameForGraph;
        FileSystemWatcher graphFileWatcher;
        int refreshedCount = 0;

        void SetNewImage(string filename)
        {
            graphFileWatcher.EnableRaisingEvents = false;
            graphFilePath = Path.GetDirectoryName(filename);
            graphFileName = Path.GetFileName(filename);
            graphFullFilePath = Path.Combine(graphFilePath, graphFileName);
            graphFileWatcher.Filter = graphFileName;
            graphFileWatcher.Path = graphFilePath;
            RefreshGraph();
            graphFileWatcher.EnableRaisingEvents = true;
        }
        public Form1()
        {
            InitializeComponent();
            this.Text = "No File";
           
            graphFilePath = DebugDotFilePath;
            dotExeFullFilePath = Path.Combine(DefaultBinLocation, "dot.exe");
            // Create a new FileSystemWatcher and set its properties.

          //  panel = new ImagePanel();
          ////  panel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
          //  panel.Dock = DockStyle.Fill;
            panel.ZoomChangedEvent += Panel_ZoomChangedEvent;
         //   Controls.Add(panel);
            graphFileWatcher = new FileSystemWatcher();
            graphFileWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime;
            graphFileWatcher.Error += GraphFileWatcher_Error;
            graphFileWatcher.Changed += new FileSystemEventHandler(OnChanged);

            
        }
        
        private void Panel_ZoomChangedEvent(object sender, float newZoom)
        {
            this.labelZoom.Text = string.Format("{0,3}%", (int)Math.Round(newZoom / 1.0f * 100.0f));
        }

        private void GraphFileWatcher_Error(object sender, ErrorEventArgs e)
        {
            DoError("FileWatcherError: " + e.GetException().Message);
        }

        delegate void InvokeCallback();
        private void OnChanged(object sender, FileSystemEventArgs e)
        {
          
            if (this.InvokeRequired)
            {
                InvokeCallback d = new InvokeCallback(RefreshGraph);
                this.Invoke(d);
            }
            else RefreshGraph();
        }
        ProcessStartInfo CreateProcessStartInfoForGraph()
        {
            string arguments = "\"" + graphFullFilePath + "\" -Tpng";// -o\"" + targetPng +"\""; // output to standard output
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = dotExeFullFilePath;
            info.Arguments = arguments;
            info.WorkingDirectory = graphFilePath;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            return info;
        }
        // Had to put this in a thread cause Process gets locked on reading large amounts of standard input
        void UpdateImage(Bitmap image)
        {
            if (this.InvokeRequired)
            {
                Action<Bitmap> d = new Action<Bitmap>(this.UpdateImage);
                this.Invoke(d, image);
            }
            else {
                if(image == null)
                {
                    this.btnRefresh.Enabled = false;
                    this.btnResetZoom.Enabled = false;
                } else
                {
                    this.btnRefresh.Enabled = true;
                    this.btnResetZoom.Enabled = true;
                }
                this.panel.Image = image;
            }
        }
        void UpdateTitle(string title)
        {
            if (this.InvokeRequired)
            {
                Action<string> d = new Action<string>(this.UpdateTitle);
                this.Invoke(d, title);
            }
            else {
                if(title == null)
                {
                    if (this.panel.Image !=null)
                    {
                        DateTime fileTime = File.GetLastWriteTime(graphFullFilePath);
                        title = string.Format("{0} Refreshed: {1}  LastRefresh: {2}", graphFullFilePath, refreshedCount, fileTime);
                    } else title = "No File Loaded";   
                }
                this.Text = title;
            }
        }
        void DoError(string message)
        {
            if (this.InvokeRequired)
            {
                Action<string> d = new Action<string>(this.DoError);
                this.Invoke(d, message);
            }
            else {
                UpdateImage(null);
                UpdateTitle(null);// order matters here
                graphFileWatcher.EnableRaisingEvents = false; // hope this works in a seperate thread
                MessageBox.Show(message);
            }
        }
        const int ERROR_SHARING_VIOLATION = 32;
        const int ERROR_LOCK_VIOLATION = 33;
        static bool IsFileLocked(Exception exception)
        {
            int errorCode = System.Runtime.InteropServices.Marshal.GetHRForException(exception) & ((1 << 16) - 1);
            return errorCode == ERROR_SHARING_VIOLATION || errorCode == ERROR_LOCK_VIOLATION;
        }
        // There has GOT to be a better way
        // This is also very hacky cause there is a delay (insanily SMALL delay, but still a delay) between
        // this function and the dot.exe process.  But I have file locking usses to use this
        // A better solution will be to copy the file to a temp file, then just open that, but for this quick
        // hack job of a program I will hold off
        // maybe just pump it all to the standard input? humm
        static bool CanReadFile(string filePath)
        {
            //Try-Catch so we dont crash the program and can check the exception
            try
            {
                //The "using" is important because FileStream implements IDisposable and
                //"using" will avoid a heap exhaustion situation when too many handles  
                //are left undisposed.
                using (FileStream fileStream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    if (fileStream != null) fileStream.Close();  //This line is me being overly cautious, fileStream will never be null unless an exception occurs... and I know the "using" does it but its helpful to be explicit - especially when we encounter errors - at least for me anyway!
                }
            }
            catch (IOException ex)
            {
                //THE FUNKY MAGIC - TO SEE IF THIS FILE REALLY IS LOCKED!!!
                if (IsFileLocked(ex))
                {
                    // do something, eg File.Copy or present the user with a MsgBox - I do not recommend Killing the process that is locking the file
                    return false;
                }
            }
            finally
            { }
            return true;
        }
    
        void RefreshGraphThread()
        {
            try
            {
                Process processTemp = new Process();
                processTemp.StartInfo = CreateProcessStartInfoForGraph();
                processTemp.EnableRaisingEvents = true; // not sure we need this
                while (!CanReadFile(graphFullFilePath)) ; // infanit loop meh
                processTemp.Start();
                UpdateTitle("Loading Image...");
                MemoryStream ms = new MemoryStream(500000);// clear the memory stream and give it half a meg on the safe side
                while (!processTemp.HasExited) processTemp.StandardOutput.BaseStream.CopyTo(ms);
                processTemp.StandardOutput.BaseStream.CopyTo(ms);
                if (processTemp.ExitCode == 0)
                {
                    ms.Position = 0; // ugh be sure to reset the position before giving it to bitmap
                    Bitmap image = new Bitmap(ms);
                    if (image != null) UpdateImage(image);
                    else throw new Exception("No Imagedata from Process");
                }
                else throw new Exception("dot.exe: " + processTemp.StandardError.ReadToEnd());
                UpdateTitle(null);
                graphFileWatcher.EnableRaisingEvents = true; // eveything worked so start the thread raising event again     
            } catch(Exception e)
            {
                graphFileWatcher.EnableRaisingEvents = false; // hope this works in a seperate thread
                this.DoError(e.Message);
            }
        }
        void RefreshGraph()
        {
            try
            {
                refreshedCount++;
                UpdateTitle("Refreshing File..");
                graphFileWatcher.EnableRaisingEvents = false; // disable as we start the process in a sperate threat
                if (!File.Exists(dotExeFullFilePath)) throw new FileNotFoundException("Dot.exe not found", dotExeFullFilePath);
                if ( !File.Exists(graphFullFilePath)) throw new FileNotFoundException("Graph file not found", graphFullFilePath);
                Thread thread = new Thread(RefreshGraphThread);
                thread.Start();
            } catch(Exception e)
            {
                this.DoError(e.Message);
            }
        }


        private void binFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog1 = new OpenFileDialog();

            openFileDialog1.InitialDirectory = DefaultBinLocation;
            openFileDialog1.RestoreDirectory = true;
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                string path = Path.GetDirectoryName(openFileDialog1.FileName);
                path = Path.Combine(path, "dot.exe");
                if (File.Exists(path))
                    dotExeFullFilePath = path;
                else
                    MessageBox.Show("Error: Could not find dot executiable");
            }
        }


        private void dotFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog1 = new OpenFileDialog();

            openFileDialog1.InitialDirectory = graphFilePath;
            openFileDialog1.RestoreDirectory = true;
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                SetNewImage(openFileDialog1.FileName);
            }
        }

        private void Refresh_Click(object sender, EventArgs e)
        {
            RefreshGraph();
        }

        private void ResetZoom_Click(object sender, EventArgs e)
        {
            this.panel.ZoomPercent = 100;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            UpdateImage(null);
            string[] args = Environment.GetCommandLineArgs();
            for(int i=1;i < args.Length;i++)
            {
                var s = args[i];
                if (File.Exists(s))
                {
                    this.Invoke(new Action<string>(SetNewImage), s);
                   /// SetNewImage(s);
                    break;
                }
            }
        }
    }
}