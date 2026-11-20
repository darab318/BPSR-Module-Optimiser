using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using ModuleOptimiser.Core;

namespace ModuleOptimiser;

public sealed class AppSettings
{
    public string LastModulesFile { get; set; } = "";
    public List<WeightProfile> Profiles { get; set; } = new();
    public bool IcicleAdded { get; set; }
    public int IcicleVersion { get; set; }
}

// A saved set of custom weights (stat ID -> 1-100).
public sealed class WeightProfile
{
    public string Name { get; set; } = "";
    public Dictionary<int, double> Weights { get; set; } = new();
}

public partial class MainWindow : Window
{
    private List<Module> _modules = new();
    private readonly ObservableCollection<StatRow> _rows = new();
    private Point _dragStart;
    private StatRow _dragItem;
    private double _dragY;
    private DispatcherTimer _autoScrollTimer;
    private List<LoadoutResult> _results = new();
    private AppSettings _settings = new();
    private DispatcherTimer _reorderTimer;
    private bool _applyingProfile;

    public MainWindow()
    {
        InitializeComponent();
        StatList.ItemsSource = _rows;
        StatList.PreviewMouseLeftButtonDown += StatList_PreviewMouseLeftButtonDown;
        StatList.PreviewMouseMove += StatList_PreviewMouseMove;
        StatList.Drop += StatList_Drop;
        StatList.DragOver += StatList_DragOver;
        LoadSettings();
        RefreshProfileBox(_settings.Profiles.FirstOrDefault(p => p.Name == "Icicle"));
        LoadLastFile();
    }

    // Settings are saved in %AppData%\ModuleOptimiser\settings.json
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModuleOptimiser", "settings.json");

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                _settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
            }
        }
        catch
        {
            // If the settings file is damaged, just start fresh.
            _settings = new AppSettings();
        }

        _settings.Profiles ??= new List<WeightProfile>();

        // Built-in Icicle profile. Added (or updated) once per version of its numbers;
        // if it's deleted after that it stays deleted.
        if (_settings.IcicleVersion < IcicleVersion)
        {
            var icicle = IcicleProfile();
            var existing = _settings.Profiles.FirstOrDefault(p => string.Equals(p.Name, "Icicle", StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Weights = icicle.Weights;
            }
            else
            {
                _settings.Profiles.Add(icicle);
            }
            _settings.IcicleAdded = true;
            _settings.IcicleVersion = IcicleVersion;
            SaveSettings();
        }
    }

    private const int IcicleVersion = 2;

    // The "Module values" gain table, as the % numbers themselves.
    // Ranges use their midpoint. Stats the table doesn't list are 0.1.
    private static WeightProfile IcicleProfile() => new()
    {
        Name = "Icicle",
        Weights = new Dictionary<int, double>
        {
            [2404] = 10.56, // Life Wave
            [2105] = 8.59,  // Agile (only if active the whole fight)
            [2104] = 7.74,  // DMG Stack
            [1409] = 4.92,  // Crit Focus      4.79-5.04
            [1408] = 4.5,   // Attack SPD      2.5-6.5
            [1113] = 4.5,   // Special Attack  4.07-4.93
            [1114] = 4.48,  // Elite Strike
            [1112] = 4.14,  // Intellect Boost
            [1410] = 1.03,  // Luck Focus
            [1110] = 0.1, [1111] = 0.1, [1205] = 0.1, [1206] = 0.1, [1307] = 0.1, [1308] = 0.1,
            [1407] = 0.1, [2204] = 0.1, [2205] = 0.1, [2304] = 0.1, [2405] = 0.1, [2406] = 0.1,
        },
    };

    private void LoadLastFile()
    {
        if (!string.IsNullOrEmpty(_settings.LastModulesFile) && File.Exists(_settings.LastModulesFile))
        {
            LoadModulesFile(_settings.LastModulesFile);
        }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Not being able to save settings is not a reason to stop the app.
        }
    }

    private void SaveLastFile(string path)
    {
        _settings.LastModulesFile = path;
        SaveSettings();
    }

    // ---- Weight profiles ----

    private void RefreshProfileBox(WeightProfile select)
    {
        _applyingProfile = true;
        ProfileBox.ItemsSource = null;
        ProfileBox.ItemsSource = _settings.Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        ProfileBox.SelectedItem = select;
        _applyingProfile = false;
    }

    private void ProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingProfile || ProfileBox.SelectedItem is not WeightProfile profile)
        {
            return;
        }

        ApplyProfile(profile);
    }

    // Sets every slider from the profile (stats it doesn't list get 1) and sorts the list.
    private void ApplyProfile(WeightProfile profile)
    {
        if (_rows.Count == 0)
        {
            StatusText.Text = $"Profile \"{profile.Name}\" will apply once a modules file is open.";
            return;
        }

        _applyingProfile = true;
        foreach (var row in _rows)
        {
            row.Weight = profile.Weights.TryGetValue(row.StatId, out var w) ? w : 0.1;
        }
        _applyingProfile = false;

        SortRowsByWeight();
        StatusText.Text = $"Loaded profile \"{profile.Name}\".";
    }

    // Save icon: overwrite the selected profile. With none selected, ask for a name instead.
    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedItem is WeightProfile selected)
        {
            SaveProfileAs(selected.Name);
        }
        else
        {
            NewProfile_Click(sender, e);
        }
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        ProfileName.Text = "";
        ProfileName.Visibility = Visibility.Visible;
        ProfileName.Focus();
    }

    private void ProfileName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var name = ProfileName.Text.Trim();
            if (name.Length > 0)
            {
                ProfileName.Visibility = Visibility.Collapsed;
                SaveProfileAs(name);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ProfileName.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
    }

    private void ProfileName_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ProfileName.Visibility = Visibility.Collapsed;
    }

    // Saves the current sliders under this name, overwriting a profile with the same name.
    private void SaveProfileAs(string name)
    {
        if (_rows.Count == 0)
        {
            StatusText.Text = "Open a modules file first.";
            return;
        }

        var profile = _settings.Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (profile == null)
        {
            profile = new WeightProfile { Name = name };
            _settings.Profiles.Add(profile);
        }

        profile.Weights = _rows.ToDictionary(r => r.StatId, r => r.Weight);
        SaveSettings();
        RefreshProfileBox(profile);
        StatusText.Text = $"Saved profile \"{profile.Name}\".";
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not WeightProfile profile)
        {
            StatusText.Text = "Pick a profile to delete.";
            return;
        }

        var answer = MessageBox.Show(this, $"Delete the profile \"{profile.Name}\"?", "Delete profile",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _settings.Profiles.Remove(profile);
        SaveSettings();
        RefreshProfileBox(null);
        StatusText.Text = $"Deleted profile \"{profile.Name}\".";
    }

    // ---- Reorder the list when a slider moves ----

    private void Row_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StatRow.Weight) || _applyingProfile)
        {
            return;
        }

        // Wait until the slider is let go; moving the row mid-drag would pull the slider out from under the mouse.
        if (_reorderTimer == null)
        {
            _reorderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _reorderTimer.Tick += ReorderTimer_Tick;
        }
        _reorderTimer.Stop();
        _reorderTimer.Start();
    }

    private void ReorderTimer_Tick(object sender, EventArgs e)
    {
        if (Mouse.LeftButton == MouseButtonState.Pressed)
        {
            return;   // still dragging; check again next tick
        }

        _reorderTimer.Stop();
        SortRowsByWeight();
    }

    // Highest weight first. Ties keep their current order.
    private void SortRowsByWeight()
    {
        var sorted = _rows.OrderByDescending(r => r.Weight).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            int from = _rows.IndexOf(sorted[i]);
            if (from != i)
            {
                _rows.Move(from, i);
            }
        }
    }

    private void LoadModulesFile(string path)
    {
        try
        {
            _modules = ModuleLoader.Load(path);
            BuildStatRows();
            SaveLastFile(path);
            StatusText.Text = $"Loaded {_modules.Count} modules from {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not read that file: {ex.Message}";
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose ModulesSaveData.json",
            Filter = "Module save file (*.json)|*.json|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        LoadModulesFile(dialog.FileName);
    }

    private void BuildStatRows()
    {
        _rows.Clear();
        foreach (var statId in _modules.SelectMany(m => m.Stats.Keys).Distinct().OrderBy(x => x))
        {
            var row = new StatRow
            {
                StatId = statId,
                Label = StatNames.Name(statId),
                Icon = StatIcon(statId, 22)?.Source,
                MinimumText = "",
            };
            row.PropertyChanged += Row_PropertyChanged;
            _rows.Add(row);
        }

        if (ProfileBox.SelectedItem is WeightProfile profile)
        {
            ApplyProfile(profile);
        }
        else
        {
            // No profile picked: start from the list order, top = highest.
            _applyingProfile = true;
            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].Weight = Math.Max(1, _rows.Count - i);
            }
            _applyingProfile = false;
        }
    }

    // Drag a row to reorder the list. The row handle or the row itself starts the drag; the minimum box does not.
    private void StatList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragItem = null;
        if (IsInsideInput(e.OriginalSource))
        {
            return;
        }

        _dragStart = e.GetPosition(null);
        _dragItem = RowAt(e.OriginalSource);
    }

    private void StatList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null)
        {
            return;
        }

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < 4 && Math.Abs(pos.Y - _dragStart.Y) < 4)
        {
            return;
        }

        var item = _dragItem;
        _dragItem = null;

        // While dragging, scroll the list if the pointer is near its top or bottom edge.
        _dragY = StatList.ActualHeight / 2;
        _autoScrollTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _autoScrollTimer.Tick -= AutoScrollTick;
        _autoScrollTimer.Tick += AutoScrollTick;
        _autoScrollTimer.Start();

        try
        {
            DragDrop.DoDragDrop(StatList, item, DragDropEffects.Move);
        }
        finally
        {
            _autoScrollTimer.Stop();
        }
    }

    private void StatList_DragOver(object sender, DragEventArgs e)
    {
        _dragY = e.GetPosition(StatList).Y;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void AutoScrollTick(object sender, EventArgs e)
    {
        const double edge = 48;   // pixels from the top or bottom of the list
        const double step = 10;   // pixels scrolled each tick

        var scroll = FindScrollViewer(StatList);
        if (scroll == null)
        {
            return;
        }

        if (_dragY < edge)
        {
            scroll.ScrollToVerticalOffset(Math.Max(0, scroll.VerticalOffset - step));
        }
        else if (_dragY > StatList.ActualHeight - edge)
        {
            scroll.ScrollToVerticalOffset(Math.Min(scroll.ScrollableHeight, scroll.VerticalOffset + step));
        }
    }

    private static ScrollViewer FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer)
        {
            return viewer;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private void StatList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(StatRow)))
        {
            return;
        }

        var dragged = (StatRow)e.Data.GetData(typeof(StatRow));
        var target = RowAt(e.OriginalSource);
        int from = _rows.IndexOf(dragged);
        int to = target == null ? _rows.Count - 1 : _rows.IndexOf(target);
        if (from >= 0 && to >= 0 && from != to)
        {
            _rows.Move(from, to);

            // The list is sorted by weight, so give the dragged stat a weight that fits its new spot.
            var above = to > 0 ? _rows[to - 1] : null;
            var below = to < _rows.Count - 1 ? _rows[to + 1] : null;
            double weight = dragged.Weight;
            if (above != null && below != null)
            {
                weight = (above.Weight + below.Weight) / 2;
            }
            else if (above == null && below != null)
            {
                weight = Math.Max(dragged.Weight, below.Weight + 1);
            }
            else if (below == null && above != null)
            {
                weight = Math.Min(dragged.Weight, above.Weight / 2);
            }

            _applyingProfile = true;   // already in place; don't re-sort
            dragged.Weight = weight;
            _applyingProfile = false;
        }
    }

    // Enter in a weight box applies the typed number straight away.
    private void WeightBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    // True when the click landed on a slider or text box, so using it doesn't start a row drag.
    private static bool IsInsideInput(object source)
    {
        var element = source as DependencyObject;
        while (element != null && element is not ListBoxItem)
        {
            if (element is Slider || element is TextBox)
            {
                return true;
            }
            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    // Finds the list row under a point in the visual tree.
    private StatRow RowAt(object source)
    {
        var element = source as DependencyObject;
        while (element != null && element is not ListBoxItem)
        {
            element = VisualTreeHelper.GetParent(element);
        }

        return (element as ListBoxItem)?.DataContext as StatRow;
    }

    // Loads the game's icon for a stat (Assets/Icons/<statId>.png). Returns null if there isn't one.
    private static Image StatIcon(int statId, double size = 26)
    {
        try
        {
            var bitmap = new BitmapImage(new Uri($"pack://application:,,,/Assets/Icons/{statId}.png", UriKind.Absolute));
            return new Image
            {
                Source = bitmap,
                Width = size,
                Height = size,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        catch
        {
            return null;
        }
    }

    private void FindButton_Click(object sender, RoutedEventArgs e)
    {
        if (_modules.Count == 0)
        {
            StatusText.Text = "Open a modules file first.";
            return;
        }

        var minimums = new Dictionary<int, int>();
        var weights = new Dictionary<int, double>();
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            // Each stat's weight is its slider value.
            weights[row.StatId] = row.Weight;

            var text = row.MinimumText.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (!int.TryParse(text, out var value) || value < 0)
            {
                StatusText.Text = $"{row.Label}: enter a whole number, or leave it blank.";
                return;
            }

            minimums[row.StatId] = value;
        }

        FindButton.IsEnabled = false;
        StatusText.Text = "Searching...";
        ProgressFill.Width = 0;
        ProgressTrack.Visibility = Visibility.Visible;

        // Called from the search thread; hands the update to the window's thread.
        Action<double, string> report = (fraction, message) =>
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ProgressFill.Width = Math.Round(Math.Clamp(fraction, 0, 1) * ProgressTrack.Width);
                StatusText.Text = message;
            }));

        var modules = _modules;
        Task.Run(() => Optimiser.Find(modules, minimums, 25, report, weights)).ContinueWith(task =>
        {
            Dispatcher.Invoke(() =>
            {
                FindButton.IsEnabled = true;
                ProgressTrack.Visibility = Visibility.Collapsed;

                if (task.IsFaulted)
                {
                    StatusText.Text = $"Search failed: {task.Exception?.GetBaseException().Message}";
                    return;
                }

                _results = task.Result;
                ResultsList.Items.Clear();
                SaveResultsButton.Visibility = _results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                if (_results.Count == 0)
                {
                    ResultsList.Items.Add("No loadout reaches all of those minimums with these modules.");
                    StatusText.Text = "No matches.";
                    return;
                }

                try
                {
                    for (int i = 0; i < _results.Count; i++)
                    {
                        ResultsList.Items.Add(BuildResultCard(_results[i], i + 1));
                    }

                    StatusText.Text = $"{_results.Count} loadouts found.";
                }
                catch (Exception ex)
                {
                    LogError(ex);
                    StatusText.Text = $"Could not show the results: {ex.Message} (details in error-log.txt)";
                }
            });
        });
    }

    // Writes problems to error-log.txt next to the app, so they can be sent back.
    private static void LogError(Exception ex)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "error-log.txt");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n{ex}\r\n\r\n");
        }
        catch
        {
            // If the log can't be written, the status line still shows the message.
        }
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    // One loadout, kept compact: a header line, the stat totals as small chips, then one slim row per module.
    private UIElement BuildResultCard(LoadoutResult result, int rank)
    {
        var content = new StackPanel();

        // Header: "Loadout 1" on the left, score on the right.
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var score = new TextBlock
        {
            Text = $"{result.Points:0.#} pts",
            FontSize = 12,
            Foreground = Res("SubtleTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(score, Dock.Right);
        header.Children.Add(score);
        header.Children.Add(new TextBlock
        {
            Text = $"Loadout {rank}",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res("TextBrush"),
        });
        content.Children.Add(header);

        // Totals, highest first. 20s in the accent colour, 16s in white, the rest dimmed.
        var totals = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var t in result.Totals.OrderByDescending(t => t.Value).ThenBy(t => StatNames.Name(t.Key)))
        {
            totals.Children.Add(BuildTotalChip(t.Key, t.Value));
        }
        content.Children.Add(totals);

        // Modules: one row each, separated by hairlines.
        for (int i = 0; i < result.Modules.Count; i++)
        {
            if (i > 0)
            {
                content.Children.Add(new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
                    Margin = new Thickness(32, 0, 0, 0),
                });
            }
            content.Children.Add(BuildModuleRow(result.Modules[i]));
        }

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 8),
            Margin = new Thickness(0, 0, 0, 10),
            Child = content,
        };
    }

    private UIElement BuildTotalChip(int statId, int value)
    {
        var chip = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = StatIcon(statId, 14);
        if (icon != null)
        {
            icon.Margin = new Thickness(0, 0, 4, 0);
            chip.Children.Add(icon);
        }
        chip.Children.Add(new TextBlock
        {
            Text = StatNames.Name(statId),
            FontSize = 11,
            Foreground = Res("SubtleTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        });
        chip.Children.Add(new TextBlock
        {
            Text = value.ToString(),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = value >= 20 ? Res("AccentBrush") : value >= 16 ? Res("TextBrush") : Res("SubtleTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(value >= 20 ? (byte)0x22 : (byte)0x10, 0x7A, 0xB4, 0xFF)),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(7, 2, 8, 2),
            Margin = new Thickness(0, 0, 6, 4),
            Child = chip,
        };
    }

    // One module in a single line: picture, type, then its stats. The full module name is in the tooltip.
    private UIElement BuildModuleRow(Module module)
    {
        var info = ModuleTypes.Get(module.ConfigId);

        var row = new Grid { Margin = new Thickness(0, 5, 0, 5), ToolTip = info.Name };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var moduleIcon = ModuleIcon(module.ConfigId, 24);
        if (moduleIcon != null)
        {
            moduleIcon.Margin = new Thickness(0);
            moduleIcon.HorizontalAlignment = HorizontalAlignment.Left;
            row.Children.Add(moduleIcon);
        }

        var type = new TextBlock
        {
            Text = info.Type,
            FontSize = 12,
            Foreground = Res("SubtleTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(type, 1);
        row.Children.Add(type);

        var stats = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var stat in module.Stats.OrderByDescending(s => s.Value))
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 14, 1) };
            var icon = StatIcon(stat.Key, 16);
            if (icon != null)
            {
                icon.Margin = new Thickness(0, 0, 5, 0);
                item.Children.Add(icon);
            }
            item.Children.Add(new TextBlock
            {
                Text = StatNames.Name(stat.Key),
                FontSize = 12,
                Foreground = Res("TextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
            });
            item.Children.Add(new TextBlock
            {
                Text = $"+{stat.Value}",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Res("AccentBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            stats.Children.Add(item);
        }
        Grid.SetColumn(stats, 2);
        row.Children.Add(stats);

        return row;
    }

    private static Image ModuleIcon(int configId, double size)
    {
        try
        {
            return new Image
            {
                Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/ModuleIcons/{configId}.png", UriKind.Absolute)),
                Width = size,
                Height = size,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        catch
        {
            return null;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_results.Count == 0)
        {
            StatusText.Text = "Nothing to save yet.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save loadouts",
            FileName = "loadouts.json",
            Filter = "JSON file (*.json)|*.json",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(_results, new JsonSerializerOptions { WriteIndented = true }));
        StatusText.Text = $"Saved to {Path.GetFileName(dialog.FileName)}";
    }
}

// One row in the stat list.
public sealed class StatRow : INotifyPropertyChanged
{
    private double _weight = 1;

    public event PropertyChangedEventHandler PropertyChanged;

    public int StatId { get; set; }
    public string Label { get; set; } = "";
    public ImageSource Icon { get; set; }
    public string MinimumText { get; set; } = "";

    // Weight, 0-100, up to 2 decimals.
    public double Weight
    {
        get => _weight;
        set
        {
            double clamped = Math.Round(Math.Clamp(double.IsNaN(value) ? 0 : value, 0, 100), 2);
            if (clamped == _weight)
            {
                return;
            }
            _weight = clamped;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Weight)));
        }
    }
}
