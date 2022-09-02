using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MessageBox.Avalonia.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace UABEAvalonia
{
    public class MainWindow : Window
    {
        //controls
        private Menu menuMain;
        private MenuItem menuOpen;
        private MenuItem menuLoadPackageFile;
        private MenuItem menuClose;
        private MenuItem menuSave;
        private MenuItem menuCompress;
        private MenuItem menuCreateStandaloneInstaller;
        private MenuItem menuCreatePackageFile;
        private MenuItem menuExit;
        private MenuItem menuEditTypeDatabase;
        private MenuItem menuEditTypePackage;
        private MenuItem menuToggleDarkTheme;
        private MenuItem menuAbout;
        private TextBlock lblFileName;
        private ComboBox comboBox;
        private Button btnExport;
        private Button btnImport;
        private Button btnRemove;
        private Button btnInfo;
        private Button btnExportAll;
        private Button btnImportAll;
        private Button btnBatchFgo;

        private AssetsManager am;
        private BundleFileInstance bundleInst;

        private Dictionary<string, BundleReplacer> newFiles;
        private bool changesUnsaved; //sets false after saving
        private bool changesMade; //stays true even after saving
        private bool ignoreCloseEvent;

        public ObservableCollection<ComboBoxItem> comboItems;

        private string latestFile;

        public MainWindow()
        {
            //has to happen BEFORE initcomponent
            Initialized += MainWindow_Initialized;

            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            //generated items
            menuMain = this.FindControl<Menu>("menuMain");
            menuOpen = this.FindControl<MenuItem>("menuOpen");
            menuLoadPackageFile = this.FindControl<MenuItem>("menuLoadPackageFile");
            menuClose = this.FindControl<MenuItem>("menuClose");
            menuSave = this.FindControl<MenuItem>("menuSave");
            menuCompress = this.FindControl<MenuItem>("menuCompress");
            menuCreateStandaloneInstaller = this.FindControl<MenuItem>("menuCreateStandaloneInstaller");
            menuCreatePackageFile = this.FindControl<MenuItem>("menuCreatePackageFile");
            menuExit = this.FindControl<MenuItem>("menuExit");
            menuEditTypeDatabase = this.FindControl<MenuItem>("menuEditTypeDatabase");
            menuEditTypePackage = this.FindControl<MenuItem>("menuEditTypePackage");
            menuToggleDarkTheme = this.FindControl<MenuItem>("menuToggleDarkTheme");
            menuAbout = this.FindControl<MenuItem>("menuAbout");
            lblFileName = this.FindControl<TextBlock>("lblFileName");
            comboBox = this.FindControl<ComboBox>("comboBox");
            btnExport = this.FindControl<Button>("btnExport");
            btnImport = this.FindControl<Button>("btnImport");
            btnRemove = this.FindControl<Button>("btnRemove");
            btnInfo = this.FindControl<Button>("btnInfo");
            btnExportAll = this.FindControl<Button>("btnExportAll");
            btnImportAll = this.FindControl<Button>("btnImportAll");
            btnBatchFgo = this.FindControl<Button>("btnBatchFgo");
            //generated events
            menuOpen.Click += MenuOpen_Click;
            menuLoadPackageFile.Click += MenuLoadPackageFile_Click;
            menuClose.Click += MenuClose_Click;
            menuSave.Click += MenuSave_Click;
            menuCompress.Click += MenuCompress_Click;
            menuCreatePackageFile.Click += MenuCreatePackageFile_Click;
            menuExit.Click += MenuExit_Click;
            menuToggleDarkTheme.Click += MenuToggleDarkTheme_Click;
            menuAbout.Click += MenuAbout_Click;
            btnExport.Click += BtnExport_Click;
            btnImport.Click += BtnImport_Click;
            btnRemove.Click += BtnRemove_Click;
            btnInfo.Click += BtnInfo_Click;
            btnExportAll.Click += BtnExportAll_Click;
            btnBatchFgo.Click += BtnBatchFgo_Click;
            Closing += MainWindow_Closing;

            newFiles = new Dictionary<string, BundleReplacer>();
            changesUnsaved = false;
            changesMade = false;
            ignoreCloseEvent = false;

            AddHandler(DragDrop.DropEvent, Drop);

            ThemeHandler.UseDarkTheme = ConfigurationManager.Settings.UseDarkTheme;
        }

        private async void BtnBatchFgo_Click(object? sender, RoutedEventArgs e)
        {
            await MessageBoxUtil.ShowDialog(this, "����", "̫�鷳��");
        }

        private async void MainWindow_Initialized(object? sender, EventArgs e)
        {
            am = new AssetsManager();
            string classDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "classdata.tpk");
            if (File.Exists(classDataPath))
            {
                am.LoadClassPackage(classDataPath);
            }
            else
            {
                await MessageBoxUtil.ShowDialog(this, "Error", "Missing classdata.tpk by exe.\nPlease make sure it exists.");
                Close();
                Environment.Exit(1);
            }
        }

        async Task OpenFiles(string[] files)
        {
            string selectedFile = files[0];

            latestFile = selectedFile;

            DetectedFileType fileType = AssetBundleDetector.DetectFileType(selectedFile);

            await CloseAllFiles();

            //can you even have split bundles?
            if (fileType != DetectedFileType.Unknown)
            {
                if (selectedFile.EndsWith(".split0"))
                {
                    string? splitFilePath = await AskLoadSplitFile(selectedFile);
                    if (splitFilePath == null)
                        return;
                    else
                        selectedFile = splitFilePath;
                }
            }

            if (fileType == DetectedFileType.AssetsFile)
            {
                AssetsFileInstance fileInst = am.LoadAssetsFile(selectedFile, true);

                if (!await LoadOrAskTypeData(fileInst))
                    return;

                List<AssetsFileInstance> fileInstances = new List<AssetsFileInstance>();
                fileInstances.Add(fileInst);

                if (files.Length > 1)
                {
                    for (int i = 1; i < files.Length; i++)
                    {
                        string otherSelectedFile = files[i];
                        DetectedFileType otherFileType = AssetBundleDetector.DetectFileType(otherSelectedFile);
                        if (otherFileType == DetectedFileType.AssetsFile)
                        {
                            try
                            {
                                fileInstances.Add(am.LoadAssetsFile(otherSelectedFile, true));
                            }
                            catch { } // no warning if the file didn't load but was detects as assets file
                        }
                    }
                }

                InfoWindow info = new InfoWindow(am, fileInstances, false);
                info.Show();
            }
            else if (fileType == DetectedFileType.BundleFile)
            {
                bundleInst = am.LoadBundleFile(selectedFile, false);
                //don't pester user to decompress if it's only the header that is compressed
                if (AssetBundleUtil.IsBundleDataCompressed(bundleInst.file))
                {
                    AskLoadCompressedBundle(bundleInst);
                }
                else
                {
                    if ((bundleInst.file.bundleHeader6.flags & 0x3F) != 0) //header is compressed (most likely)
                        bundleInst.file.UnpackInfoOnly();
                    LoadBundle(bundleInst);
                }
            }
            else
            {
                await MessageBoxUtil.ShowDialog(this, "Error", "This doesn't seem to be an assets file or bundle.");
            }
        }

        async void Drop(object sender, DragEventArgs e)
        {
            string[] files = e.Data.GetFileNames().ToArray();

            if (files == null || files.Length == 0)
                return;

            await OpenFiles(files);
        }

        private async void MenuOpen_Click(object? sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Title = "Open assets or bundle file";
            ofd.Filters = new List<FileDialogFilter>() { new FileDialogFilter() { Name = "All files", Extensions = new List<string>() { "*" } } };
            ofd.AllowMultiple = true;
            string[]? files = await ofd.ShowAsync(this);

            if (files == null || files.Length == 0)
                return;

            await OpenFiles(files);
        }

        private async void MenuLoadPackageFile_Click(object? sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filters = new List<FileDialogFilter>() {
                new FileDialogFilter() { Name = "UABE Mod Installer Package", Extensions = new List<string>() { "emip" } }
            };

            string[]? fileList = await ofd.ShowAsync(this);

            if (fileList == null || fileList.Length == 0)
                return;

            string emipPath = fileList[0];

            if (emipPath != null && emipPath != string.Empty)
            {
                AssetsFileReader r = new AssetsFileReader(File.OpenRead(emipPath)); //todo close this
                InstallerPackageFile emip = new InstallerPackageFile();
                emip.Read(r);

                LoadModPackageDialog dialog = new LoadModPackageDialog(emip, am);
                await dialog.ShowDialog(this);
            }
        }

        private void MenuAbout_Click(object? sender, RoutedEventArgs e)
        {
            About about = new About();
            about.ShowDialog(this);
        }

        private async void MenuSave_Click(object? sender, RoutedEventArgs e)
        {
            await AskForLocationAndSave();
        }

        private async void MenuCompress_Click(object? sender, RoutedEventArgs e)
        {
            await AskForLocationAndCompress();
        }

        private async void MenuClose_Click(object? sender, RoutedEventArgs e)
        {
            await AskForSave();
            await CloseAllFiles();
        }

        private async void BtnExport_Click(object? sender, RoutedEventArgs e)
        {
            if (bundleInst != null && comboBox.SelectedItem != null)
            {
                int index = (int)((ComboBoxItem)comboBox.SelectedItem).Tag;

                AssetBundleDirectoryInfo06 dirInf = bundleInst.file.bundleInf6.dirInf[index];

                string bunAssetName = dirInf.name;

                SaveFileDialog sfd = new SaveFileDialog();
                sfd.Title = "Save as...";
                sfd.InitialFileName = bunAssetName;

                string file = await sfd.ShowAsync(this);

                if (file == null)
                    return;

                using FileStream fileStream = File.OpenWrite(file);

                AssetsFileReader bundleReader = bundleInst.file.reader;
                bundleReader.Position = bundleInst.file.bundleHeader6.GetFileDataOffset() + dirInf.offset;
                bundleReader.BaseStream.CopyToCompat(fileStream, dirInf.decompressedSize);
            }
        }

        private async void BtnImport_Click(object? sender, RoutedEventArgs e)
        {
            if (bundleInst != null)
            {
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.Title = "Open";

                string[] files = await ofd.ShowAsync(this);

                if (files == null || files.Length == 0)
                    return;

                string file = files[0];

                if (file == null)
                    return;

                ImportSerializedDialog dialog = new ImportSerializedDialog();
                bool isSerialized = await dialog.ShowDialog<bool>(this);

                //todo replacer from stream rather than bytes
                //also need to handle closing them somewhere
                //and replacers don't support closing
                byte[] fileBytes = File.ReadAllBytes(file);
                string fileName = Path.GetFileName(file);

                newFiles[fileName] = AssetImportExport.CreateBundleReplacer(fileName, isSerialized, fileBytes);

                //check for existing combobox item
                ComboBoxItem? comboBoxItem = comboItems.FirstOrDefault(i => (string)i.Content == fileName);
                if (comboBoxItem != null)
                {
                    comboBox.SelectedItem = comboBoxItem;
                }
                else
                {
                    //make a new one since this is a new file
                    comboItems.Add(new ComboBoxItem()
                    {
                        Content = fileName,
                        Tag = comboItems.Count
                    });
                    comboBox.SelectedIndex = comboItems.Count - 1;
                }

                SetBundleControlsEnabled(true, true); // since it's an import it's always going to have assets in the combobox at this point so we force all the buttons to enable in case there were some that were still disabled
                changesUnsaved = true;
                changesMade = true;
            }
        }

        private async void BtnRemove_Click(object? sender, RoutedEventArgs e)
        {
            if (bundleInst != null && comboBox.SelectedItem != null)
            {
                int index = (int)((ComboBoxItem)comboBox.SelectedItem).Tag;

                if (index < bundleInst.file.bundleInf6.dirInf.Length) // Pre-existing asset when bundle was opened
                {
                    string bunAssetName = bundleInst.file.bundleInf6.dirInf[index].name;
                    newFiles.Add(bunAssetName, AssetImportExport.CreateBundleRemover(bunAssetName, true));
                }
                else // this asset was most likely imported after the bundle was opened and hasn't been saved yet
                {
                    string bunAssetName = (string)((ComboBoxItem)comboBox.SelectedItem).Content;
                    if (newFiles.ContainsKey(bunAssetName))
                    {
                        newFiles.Remove(bunAssetName);
                    }
                }

                comboItems.Remove((ComboBoxItem)comboBox.SelectedItem);
                if (comboItems.Count > 0)
                {
                    comboBox.SelectedIndex = 0;
                }

                SetBundleControlsEnabled(true, comboItems.Count > 0);
                changesUnsaved = true;
                changesMade = true;
            }
        }

        private async void BtnInfo_Click(object? sender, RoutedEventArgs e)
        {
            if (bundleInst == null || comboBox.SelectedItem == null)
                return;

            object? indexObj = ((ComboBoxItem)comboBox.SelectedItem).Tag;
            if (indexObj == null)
                return;

            int index = (int)indexObj;

            AssetBundleFile bundleFile = bundleInst.file;

            string bunAssetName = bundleFile.bundleInf6.dirInf[index].name;

            //when we make a modification to an assets file in the bundle,
            //we replace the assets file in the manager. this way, all we
            //have to do is not reload from the bundle if our assets file
            //has been modified
            MemoryStream assetStream;
            if (!newFiles.ContainsKey(bunAssetName))
            {
                byte[] assetData = BundleHelper.LoadAssetDataFromBundle(bundleFile, index);
                assetStream = new MemoryStream(assetData);
            }
            else
            {
                //unused if the file already exists
                assetStream = null;
            }

            //warning: does not update if you import an assets file onto
            //a file that wasn't originally an assets file
            var fileInf = BundleHelper.GetDirInfo(bundleFile, index);
            long bundleEntryOffset = bundleFile.bundleHeader6.GetFileDataOffset() + fileInf.offset;
            DetectedFileType fileType = AssetBundleDetector.DetectFileType(bundleFile.reader, bundleEntryOffset);

            if (fileType == DetectedFileType.AssetsFile)
            {
                string assetMemPath = Path.Combine(bundleInst.path, bunAssetName);
                AssetsFileInstance fileInst = am.LoadAssetsFile(assetStream, assetMemPath, true);

                if (!await LoadOrAskTypeData(fileInst))
                    return;

                if (bundleInst != null && fileInst.parentBundle == null)
                    fileInst.parentBundle = bundleInst;

                InfoWindow info = new InfoWindow(am, new List<AssetsFileInstance> { fileInst }, true);
                info.Closing += InfoWindow_Closing;
                info.Show();
            }
            else
            {
                await MessageBoxUtil.ShowDialog(this,
                    "Error", "This doesn't seem to be a valid assets file.\n" +
                                "If you want to export a non-assets file,\n" +
                                "use Export.");
            }
        }

        private async void BtnExportAll_Click(object? sender, RoutedEventArgs e)
        {
            if (bundleInst == null)
                return;

            OpenFolderDialog ofd = new OpenFolderDialog();
            ofd.Title = "Select export directory";

            string? dir = await ofd.ShowAsync(this);

            if (dir == null || dir == string.Empty)
                return;

            for (int i = 0; i < bundleInst.file.bundleInf6.directoryCount; i++)
            {
                AssetBundleDirectoryInfo06 dirInf = bundleInst.file.bundleInf6.dirInf[i];

                string bunAssetName = dirInf.name;
                string bunAssetPath = Path.Combine(dir, bunAssetName);

                //create dirs if bundle contains / in path
                if (bunAssetName.Contains("\\") || bunAssetName.Contains("/"))
                {
                    string bunAssetDir = Path.GetDirectoryName(bunAssetPath);
                    if (!Directory.Exists(bunAssetDir))
                    {
                        Directory.CreateDirectory(bunAssetDir);
                    }
                }

                using FileStream fileStream = File.OpenWrite(bunAssetPath);

                AssetsFileReader bundleReader = bundleInst.file.reader;
                bundleReader.Position = bundleInst.file.bundleHeader6.GetFileDataOffset() + dirInf.offset;
                bundleReader.BaseStream.CopyToCompat(fileStream, dirInf.decompressedSize);
            }
        }

        private async void MenuCreatePackageFile_Click(object? sender, RoutedEventArgs e)
        {
            await MessageBoxUtil.ShowDialog(this, "Not implemented",
                "Bundle pkgs are not supported at the moment.\n" +
                "Trying to install an emip file? Try running\n" +
                "UABEAvalonia applyemip from the command line.");
        }

        private void MenuExit_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void MenuToggleDarkTheme_Click(object? sender, RoutedEventArgs e)
        {
            ConfigurationManager.Settings.UseDarkTheme = !ConfigurationManager.Settings.UseDarkTheme;

            // thanks avalonia
            await MessageBoxUtil.ShowDialog(this, "Note",
                "Themes will be updated when you restart.");
        }

        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!changesUnsaved || ignoreCloseEvent)
            {
                e.Cancel = false;
                ignoreCloseEvent = false;
            }
            else
            {
                e.Cancel = true;
                ignoreCloseEvent = true;

                await AskForSave();
                Close(); //calling Close() triggers Closing() again
            }
        }

        private void InfoWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (sender == null)
                return;

            InfoWindow window = (InfoWindow)sender;

            if (window.Workspace.fromBundle && window.ChangedAssetsDatas != null)
            {
                List<Tuple<AssetsFileInstance, byte[]>> assetDatas = window.ChangedAssetsDatas;

                ////file that user initially selected
                //AssetsFileInstance firstFile = window.Workspace.LoadedFiles[0];

                foreach (var tup in assetDatas)
                {
                    AssetsFileInstance fileInstance = tup.Item1;
                    byte[] assetData = tup.Item2;

                    string assetName = Path.GetFileName(fileInstance.path);

                    ////only modify assets file we opened for now
                    //if (fileInstance != firstFile)
                    //    continue;

                    BundleReplacer replacer = AssetImportExport.CreateBundleReplacer(assetName, true, assetData);
                    newFiles[assetName] = replacer;

                    //replace existing assets file in the manager
                    AssetsFileInstance? inst = am.files.FirstOrDefault(i => i.name.ToLower() == assetName.ToLower());
                    string assetsManagerName;

                    if (inst != null)
                    {
                        assetsManagerName = inst.path;
                        am.files.Remove(inst);
                    }
                    else //shouldn't happen
                    {
                        //we always load bundles from file, so this
                        //should always be somewhere on the disk
                        assetsManagerName = Path.Combine(bundleInst.path, assetName);
                    }

                    MemoryStream assetsStream = new MemoryStream(assetData);
                    am.LoadAssetsFile(assetsStream, assetsManagerName, true);
                }

                if (assetDatas.Count > 0)
                {
                    changesUnsaved = true;
                    changesMade = true;
                }
            }
        }


        private async Task<bool> LoadOrAskTypeData(AssetsFileInstance fileInst)
        {
            string uVer = fileInst.file.typeTree.unityVersion;
            if (am.LoadClassDatabaseFromPackage(uVer) == null)
            {
                VersionWindow version = new VersionWindow(uVer, am.classPackage);
                var newFile = await version.ShowDialog<ClassDatabaseFile>(this);
                if (newFile == null)
                    return false;

                am.classFile = newFile;
            }
            return true;
        }

        private async Task AskForLocationAndSave()
        {
            if (changesUnsaved && bundleInst != null)
            {
                SaveFileDialog sfd = new SaveFileDialog();
                sfd.Title = "Save as...";

                string? file = await sfd.ShowAsync(this);

                if (file == null)
                    return;

                if (Path.GetFullPath(file) == Path.GetFullPath(bundleInst.path))
                {
                    await MessageBoxUtil.ShowDialog(this,
                        "File in use", "Since this file is already open in UABEA, you must pick a new file name (sorry!)");
                    return;
                }

                SaveBundle(bundleInst, file);
            }
        }

        private async Task AskForSave()
        {
            if (changesUnsaved && bundleInst != null)
            {
                ButtonResult choice = await MessageBoxUtil.ShowDialog(this,
                    "Changes made", "You've modified this file. Would you like to save?",
                    ButtonEnum.YesNo);
                if (choice == ButtonResult.Yes)
                {
                    await AskForLocationAndSave();
                }
            }
        }

        private async Task AskForLocationAndCompress()
        {
            if (bundleInst != null)
            {
                SaveFileDialog sfd = new SaveFileDialog();
                sfd.Title = "Save as...";

                var file = await sfd.ShowAsync(this);

                if (file == null)
                    return;

                string tmpFile = null;


                const string lz4Option = "LZ4";
                const string lzmaOption = "LZMA";
                const string cancelOption = "Cancel";


                string result = await MessageBoxUtil.ShowDialogCustom(
                    this, "��ѡ��ѹ����ʽ", "LZ4: �ٶȿ죬�ļ���\nLZMA: �ٶ������ļ�С��FGOѡ�������",
                    lz4Option, lzmaOption, cancelOption);

                AssetBundleCompressionType compType = result switch
                {
                    lz4Option => AssetBundleCompressionType.LZ4,
                    lzmaOption => AssetBundleCompressionType.LZMA,
                    _ => AssetBundleCompressionType.NONE
                };

                if (compType != AssetBundleCompressionType.NONE)
                {
                    if (changesMade)
                    {
                        tmpFile = $"tmp_{DateTimeOffset.Now.ToUnixTimeMilliseconds()}";
                        SaveBundle(bundleInst, tmpFile);
                        await OpenFiles(new string[] { tmpFile });
                    }
                    lblFileName.Text = "ѹ���У�����رմ��ںͼ������ļ�...";
                    await CompressBundle(bundleInst, file, compType);
                }
                else
                {
                    lblFileName.Text = "��ȡ��ѹ������";
                    return;
                }

                await CloseAllFiles();
                lblFileName.Text = "ѹ������ɹ�";

                if (tmpFile != null)
                {
                    if (File.Exists(tmpFile))
                        File.Delete(tmpFile);
                }
            }
            else
            {
                await MessageBoxUtil.ShowDialog(this, "Note", "Please open a bundle file before using compress.");
            }
        }

        private async Task<string?> AskLoadSplitFile(string selectedFile)
        {
            ButtonResult splitRes = await MessageBoxUtil.ShowDialog(this,
                "Split file detected", "This file ends with .split0. Create merged file?\n",
                ButtonEnum.YesNoCancel);

            if (splitRes == ButtonResult.Yes)
            {
                SaveFileDialog sfd = new SaveFileDialog();
                sfd.Title = "Select location for merged file";
                sfd.Directory = Path.GetDirectoryName(selectedFile);
                sfd.InitialFileName = Path.GetFileName(selectedFile.Substring(0, selectedFile.Length - ".split0".Length));
                string splitFilePath = await sfd.ShowAsync(this);

                if (splitFilePath == null || splitFilePath == string.Empty)
                    return null;

                using (FileStream mergeFile = File.OpenWrite(splitFilePath))
                {
                    int idx = 0;
                    string thisSplitFileNoNum = selectedFile.Substring(0, selectedFile.Length - 1);
                    string thisSplitFileNum = selectedFile;
                    while (File.Exists(thisSplitFileNum))
                    {
                        using (FileStream thisSplitFile = File.OpenRead(thisSplitFileNum))
                        {
                            thisSplitFile.CopyTo(mergeFile);
                        }

                        idx++;
                        thisSplitFileNum = $"{thisSplitFileNoNum}{idx}";
                    };
                }
                return splitFilePath;
            }
            else if (splitRes == ButtonResult.No)
            {
                return selectedFile;
            }
            else //if (splitRes == ButtonResult.Cancel)
            {
                return null;
            }
        }

        private async void AskLoadCompressedBundle(BundleFileInstance bundleInst)
        {
            bundleInst.file.UnpackInfoOnly();
            string decompSize = Extensions.GetFormattedByteSize(GetBundleDataDecompressedSize(bundleInst.file));

            const string fileOption = "File";
            const string memoryOption = "Memory";
            const string cancelOption = "Cancel";
            string result = await MessageBoxUtil.ShowDialogCustom(
                this, "Note", "This bundle is compressed. Decompress to file or memory?\nSize: " + decompSize,
                fileOption, memoryOption, cancelOption);

            if (result == fileOption)
            {
                SaveFileDialog sfd = new SaveFileDialog();
                sfd.Title = "Save as...";
                sfd.Filters = new List<FileDialogFilter>() { new FileDialogFilter() { Name = "All files", Extensions = new List<string>() { "*" } } };

                string savePath;
                while (true)
                {
                    savePath = await sfd.ShowAsync(this);

                    if (savePath == "" || savePath == null)
                        return;

                    if (Path.GetFullPath(savePath) == Path.GetFullPath(bundleInst.path))
                    {
                        await MessageBoxUtil.ShowDialog(this,
                            "File in use", "Since this file is already open in UABEA, you must pick a new file name (sorry!)");
                        continue;
                    }
                    else
                    {
                        break;
                    }
                }

                DecompressToFile(bundleInst, savePath);
            }
            else if (result == memoryOption)
            {
                DecompressToMemory(bundleInst);
            }
            else //if (result == cancelOption || result == closeOption)
            {
                return;
            }

            LoadBundle(bundleInst);
        }

        private void DecompressToFile(BundleFileInstance bundleInst, string savePath)
        {
            AssetBundleFile bundle = bundleInst.file;

            FileStream bundleStream = File.Open(savePath, FileMode.Create);
            bundle.Unpack(bundle.reader, new AssetsFileWriter(bundleStream));

            bundleStream.Position = 0;

            AssetBundleFile newBundle = new AssetBundleFile();
            newBundle.Read(new AssetsFileReader(bundleStream), false);

            bundle.reader.Close();
            bundleInst.file = newBundle;
        }

        private void DecompressToMemory(BundleFileInstance bundleInst)
        {
            AssetBundleFile bundle = bundleInst.file;

            MemoryStream bundleStream = new MemoryStream();
            bundle.Unpack(bundle.reader, new AssetsFileWriter(bundleStream));

            bundleStream.Position = 0;

            AssetBundleFile newBundle = new AssetBundleFile();
            newBundle.Read(new AssetsFileReader(bundleStream), false);

            bundle.reader.Close();
            bundleInst.file = newBundle;
        }

        private void LoadBundle(BundleFileInstance bundleInst)
        {
            var infos = bundleInst.file.bundleInf6.dirInf;
            comboItems = new ObservableCollection<ComboBoxItem>();
            for (int i = 0; i < infos.Length; i++)
            {
                var info = infos[i];
                comboItems.Add(new ComboBoxItem()
                {
                    Content = info.name,
                    Tag = i
                });
            }
            comboBox.Items = comboItems;
            comboBox.SelectedIndex = 0;

            lblFileName.Text = bundleInst.name;

            SetBundleControlsEnabled(true, comboItems.Count > 0);
        }

        private void SaveBundle(BundleFileInstance bundleInst, string path)
        {
            using (FileStream fs = File.OpenWrite(path))
            using (AssetsFileWriter w = new AssetsFileWriter(fs))
            {
                bundleInst.file.Write(w, newFiles.Values.ToList());
            }
            changesUnsaved = false;
        }

        private static Task CompressBundle(BundleFileInstance bundleInst, string path, AssetBundleCompressionType compType)
        {
            return Task.Run(() =>
             {
                 using FileStream fs = File.OpenWrite(path);
                 using AssetsFileWriter w = new AssetsFileWriter(fs);
                 bundleInst.file.Pack(bundleInst.file.reader, w, compType);
             });
        }

        private Task CloseAllFiles()
        {
            newFiles.Clear();
            changesUnsaved = false;
            changesMade = false;

            am.UnloadAllAssetsFiles(true);
            am.UnloadAllBundleFiles();

            comboItems = new ObservableCollection<ComboBoxItem>();
            comboBox.Items = comboItems;

            SetBundleControlsEnabled(false);

            bundleInst = null;

            lblFileName.Text = "No file opened.";

            return Task.CompletedTask;
        }

        private void SetBundleControlsEnabled(bool enabled, bool hasAssets = false)
        {
            // buttons that i want to enable only if i have assets they can interact with, always disable when it's time to disable every button
            btnExport.IsEnabled = (enabled ? hasAssets : false);
            btnRemove.IsEnabled = (enabled ? hasAssets : false);
            btnInfo.IsEnabled = (enabled ? hasAssets : false);
            btnExportAll.IsEnabled = (enabled ? hasAssets : false);
            btnBatchFgo.IsEnabled = (enabled ? hasAssets : false);

            // always enable / disable no matter if there's assets or not
            comboBox.IsEnabled = enabled;
            btnImport.IsEnabled = enabled;
            btnImportAll.IsEnabled = enabled;
        }

        private long GetBundleDataDecompressedSize(AssetBundleFile bundleFile)
        {
            long totalSize = 0;
            foreach (AssetBundleDirectoryInfo06 dirInf in bundleFile.bundleInf6.dirInf)
            {
                totalSize += dirInf.decompressedSize;
            }
            return totalSize;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
