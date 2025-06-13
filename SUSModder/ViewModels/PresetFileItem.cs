using System;
using System.ComponentModel;

namespace SUSModder.ViewModels
{
    public class PresetFileItem : INotifyPropertyChanged
    {
        private string _originalName = "";
        private string _newName = "";
        private string _fullPath = "";

        public string OriginalName
        {
            get => _originalName;
            set
            {
                _originalName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OriginalName)));
            }
        }

        public string NewName
        {
            get => _newName;
            set
            {
                _newName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NewName)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasChanges)));
            }
        }

        public string FullPath
        {
            get => _fullPath;
            set
            {
                _fullPath = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FullPath)));
            }
        }

        public bool HasChanges => !string.Equals(OriginalName, NewName, StringComparison.OrdinalIgnoreCase);

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
