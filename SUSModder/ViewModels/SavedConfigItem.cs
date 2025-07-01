using System.ComponentModel;

namespace SUSModder.ViewModels
{
    public class SavedConfigItem : INotifyPropertyChanged
    {
        private string _hash = "";
        private string _date = "";
        private bool _isSelected = false;

        public string Hash
        {
            get => _hash;
            set
            {
                _hash = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Hash)));
            }
        }

        public string Date
        {
            get => _date;
            set
            {
                _date = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Date)));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public string DisplayText => $"{Date} - {Hash}";

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
