﻿/*
ImageGlass Project - Image viewer for Windows
Copyright (C) 2017 DUONG DIEU PHAP
Project homepage: http://imageglass.org

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using ImageGlass.Core;
using ImageGlass.Library.Image;
using ImageGlass.Library.Comparer;
using System.IO;
using System.Diagnostics;
using ImageGlass.Services.Configuration;
using ImageGlass.Library;
using System.Collections.Specialized;
using ImageGlass.Services.InstanceManagement;
using System.Drawing.Imaging;
using ImageGlass.Theme;
using System.Threading.Tasks;
using ImageGlass.Library.WinAPI;
using System.Globalization;

namespace ImageGlass
{
    public partial class frmMain : Form
    {
        public frmMain()
        {
            InitializeComponent();
            mnuMain.Renderer = mnuPopup.Renderer = new Theme.ModernMenuRenderer();

            //Check DPI Scaling ratio
            DPIScaling.CurrentDPI = DPIScaling.GetSystemDpi();
            OnDpiChanged();
        }



        #region Local variables

        private string _imageInfo = "";

        // window size value before resizing
        private Size _windowSize = new Size(600, 500);

        // determine if the image is zoomed
        private bool _isZoomed = false;

        //determine if toolbar is shown
        private bool _isShowToolbar = true;

        private bool _isWindowsKeyPressed = false;

        private bool _isDraggingImage = false;
        #endregion



        #region Drag - drop
        private void picMain_DragOver(object sender, DragEventArgs e)
        {
            string filePath = ((string[])e.Data.GetData(DataFormats.FileDrop))[0];

            // Drag file from DESKTOP to APP
            if (GlobalSetting.ImageList.IndexOf(filePath) == -1)
            {
                e.Effect = DragDropEffects.Move;
            }
            // Drag file from APP to DESKTOP
            else
            {
                e.Effect = DragDropEffects.Copy;
            }

        }
        private void picMain_DragDrop(object sender, DragEventArgs e)
        {
            string filePath = ((string[])e.Data.GetData(DataFormats.FileDrop))[0];

            // Drag file from DESKTOP to APP
            if (GlobalSetting.ImageList.IndexOf(filePath) == -1)
            {
                Prepare(filePath);
            }
        }

        private void picMain_MouseDown(object sender, MouseEventArgs e)
        {
            if (_isDraggingImage)
            {
                string[] paths = new string[1];
                paths[0] = GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex);

                var data = new DataObject(DataFormats.FileDrop, paths);
                picMain.DoDragDrop(data, DragDropEffects.Copy);

                _isDraggingImage = false;
            }
        }
        
        #endregion



        #region Preparing image
        /// <summary>
        /// Open an image
        /// </summary>
        private void OpenFile()
        {
            OpenFileDialog o = new OpenFileDialog();
            o.Filter = GlobalSetting.LangPack.Items["frmMain._OpenFileDialog"] + "|" +
                        GlobalSetting.AllImageFormats;

            if (o.ShowDialog() == DialogResult.OK && File.Exists(o.FileName))
            {
                Prepare(o.FileName);
            }
            o.Dispose();
        }

        /// <summary>
        /// Prepare to load image
        /// </summary>
        /// <param name="path">Path</param>
        public void Prepare(string path)
        {
            if (File.Exists(path) == false && Directory.Exists(path) == false)
                return;

            //Reset current index
            GlobalSetting.CurrentIndex = 0;
            string filePath = "";
            string dirPath = "";

            //Check path is file or directory?
            if (File.Exists(path))
            {
                filePath = path;

                // get directory
                dirPath = (Path.GetDirectoryName(path) + "\\").Replace("\\\\", "\\");
                dirPath = path.Substring(0, path.LastIndexOf("\\") + 1);
            }
            else if (Directory.Exists(path))
            {
                dirPath = (path + "\\").Replace("\\\\", "\\");
            }

            //Declare a new list to store filename
            var _imageFilenameList = new List<string>();

            //Get supported image extensions from directory
            _imageFilenameList = LoadImageFilesFromDirectory(dirPath);

            //Dispose all garbage
            GlobalSetting.ImageList.Dispose();

            //Set filename to image list
            GlobalSetting.ImageList = new ImgMan(_imageFilenameList.ToArray());
            //Track image loading progress
            GlobalSetting.ImageList.OnFinishLoadingImage += ImageList_OnFinishLoadingImage;

            //Find the index of current image
            if (filePath.Length > 0)
            {
                GlobalSetting.CurrentIndex = GlobalSetting.ImageList.IndexOf(filePath);
            }
            else
            {
                GlobalSetting.CurrentIndex = 0;
            }            

            //Load thumnbnail
            LoadThumbnails();

            //Cannot find the index
            if (GlobalSetting.CurrentIndex == -1)
            {
                //Mark as Image Error
                GlobalSetting.IsImageError = true;
                Text = $"ImageGlass - {filePath} - {ImageInfo.GetFileSize(filePath)}";

                picMain.Text = GlobalSetting.LangPack.Items["frmMain.picMain._ErrorText"];
                picMain.Image = null;

                //Exit function
                return;
            }

            //Start loading image
            NextPic(0);

            //Watch all changes of current path
            sysWatch.Path = Path.GetDirectoryName(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex));
            sysWatch.EnableRaisingEvents = true;
        }

        private void ImageList_OnFinishLoadingImage(object sender, EventArgs e)
        {
            //clear text when finishing
            DisplayTextMessage("", 0);
        }

        /// <summary>
        /// Select current thumbnail
        /// </summary>
        private void SelectCurrentThumbnail()
        {
            if (thumbnailBar.Items.Count > 0)
            {
                thumbnailBar.ClearSelection();

                try
                {
                    thumbnailBar.Items[GlobalSetting.CurrentIndex].Selected = true;
                    thumbnailBar.Items[GlobalSetting.CurrentIndex].Focused = true;
                    thumbnailBar.EnsureVisible(GlobalSetting.CurrentIndex);
                }
                catch (Exception ex) { }
            }
        }

        /// <summary>
        /// Sort and find all supported image from directory
        /// </summary>
        /// <param name="path">Image folder path</param>
        private List<string> LoadImageFilesFromDirectory(string path)
        {
            //Load image order from config
            GlobalSetting.LoadImageOrderConfig();

            var list = new List<string>();

            //Get files from dir
            var dsFile = DirectoryFinder.FindFiles(path,
                GlobalSetting.IsRecursiveLoading,
                new Predicate<string>(delegate (String f)
                {
                    Application.DoEvents();

                    string extension = Path.GetExtension(f).ToLower() ?? ""; //remove blank extension
                    // checks if image is hidden and ignores it if so
                    if (GlobalSetting.IsShowingHiddenImages == false)
                    {
                        var attributes = File.GetAttributes(f);
                        var isHidden = attributes.HasFlag(FileAttributes.Hidden);
                        if (isHidden)
                        {
                            return false;
                        }
                    }
                    if (extension.Length > 0 && GlobalSetting.AllImageFormats.Contains(extension))
                    {
                        return true;
                    }

                    return false;
                }));

            //Sort image file
            if (GlobalSetting.ImageLoadingOrder == ImageOrderBy.Name)
            {
                var arr = dsFile.ToArray();
                Array.Sort(arr, new WindowsNaturalSort());
                list.AddRange(arr);

                //list.AddRange(FileLogicalComparer.Sort(dsFile.ToArray()));
            }
            else if (GlobalSetting.ImageLoadingOrder == ImageOrderBy.Length)
            {
                list.AddRange(dsFile
                    .OrderBy(f => new FileInfo(f).Length));
            }
            else if (GlobalSetting.ImageLoadingOrder == ImageOrderBy.CreationTime)
            {
                list.AddRange(dsFile
                    .OrderBy(f => new FileInfo(f).CreationTimeUtc));
            }
            else if (GlobalSetting.ImageLoadingOrder == ImageOrderBy.Extension)
            {
                list.AddRange(dsFile
                    .OrderBy(f => new FileInfo(f).Extension));
            }
            else if (GlobalSetting.ImageLoadingOrder == ImageOrderBy.LastAccessTime)
            {
                list.AddRange(dsFile
                    .OrderBy(f => new FileInfo(f).LastAccessTime));
            }
            else if (GlobalSetting.ImageLoadingOrder == ImageOrderBy.LastWriteTime)
            {
                list.AddRange(dsFile
                    .OrderBy(f => new FileInfo(f).LastWriteTime));
            }
            else if (GlobalSetting.ImageLoadingOrder == ImageOrderBy.Random)
            {
                list.AddRange(dsFile
                    .OrderBy(f => Guid.NewGuid()));
            }

            return list;
        }

        /// <summary>
        /// Clear and reload all thumbnail image
        /// </summary>
        private void LoadThumbnails()
        {
            thumbnailBar.Items.Clear();
            thumbnailBar.ThumbnailSize = new Size(GlobalSetting.ThumbnailDimension, GlobalSetting.ThumbnailDimension);

            for (int i = 0; i < GlobalSetting.ImageList.Length; i++)
            {
                ImageListView.ImageListViewItem lvi = new ImageListView.ImageListViewItem(GlobalSetting.ImageList.GetFileName(i));
                lvi.Tag = GlobalSetting.ImageList.GetFileName(i);

                thumbnailBar.Items.Add(lvi);
            }

        }

        /// <summary>
        /// Change image
        /// </summary>
        /// <param name="step">Image step to change. Zero is reload the current image.</param>
        private void NextPic(int step)
        {
            NextPic(step, false);
        }

        /// <summary>
        /// Change image
        /// </summary>
        /// <param name="step">Image step to change. Zero is reload the current image.</param>
        /// <param name="configs">Configuration for the next load</param>
        private void NextPic(int step, bool isKeepZoomRatio)
        {
            //Save previous image if it was modified
            if (File.Exists(LocalSetting.ImageModifiedPath))
            {
                DisplayTextMessage(GlobalSetting.LangPack.Items["frmMain._SaveChanges"], 2000);

                Application.DoEvents();
                ImageSaveChange();
                return;
            }
            
            Application.DoEvents();

            picMain.Text = "";
            GlobalSetting.IsTempMemoryData = false;

            if (GlobalSetting.ImageList.Length < 1)
            {
                Text = $"ImageGlass";

                GlobalSetting.IsImageError = true;
                picMain.Image = null;
                LocalSetting.ImageModifiedPath = "";

                return;
            }

            //temp index
            int tempIndex = GlobalSetting.CurrentIndex + step;

            if (!GlobalSetting.IsPlaySlideShow && !GlobalSetting.IsLoopBackViewer)
            {
                //Reach end of list
                if (tempIndex >= GlobalSetting.ImageList.Length)
                {
                    DisplayTextMessage(GlobalSetting.LangPack.Items["frmMain._LastItemOfList"], 1000);
                    return;
                }

                //Reach the first item of list
                if (tempIndex < 0)
                {
                    DisplayTextMessage(GlobalSetting.LangPack.Items["frmMain._FirstItemOfList"], 1000);
                    return;
                }
            }

            //Check if current index is greater than upper limit
            if (tempIndex >= GlobalSetting.ImageList.Length)
                tempIndex = 0;

            //Check if current index is less than lower limit
            if (tempIndex < 0)
                tempIndex = GlobalSetting.ImageList.Length - 1;

            //Update current index
            GlobalSetting.CurrentIndex = tempIndex;


            //The image data will load
            Image im = null;

            try
            {
                //Read image data
                im = GlobalSetting.ImageList.GetImage(GlobalSetting.CurrentIndex);

                GlobalSetting.IsImageError = GlobalSetting.ImageList.IsErrorImage;

                //picMain.ZoomToFit();

                //Lock zoom ratio if required
                bool isEnabledZoomLock = GlobalSetting.IsEnabledZoomLock;
                if (isKeepZoomRatio)
                {
                    GlobalSetting.IsEnabledZoomLock = true;
                    GlobalSetting.ZoomLockValue = picMain.Zoom;

                    //prevent scrollbar position reset
                    LocalSetting.IsResetScrollPosition = false;
                }

                //Show image
                picMain.Image = im;

                //refresh image
                mnuMainRefresh_Click(null, null);

                //Run in another thread
                Parallel.Invoke(() =>
                {
                    //Unlock zoom ratio before
                    if (isKeepZoomRatio)
                    {
                        //reset to default values
                        GlobalSetting.IsEnabledZoomLock = isEnabledZoomLock;
                        GlobalSetting.ZoomLockValue = 100;
                        LocalSetting.IsResetScrollPosition = true;
                    }

                    //Release unused images
                    if (GlobalSetting.CurrentIndex - 2 >= 0)
                    {
                        GlobalSetting.ImageList.Unload(GlobalSetting.CurrentIndex - 2);
                    }
                    if (!GlobalSetting.IsImageBoosterBack && GlobalSetting.CurrentIndex - 1 >= 0)
                    {
                        GlobalSetting.ImageList.Unload(GlobalSetting.CurrentIndex - 1);
                    }
                });
                
            }
            catch
            {
                picMain.Image = null;
                LocalSetting.ImageModifiedPath = "";

                Application.DoEvents();
                if (!File.Exists(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)))
                {
                    GlobalSetting.ImageList.Unload(GlobalSetting.CurrentIndex);
                }
            }

            if (GlobalSetting.IsImageError)
            {
                picMain.Text = GlobalSetting.LangPack.Items["frmMain.picMain._ErrorText"];
                picMain.Image = null;
                LocalSetting.ImageModifiedPath = "";
            }

            //Select thumbnail item
            SelectCurrentThumbnail();

            //Collect system garbage
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }


        /// <summary>
        /// Update image information on status bar
        /// </summary>
        private void UpdateStatusBar(bool @zoomOnly = false)
        {
            string fileinfo = "";

            if (GlobalSetting.ImageList.Length < 1)
            {
                this.Text = $"ImageGlass {fileinfo}";
                return;
            }

            //Set the text of Window title
            this.Text = "ImageGlass - " +
                        (GlobalSetting.CurrentIndex + 1) + "/" + GlobalSetting.ImageList.Length + " " +
                        GlobalSetting.LangPack.Items["frmMain._Text"] + " - " +
                        GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex);

            if (GlobalSetting.IsImageError)
            {
                try
                {
                    fileinfo = ImageInfo.GetFileSize(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)) + "\t  |  ";
                    fileinfo += Path.GetExtension(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)).Replace(".", "").ToUpper() + "  |  ";
                    fileinfo += File.GetCreationTime(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)).ToString("yyyy/M/d HH:m:s");
                    _imageInfo = fileinfo;
                }
                catch { fileinfo = ""; }
            }
            else
            {
                try
                {
                    fileinfo += picMain.Image.Width + " x " + picMain.Image.Height + " px  |  ";
                }
                catch { }

                if (zoomOnly)
                {
                    fileinfo = picMain.Zoom.ToString() + "%  |  " + _imageInfo;
                }
                else
                {
                    fileinfo += ImageInfo.GetFileSize(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)) + "\t  |  ";
                    fileinfo += File.GetCreationTime(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)).ToString("yyyy/M/d HH:m:s");

                    _imageInfo = fileinfo;

                    fileinfo = picMain.Zoom.ToString() + "%  |  " + fileinfo;
                }
            }

            //Move image information to Window title
            this.Text += "  |  " + fileinfo;

        }
        #endregion



        #region Key event

        private void frmMain_KeyDown(object sender, KeyEventArgs e)
        {
            //this.Text = e.KeyValue.ToString();



            #region Detect WIN logo key
            _isWindowsKeyPressed = false;
            if (e.KeyData == Keys.LWin || e.KeyData == Keys.RWin)
            {
                _isWindowsKeyPressed = true;
            }
            #endregion

            
            //Show main menu
            #region Ctrl + `
            if (e.KeyValue == 192 && !e.Control && !e.Shift && !e.Alt) // `
            {
                mnuMain.Show(picMain, 0, picMain.Top);
            }
            #endregion


            // Rotation Counterclockwise----------------------------------------------------
            #region Ctrl + ,
            if (e.KeyValue == 188 && e.Control && !e.Shift && !e.Alt)//Ctrl + ,
            {
                mnuMainRotateCounterclockwise_Click(null, null);
                return;
            }
            #endregion


            //Rotate Clockwise--------------------------------------------------------------
            #region Ctrl + .
            if (e.KeyValue == 190 && e.Control && !e.Shift && !e.Alt)//Ctrl + .
            {
                mnuMainRotateClockwise_Click(null, null);
                return;
            }
            #endregion
            

            //Clear clipboard----------------------------------------------------------------
            #region CTRL + `
            if (e.KeyValue == 192 && e.Control && !e.Shift && !e.Alt)//CTRL + `
            {
                mnuMainClearClipboard_Click(null, null);
                return;
            }
            #endregion


            //Zoom + ------------------------------------------------------------------------
            #region Ctrl + = / = / + (numPad)
            if (((e.KeyValue == 187 && e.Control) || (e.KeyValue == 107 && !e.Control)) && !e.Shift && !e.Alt)// Ctrl + =
            {
                btnZoomIn_Click(null, null);
                return;
            }
            #endregion


            //Zoom - ------------------------------------------------------------------------
            #region Ctrl + - / - / - (numPad)
            if (((e.KeyValue == 189 && e.Control) || (e.KeyValue == 109 && !e.Control)) && !e.Shift && !e.Alt)// Ctrl + -
            {
                btnZoomOut_Click(null, null);
                return;
            }
            #endregion
            

            //Actual size image -------------------------------------------------------------
            #region Ctrl + 0 / Ctrl + Num0 / 0 / Num0
            if (!e.Shift && !e.Alt && (e.KeyValue == 48 || e.KeyValue == 96)) // 0 || Num0 || Ctrl + 0 || Ctrl + Num0
            {
                btnActualSize_Click(null, null);
                return;
            }
            #endregion
            

            //Full screen--------------------------------------------------------------------
            #region ALT + ENTER
            if (e.Alt && e.KeyCode == Keys.Enter && !e.Control && !e.Shift)//Alt + Enter
            {
                btnFullScreen.PerformClick();
                return;
            }
            #endregion


            //ESC ultility------------------------------------------------------------------
            #region ESC
            if (e.KeyCode == Keys.Escape && !e.Control && !e.Shift && !e.Alt)//ESC
            {
                //exit slideshow
                if (GlobalSetting.IsPlaySlideShow)
                {
                    mnuMainSlideShowExit_Click(null, null);
                }
                //exit full screen
                else if (GlobalSetting.IsFullScreen)
                {
                    btnFullScreen.PerformClick();
                }
                //Quit ImageGlass
                else if (GlobalSetting.IsPressESCToQuit)
                {
                    Application.Exit();
                }
                return;
            }
            #endregion


            //Ctrl---------------------------------------------------------------------------
            #region CTRL (for Zooming)
            if (e.Control && !e.Alt && !e.Shift)//Ctrl
            {
                //Enable dragging viewing image to desktop feature---------------------------
                _isDraggingImage = true;

                if (GlobalSetting.IsMouseNavigation)
                {
                    _isZoomed = true;
                    picMain.AllowZoom = true;
                }
                return;
            }
            #endregion


        }
        
        private void frmMain_KeyUp(object sender, KeyEventArgs e)
        {
            //this.Text = e.KeyValue.ToString();

            //Ctrl---------------------------------------------------------------------------
            #region CTRL (for Zooming)
            if (e.KeyData == Keys.ControlKey && !e.Alt && !e.Shift)//Ctrl
            {
                //Disable dragging viewing image to desktop feature--------------------------
                _isDraggingImage = false;

                if (GlobalSetting.IsMouseNavigation)
                {
                    _isZoomed = false;
                    picMain.AllowZoom = false;
                }
                return;
            }
            #endregion
            

            //Previous Image----------------------------------------------------------------
            #region LEFT ARROW / PAGE UP
            if (!_isWindowsKeyPressed && (e.KeyValue == 33 || e.KeyValue == 37) &&
                !e.Control && !e.Shift && !e.Alt)//Left arrow / PageUp
            {
                NextPic(-1);
                return;
            }
            #endregion


            //Next Image---------------------------------------------------------------------
            #region RIGHT ARROW / PAGE DOWN
            if (!_isWindowsKeyPressed && (e.KeyValue == 34 || e.KeyValue == 39) &&
                !e.Control && !e.Shift && !e.Alt)//Right arrow / Pagedown
            {
                NextPic(1);
                return;
            }
            #endregion


            //Goto the first Image---------------------------------------------------------------
            #region HOME
            if ((e.KeyValue == 36 || e.KeyValue == 39) &&
                !e.Control && !e.Shift && !e.Alt)
            {
                mnuMainGotoFirst_Click(null, e);
                return;
            }
            #endregion


            //Goto the last Image---------------------------------------------------------------
            #region END
            if ((e.KeyValue == 35 || e.KeyValue == 39) &&
                !e.Control && !e.Shift && !e.Alt)
            {
                mnuMainGotoLast_Click(null, e);
                return;
            }
            #endregion


            //Start / stop slideshow---------------------------------------------------------
            #region SPACE
            if (GlobalSetting.IsPlaySlideShow && e.KeyCode == Keys.Space && !e.Control && !e.Shift && !e.Alt)//SPACE
            {
                mnuMainSlideShowPause_Click(null, null);
                return;
            }
            #endregion
            
        }
        #endregion



        #region Private functions
        /// <summary>
        /// Update editing association app info and icon for Edit Image menu
        /// </summary>
        private void UpdateEditingAssocAppInfoForMenu()
        {
            string appName = "";
            mnuMainEditImage.Image = null;
            
            //Temporary memory data
            if (GlobalSetting.IsTempMemoryData)
            { }
            else
            {
                //Find file format
                var ext = Path.GetExtension(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)).ToLower();
                var assoc = GlobalSetting.GetImageEditingAssociationFromList(ext);

                //Get App assoc info
                if (assoc != null && File.Exists(assoc.AppPath))
                {
                    appName = $"({ assoc.AppName})";

                    //Update icon
                    Icon ico = Icon.ExtractAssociatedIcon(assoc.AppPath);
                    double scaleFactor = DPIScaling.GetDPIScaleFactor();
                    int iconWidth = (int)((int)Constants.MENU_ICON_HEIGHT * scaleFactor);

                    mnuMainEditImage.Image = new Bitmap(ico.ToBitmap(), iconWidth, iconWidth);
                }
            }

            mnuMainEditImage.Text = string.Format(GlobalSetting.LangPack.Items["frmMain.mnuMainEditImage"], appName);
        }

        /// <summary>
        /// Start Zoom optimization
        /// </summary>
        private void ZoomOptimization()
        {
            if (GlobalSetting.ZoomOptimizationMethod == ZoomOptimizationValue.Auto)
            {
                if (picMain.Zoom > 100)
                {
                    picMain.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                }
                else if (picMain.Zoom < 100)
                {
                    picMain.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                }
            }
            else if (GlobalSetting.ZoomOptimizationMethod == ZoomOptimizationValue.ClearPixels)
            {
                picMain.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            }
            else if (GlobalSetting.ZoomOptimizationMethod == ZoomOptimizationValue.SmoothPixels)
            {
                picMain.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
            }
        }

        /// <summary>
        /// Rename image
        /// </summary>
        private void RenameImage()
        {
            try
            {
                if (GlobalSetting.IsImageError || !File.Exists(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)))
                {
                    return;
                }
            }
            catch { return; }

            //Get filename
            string oldName;
            string newName;
            oldName = newName = Path.GetFileName(
                GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex));
            string currentPath = (Path.GetDirectoryName(
                GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)) + "\\")
                .Replace("\\\\", "\\");

            //Get file extension
            string ext = newName.Substring(newName.LastIndexOf("."));
            newName = newName.Substring(0, newName.Length - ext.Length);

            //Show input box
            string str = null;            

            if (InputBox.ShowDiaLog(GlobalSetting.LangPack.Items["frmMain._RenameDialogText"], GlobalSetting.LangPack.Items["frmMain._RenameDialog"], newName, false) == DialogResult.OK)
            {
                str = InputBox.Message;
            }

            if (string.IsNullOrWhiteSpace(str))
            {
                return;
            }

            newName = str + ext;

            //duplicated name
            if (oldName == newName)
            {
                return;
            }

            try
            {
                //Rename file
                ImageInfo.RenameFile(currentPath + oldName, currentPath + newName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Display a message on picture box
        /// </summary>
        /// <param name="msg">Message</param>
        /// <param name="duration">Duration (milisecond)</param>
        private void DisplayTextMessage(string msg, int duration)
        {
            if (duration == 0)
            {
                picMain.TextBackColor = Color.Transparent;
                picMain.Font = Font;
                picMain.ForeColor = LocalSetting.Theme.TextInfoColor;
                picMain.Text = string.Empty;
                return;
            }

            Timer tmsg = new Timer();
            tmsg.Enabled = false;
            tmsg.Tick += tmsg_Tick;
            tmsg.Interval = duration; //display in xxx mili seconds

            picMain.TextBackColor = Color.Black;
            picMain.Font = new Font(Font.FontFamily, 12);
            picMain.ForeColor = Color.White;
            picMain.Text = msg;

            //Start timer
            tmsg.Enabled = true;
            tmsg.Start();
        }

        private void tmsg_Tick(object sender, EventArgs e)
        {
            Timer tmsg = (Timer)sender;
            tmsg.Stop();

            if(GlobalSetting.IsImageError)
            {
                return;
            }

            picMain.TextBackColor = Color.Transparent;
            picMain.Font = Font;
            picMain.ForeColor = Color.Black;
            picMain.Text = string.Empty;
        }

        private void CopyFile()
        {
            try
            {
                if (GlobalSetting.IsImageError || !File.Exists(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)))
                {
                    return;
                }
            }
            catch { return; }

            GlobalSetting.StringClipboard = new StringCollection();
            GlobalSetting.StringClipboard.Add(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex));
            Clipboard.SetFileDropList(GlobalSetting.StringClipboard);

            DisplayTextMessage(
                string.Format(GlobalSetting.LangPack.Items["frmMain._CopyFileText"],
                GlobalSetting.StringClipboard.Count), 1000);
        }

        private void CopyMultiFiles()
        {
            try
            {
                if (GlobalSetting.IsImageError || !File.Exists(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)))
                {
                    return;
                }
            }
            catch { return; }

            //get filename
            string filename = GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex);

            //exit if duplicated filename
            if (GlobalSetting.StringClipboard.IndexOf(filename) != -1)
            {
                return;
            }

            //add filename to clipboard
            GlobalSetting.StringClipboard.Add(filename);
            Clipboard.SetFileDropList(GlobalSetting.StringClipboard);

            DisplayTextMessage(
                string.Format(GlobalSetting.LangPack.Items["frmMain._CopyFileText"],
                GlobalSetting.StringClipboard.Count), 1000);
        }

        private void CutFile()
        {
            try
            {
                if (GlobalSetting.IsImageError || !File.Exists(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)))
                {
                    return;
                }
            }
            catch { return; }

            GlobalSetting.StringClipboard = new StringCollection();
            GlobalSetting.StringClipboard.Add(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex));

            byte[] moveEffect = new byte[] { 2, 0, 0, 0 };
            MemoryStream dropEffect = new MemoryStream();
            dropEffect.Write(moveEffect, 0, moveEffect.Length);

            DataObject data = new DataObject();
            data.SetFileDropList(GlobalSetting.StringClipboard);
            data.SetData("Preferred DropEffect", dropEffect);

            Clipboard.Clear();
            Clipboard.SetDataObject(data, true);

            DisplayTextMessage(
                string.Format(GlobalSetting.LangPack.Items["frmMain._CutFileText"],
                GlobalSetting.StringClipboard.Count), 1000);
        }

        private void CutMultiFiles()
        {
            try
            {
                if (GlobalSetting.IsImageError || !File.Exists(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)))
                {
                    return;
                }
            }
            catch { return; }

            //get filename
            string filename = GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex);

            //exit if duplicated filename
            if (GlobalSetting.StringClipboard.IndexOf(filename) != -1)
            {
                return;
            }

            //add filename to clipboard
            GlobalSetting.StringClipboard.Add(filename);

            byte[] moveEffect = new byte[] { 2, 0, 0, 0 };
            MemoryStream dropEffect = new MemoryStream();
            dropEffect.Write(moveEffect, 0, moveEffect.Length);

            DataObject data = new DataObject();
            data.SetFileDropList(GlobalSetting.StringClipboard);
            data.SetData("Preferred DropEffect", dropEffect);

            Clipboard.Clear();
            Clipboard.SetDataObject(data, true);

            DisplayTextMessage(
                string.Format(GlobalSetting.LangPack.Items["frmMain._CutFileText"],
                GlobalSetting.StringClipboard.Count), 1000);
        }

        /// <summary>
        /// Save all change of image
        /// </summary>
        private void ImageSaveChange()
        {
            try
            {
                ImageInfo.SaveImage(picMain.Image, LocalSetting.ImageModifiedPath);
            }
            catch { }
            
            LocalSetting.ImageModifiedPath = "";
        }


        /// <summary>
        /// Handle the event when Dpi changed
        /// </summary>
        private void OnDpiChanged()
        {
            //Get Scaling factor
            double scaleFactor = DPIScaling.GetDPIScaleFactor();


            #region change size of toolbar
            //Update size of toolbar
            toolMain.Height = (int)((int)Constants.TOOLBAR_HEIGHT * scaleFactor);

            //Get new toolbar item height
            int currentToolbarHeight = toolMain.Height;
            int newToolBarItemHeight = int.Parse(Math.Floor((currentToolbarHeight * 0.8)).ToString());

            //Update toolbar items size
            //Tool bar buttons
            foreach (var item in toolMain.Items.OfType<ToolStripButton>())
            {
                item.Size = new Size(newToolBarItemHeight, newToolBarItemHeight);
            }

            //Tool bar menu buttons
            foreach (var item in toolMain.Items.OfType<ToolStripDropDownButton>())
            {
                item.Size = new Size(newToolBarItemHeight, newToolBarItemHeight);
            }

            //Tool bar separators
            foreach (var item in toolMain.Items.OfType<ToolStripSeparator>())
            {
                item.Size = new Size(5, newToolBarItemHeight);
            }

            //Update toolbar icon size
            var themeConfigFile = GlobalSetting.GetConfig("Theme", "default");
            if (!File.Exists(themeConfigFile))
            {
                themeConfigFile = Path.Combine(GlobalSetting.StartUpDir, @"DefaultTheme\config.xml");
            }

            Theme.Theme t = new Theme.Theme(themeConfigFile);
            LoadToolbarIcons(t);

            #endregion

            #region change size of menu items
            int newMenuIconHeight = (int)((int)Constants.MENU_ICON_HEIGHT * scaleFactor);

            mnuMainAbout.Image = new Bitmap(newMenuIconHeight, newMenuIconHeight);
            mnuMainViewNext.Image = new Bitmap(newMenuIconHeight, newMenuIconHeight);
            mnuMainSlideShowStart.Image = new Bitmap(newMenuIconHeight, newMenuIconHeight);
            mnuMainRotateCounterclockwise.Image = new Bitmap(newMenuIconHeight, newMenuIconHeight);

            mnuMainClearClipboard.Image = new Bitmap(newMenuIconHeight, newMenuIconHeight);
            mnuMainShareFacebook.Image = new Bitmap(newMenuIconHeight, newMenuIconHeight);
            mnuMainToolbar.Image = new Bitmap(newMenuIconHeight, newMenuIconHeight);
            mnuMainExtensionManager.Image = new Bitmap(newMenuIconHeight, newMenuIconHeight);

            #endregion

        }
        #endregion



        #region Configurations
        
        /// <summary>
        /// Apply ImageGlass theme
        /// </summary>
        /// <param name="themeConfigPath">config.xml path. By default, load default theme</param>
        private Theme.Theme ApplyTheme(string @themeConfigPath = "default")
        {
            if (File.Exists(themeConfigPath))
            {
                GlobalSetting.SetConfig("Theme", themeConfigPath);
            }

            Theme.Theme th = new Theme.Theme(themeConfigPath);
            LoadTheme(th);

            return th;

            void LoadTheme(Theme.Theme t)
            {
                // <main>
                picMain.BackColor = t.BackgroundColor;
                GlobalSetting.BackgroundColor = t.BackgroundColor;

                toolMain.BackgroundImage = t.ToolbarBackgroundImage.Image;
                toolMain.BackColor = t.ToolbarBackgroundColor;

                thumbnailBar.BackgroundImage = t.ThumbnailBackgroundImage.Image;
                thumbnailBar.BackColor = t.ThumbnailBackgroundColor;
                sp1.BackColor = t.ThumbnailBackgroundColor;

                lblInfo.ForeColor = t.TextInfoColor;
                picMain.ForeColor = t.TextInfoColor;

                // <toolbar_icon>
                LoadToolbarIcons(t);
            }
        }

        /// <summary>
        /// Load toolbar icons
        /// </summary>
        /// <param name="t">Theme</param>
        private void LoadToolbarIcons(Theme.Theme t)
        {
            // <toolbar_icon>
            btnBack.Image = t.ToolbarIcons.ViewPreviousImage.Image;
            btnNext.Image = t.ToolbarIcons.ViewNextImage.Image;

            btnRotateLeft.Image = t.ToolbarIcons.RotateLeft.Image;
            btnRotateRight.Image = t.ToolbarIcons.RotateRight.Image;
            btnZoomIn.Image = t.ToolbarIcons.ZoomIn.Image;
            btnZoomOut.Image = t.ToolbarIcons.ZoomOut.Image;
            btnActualSize.Image = t.ToolbarIcons.ActualSize.Image;
            btnZoomLock.Image = t.ToolbarIcons.LockRatio.Image;
            btnScaletoWidth.Image = t.ToolbarIcons.ScaleToWidth.Image;
            btnScaletoHeight.Image = t.ToolbarIcons.ScaleToHeight.Image;
            btnWindowAutosize.Image = t.ToolbarIcons.AdjustWindowSize.Image;

            btnOpen.Image = t.ToolbarIcons.OpenFile.Image;
            btnRefresh.Image = t.ToolbarIcons.Refresh.Image;
            btnGoto.Image = t.ToolbarIcons.GoToImage.Image;
            btnThumb.Image = t.ToolbarIcons.ThumbnailBar.Image;
            btnCheckedBackground.Image = t.ToolbarIcons.CheckedBackground.Image;
            btnFullScreen.Image = t.ToolbarIcons.FullScreen.Image;
            btnSlideShow.Image = t.ToolbarIcons.Slideshow.Image;

            btnConvert.Image = t.ToolbarIcons.Convert.Image;
            btnPrintImage.Image = t.ToolbarIcons.Print.Image;
            btnFacebook.Image = t.ToolbarIcons.Sharing.Image;
            btnExtension.Image = t.ToolbarIcons.Plugins.Image;
            btnSetting.Image = t.ToolbarIcons.Settings.Image;
            btnHelp.Image = t.ToolbarIcons.About.Image;
            btnMenu.Image = t.ToolbarIcons.Menu.Image;
        }

        /// <summary>
        /// If true is passed, try to use a 10ms system clock for animating GIFs, otherwise
        /// use the default animator.
        /// </summary>
        private void CheckAnimationClock(bool isUsingFasterClock) {
            if (isUsingFasterClock) {
                if (!TimerAPI.HasRequestedRateAtLeastAsFastAs(10) && TimerAPI.TimeBeginPeriod(10))
                    HighResolutionGifAnimator.SetTickTimeInMilliseconds(10);
                picMain.Animator = new HighResolutionGifAnimator();
            }
            else {
                if (TimerAPI.HasRequestedRateAlready(10))
                    TimerAPI.TimeEndPeriod(10);
                picMain.Animator = new DefaultGifAnimator();
            }
        }

        /// <summary>
        /// Load app configurations
        /// </summary>
        private void LoadConfig()
        {
            //Load language pack-------------------------------------------------------------
            string configValue = GlobalSetting.GetConfig("Language", "English");
            if (configValue.ToLower().CompareTo("english") != 0 && File.Exists(configValue))
            {
                GlobalSetting.LangPack = new Library.Language(configValue);

                //force update language pack
                GlobalSetting.IsForcedActive = true;
                frmMain_Activated(null, null);
            }
            
            //Windows Bound (Position + Size)------------------------------------------------
            Rectangle rc = GlobalSetting.StringToRect(GlobalSetting.GetConfig($"{Name}.WindowsBound", "280,125,850,550"));

            if (!Helper.IsOnScreen(rc.Location))
            {
                rc.Location = new Point(280, 125);
            }
            Bounds = rc;


            //windows state--------------------------------------------------------------
            configValue = GlobalSetting.GetConfig($"{Name}.WindowsState", "Normal");
            if (configValue == "Normal")
            {
                WindowState = FormWindowState.Normal;
            }
            else if (configValue == "Maximized")
            {
                WindowState = FormWindowState.Maximized;
            }

            // Read suported image formats ------------------------------------------------
            var extGroups = GlobalSetting.BuiltInImageFormats.Split("|".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

            //Load Default Image Formats
            GlobalSetting.DefaultImageFormats = GlobalSetting.GetConfig("DefaultImageFormats", extGroups[0]);

            //Load Optional Image Formats
            GlobalSetting.OptionalImageFormats = GlobalSetting.GetConfig("OptionalImageFormats", extGroups[1]);

            if(GlobalSetting.AllImageFormats.Length == 0)
            {
                //If no formats from settings, we need to load from built-in configs
                GlobalSetting.LoadBuiltInImageFormats();

                //Write configs
                GlobalSetting.SetConfig("DefaultImageFormats", GlobalSetting.DefaultImageFormats);
                GlobalSetting.SetConfig("OptionalImageFormats", GlobalSetting.OptionalImageFormats);
            }

            //Slideshow Interval-----------------------------------------------------------
            int i = int.Parse(GlobalSetting.GetConfig("SlideShowInterval", "5"));
            if (!(0 < i && i < 61)) i = 5;//time limit [1; 60] seconds
            GlobalSetting.SlideShowInterval = i;
            timSlideShow.Interval = 1000 * GlobalSetting.SlideShowInterval;

            //Show checked bakcground-------------------------------------------------------
            GlobalSetting.IsShowCheckedBackground = bool.Parse(GlobalSetting.GetConfig("IsShowCheckedBackground", "False").ToString());
            GlobalSetting.IsShowCheckedBackground = !GlobalSetting.IsShowCheckedBackground;
            mnuMainCheckBackground_Click(null, EventArgs.Empty);

            //Recursive loading--------------------------------------------------------------
            GlobalSetting.IsRecursiveLoading = bool.Parse(GlobalSetting.GetConfig("IsRecursiveLoading", "False"));

            //Show hidden images------------------------------------------------------------
            GlobalSetting.IsShowingHiddenImages = bool.Parse(GlobalSetting.GetConfig("IsShowingHiddenImages", "False"));
            
            //Load is loop back slideshow---------------------------------------------------
            GlobalSetting.IsLoopBackViewer = bool.Parse(GlobalSetting.GetConfig("IsLoopBackViewer", "True"));

            //Load is loop back slideshow---------------------------------------------------
            GlobalSetting.IsLoopBackSlideShow = bool.Parse(GlobalSetting.GetConfig("IsLoopBackSlideShow", "True"));

            //Load IsPressESCToQuit---------------------------------------------------------
            GlobalSetting.IsPressESCToQuit = bool.Parse(GlobalSetting.GetConfig("IsPressESCToQuit", "True"));

            //Load image order config------------------------------------------------------
            GlobalSetting.LoadImageOrderConfig();

            //Load state of Image Booster --------------------------------------------------
            GlobalSetting.IsImageBoosterBack = bool.Parse(GlobalSetting.GetConfig("IsImageBoosterBack", "True"));

            //Load state of Toolbar---------------------------------------------------------
            GlobalSetting.IsShowToolBar = bool.Parse(GlobalSetting.GetConfig("IsShowToolBar", "True"));
            GlobalSetting.IsShowToolBar = !GlobalSetting.IsShowToolBar;
            mnuMainToolbar_Click(null, EventArgs.Empty);

            //Load Zoom to Fit value---------------------------------------------------------
            GlobalSetting.IsZoomToFit = bool.Parse(GlobalSetting.GetConfig("IsZoomToFit", "False"));
            mnuMainZoomToFit.Checked = GlobalSetting.IsZoomToFit;

            //Load Zoom lock value
            int zoomLock = int.Parse(GlobalSetting.GetConfig("ZoomLockValue", "-1"), GlobalSetting.NumberFormat);

            GlobalSetting.IsEnabledZoomLock = zoomLock > 0 ? true : false;
            mnuMainLockZoomRatio.Checked = btnZoomLock.Checked = GlobalSetting.IsEnabledZoomLock;
            GlobalSetting.ZoomLockValue = zoomLock > 0 ? zoomLock : 100;            

            //Zoom optimization method-------------------------------------------------------
            string configValue2 = GlobalSetting.GetConfig("ZoomOptimization", "0");
            if (int.TryParse(configValue2, out int zoomValue))
            {
                if (-1 < zoomValue && zoomValue < Enum.GetNames(typeof(ZoomOptimizationValue)).Length)
                { }
                else
                {
                    zoomValue = 0;
                }
            }
            GlobalSetting.ZoomOptimizationMethod = (ZoomOptimizationValue)zoomValue;

            //Image loading order -----------------------------------------------------------
            configValue2 = GlobalSetting.GetConfig("ImageLoadingOrder", "0");
            if (int.TryParse(configValue2, out int orderValue))
            {
                if (-1 < orderValue && orderValue < Enum.GetNames(typeof(ImageOrderBy)).Length)
                { }
                else
                {
                    orderValue = 0;
                }
            }
            GlobalSetting.ImageLoadingOrder = (ImageOrderBy)orderValue;

            //Load theme--------------------------------------------------------------------
            thumbnailBar.SetRenderer(new ImageListView.ImageListViewRenderers.ThemeRenderer()); //ThumbnailBar Renderer must be done BEFORE loading theme            
            LocalSetting.Theme = ApplyTheme(GlobalSetting.GetConfig("Theme", "default"));
            Application.DoEvents();

            //Load background---------------------------------------------------------------
            configValue2 = GlobalSetting.GetConfig("BackgroundColor", LocalSetting.Theme.BackgroundColor.ToArgb().ToString(GlobalSetting.NumberFormat));
            
            GlobalSetting.BackgroundColor = Color.FromArgb(int.Parse(configValue2, GlobalSetting.NumberFormat));
            picMain.BackColor = GlobalSetting.BackgroundColor;

            //Load scrollbars visibility-----------------------------------------------------
            GlobalSetting.IsScrollbarsVisible = bool.Parse(GlobalSetting.GetConfig("IsScrollbarsVisible", "False"));
            if (GlobalSetting.IsScrollbarsVisible)
            {
                picMain.HorizontalScrollBarStyle = ImageBoxScrollBarStyle.Auto;
                picMain.VerticalScrollBarStyle = ImageBoxScrollBarStyle.Auto;
            }

            //Load Thumbnail dimension-------------------------------------------------------
            if (int.TryParse(GlobalSetting.GetConfig("ThumbnailDimension", "48"), out i))
            {
                GlobalSetting.ThumbnailDimension = i;
            }
            else
            {
                GlobalSetting.ThumbnailDimension = 48;
            }

            //Load thumbnail bar width--------------------------------------------------------
            int tb_width = 0;
            if (!int.TryParse(GlobalSetting.GetConfig("ThumbnailBarWidth", "0"), out tb_width))
            {
                tb_width = 0;
            }

            //Get minimum width needed for thumbnail dimension
            var tb_minWidth = new ThumbnailItemInfo(GlobalSetting.ThumbnailDimension, true).GetTotalDimension();
            //Get the greater width value
            GlobalSetting.ThumbnailBarWidth = Math.Max(tb_width, tb_minWidth);
            
            //Load thumbnail orientation state: NOTE needs to be done BEFORE the mnuMainThumbnailBar_Click invocation below!
            GlobalSetting.IsThumbnailHorizontal = bool.Parse(GlobalSetting.GetConfig("IsThumbnailHorizontal", "True"));

            //Load vertical thumbnail bar width
            if (GlobalSetting.IsThumbnailHorizontal == false)
            {
                int vtb_width;
                if (int.TryParse(GlobalSetting.GetConfig("ThumbnailBarWidth", "48"), out vtb_width))
                {
                    GlobalSetting.ThumbnailBarWidth = vtb_width;
                }
            }

            //Load state of Thumbnail---------------------------------------------------------
            GlobalSetting.IsShowThumbnail = bool.Parse(GlobalSetting.GetConfig("IsShowThumbnail", "False"));
            GlobalSetting.IsShowThumbnail = !GlobalSetting.IsShowThumbnail;
            mnuMainThumbnailBar_Click(null, EventArgs.Empty);

            //Load state of IsWindowAlwaysOnTop value-----------------------------------------
            GlobalSetting.IsWindowAlwaysOnTop = bool.Parse(GlobalSetting.GetConfig("IsWindowAlwaysOnTop", "False"));
            TopMost = mnuMainAlwaysOnTop.Checked = GlobalSetting.IsWindowAlwaysOnTop;

            //Load state of IsMouseNavigation value-------------------------------------------
            GlobalSetting.IsMouseNavigation = bool.Parse(GlobalSetting.GetConfig("IsMouseNavigation", "False"));
            picMain.AllowZoom = !GlobalSetting.IsMouseNavigation;

            //Get IsConfirmationDelete value --------------------------------------------------
            GlobalSetting.IsConfirmationDelete = bool.Parse(GlobalSetting.GetConfig("IsConfirmationDelete", "False"));

            //Get ImageEditingAssociationList ------------------------------------------------------
            configValue2 = GlobalSetting.GetConfig("ImageEditingAssociationList", "");
            string[] editingAssoclist = configValue2.Split("[]".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

            if(editingAssoclist.Length > 0)
            {
                foreach(var configString in editingAssoclist)
                {
                    try
                    {
                        var extAssoc = new ImageEditingAssociation(configString);
                        GlobalSetting.ImageEditingAssociationList.Add(extAssoc);
                    }
                    catch (InvalidCastException) { }
                }
            }

            //Get welcome screen------------------------------------------------------------
            GlobalSetting.IsShowWelcome = bool.Parse(GlobalSetting.GetConfig("IsShowWelcome", "True"));
            if (GlobalSetting.IsShowWelcome)
            {
                //Do not show welcome image if params exist.
                if (Environment.GetCommandLineArgs().Count() < 2)
                {
                    Prepare(Path.Combine(GlobalSetting.StartUpDir, "default.png"));
                }
            }
        }


        /// <summary>
        /// Save app configurations
        /// </summary>
        private void SaveConfig()
        {
            GlobalSetting.SetConfig("AppVersion", Application.ProductVersion.ToString());

            if (WindowState == FormWindowState.Normal)
            {
                //Windows Bound-------------------------------------------------------------------
                GlobalSetting.SetConfig($"{Name}.WindowsBound", GlobalSetting.RectToString(Bounds));
            }

            //Windows State-------------------------------------------------------------------
            GlobalSetting.SetConfig($"{Name}.WindowsState", WindowState.ToString());

            //Checked background
            GlobalSetting.SetConfig("IsShowCheckedBackground", GlobalSetting.IsShowCheckedBackground.ToString());

            //Tool bar state
            GlobalSetting.SetConfig("IsShowToolBar", GlobalSetting.IsShowToolBar.ToString());

            //Window always on top
            GlobalSetting.SetConfig("IsWindowAlwaysOnTop", GlobalSetting.IsWindowAlwaysOnTop.ToString());
            
            //Zoom to fit
            GlobalSetting.SetConfig("IsZoomToFit", GlobalSetting.IsZoomToFit.ToString());

            //Lock zoom ratio
            GlobalSetting.SetConfig("ZoomLockValue", (GlobalSetting.IsEnabledZoomLock) ? GlobalSetting.ZoomLockValue.ToString(GlobalSetting.NumberFormat) : "-1");

            //Thumbnail panel
            GlobalSetting.SetConfig("IsShowThumbnail", GlobalSetting.IsShowThumbnail.ToString());
            
            // Save thumbnail bar orientation state
            GlobalSetting.SetConfig("IsThumbnailHorizontal", GlobalSetting.IsThumbnailHorizontal.ToString());

            //Save thumbnail bar width
            GlobalSetting.ThumbnailBarWidth = sp1.Width - sp1.SplitterDistance;
            GlobalSetting.SetConfig("ThumbnailBarWidth", GlobalSetting.ThumbnailBarWidth.ToString(GlobalSetting.NumberFormat));

            // Save vertical thumbnail bar width
            if (GlobalSetting.IsThumbnailHorizontal == false)
            {
                GlobalSetting.SetConfig("ThumbnailBarWidth", (sp1.Width - sp1.SplitterDistance).ToString(GlobalSetting.NumberFormat));
            }

            //Save previous image if it was modified
            if (File.Exists(LocalSetting.ImageModifiedPath))
            {
                DisplayTextMessage(GlobalSetting.LangPack.Items["frmMain._SaveChanges"], 1000);

                Application.DoEvents();
                ImageSaveChange();
            }
        }

        #endregion



        #region Form events
        protected override void WndProc(ref Message m)
        {
            //Check if the received message is WM_SHOWME
            if (m.Msg == NativeMethods.WM_SHOWME)
            {
                //Set frmMain of the first instance to TopMost
                if (WindowState == FormWindowState.Minimized)
                {
                    WindowState = FormWindowState.Normal;
                }
                // get our current "TopMost" value (ours will always be false though)
                bool top = TopMost;
                // make our form jump to the top of everything
                TopMost = true;
                // set it back to whatever it was
                TopMost = top;
            }
            //This message is sent when the form is dragged to a different monitor i.e. when
            //the bigger part of its are is on the new monitor. 
            else if (m.Msg == DPIScaling.WM_DPICHANGED)
            {
                DPIScaling.CurrentDPI = DPIScaling.LOWORD((int)m.WParam);
                OnDpiChanged();                
            }
            base.WndProc(ref m);
        }
        

        private void frmMain_Load(object sender, EventArgs e)
        {
            //Remove white line under tool strip
            toolMain.Renderer = new Theme.ToolStripRenderer();

            //Trigger Mouse Wheel event
            picMain.MouseWheel += picMain_MouseWheel;

            LoadConfig();
            Application.DoEvents();

            //Try to use a faster image clock for animating GIFs
            CheckAnimationClock(true);

            //Load image from param
            LoadFromParams(Environment.GetCommandLineArgs());
        }

        public void LoadFromParams(string[] args)
        {
            //Load image from param
            if (args.Length >= 2)
            {
                for (int i = 1; i < args.Length; i++)
                {
                    //only read the path, exclude configs parameter which starts with "--"
                    if(!args[i].StartsWith("--"))
                    {
                        string filename = args[i];

                        if (File.Exists(filename))
                        {
                            FileInfo f = new FileInfo(filename);
                            Prepare(f.FullName);
                        }
                        else if (Directory.Exists(filename))
                        {
                            DirectoryInfo d = new DirectoryInfo(filename);
                            Prepare(d.FullName);
                        }

                        break;
                    }
                }
                
            }
        }

        private void frmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            //clear temp files
            string temp_dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"ImageGlass\Temp");
            if (Directory.Exists(temp_dir))
            {
                Directory.Delete(temp_dir, true);
            }            

            SaveConfig();
        }

        private void frmMain_Deactivate(object sender, EventArgs e)
        {
        }

        private void frmMain_Activated(object sender, EventArgs e)
        {
            if (GlobalSetting.IsForcedActive)
            {
                //Update thumbnail bar position--------
                GlobalSetting.IsShowThumbnail = !GlobalSetting.IsShowThumbnail;
                mnuMainThumbnailBar_Click(null, null);

                //Update thumbnail image size
                if(LocalSetting.IsThumbnailDimensionChanged)
                {
                    LocalSetting.IsThumbnailDimensionChanged = false;

                    LoadThumbnails();
                }

                //Update scrollbars visibility
                if (GlobalSetting.IsScrollbarsVisible)
                {
                    picMain.HorizontalScrollBarStyle = ImageBoxScrollBarStyle.Auto;
                    picMain.VerticalScrollBarStyle = ImageBoxScrollBarStyle.Auto;
                }
                else
                {
                    picMain.HorizontalScrollBarStyle = ImageBoxScrollBarStyle.Hide;
                    picMain.VerticalScrollBarStyle = ImageBoxScrollBarStyle.Hide;
                }
                

                //Update background---------------------
                picMain.BackColor = GlobalSetting.BackgroundColor;

                //Update language pack------------------
                RightToLeft = GlobalSetting.LangPack.IsRightToLeftLayout;

                //Update slideshow interval value of timer
                timSlideShow.Interval = GlobalSetting.SlideShowInterval * 1000;

                //Prevent zooming by scrolling mouse
                _isZoomed = picMain.AllowZoom = !GlobalSetting.IsMouseNavigation;

                #region Update language strings
                //Toolbar
                btnBack.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnBack"];
                btnNext.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnNext"];
                btnRotateLeft.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnRotateLeft"];
                btnRotateRight.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnRotateRight"];
                btnZoomIn.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnZoomIn"];
                btnZoomOut.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnZoomOut"];
                btnActualSize.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnActualSize"];
                btnZoomLock.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnZoomLock"];
                btnScaletoWidth.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnScaletoWidth"];
                btnScaletoHeight.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnScaletoHeight"];
                btnWindowAutosize.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnWindowAutosize"];
                btnOpen.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnOpen"];
                btnRefresh.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnRefresh"];
                btnGoto.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnGoto"];
                btnThumb.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnThumb"];
                btnCheckedBackground.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnCaro"];
                btnFullScreen.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnFullScreen"];
                btnSlideShow.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnSlideShow"];
                btnConvert.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnConvert"];
                btnPrintImage.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnPrintImage"];
                btnFacebook.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnFacebook"];
                btnExtension.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnExtension"];
                btnSetting.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnSetting"];
                btnHelp.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnHelp"];
                btnMenu.ToolTipText = GlobalSetting.LangPack.Items["frmMain.btnMenu"];

                //Main menu
                mnuMainOpenFile.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainOpenFile"];
                mnuMainOpenImageData.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainOpenImageData"];
                mnuMainSaveAs.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainSaveAs"];
                mnuMainRefresh.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainRefresh"];
                mnuMainEditImage.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainEditImage"];

                mnuMainNavigation.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainNavigation"];
                mnuMainViewNext.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainViewNext"];
                mnuMainViewPrevious.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainViewPrevious"];
                mnuMainGoto.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainGoto"];
                mnuMainGotoFirst.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainGotoFirst"];
                mnuMainGotoLast.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainGotoLast"];

                mnuMainFullScreen.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainFullScreen"];

                mnuMainSlideShow.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainSlideShow"];
                mnuMainSlideShowStart.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainSlideShowStart"];
                mnuMainSlideShowPause.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainSlideShowPause"];
                mnuMainSlideShowExit.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainSlideShowExit"];

                mnuMainPrint.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainPrint"];

                mnuMainManipulation.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainManipulation"];
                mnuMainRotateCounterclockwise.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainRotateCounterclockwise"];
                mnuMainRotateClockwise.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainRotateClockwise"];
                mnuMainZoomIn.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainZoomIn"];
                mnuMainZoomOut.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainZoomOut"];
                mnuMainZoomToFit.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainZoomToFit"];
                mnuMainActualSize.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainActualSize"];
                mnuMainLockZoomRatio.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainLockZoomRatio"];
                mnuMainScaleToWidth.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainScaleToWidth"];
                mnuMainScaleToHeight.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainScaleToHeight"];
                mnuMainWindowAdaptImage.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainWindowAdaptImage"];
                mnuMainRename.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainRename"];
                mnuMainMoveToRecycleBin.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainMoveToRecycleBin"];
                mnuMainDeleteFromHardDisk.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainDeleteFromHardDisk"];
                mnuMainExtractFrames.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainExtractFrames"];
                mnuMainStartStopAnimating.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainStartStopAnimating"];
                mnuMainSetAsDesktop.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainSetAsDesktop"];
                mnuMainImageLocation.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainImageLocation"];
                mnuMainImageProperties.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainImageProperties"];

                mnuMainClipboard.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainClipboard"];
                mnuMainCopy.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainCopy"];
                mnuMainCopyMulti.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainCopyMulti"];
                mnuMainCut.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainCut"];
                mnuMainCutMulti.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainCutMulti"];
                mnuMainCopyImagePath.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainCopyImagePath"];
                mnuMainClearClipboard.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainClearClipboard"];

                mnuMainShare.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainShare"];
                mnuMainShareFacebook.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainShareFacebook"];

                mnuMainLayout.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainLayout"];
                mnuMainToolbar.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainToolbar"];
                mnuMainThumbnailBar.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainThumbnailBar"];
                mnuMainCheckBackground.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainCheckBackground"];
                mnuMainAlwaysOnTop.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainAlwaysOnTop"];

                mnuMainTools.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainTools"];
                mnuMainExtensionManager.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainExtensionManager"];
                mnuMainColorPicker.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainColorPicker"];

                mnuMainSettings.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainSettings"];
                mnuMainAbout.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainAbout"];
                mnuMainReportIssue.Text = GlobalSetting.LangPack.Items["frmMain.mnuMainReportIssue"];
                #endregion

            }

            GlobalSetting.IsForcedActive = false;
        }

        private void frmMain_ResizeBegin(object sender, EventArgs e)
        {
            _windowSize = Size;
        }

        private void frmMain_ResizeEnd(object sender, EventArgs e)
        {
            if (Size != _windowSize && !_isZoomed)
            {
                mnuMainRefresh_Click(null, null);

                SaveConfig();
            }
            
        }

        private void thumbnailBar_ItemClick(object sender, ImageListView.ItemClickEventArgs e)
        {
            GlobalSetting.CurrentIndex = e.Item.Index;
            NextPic(0);
        }

        private void timSlideShow_Tick(object sender, EventArgs e)
        {
            NextPic(1);

            //stop playing slideshow at last image
            if (GlobalSetting.CurrentIndex == GlobalSetting.ImageList.Length - 1)
            {
                if (!GlobalSetting.IsLoopBackSlideShow)
                {
                    mnuMainSlideShowPause_Click(null, null);
                }
            }
        }

        private void sysWatch_Renamed(object sender, RenamedEventArgs e)
        {
            string newName = e.FullPath;
            string oldName = e.OldFullPath;

            //Get index of renamed image
            int imgIndex = GlobalSetting.ImageList.IndexOf(oldName);

            if (imgIndex > -1)
            {
                //Rename image list
                GlobalSetting.ImageList.SetFileName(imgIndex, newName);

                //Update status bar title
                UpdateStatusBar();

                try
                {
                    //Rename image in thumbnail bar
                    thumbnailBar.Items[imgIndex].Text = e.Name;
                    thumbnailBar.Items[imgIndex].Tag = newName;
                }
                catch { }
            }
        }
        
        private void sysWatch_Changed(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType == WatcherChangeTypes.Deleted)
            {
                //Get index of deleted image
                int imgIndex = GlobalSetting.ImageList.IndexOf(e.FullPath);

                if (imgIndex > -1)
                {
                    //delete image list
                    GlobalSetting.ImageList.Remove(imgIndex);

                    try
                    {
                        //delete thumbnail list
                        thumbnailBar.Items.RemoveAt(imgIndex);

                        //In case multiple files are deleted, to avoid the app doesnt crash
                        //We just display message instead
                        picMain.Text = GlobalSetting.LangPack.Items["frmMain._ImageNotExist"];
                        picMain.Image = null;
                        this.Text = "ImageGlass";
                        LocalSetting.ImageModifiedPath = "";
                    }
                    catch (Exception ex) { }

                }
            }
            else if (e.ChangeType == WatcherChangeTypes.Created)
            {
                if (!File.Exists(e.FullPath))
                {
                    return;
                }

                //Add the new image to the list
                GlobalSetting.ImageList.AddItem(e.FullPath);

                //Add the new image to thumbnail bar
                ImageListView.ImageListViewItem lvi = new ImageListView.ImageListViewItem(e.FullPath);
                lvi.Tag = e.FullPath;
                thumbnailBar.Items.Add(lvi);

            }
        }

        // Use mouse wheel to navigate images
        private void picMain_MouseWheel(object sender, MouseEventArgs e)
        {
            if (GlobalSetting.IsMouseNavigation && !_isZoomed)
            {
                //Prevent picmain zooming
                picMain.AllowZoom = false;

                if (e.Delta < 0)
                {
                    //Next pic
                    mnuMainViewNext_Click(null, null);
                }
                else
                {
                    //Previous pic
                    mnuMainViewPrevious_Click(null, null);
                }
            }
        }

        private void picMain_Zoomed(object sender, ImageBoxZoomEventArgs e)
        {
            if (!GlobalSetting.IsMouseNavigation)
            {
                _isZoomed = true;

                if (GlobalSetting.IsEnabledZoomLock)
                {
                    GlobalSetting.ZoomLockValue = e.NewZoom;
                }

                //Zoom optimization
                ZoomOptimization();

                UpdateStatusBar(true);
            }            
        }

        private void picMain_DoubleClick(object sender, EventArgs e)
        {
            if (picMain.Zoom < 100)
            {
                mnuMainActualSize_Click(null, null);
            }
            else
            {
                mnuMainRefresh_Click(null, null);
            }
        }

        private void picMain_MouseClick(object sender, MouseEventArgs e)
        {
            switch (e.Button)
            {
                case MouseButtons.Middle: //Refresh
                    mnuMainRefresh_Click(null, null);
                    break;

                case MouseButtons.XButton1: //Back
                    mnuMainViewPrevious_Click(null, null);
                    break;

                case MouseButtons.XButton2: //Next
                    mnuMainViewNext_Click(null, null);
                    break;

                default:
                    break;
            }
        }

        private void toolMain_SizeChanged(object sender, EventArgs e)
        {
            if (toolMain.PreferredSize.Width > toolMain.Size.Width)
            {
                btnMenu.Alignment = ToolStripItemAlignment.Left;
            }
            else
            {
                btnMenu.Alignment = ToolStripItemAlignment.Right;
            }
        }

        #endregion



        #region Toolbar Button
        private void btnNext_Click(object sender, EventArgs e)
        {
            mnuMainViewNext_Click(null, e);
        }

        private void btnBack_Click(object sender, EventArgs e)
        {
            mnuMainViewPrevious_Click(null, e);
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            mnuMainRefresh_Click(null, e);
        }

        private void btnRotateRight_Click(object sender, EventArgs e)
        {
            mnuMainRotateClockwise_Click(null, e);
        }

        private void btnRotateLeft_Click(object sender, EventArgs e)
        {
            mnuMainRotateCounterclockwise_Click(null, e);
        }

        private void btnOpen_Click(object sender, EventArgs e)
        {
            mnuMainOpenFile_Click(null, e);
        }

        private void btnThumb_Click(object sender, EventArgs e)
        {
            mnuMainThumbnailBar_Click(null, e);
        }

        private void btnActualSize_Click(object sender, EventArgs e)
        {
            mnuMainActualSize_Click(null, e);
        }

        private void btnScaletoWidth_Click(object sender, EventArgs e)
        {
            mnuMainScaleToWidth_Click(null, e);
        }

        private void btnScaletoHeight_Click(object sender, EventArgs e)
        {
            mnuMainScaleToHeight_Click(null, e);
        }

        private void btnWindowAutosize_Click(object sender, EventArgs e)
        {
            mnuMainWindowAdaptImage_Click(null, e);
        }

        private void btnGoto_Click(object sender, EventArgs e)
        {
            mnuMainGoto_Click(null, e);
        }

        private void btnCheckedBackground_Click(object sender, EventArgs e)
        {
            mnuMainCheckBackground_Click(null, e);
        }

        private void btnZoomIn_Click(object sender, EventArgs e)
        {
            mnuMainZoomIn_Click(null, e);
        }

        private void btnZoomOut_Click(object sender, EventArgs e)
        {
            mnuMainZoomOut_Click(null, e);
        }

        private void btnZoomLock_Click(object sender, EventArgs e)
        {
            mnuMainLockZoomRatio_Click(null, e);
        }

        private void btnSlideShow_Click(object sender, EventArgs e)
        {
            mnuMainSlideShowStart_Click(null, null);
        }

        private void btnFullScreen_Click(object sender, EventArgs e)
        {
            mnuMainFullScreen_Click(null, e);
        }

        private void btnPrintImage_Click(object sender, EventArgs e)
        {
            mnuMainPrint_Click(null, e);
        }

        private void btnFacebook_Click(object sender, EventArgs e)
        {
            mnuMainShareFacebook_Click(null, e);
        }

        private void btnExtension_Click(object sender, EventArgs e)
        {
            mnuMainExtensionManager_Click(null, e);
        }

        private void btnSetting_Click(object sender, EventArgs e)
        {
            mnuMainSettings_Click(null, e);
        }

        private void btnHelp_Click(object sender, EventArgs e)
        {
            mnuMainAbout_Click(null, e);
        }

        private void btnConvert_Click(object sender, EventArgs e)
        {
            mnuMainSaveAs_Click(null, e);
        }

        private void btnReport_Click(object sender, EventArgs e)
        {
            mnuMainReportIssue_Click(null, e);
        }
        #endregion
        


        #region Popup Menu
        private void mnuPopup_Opening(object sender, CancelEventArgs e)
        {
            try
            {
                if (!File.Exists(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)) ||
                                 GlobalSetting.IsImageError)
                {
                    e.Cancel = true;
                    return;
                }
            }
            catch { e.Cancel = true; return; }

            //clear current items
            mnuPopup.Items.Clear();

            if (GlobalSetting.IsPlaySlideShow)
            {
                mnuPopup.Items.Add(Library.Menu.Clone(mnuMainSlideShowPause));
                mnuPopup.Items.Add(Library.Menu.Clone(mnuMainSlideShowExit));
                mnuPopup.Items.Add(new ToolStripSeparator());//---------------
            }
            
            //toolbar menu
            mnuPopup.Items.Add(Library.Menu.Clone(mnuMainToolbar));
            mnuPopup.Items.Add(Library.Menu.Clone(mnuMainAlwaysOnTop));
            mnuPopup.Items.Add(new ToolStripSeparator());//---------------

            //Get Editing Assoc App info
            UpdateEditingAssocAppInfoForMenu();
            mnuPopup.Items.Add(Library.Menu.Clone(mnuMainEditImage));
            
            //check if image can animate (GIF)
            try
            {
                Image img = GlobalSetting.ImageList.GetImage(GlobalSetting.CurrentIndex);
                FrameDimension dim = new FrameDimension(img.FrameDimensionsList[0]);
                int frameCount = img.GetFrameCount(dim);

                if (frameCount > 1)
                {
                    var mi = Library.Menu.Clone(mnuMainExtractFrames);
                    mi.Text = string.Format(GlobalSetting.LangPack.Items["frmMain.mnuMainExtractFrames"], frameCount);

                    mnuPopup.Items.Add(Library.Menu.Clone(mi));
                    mnuPopup.Items.Add(Library.Menu.Clone(mnuMainStartStopAnimating));
                }

            }
            catch { }


            mnuPopup.Items.Add(Library.Menu.Clone(mnuMainSetAsDesktop));

            mnuPopup.Items.Add(new ToolStripSeparator());//------------
            mnuPopup.Items.Add(Library.Menu.Clone(mnuMainOpenImageData));
            mnuPopup.Items.Add(Library.Menu.Clone(mnuMainCopy));
            mnuPopup.Items.Add(Library.Menu.Clone(mnuMainCut));
            mnuPopup.Items.Add(Library.Menu.Clone(mnuMainClearClipboard));

            mnuPopup.Items.Add(new ToolStripSeparator());//------------
            mnuPopup.Items.Add(Library.Menu.Clone(mnuMainRename));
            mnuPopup.Items.Add(Library.Menu.Clone(mnuMainMoveToRecycleBin));
            mnuPopup.Items.Add(Library.Menu.Clone(mnuMainDeleteFromHardDisk));

            mnuPopup.Items.Add(new ToolStripSeparator());//------------
            mnuPopup.Items.Add(Library.Menu.Clone(mnuMainShareFacebook));

            mnuPopup.Items.Add(new ToolStripSeparator());//------------
            mnuPopup.Items.Add(Library.Menu.Clone(mnuMainCopyImagePath));
            mnuPopup.Items.Add(Library.Menu.Clone(mnuMainImageLocation));
            mnuPopup.Items.Add(Library.Menu.Clone(mnuMainImageProperties));

        }
        #endregion



        #region Main Menu (Main function)

        private void mnuMainOpenFile_Click(object sender, EventArgs e)
        {
            OpenFile();
        }

        private void mnuMainOpenImageData_Click(object sender, EventArgs e)
        {
            //Is there a file in clipboard ?--------------------------------------------------
            if (Clipboard.ContainsFileDropList())
            {
                string[] sFile = (string[])Clipboard.GetData(DataFormats.FileDrop);
                int fileCount = 0;

                fileCount = sFile.Length;

                // load file
                Prepare(sFile[0]);
            }


            //Is there a image in clipboard ?-------------------------------------------------
            //CheckImageInClipboard: ;
            else if (Clipboard.ContainsImage())
            {
                picMain.Image = Clipboard.GetImage();
                GlobalSetting.IsTempMemoryData = true;
            }

            //Is there a filename in clipboard?-----------------------------------------------
            //CheckPathInClipboard: ;
            else if (Clipboard.ContainsText())
            {
                if (File.Exists(Clipboard.GetText()) || Directory.Exists(Clipboard.GetText()))
                {
                    Prepare(Clipboard.GetText());
                }
                //get image from Base64string 
                else
                {
                    try
                    {
                        // data:image/jpeg;base64,xxxxxxxx
                        string base64str = Clipboard.GetText().Substring(Clipboard.GetText().LastIndexOf(',') + 1);
                        var file_bytes = Convert.FromBase64String(base64str);
                        var file_stream = new MemoryStream(file_bytes);
                        var file_image = Image.FromStream(file_stream);

                        picMain.Image = file_image;
                        GlobalSetting.IsTempMemoryData = true;
                    }
                    catch { }
                }
            }
        }

        private void mnuMainSaveAs_Click(object sender, EventArgs e)
        {
            if (picMain.Image == null)
            {
                return;
            }

            string filename = GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex);
            if (filename == "")
            {
                filename = "untitled.png";
            }

            Library.Image.ImageInfo.ConvertImage(picMain.Image, filename);
        }

        private void mnuMainRefresh_Click(object sender, EventArgs e)
        {
            // Reset scrollbar position
            if (LocalSetting.IsResetScrollPosition)
            {
                picMain.ScrollTo(0, 0, 0, 0);
            }
            
            //Zoom condition
            if (GlobalSetting.IsEnabledZoomLock)
            {
                picMain.Zoom = GlobalSetting.ZoomLockValue;
            }
            else
            {
                //Reset zoom
                if (GlobalSetting.IsZoomToFit)
                {
                    picMain.ZoomToFit();
                }
                else
                {
                    picMain.ZoomAuto();
                }

                _isZoomed = false;
            }

            //Get image file information
            UpdateStatusBar();
        }

        private void mnuMainEditImage_Click(object sender, EventArgs e)
        {
            if (GlobalSetting.IsImageError)
            {
                return;
            }

            // Viewing image filename
            string filename = GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex);

            // If viewing image is temporary memory data
            if (GlobalSetting.IsTempMemoryData)
            {
                // Save to temp file
                filename = SaveTemporaryMemoryData();

                EditByDefaultApp();
            }
            else
            {
                // Get extension
                var ext = Path.GetExtension(filename).ToLower();

                // Get association App for editing
                var assoc = GlobalSetting.GetImageEditingAssociationFromList(ext);

                if (assoc != null && File.Exists(assoc.AppPath))
                {
                    // Open configured app for editing
                    Process p = new Process();
                    p.StartInfo.FileName = assoc.AppPath;
                    p.StartInfo.Arguments = $"\"{filename}\" {assoc.AppArguments}";

                    //show error dialog
                    p.StartInfo.ErrorDialog = true;

                    try
                    {
                        p.Start();
                    }
                    catch (Exception)
                    { }
                }
                else // Edit by default associated app
                {
                    EditByDefaultApp();
                }
            }

            void EditByDefaultApp()
            {
                Process p = new Process();
                p.StartInfo.FileName = filename;
                p.StartInfo.Verb = "edit";

                //show error dialog
                p.StartInfo.ErrorDialog = true;

                try
                {
                    p.Start();
                }
                catch (Exception)
                { }
            }
        }

        private void mnuMainViewNext_Click(object sender, EventArgs e)
        {
            if (GlobalSetting.ImageList.Length < 1)
            {
                return;
            }

            NextPic(1);
        }

        private void mnuMainViewPrevious_Click(object sender, EventArgs e)
        {
            if (GlobalSetting.ImageList.Length < 1)
            {
                return;
            }

            NextPic(-1);
        }

        private void mnuMainGoto_Click(object sender, EventArgs e)
        {
            int n = GlobalSetting.CurrentIndex;
            string s = "0";
            if (InputBox.ShowDiaLog("Message", GlobalSetting.LangPack.Items["frmMain._GotoDialogText"],
                                    "0", true, this.TopMost) == DialogResult.OK)
            {
                s = InputBox.Message;
            }

            if (int.TryParse(s, out n))
            {
                n--;

                if (-1 < n && n < GlobalSetting.ImageList.Length)
                {
                    GlobalSetting.CurrentIndex = n;
                    NextPic(0);
                }
            }
        }

        private void mnuMainGotoFirst_Click(object sender, EventArgs e)
        {
            GlobalSetting.CurrentIndex = 0;
            NextPic(0);
        }

        private void mnuMainGotoLast_Click(object sender, EventArgs e)
        {
            GlobalSetting.CurrentIndex = GlobalSetting.ImageList.Length - 1;
            NextPic(0);
        }

        private void mnuMainFullScreen_Click(object sender, EventArgs e)
        {
            //full screen
            if (!GlobalSetting.IsFullScreen)
            {
                SaveConfig();

                //save last state of toolbar
                _isShowToolbar = GlobalSetting.IsShowToolBar;

                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                GlobalSetting.IsFullScreen = true;
                Application.DoEvents();
                Bounds = Screen.FromControl(this).Bounds;

                //Hide
                toolMain.Visible = false;
                GlobalSetting.IsShowToolBar = false;
                mnuMainToolbar.Checked = false;

                DisplayTextMessage(GlobalSetting.LangPack.Items["frmMain._FullScreenMessage"]
                    , 2000);
            }
            //exit full screen
            else
            {
                //restore last state of toolbar
                GlobalSetting.IsShowToolBar = _isShowToolbar;

                FormBorderStyle = FormBorderStyle.Sizable;

                //windows state
                string state_str = GlobalSetting.GetConfig($"{Name}.WindowsState", "Normal");
                if (state_str == "Normal")
                {
                    WindowState = FormWindowState.Normal;
                }
                else if (state_str == "Maximized")
                {
                    WindowState = FormWindowState.Maximized;
                }

                //Windows Bound (Position + Size)
                Bounds = GlobalSetting.StringToRect(GlobalSetting.GetConfig($"{Name}.WindowsBound", "280,125,750,545"));

                GlobalSetting.IsFullScreen = false;
                Application.DoEvents();

                if (GlobalSetting.IsShowToolBar)
                {
                    //Show toolbar
                    toolMain.Visible = true;
                    mnuMainToolbar.Checked = true;
                }
            }
        }
        

        private void mnuMainSlideShowStart_Click(object sender, EventArgs e)
        {
            if (GlobalSetting.ImageList.Length < 1)
            {
                return;
            }

            //not performing
            if (!GlobalSetting.IsPlaySlideShow)
            {
                //perform slideshow
                picMain.BackColor = Color.Black;
                btnFullScreen.PerformClick();

                timSlideShow.Start();
                timSlideShow.Enabled = true;

                GlobalSetting.IsPlaySlideShow = true;
            }
            //performing
            else
            {
                mnuMainSlideShowExit_Click(null, null);
            }

            DisplayTextMessage(GlobalSetting.LangPack.Items["frmMain._SlideshowMessage"], 2000);
        }

        private void mnuMainSlideShowPause_Click(object sender, EventArgs e)
        {
            //performing
            if (timSlideShow.Enabled)
            {
                timSlideShow.Enabled = false;
                timSlideShow.Stop();

                DisplayTextMessage(GlobalSetting.LangPack.Items["frmMain._SlideshowMessagePause"], 2000);
            }
            else
            {
                timSlideShow.Enabled = true;
                timSlideShow.Start();

                DisplayTextMessage(GlobalSetting.LangPack.Items["frmMain._SlideshowMessageResume"], 2000);
            }

        }

        private void mnuMainSlideShowExit_Click(object sender, EventArgs e)
        {
            timSlideShow.Stop();
            timSlideShow.Enabled = false;
            GlobalSetting.IsPlaySlideShow = false;

            picMain.BackColor = GlobalSetting.BackgroundColor;
            btnFullScreen.PerformClick();
        }

        /// <summary>
        /// Save current loaded image to file and print it
        /// </summary>
        private string SaveTemporaryMemoryData()
        {
            if (!Directory.Exists(GlobalSetting.TempDir))
            {
                Directory.CreateDirectory(GlobalSetting.TempDir);
            }

            string filename = Path.Combine(GlobalSetting.TempDir, "temp_" + DateTime.Now.ToString("yyyy-MM-dd-hh-mm-ss") + ".png");

            picMain.Image.Save(filename, ImageFormat.Png);

            return filename;
        }

        private void mnuMainPrint_Click(object sender, EventArgs e)
        {
            string temFile = "";
            
            //image error
            if (GlobalSetting.ImageList.Length < 1 || GlobalSetting.IsImageError)
            {
                return;
            }
            else
            {
                //save image to temp file
                temFile = SaveTemporaryMemoryData();
            }

            Process p = new Process();
            p.StartInfo.FileName = temFile;
            p.StartInfo.Verb = "print";

            //show error dialog
            p.StartInfo.ErrorDialog = true;

            try
            {
                p.Start();
            }
            catch (Exception)
            { }

        }

        private void mnuMainRotateCounterclockwise_Click(object sender, EventArgs e)
        {
            if (picMain.Image == null || picMain.CanAnimate)
            {
                return;
            }

            Bitmap bmp = new Bitmap(picMain.Image);
            bmp.RotateFlip(RotateFlipType.Rotate270FlipNone);
            picMain.Image = bmp;

            /*
            try
            {
                LocalSetting.ImageModifiedPath = GlobalSetting.ImageFilenameList[GlobalSetting.CurrentIndex];
            }
            catch { }
            */
        }

        private void mnuMainRotateClockwise_Click(object sender, EventArgs e)
        {
            if (picMain.Image == null || picMain.CanAnimate)
            {
                return;
            }

            Bitmap bmp = new Bitmap(picMain.Image);
            bmp.RotateFlip(RotateFlipType.Rotate90FlipNone);
            picMain.Image = bmp;

            /*
            try
            {
                LocalSetting.ImageModifiedPath = GlobalSetting.ImageFilenameList[GlobalSetting.CurrentIndex];
            }
            catch { }
            */
        }

        private void mnuMainZoomIn_Click(object sender, EventArgs e)
        {
            if (picMain.Image == null)
            {
                return;
            }

            picMain.ZoomIn();
        }

        private void mnuMainZoomOut_Click(object sender, EventArgs e)
        {
            if (picMain.Image == null)
            {
                return;
            }

            picMain.ZoomOut();
        }

        private void mnuMainZoomToFit_Click(object sender, EventArgs e)
        {
            GlobalSetting.IsZoomToFit = mnuMainZoomToFit.Checked;
            mnuMainRefresh_Click(null, null);
        }

        private void mnuMainActualSize_Click(object sender, EventArgs e)
        {
            if (picMain.Image == null)
            {
                return;
            }

            picMain.ActualSize();
            picMain.CenterToImage();            
        }

        private void mnuMainLockZoomRatio_Click(object sender, EventArgs e)
        {
            if (!GlobalSetting.IsEnabledZoomLock)
            {
                GlobalSetting.IsEnabledZoomLock = btnZoomLock.Checked = true;
                GlobalSetting.ZoomLockValue = picMain.Zoom;
            }
            else
            {
                GlobalSetting.IsEnabledZoomLock = btnZoomLock.Checked = false;
                GlobalSetting.ZoomLockValue = 100;
            }
        }

        private void mnuMainScaleToWidth_Click(object sender, EventArgs e)
        {
            if (picMain.Image == null)
            {
                return;
            }

            // Reset scrollbar position
            if (LocalSetting.IsResetScrollPosition)
            {
                picMain.ScrollTo(0, 0, 0, 0);
            }

            // Scale to Width
            double frac = picMain.Width / (1.0 * picMain.Image.Width);
            picMain.Zoom = (int)(frac * 100);
        }

        private void mnuMainScaleToHeight_Click(object sender, EventArgs e)
        {
            if (picMain.Image == null)
            {
                return;
            }

            // Reset scrollbar position
            if (LocalSetting.IsResetScrollPosition)
            {
                picMain.ScrollTo(0, 0, 0, 0);
            }

            // Scale to Height
            double frac = picMain.Height / (1.0 * picMain.Image.Height);
            picMain.Zoom = (int)(frac * 100);
        }

        private void mnuMainWindowAdaptImage_Click(object sender, EventArgs e)
        {
            if (picMain.Image == null)
            {
                return;
            }
            
            Rectangle screen = Screen.FromControl(this).WorkingArea;
            WindowState = FormWindowState.Normal;

            //if image size is bigger than screen
            if (picMain.Image.Width >= screen.Width || picMain.Height >= screen.Height)
            {
                Left = Top = 0;
                Width = screen.Width;
                Height = screen.Height;
            }
            else
            {
                Size = new Size(Width += picMain.Image.Width - picMain.Width,
                                Height += picMain.Image.Height - picMain.Height);

                picMain.Bounds = new Rectangle(Point.Empty, picMain.Image.Size);
                Top = (screen.Height - Height) / 2 + screen.Top;
                Left = (screen.Width - Width) / 2 + screen.Left;
            }

            //reset zoom
            mnuMainRefresh_Click(null, null);
        }

        private void mnuMainRename_Click(object sender, EventArgs e)
        {
            RenameImage();
        }

        private void mnuMainMoveToRecycleBin_Click(object sender, EventArgs e)
        {
            try
            {
                if (!File.Exists(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)))
                {
                    return;
                }
            }
            catch { return; }

            DialogResult msg = DialogResult.Yes;

            if (GlobalSetting.IsConfirmationDelete)
            {
                msg = MessageBox.Show(string.Format(GlobalSetting.LangPack.Items["frmMain._DeleteDialogText"], GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)), GlobalSetting.LangPack.Items["frmMain._DeleteDialogTitle"], MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            }

            if (msg == DialogResult.Yes)
            {

                string f = GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex);
                try
                {
                    //in case of GIF file...
                    string ext = Path.GetExtension(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)).ToLower();
                    if (ext == ".gif")
                    {
                        try
                        {
                            //delete thumbnail list
                            thumbnailBar.Items.RemoveAt(GlobalSetting.CurrentIndex);
                        }
                        catch { }

                        //delete image list
                        GlobalSetting.ImageList.Remove(GlobalSetting.CurrentIndex);

                        NextPic(0);
                    }

                    ImageInfo.DeleteFile(f, true);

                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void mnuMainDeleteFromHardDisk_Click(object sender, EventArgs e)
        {
            try
            {
                if (!File.Exists(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)))
                {
                    return;
                }
            }
            catch { return; }

            DialogResult msg = MessageBox.Show(string.Format(GlobalSetting.LangPack.Items["frmMain._DeleteDialogText"], GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)), GlobalSetting.LangPack.Items["frmMain._DeleteDialogTitle"], MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (msg == DialogResult.Yes)
            {
                string f = GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex);
                try
                {
                    //If ext == GIF, release memory before deleting
                    string ext = Path.GetExtension(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)).ToLower();
                    if (ext == ".gif")
                    {
                        try
                        {
                            //delete thumbnail list
                            thumbnailBar.Items.RemoveAt(GlobalSetting.CurrentIndex);
                        }
                        catch { }

                        //delete image list
                        GlobalSetting.ImageList.Remove(GlobalSetting.CurrentIndex);

                        NextPic(0);
                    }

                    ImageInfo.DeleteFile(f);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void mnuMainExtractFrames_Click(object sender, EventArgs e)
        {
            if (!(sender as ToolStripMenuItem).Enabled) // Shortcut keys still work even when menu is disabled!
                return;

            if (!GlobalSetting.IsImageError)
            {
                FolderBrowserDialog f = new FolderBrowserDialog();
                f.Description = GlobalSetting.LangPack.Items["frmMain._ExtractFrameText"];
                f.ShowNewFolderButton = true;
                DialogResult res = f.ShowDialog();

                if (res == DialogResult.OK && Directory.Exists(f.SelectedPath))
                {
                    Animation ani = new Animation();
                    ani.ExtractAllFrames(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex),
                                                f.SelectedPath);
                }

                f = null;
            }
        }

        // ReSharper disable once EmptyGeneralCatchClause
        private void mnuMainSetAsDesktop_Click(object sender, EventArgs e)
        {
            if (GlobalSetting.IsImageError)
                return;

            try
            {
                Process p = new Process();
                p.StartInfo.FileName = GlobalSetting.StartUpDir + "igtasks.exe";
                p.StartInfo.Arguments = "setwallpaper " + //name of param
                                        "\"" + GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex) + "\" " + //arg 1
                                        "\"" + "0" + "\" "; //arg 2
                p.Start();
            }
            catch (Exception)
            { }
        }

        private void mnuMainImageLocation_Click(object sender, EventArgs e)
        {
            if (GlobalSetting.ImageList.Length > 0)
            {
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" +
                    GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex) + "\"");
            }
        }

        private void mnuMainImageProperties_Click(object sender, EventArgs e)
        {
            if (GlobalSetting.ImageList.Length > 0)
            {
                ImageInfo.DisplayFileProperties(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex),
                                                Handle);
            }
        }

        private void mnuMainCopy_Click(object sender, EventArgs e)
        {
            CopyFile();
        }

        private void mnuMainCopyMulti_Click(object sender, EventArgs e)
        {
            CopyMultiFiles();
        }

        private void mnuMainCut_Click(object sender, EventArgs e)
        {
            CutFile();
        }

        private void mnuMainCutMulti_Click(object sender, EventArgs e)
        {
            CutMultiFiles();
        }

        private void mnuMainCopyImagePath_Click(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex));
            }
            catch { }
        }

        private void mnuMainClearClipboard_Click(object sender, EventArgs e)
        {
            //clear copied files in clipboard
            if (GlobalSetting.StringClipboard.Count > 0)
            {
                GlobalSetting.StringClipboard = new StringCollection();
                Clipboard.Clear();
                DisplayTextMessage(GlobalSetting.LangPack.Items["frmMain._ClearClipboard"], 1000);
            }
        }

        private void mnuMainShareFacebook_Click(object sender, EventArgs e)
        {
            if (GlobalSetting.ImageList.Length > 0 && File.Exists(GlobalSetting.ImageList.GetFileName(GlobalSetting.CurrentIndex)))
            {
                if (LocalSetting.FFacebook.IsDisposed)
                {
                    LocalSetting.FFacebook = new frmFacebook();
                }

                //CHECK FILE EXTENSION BEFORE UPLOADING
                string tempFile = "";

                
                //image error
                if (GlobalSetting.ImageList.Length < 1 || GlobalSetting.IsImageError)
                {
                    return;
                }
                else
                {
                    //save image to tem file
                    tempFile = SaveTemporaryMemoryData();
                }

                LocalSetting.FFacebook.TopMost = this.TopMost;
                LocalSetting.FFacebook.Filename = tempFile;
                GlobalSetting.IsForcedActive = false;
                LocalSetting.FFacebook.Show();
                LocalSetting.FFacebook.Activate();
            }
        }

        private void mnuMainToolbar_Click(object sender, EventArgs e)
        {
            GlobalSetting.IsShowToolBar = !GlobalSetting.IsShowToolBar;
            if (GlobalSetting.IsShowToolBar)
            {
                //Hien
                toolMain.Visible = true;
            }
            else
            {
                //An
                toolMain.Visible = false;
            }
            mnuMainToolbar.Checked = GlobalSetting.IsShowToolBar;
        }

        private void mnuMainThumbnailBar_Click(object sender, EventArgs e)
        {
            GlobalSetting.IsShowThumbnail = !GlobalSetting.IsShowThumbnail;
            sp1.Panel2Collapsed = !GlobalSetting.IsShowThumbnail;
            btnThumb.Checked = GlobalSetting.IsShowThumbnail;

            if (GlobalSetting.IsShowThumbnail)
            {
                float scaleFactor = ((float)DPIScaling.CurrentDPI) / DPIScaling.DPI_DEFAULT;
                int gap = (int)((SystemInformation.HorizontalScrollBarHeight * scaleFactor) + (25 / scaleFactor * 1.05));

                //show
                var tb = new ThumbnailItemInfo(GlobalSetting.ThumbnailDimension, GlobalSetting.IsThumbnailHorizontal);
                int minSize = tb.GetTotalDimension() + gap;
                //sp1.Panel2MinSize = tb.GetTotalDimension() + gap;


                int splitterDistance = Math.Abs(sp1.Height - minSize);

                if (GlobalSetting.IsThumbnailHorizontal)
                {
                    // BOTTOM
                    sp1.SplitterWidth = 1;
                    sp1.Orientation = Orientation.Horizontal;
                    sp1.SplitterDistance = splitterDistance;
                    thumbnailBar.View = ImageListView.View.Gallery;
                }
                else
                {
                    // RIGHT
                    sp1.IsSplitterFixed = false; //Allow user to resize
                    sp1.SplitterWidth = (int)Math.Ceiling(3 * scaleFactor);
                    sp1.Orientation = Orientation.Vertical;
                    sp1.SplitterDistance = sp1.Width - GlobalSetting.ThumbnailBarWidth;
                    thumbnailBar.View = ImageListView.View.Thumbnails;
                }
            }
            else
            {
                //Save thumbnail bar width when closing
                if (!GlobalSetting.IsThumbnailHorizontal)
                {
                    GlobalSetting.ThumbnailBarWidth = sp1.Width - sp1.SplitterDistance;
                }
            }
            mnuMainThumbnailBar.Checked = GlobalSetting.IsShowThumbnail;
            SelectCurrentThumbnail();
        }

        private void mnuMainCheckBackground_Click(object sender, EventArgs e)
        {
            GlobalSetting.IsShowCheckedBackground = !GlobalSetting.IsShowCheckedBackground;
            btnCheckedBackground.Checked = GlobalSetting.IsShowCheckedBackground;

            if (btnCheckedBackground.Checked)
            {
                //show
                picMain.GridDisplayMode = ImageBoxGridDisplayMode.Client;
            }
            else
            {
                //hide
                picMain.GridDisplayMode = ImageBoxGridDisplayMode.None;
            }

            mnuMainCheckBackground.Checked = btnCheckedBackground.Checked;
        }

        private void mnuMainAlwaysOnTop_Click(object sender, EventArgs e)
        {
            TopMost = 
                mnuMainAlwaysOnTop.Checked = 
                GlobalSetting.IsWindowAlwaysOnTop = !GlobalSetting.IsWindowAlwaysOnTop;
        }

        private void mnuMainExtensionManager_Click(object sender, EventArgs e)
        {
            if (LocalSetting.FExtension.IsDisposed)
            {
                LocalSetting.FExtension = new frmExtension();
            }
            GlobalSetting.IsForcedActive = false;
            LocalSetting.FExtension.TopMost = this.TopMost;
            LocalSetting.FExtension.Show();
            LocalSetting.FExtension.Activate();
        }

        private void mnuMainColorPicker_Click(object sender, EventArgs e)
        {
            if (LocalSetting.FColorPicker.IsDisposed)
            {
                LocalSetting.FColorPicker = new frmColorPicker();
            }
            GlobalSetting.IsForcedActive = true;

            if (!LocalSetting.FColorPicker.Visible)
            {
                LocalSetting.FColorPicker.SetImageBox(picMain);
                LocalSetting.FColorPicker.Show(this);
            }
        }

        private void mnuMainSettings_Click(object sender, EventArgs e)
        {
            if (LocalSetting.FSetting.IsDisposed)
            {
                LocalSetting.FSetting = new frmSetting();
            }

            GlobalSetting.IsForcedActive = false;
            LocalSetting.FSetting.TopMost = this.TopMost;
            LocalSetting.FSetting.Show();
            LocalSetting.FSetting.Activate();
        }

        private void mnuMainAbout_Click(object sender, EventArgs e)
        {
            frmAbout f = new frmAbout();
            f.TopMost = this.TopMost;
            f.ShowDialog();
        }

        private void mnuMainReportIssue_Click(object sender, EventArgs e)
        {
            try
            {
                Process.Start("https://github.com/d2phap/ImageGlass/issues");
            }
            catch { }
        }

        private void mnuMainStartStopAnimating_Click(object sender, EventArgs e)
        {
            if (picMain.IsAnimating)
            {
                picMain.StopAnimating();
            }
            else
            {
                picMain.StartAnimating();
            }
        }

        private void mnuMain_Opening(object sender, CancelEventArgs e)
        {
            try
            {
                mnuMainExtractFrames.Enabled = false;
                mnuMainStartStopAnimating.Enabled = false;

                Image img = GlobalSetting.ImageList.GetImage(GlobalSetting.CurrentIndex);
                FrameDimension dim = new FrameDimension(img.FrameDimensionsList[0]);
                int frameCount = img.GetFrameCount(dim);

                mnuMainExtractFrames.Text = string.Format(GlobalSetting.LangPack.Items["frmMain.mnuMainExtractFrames"], frameCount);

                if (frameCount > 1)
                {
                    mnuMainExtractFrames.Enabled = true;
                    mnuMainStartStopAnimating.Enabled = true;
                }

                // Get association App for editing
                UpdateEditingAssocAppInfoForMenu();

            }
            catch { }
        }










        #endregion

        
    }
}
