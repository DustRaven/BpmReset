using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BpmReset.Model;

public partial class CalendarDate : ObservableObject
{
    [ObservableProperty] private DateTime _date = DateTime.Today;
    [ObservableProperty] private bool _isCrash = false;
    [ObservableProperty] private bool _isBackup = false;
    [ObservableProperty] private string _path  = string.Empty;
    
    public string FormattedDate => string.Join(' ', Date.ToString("dd.MM.yyyy, HH:mm"), "Uhr");
}