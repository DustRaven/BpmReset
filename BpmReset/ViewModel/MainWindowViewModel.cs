using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using BpmReset.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WindowsAPICodePack.Dialogs;

namespace BpmReset.ViewModel;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(RestoreBackupCommand))]
    private CalendarDate? _selectedDate;

    [ObservableProperty] private double _copyProgress;
    [ObservableProperty] private ObservableCollection<CalendarDate> _dateList = new();

    [ObservableProperty] private DateTime _dateSelected = DateTime.Today;

    [ObservableProperty] private string _statusText = "Lade...";

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(RestoreBackupCommand))]
    private string? _dataFolder;

    [ObservableProperty] private int _version = 4;
    public string WindowTitle => $"BPM Studio Reset (Version {Version})";
    private const string Format4 = "dd.MM.yyyy - HH_mm_ss";
    private const string Format5 = "yyyy-MM-dd HH.mm";
    private const string BpmStudioGeneralRegKey = @"SOFTWARE\ALCATech\BPM Profi\General";

    public MainWindowViewModel()
    {
        Init();
    }

    private async void Init()
    {
        StatusText = "Suche Datenverzeichnis...";
        DataFolder = await GetDataFolder();
        StatusText = "Erkenne Version...";
        Version = CheckVersion();
        StatusText = "Lese Backups...";

        if (string.IsNullOrEmpty(DataFolder))
        {
            StatusText = "Konnte Verzeichnis nicht laden!";
            DateList = new ObservableCollection<CalendarDate>();
        }
        else
        {
            DateList = await ReadBackups();
        }

        StatusText = "Sammle Absturz-Ereignisse...";
        await ReadCrashes();

        StatusText = "Bereit.";
    }

    [RelayCommand(CanExecute = nameof(CanRestoreBackup))]
    private async Task RestoreBackup()
    {
        StatusText = "Stelle Daten wieder her...";
        if (SelectedDate is null || DataFolder is null) return;
        var filePaths = Directory.GetFiles(SelectedDate.Path, "*", SearchOption.AllDirectories);
        var progressIndicator = new Progress<double>(ReportProgress);
        await Task.Run(() =>
        {
            for (var i = 0; i < filePaths.Length; i++)
            {
                var destinationPath = filePaths[i].Replace(SelectedDate.Path, DataFolder);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? throw new InvalidOperationException());
                File.Copy(filePaths[i], destinationPath, true);

                ((IProgress<double>)progressIndicator).Report((double)(i + 1) / filePaths.Length * 100.0);
            }
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusText = "Abgeschlossen.";
                Task.Delay(1000);
                StatusText = "Bereit.";
            });
        });
    }
    
    private void ReportProgress(double progress)
    {
        CopyProgress = progress;
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        if (DataFolder is null) return;
        Process.Start("explorer.exe", DataFolder);
    }

    [RelayCommand]
    private async Task<string?> GetDataFolder()
    {
        var dataPath = await Task.Run(() =>
        {
            try
            {
                var key = Registry.CurrentUser.OpenSubKey(BpmStudioGeneralRegKey);
                var dirPath = key?.GetValue("DataPath") as string;
                return dirPath;
            }
            catch (Exception)
            {
                DataFolder = ShowFolderSelectionDialog();
                return null;
            }
        });

        return dataPath;
    }

    [RelayCommand]
    private void SelectDataFolder()
    {
        DataFolder = ShowFolderSelectionDialog();
    }

    private async Task<ObservableCollection<CalendarDate>> ReadBackups()
    {
        var backupPath = Path.Combine(DataFolder!, "Backups");
        var datesOfBackups = new ObservableCollection<CalendarDate>();
        var backupFolders = Directory.GetDirectories(backupPath);
        var format = Version == 4 ? Format4 : Format5;
        var backupFolderNames = await Task.Run(() =>
        {
            return backupFolders
                .Select(dir => new DirectoryInfo(dir).Name)
                .Where(name => DateTime.TryParseExact(name, format, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _))
                .Select(name => DateTime.ParseExact(name, format, CultureInfo.InvariantCulture))
                .ToList();
        });

        foreach (var backup in backupFolderNames)
        {
            datesOfBackups.Add(new CalendarDate
            {
                Date = backup,
                IsBackup = true,
                Path = Path.Combine(backupPath, backup.ToString(format))
            });
        }

        return datesOfBackups;
    }

    private bool CanRestoreBackup()
    {
        return !string.IsNullOrEmpty(DataFolder) && SelectedDate is not null;
    }

    private IEnumerable<string> ListFolders(string baseDir)
    {
        try
        {
            return Directory.GetDirectories(baseDir).Select(dir => new DirectoryInfo(dir).Name);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private void ShowError(string message)
    {
        MessageBox.Show(message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private string? ShowFolderSelectionDialog()
    {
        var dialog = new CommonOpenFileDialog();
        dialog.IsFolderPicker = true;
        return dialog.ShowDialog() == CommonFileDialogResult.Ok ? dialog.FileName : null;
    }

    private int CheckVersion()
    {
        if (DataFolder is null) return 0;
        var firstFolder = Directory.GetDirectories(Path.Combine(DataFolder, "Backups"))
            .Select(name => new DirectoryInfo(name))
            .FirstOrDefault()
            ?.Name;

        if (firstFolder is null) return 0;

        if (DateTime.TryParseExact(firstFolder,
                Format4, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return 4;
        }

        return DateTime.TryParseExact(firstFolder, Format5, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? 5
            : 0;
    }

    private async Task ReadCrashes()
    {
        await Task.Run(() =>
        {
            try
            {
                var eventLog = new EventLog("System");
                var calendarDatesToUpdate = new List<CalendarDate>();
                var oldestDate = DateTime.Now.AddDays(-30);

                var entries = eventLog.Entries.Cast<EventLogEntry>()
                    .Where(x => x.TimeGenerated.Date >= oldestDate.Date)
                    .Select(x => new
                    {
                        x.TimeGenerated,
                        x.InstanceId
                    })
                    .ToList();

                using (var writer = new StreamWriter("log.txt"))
                {
                    for (var i = entries.Count - 1; i >= 0; i--)
                    {
                        
                        var entry = entries[i];
                        var eventId = (int)(entry.InstanceId & 0xFFFF);
                        writer.WriteLine($"{entry.TimeGenerated} - {eventId}");

                        if (eventId != 6008) continue;
                        
                        var timeGenerated = entry.TimeGenerated;
                        var calendarDate =
                            DateList.FirstOrDefault(d => d.Date == timeGenerated) ?? new CalendarDate
                            {
                                Date = timeGenerated
                            };
                        calendarDate.IsCrash = true;
                        calendarDatesToUpdate.Add(calendarDate);
                    }
                }


                Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var calendarDate in calendarDatesToUpdate)
                    {
                        DateList.Add(calendarDate);
                    }

                    StatusText = "Sortiere...";

                    var view = (CollectionView)CollectionViewSource.GetDefaultView(DateList);
                    view.SortDescriptions.Add(new SortDescription("Date", ListSortDirection.Ascending));
                });
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        });
    }
}