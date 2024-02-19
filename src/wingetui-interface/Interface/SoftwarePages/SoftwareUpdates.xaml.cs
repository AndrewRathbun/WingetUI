using CommunityToolkit.WinUI.Notifications;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModernWindow.Data;
using ModernWindow.Essentials;
using ModernWindow.Interface.Widgets;
using ModernWindow.PackageEngine.Classes;
using ModernWindow.PackageEngine.Operations;
using ModernWindow.Structures;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.UI.Core;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ModernWindow.Interface
{

    public partial class SoftwareUpdatesPage : Page
    {
        public ObservableCollection<UpgradablePackage> Packages = new();
        public SortableObservableCollection<UpgradablePackage> FilteredPackages = new() { SortingSelector = (a) => (a.Name) };
        protected List<PackageManager> UsedManagers = new();
        protected Dictionary<PackageManager, List<ManagerSource>> UsedSourcesForManager = new();
        protected Dictionary<PackageManager, TreeViewNode> RootNodeForManager = new();
        protected Dictionary<ManagerSource, TreeViewNode> NodesForSources = new();
        protected AppTools bindings = AppTools.Instance;

        protected TranslatedTextBlock MainTitle;
        protected TranslatedTextBlock MainSubtitle;
        protected ListView PackageList;
        protected ProgressBar LoadingProgressBar;
        protected MenuFlyout ContextMenu;

        private bool IsDescending = true;
        private bool Initialized = false;
        private bool AllSelected = true;

        public string InstantSearchSettingString = "DisableInstantSearchUpdatesTab";
        public SoftwareUpdatesPage()
        {
            InitializeComponent();
            MainTitle = __main_title;
            MainSubtitle = __main_subtitle;
            PackageList = __package_list;
            LoadingProgressBar = __loading_progressbar;
            LoadingProgressBar.Visibility = Visibility.Collapsed;
            Initialized = true;
            ReloadButton.Click += async (s, e) => { await LoadPackages(); };
            FindButton.Click += (s, e) => { FilterPackages(QueryBlock.Text); };
            QueryBlock.TextChanged += (s, e) => { if (InstantSearchCheckbox.IsChecked == true) FilterPackages(QueryBlock.Text); };
            QueryBlock.KeyUp += (s, e) => { if (e.Key == Windows.System.VirtualKey.Enter) FilterPackages(QueryBlock.Text); };

            SourcesTreeView.Tapped += (s, e) =>
            {
                if (e.OriginalSource != null && (e.OriginalSource as FrameworkElement).DataContext != null)
                {
                    AppTools.Log(e);
                    AppTools.Log(e.OriginalSource);
                    AppTools.Log(e.OriginalSource as FrameworkElement);
                    AppTools.Log((e.OriginalSource as FrameworkElement).DataContext);
                    if ((e.OriginalSource as FrameworkElement).DataContext is TreeViewNode)
                    {
                        TreeViewNode node = (e.OriginalSource as FrameworkElement).DataContext as TreeViewNode;
                        if (node == null)
                            return;
                        if (SourcesTreeView.SelectedNodes.Contains(node))
                            SourcesTreeView.SelectedNodes.Remove(node);
                        else
                            SourcesTreeView.SelectedNodes.Add(node);
                    }
                    else
                    {
                        AppTools.Log((e.OriginalSource as FrameworkElement).DataContext.GetType());
                    }
                }
            };
            PackageList.DoubleTapped += (s, e) =>
            {
                _ = bindings.App.mainWindow.NavigationPage.ShowPackageDetails(PackageList.SelectedItem as Package, OperationType.Update);
            };

            PackageList.RightTapped += (s, e) =>
            {
                if (e.OriginalSource is FrameworkElement element)
                {
                    try
                    {
                        if (element.DataContext != null && element.DataContext is Package package)
                        {
                            PackageList.SelectedItem = package;
                            MenuAsAdmin.IsEnabled = package.Manager.Capabilities.CanRunAsAdmin;
                            MenuInteractive.IsEnabled = package.Manager.Capabilities.CanRunInteractively;
                            MenuskipHash.IsEnabled = package.Manager.Capabilities.CanSkipIntegrityChecks;
                            PackageContextMenu.ShowAt(PackageList, e.GetPosition(PackageList));
                        }
                    }
                    catch (Exception ex)
                    {
                        AppTools.Log(ex);
                    }
                }
            };

            PackageList.KeyUp += async (s, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Enter && PackageList.SelectedItem != null)
                {
                    if (InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down))
                    {
                        if (await bindings.App.mainWindow.NavigationPage.ShowInstallationSettingsForPackageAndContinue(PackageList.SelectedItem as Package, OperationType.Update))
                            bindings.AddOperationToList(new UninstallPackageOperation(PackageList.SelectedItem as Package));
                    }
                    else if (InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down))
                        bindings.AddOperationToList(new UpdatePackageOperation(PackageList.SelectedItem as Package));
                    else
                        _ = bindings.App.mainWindow.NavigationPage.ShowPackageDetails(PackageList.SelectedItem as Package, OperationType.Update);
                }
                else if (e.Key == Windows.System.VirtualKey.A && InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down))
                {
                    if (AllSelected)
                        ClearItemSelection();
                    else
                        SelectAllItems();
                }
                else if (e.Key == Windows.System.VirtualKey.Space && PackageList.SelectedItem != null)
                {
                    (PackageList.SelectedItem as Package).IsChecked = !(PackageList.SelectedItem as Package).IsChecked;
                }
                else if (e.Key == Windows.System.VirtualKey.F5)
                {
                    _ = LoadPackages();
                }
            };


            GenerateToolBar();
            LoadInterface();
            _ = LoadPackages();
        }

        protected void AddPackageToSourcesList(UpgradablePackage package)
        {
            if (!Initialized)
                return;

            ManagerSource source = package.Source;
            if (!UsedManagers.Contains(source.Manager))
            {
                UsedManagers.Add(source.Manager);
                TreeViewNode Node;
                Node = new TreeViewNode() { Content = source.Manager.Name + " ", IsExpanded = false };
                SourcesTreeView.RootNodes.Add(Node);
                SourcesTreeView.SelectedNodes.Add(Node);
                RootNodeForManager.Add(source.Manager, Node);
                UsedSourcesForManager.Add(source.Manager, new List<ManagerSource>());
                SourcesPlaceholderText.Visibility = Visibility.Collapsed;
                SourcesTreeViewGrid.Visibility = Visibility.Visible;
            }

            if ((!UsedSourcesForManager.ContainsKey(source.Manager) || !UsedSourcesForManager[source.Manager].Contains(source)) && source.Manager.Capabilities.SupportsCustomSources)
            {
                UsedSourcesForManager[source.Manager].Add(source);
                TreeViewNode item = new() { Content = source.Name + " " };
                NodesForSources.Add(source, item);
                RootNodeForManager[source.Manager].Children.Add(item);

            }
        }

        private void PackageContextMenu_AboutToShow(object sender, Package _package)
        {
            UpgradablePackage package = _package as UpgradablePackage;
            if (!Initialized)
                return;
            PackageList.SelectedItem = package;
        }

        private void FilterOptionsChanged(object sender, RoutedEventArgs e)
        {
            if (!Initialized)
                return;
            FilterPackages(QueryBlock.Text);
        }

        private void InstantSearchValueChanged(object sender, RoutedEventArgs e)
        {
            if (!Initialized)
                return;
            bindings.SetSettings(InstantSearchSettingString, InstantSearchCheckbox.IsChecked == false);
        }
        private void SourcesTreeView_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
        {
            FilterPackages(QueryBlock.Text);
        }

        /*
         * 
         * 
         *  DO NOT MODIFY THE UPPER PART OF THIS FILE
         * 
         * 
         */

        public async Task LoadPackages()
        {
            if (!Initialized)
                return;

            if (LoadingProgressBar.Visibility == Visibility.Visible)
                return; // If already loading, don't load again

            MainSubtitle.Text = "Loading...";
            BackgroundText.Text = "Loading...";
            BackgroundText.Visibility = Visibility.Visible;
            LoadingProgressBar.Visibility = Visibility.Visible;
            SourcesPlaceholderText.Visibility = Visibility.Visible;
            SourcesPlaceholderText.Text = "Loading...";
            SourcesTreeViewGrid.Visibility = Visibility.Collapsed;

            Packages.Clear();
            FilteredPackages.Clear();
            UsedManagers.Clear();
            SourcesTreeView.RootNodes.Clear();
            UsedSourcesForManager.Clear();
            RootNodeForManager.Clear();
            NodesForSources.Clear();

            await Task.Delay(100);

            List<Task<UpgradablePackage[]>> tasks = new();

            foreach (PackageManager manager in bindings.App.PackageManagerList)
            {
                if (manager.IsEnabled() && manager.Status.Found)
                {
                    Task<UpgradablePackage[]> task = manager.GetAvailableUpdates();
                    tasks.Add(task);
                }
            }

            while (tasks.Count > 0)
            {
                foreach (Task<UpgradablePackage[]> task in tasks.ToArray())
                {
                    if (!task.IsCompleted)
                        await Task.Delay(100);

                    if (task.IsCompleted)
                    {
                        if (task.IsCompletedSuccessfully)
                        {
                            int InitialCount = Packages.Count;
                            foreach (UpgradablePackage package in task.Result)
                            {
                                if (await package.HasUpdatesIgnoredAsync(package.NewVersion))
                                    continue;

                                if (package.NewVersionIsInstalled())
                                    AppTools.Log("Package Id={0} with NewVersion={1} is already installed, skipping it...".Replace("{0}", package.Id).Replace("{1}", package.NewVersion));

                                Packages.Add(package);
                                AddPackageToSourcesList(package);
                            }
                            if (InitialCount < Packages.Count)
                                FilterPackages(QueryBlock.Text.Trim(), StillLoading: true);
                        }
                        tasks.Remove(task);
                    }
                }
            }
            FilterPackages(QueryBlock.Text);
            LoadingProgressBar.Visibility = Visibility.Collapsed;



            if (Packages.Count > 0)
            {
                string body = "";
                string title = "";
                string attribution = "";
                bool ShowButtons = false;
                if (bindings.GetSettings("AutomaticallyUpdatePackages") || Environment.GetCommandLineArgs().Contains("--updateapps"))
                {
                    if (Packages.Count == 1)
                    {
                        title = bindings.Translate("An update was found!");
                        body = bindings.Translate("{0} is being updates to version {1}").Replace("{0}", Packages[0].Name).Replace("{1}", Packages[0].NewVersion);
                        attribution = bindings.Translate("You have currently version {0} installed").Replace("{0}", Packages[0].Version);
                    }
                    else
                    {
                        title = bindings.Translate("Updates found!");
                        body = bindings.Translate("{0} packages are being updated").Replace("{0}", Packages.Count.ToString()); ;
                        foreach (UpgradablePackage package in Packages)
                        {
                            attribution += package.Name + ", ";
                        }
                        attribution = attribution.TrimEnd(' ').TrimEnd(',');
                    }
                    UpdateAll();
                }
                else
                {
                    if (Packages.Count == 1)
                    {
                        title = bindings.Translate("An update was found!");
                        body = bindings.Translate("{0} can be updated to version {1}").Replace("{0}", Packages[0].Name).Replace("{1}", Packages[0].NewVersion);
                        attribution = bindings.Translate("You have currently version {0} installed").Replace("{0}", Packages[0].Version);
                    }
                    else
                    {
                        title = bindings.Translate("Updates found!");
                        body = bindings.Translate("{0} packages can be updated").Replace("{0}", Packages.Count.ToString()); ;
                        foreach (UpgradablePackage package in Packages)
                        {
                            attribution += package.Name + ", ";
                        }
                        attribution = attribution.TrimEnd(' ').TrimEnd(',');
                        ShowButtons = true;
                    }
                }

                if (!bindings.GetSettings("DisableUpdatesNotifications") && !bindings.GetSettings("DisableNotifications"))
                {
                    ToastContentBuilder toast = new();
                    toast.AddArgument("action", "openWingetUI");
                    toast.AddArgument("notificationId", CoreData.UpdatesAvailableNotificationId);
                    toast.AddText(title);
                    toast.AddText(body);
                    toast.AddAttributionText(attribution);
                    if (ShowButtons)
                    {
                        toast.AddButton(new ToastButton()
                            .SetContent(bindings.Translate("Open WingetUI"))
                            .AddArgument("action", "openWingetUI")
                            .SetBackgroundActivation());
                        toast.AddButton(new ToastButton()
                            .SetContent(bindings.Translate("Update all"))
                            .AddArgument("action", "updateAll")
                            .SetBackgroundActivation());
                    }
                    toast.Show();
                }
            }
        }

        public void UpdateAll()
        {
            foreach (UpgradablePackage package in Packages)
            {
                bindings.AddOperationToList(new UpdatePackageOperation(package));
            }
        }

        public void FilterPackages(string query, bool StillLoading = false)
        {
            if (!Initialized)
                return;

            FilteredPackages.Clear();

            List<ManagerSource> VisibleSources = new();
            List<PackageManager> VisibleManagers = new();

            if (SourcesTreeView.SelectedNodes.Count > 0)
            {
                foreach (TreeViewNode node in SourcesTreeView.SelectedNodes)
                {
                    if (NodesForSources.ContainsValue(node))
                        VisibleSources.Add(NodesForSources.First(x => x.Value == node).Key);
                    else if (RootNodeForManager.ContainsValue(node))
                        VisibleManagers.Add(RootNodeForManager.First(x => x.Value == node).Key);
                }
            }


            UpgradablePackage[] MatchingList;

            Func<string, string> CaseFunc;
            if (UpperLowerCaseCheckbox.IsChecked == true)
                CaseFunc = (x) => { return x; };
            else
                CaseFunc = (x) => { return x.ToLower(); };

            Func<string, string> CharsFunc;
            if (IgnoreSpecialCharsCheckbox.IsChecked == true)
                CharsFunc = (x) =>
                {
                    string temp_x = CaseFunc(x).Replace("-", "").Replace("_", "").Replace(" ", "").Replace("@", "").Replace("\t", "").Replace(".", "").Replace(",", "").Replace(":", "");
                    foreach (KeyValuePair<char, string> entry in new Dictionary<char, string>
                        {
                            {'a', "àáäâ"},
                            {'e', "èéëê"},
                            {'i', "ìíïî"},
                            {'o', "òóöô"},
                            {'u', "ùúüû"},
                            {'y', "ýÿ"},
                            {'c', "ç"},
                            {'ñ', "n"},
                        })
                    {
                        foreach (char InvalidChar in entry.Value)
                            x = x.Replace(InvalidChar, entry.Key);
                    }
                    return temp_x;
                };
            else
                CharsFunc = (x) => { return CaseFunc(x); };

            if (QueryIdRadio.IsChecked == true)
                MatchingList = Packages.Where(x => CharsFunc(x.Name).Contains(CharsFunc(query))).ToArray();
            else if (QueryNameRadio.IsChecked == true)
                MatchingList = Packages.Where(x => CharsFunc(x.Id).Contains(CharsFunc(query))).ToArray();
            else // QueryBothRadio.IsChecked == true
                MatchingList = Packages.Where(x => CharsFunc(x.Name).Contains(CharsFunc(query)) | CharsFunc(x.Id).Contains(CharsFunc(query))).ToArray();

            FilteredPackages.BlockSorting = true;
            int HiddenPackagesDueToSource = 0;
            foreach (UpgradablePackage match in MatchingList)
            {
                if (VisibleManagers.Contains(match.Manager) || VisibleSources.Contains(match.Source))
                    FilteredPackages.Add(match);
                else
                    HiddenPackagesDueToSource++;
            }
            FilteredPackages.BlockSorting = false;
            FilteredPackages.Sort();

            if (MatchingList.Count() == 0)
            {
                if (!StillLoading)
                {
                    if (Packages.Count() == 0)
                    {
                        BackgroundText.Text = SourcesPlaceholderText.Text = "Hooray! No updates were found.";
                        SourcesPlaceholderText.Text = "Everything is up to date";
                        MainSubtitle.Text = "Everything is up to date";
                    }
                    else
                    {
                        BackgroundText.Text = "No results were found matching the input criteria";
                        SourcesPlaceholderText.Text = "No packages were found";
                        MainSubtitle.Text = bindings.Translate("{0} packages were found, {1} of which match the specified filters.").Replace("{0}", Packages.Count.ToString()).Replace("{1}", (MatchingList.Length - HiddenPackagesDueToSource).ToString());
                    }
                    BackgroundText.Visibility = Visibility.Visible;
                }

            }
            else
            {
                BackgroundText.Visibility = Visibility.Collapsed;
                MainSubtitle.Text = bindings.Translate("{0} packages were found, {1} of which match the specified filters.").Replace("{0}", Packages.Count.ToString()).Replace("{1}", (MatchingList.Length - HiddenPackagesDueToSource).ToString());
            }

            bindings.App.mainWindow.NavigationPage.UpdatesBadge.Visibility = Packages.Count() == 0 ? Visibility.Collapsed : Visibility.Visible;
            bindings.App.mainWindow.NavigationPage.UpdatesBadge.Value = Packages.Count();
            try
            {
                bindings.TooltipStatus.AvailableUpdates = Packages.Count();
            }
            catch (Exception) { }
        }

        public void SortPackages(string Sorter)
        {
            if (!Initialized)
                return;

            FilteredPackages.Descending = !FilteredPackages.Descending;
            FilteredPackages.SortingSelector = (a) => (a.GetType().GetProperty(Sorter).GetValue(a));
            object Item = PackageList.SelectedItem;
            FilteredPackages.Sort();

            if (Item != null)
                PackageList.SelectedItem = Item;
            PackageList.ScrollIntoView(Item);
        }

        public void LoadInterface()
        {
            if (!Initialized)
                return;
            MainTitle.Text = "Software Updates";
            HeaderIcon.Glyph = "\uE895";
            HeaderIcon.FontWeight = new Windows.UI.Text.FontWeight(700);
            CheckboxHeader.Content = " ";
            NameHeader.Content = bindings.Translate("Package Name");
            IdHeader.Content = bindings.Translate("Package ID");
            VersionHeader.Content = bindings.Translate("Version");
            NewVersionHeader.Content = bindings.Translate("New version");
            SourceHeader.Content = bindings.Translate("Source");

            CheckboxHeader.Click += (s, e) => { SortPackages("IsCheckedAsString"); };
            NameHeader.Click += (s, e) => { SortPackages("Name"); };
            IdHeader.Click += (s, e) => { SortPackages("Id"); };
            VersionHeader.Click += (s, e) => { SortPackages("VersionAsFloat"); };
            NewVersionHeader.Click += (s, e) => { SortPackages("NewVersionAsFloat"); };
            SourceHeader.Click += (s, e) => { SortPackages("SourceAsString"); };
        }


        public void GenerateToolBar()
        {
            if (!Initialized)
                return;
            AppBarButton UpdateSelected = new();
            AppBarButton UpdateAsAdmin = new();
            AppBarButton UpdateSkipHash = new();
            AppBarButton UpdateInteractive = new();

            AppBarButton InstallationSettings = new();

            AppBarButton PackageDetails = new();
            AppBarButton SharePackage = new();

            AppBarButton SelectAll = new();
            AppBarButton SelectNone = new();

            AppBarButton IgnoreSelected = new();
            AppBarButton ManageIgnored = new();

            AppBarButton HelpButton = new();

            ToolBar.PrimaryCommands.Add(UpdateSelected);
            ToolBar.PrimaryCommands.Add(UpdateAsAdmin);
            ToolBar.PrimaryCommands.Add(UpdateSkipHash);
            ToolBar.PrimaryCommands.Add(UpdateInteractive);
            ToolBar.PrimaryCommands.Add(new AppBarSeparator());
            ToolBar.PrimaryCommands.Add(InstallationSettings);
            ToolBar.PrimaryCommands.Add(new AppBarSeparator());
            ToolBar.PrimaryCommands.Add(PackageDetails);
            ToolBar.PrimaryCommands.Add(SharePackage);
            ToolBar.PrimaryCommands.Add(new AppBarSeparator());
            ToolBar.PrimaryCommands.Add(SelectAll);
            ToolBar.PrimaryCommands.Add(SelectNone);
            ToolBar.PrimaryCommands.Add(new AppBarSeparator());
            ToolBar.PrimaryCommands.Add(IgnoreSelected);
            ToolBar.PrimaryCommands.Add(ManageIgnored);
            ToolBar.PrimaryCommands.Add(new AppBarSeparator());
            ToolBar.PrimaryCommands.Add(HelpButton);

            Dictionary<AppBarButton, string> Labels = new()
            { // Entries with a trailing space are collapsed
              // Their texts will be used as the tooltip
                { UpdateSelected,      "Update selected packages" },
                { UpdateAsAdmin,       " Update as administrator" },
                { UpdateSkipHash,      " Skip integrity checks" },
                { UpdateInteractive,   " Interactive update" },
                { InstallationSettings, " Installation options" },
                { PackageDetails,       " Package details" },
                { SharePackage,         " Share" },
                { SelectAll,            " Select all" },
                { SelectNone,           " Clear selection" },
                { IgnoreSelected,       "Ignore selected packages" },
                { ManageIgnored,        "Manage ignored updates" },
                { HelpButton,           "Help" }
            };

            foreach (AppBarButton toolButton in Labels.Keys)
            {
                toolButton.IsCompact = Labels[toolButton][0] == ' ';
                if (toolButton.IsCompact)
                    toolButton.LabelPosition = CommandBarLabelPosition.Collapsed;
                toolButton.Label = bindings.Translate(Labels[toolButton].Trim());
            }

            Dictionary<AppBarButton, string> Icons = new()
            {
                { UpdateSelected,      "menu_updates" },
                { UpdateAsAdmin,       "runasadmin" },
                { UpdateSkipHash,      "checksum" },
                { UpdateInteractive,   "interactive" },
                { InstallationSettings, "options" },
                { PackageDetails,       "info" },
                { SharePackage,         "share" },
                { SelectAll,            "selectall" },
                { SelectNone,           "selectnone" },
                { IgnoreSelected,       "pin" },
                { ManageIgnored,        "blacklist" },
                { HelpButton,           "help" }
            };

            foreach (AppBarButton toolButton in Icons.Keys)
                toolButton.Icon = new LocalIcon(Icons[toolButton]);


            PackageDetails.Click += (s, e) => { _ = bindings.App.mainWindow.NavigationPage.ShowPackageDetails(PackageList.SelectedItem as Package, OperationType.Update); };
            HelpButton.Click += (s, e) => { bindings.App.mainWindow.NavigationPage.ShowHelp(); };


            InstallationSettings.Click += async (s, e) =>
            {
                if (PackageList.SelectedItem != null && await bindings.App.mainWindow.NavigationPage.ShowInstallationSettingsForPackageAndContinue(PackageList.SelectedItem as Package, OperationType.Update))
                    bindings.AddOperationToList(new UpdatePackageOperation(PackageList.SelectedItem as UpgradablePackage));
            };

            ManageIgnored.Click += async (s, e) => { await bindings.App.mainWindow.NavigationPage.ManageIgnoredUpdatesDialog(); };
            IgnoreSelected.Click += async (s, e) =>
            {
                foreach (UpgradablePackage package in FilteredPackages.ToArray()) if (package.IsChecked)
                        await package.AddToIgnoredUpdatesAsync();
            };

            UpdateSelected.Click += (s, e) =>
            {
                foreach (UpgradablePackage package in FilteredPackages.ToArray()) if (package.IsChecked)
                        bindings.AddOperationToList(new UpdatePackageOperation(package));
            };
            UpdateAsAdmin.Click += (s, e) =>
            {
                foreach (UpgradablePackage package in FilteredPackages.ToArray()) if (package.IsChecked)
                        bindings.AddOperationToList(new UpdatePackageOperation(package,
                            new InstallationOptions(package) { RunAsAdministrator = true }));
            };
            UpdateSkipHash.Click += (s, e) =>
            {
                foreach (UpgradablePackage package in FilteredPackages.ToArray()) if (package.IsChecked)
                        bindings.AddOperationToList(new UpdatePackageOperation(package,
                            new InstallationOptions(package) { SkipHashCheck = true }));
            };
            UpdateInteractive.Click += (s, e) =>
            {
                foreach (UpgradablePackage package in FilteredPackages.ToArray()) if (package.IsChecked)
                        bindings.AddOperationToList(new UpdatePackageOperation(package,
                            new InstallationOptions(package) { InteractiveInstallation = true }));
            };

            SharePackage.Click += (s, e) => { bindings.App.mainWindow.SharePackage(PackageList.SelectedItem as Package); };

            SelectAll.Click += (s, e) => { SelectAllItems(); };
            SelectNone.Click += (s, e) => { ClearItemSelection(); };

        }
        private void MenuDetails_Invoked(object sender, RoutedEventArgs e)
        {
            if (!Initialized || PackageList.SelectedItem == null)
                return;
            _ = bindings.App.mainWindow.NavigationPage.ShowPackageDetails(PackageList.SelectedItem as Package, OperationType.Update);
        }

        private void MenuShare_Invoked(object sender, RoutedEventArgs e)
        {
            if (!Initialized || PackageList.SelectedItem == null)
                return;
            bindings.App.mainWindow.SharePackage(PackageList.SelectedItem as UpgradablePackage);
        }

        private void MenuInstall_Invoked(object sender, RoutedEventArgs e)
        {
            if (!Initialized || PackageList.SelectedItem == null)
                return;
            bindings.AddOperationToList(new UpdatePackageOperation(PackageList.SelectedItem as UpgradablePackage));
        }

        private void MenuSkipHash_Invoked(object sender, RoutedEventArgs e)
        {
            if (!Initialized || PackageList.SelectedItem == null)
                return;
            bindings.AddOperationToList(new UpdatePackageOperation(PackageList.SelectedItem as UpgradablePackage,
                new InstallationOptions(PackageList.SelectedItem as UpgradablePackage) { SkipHashCheck = true }));
        }

        private void MenuInteractive_Invoked(object sender, RoutedEventArgs e)
        {
            if (!Initialized || PackageList.SelectedItem == null)
                return;
            bindings.AddOperationToList(new UpdatePackageOperation(PackageList.SelectedItem as UpgradablePackage,
                new InstallationOptions(PackageList.SelectedItem as UpgradablePackage) { InteractiveInstallation = true }));
        }

        private void MenuAsAdmin_Invoked(object sender, RoutedEventArgs e)
        {
            if (!Initialized || PackageList.SelectedItem == null)
                return;
            bindings.AddOperationToList(new UpdatePackageOperation(PackageList.SelectedItem as UpgradablePackage,
                new InstallationOptions(PackageList.SelectedItem as UpgradablePackage) { RunAsAdministrator = true }));
        }

        private void MenuUpdateAfterUninstall_Invoked(object sender, RoutedEventArgs e)
        {
            if (!Initialized || PackageList.SelectedItem == null)
                return;
            bindings.AddOperationToList(new UninstallPackageOperation(PackageList.SelectedItem as UpgradablePackage));
            bindings.AddOperationToList(new InstallPackageOperation(PackageList.SelectedItem as UpgradablePackage));
        }

        private void MenuUninstall_Invoked(object sender, RoutedEventArgs e)
        {
            if (!Initialized || PackageList.SelectedItem == null)
                return;
            bindings.AddOperationToList(new UninstallPackageOperation(PackageList.SelectedItem as UpgradablePackage));
        }

        private void MenuIgnorePackage_Invoked(object sender, RoutedEventArgs e)
        {
            if (!Initialized || PackageList.SelectedItem == null)
                return;
            _ = (PackageList.SelectedItem as UpgradablePackage).AddToIgnoredUpdatesAsync();
        }

        private void MenuSkipVersion_Invoked(object sender, RoutedEventArgs e)
        {
            if (!Initialized || PackageList.SelectedItem == null)
                return;
            _ = (PackageList.SelectedItem as UpgradablePackage).AddToIgnoredUpdatesAsync((PackageList.SelectedItem as UpgradablePackage).Version);
        }

        private void SelectAllSourcesButton_Click(object sender, RoutedEventArgs e)
        {
            SourcesTreeView.SelectAll();
        }

        private void ClearSourceSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            SourcesTreeView.SelectedItems.Clear();
            FilterPackages(QueryBlock.Text.Trim());
        }

        private async void MenuInstallSettings_Invoked(object sender, RoutedEventArgs e)
        {
            if (!Initialized || PackageList.SelectedItem == null)
                return;

            if (await bindings.App.mainWindow.NavigationPage.ShowInstallationSettingsForPackageAndContinue(PackageList.SelectedItem as UpgradablePackage, OperationType.Update))
                bindings.AddOperationToList(new UpdatePackageOperation(PackageList.SelectedItem as UpgradablePackage));
        }

        private void SelectAllItems()
        {
            foreach (UpgradablePackage package in FilteredPackages)
                package.IsChecked = true;
            AllSelected = true;
        }

        private void ClearItemSelection()
        {
            foreach (UpgradablePackage package in FilteredPackages)
                package.IsChecked = false;
            AllSelected = false;
        }
        public void RemoveCorrespondingPackages(Package foreignPackage)
        {
            foreach (UpgradablePackage package in Packages.ToArray())
                if (package == foreignPackage || package.Equals(foreignPackage))
                {
                    Packages.Remove(package);
                    if (FilteredPackages.Contains(package))
                        FilteredPackages.Remove(package);
                }
            if (bindings.App.mainWindow.NavigationPage.CurrentPage != bindings.App.mainWindow.NavigationPage.UpdatesPage)
                FilterPackages(QueryBlock.Text.Trim());
        }

    }
}
