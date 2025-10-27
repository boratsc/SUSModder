using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;

namespace SUSModder.Behaviors;

/// <summary>
/// Behavior rozróżniający single i double click z własnymi komendami.
/// Dwuklik ma priorytet - jeśli zostanie wykryty, single click nie zostanie wykonany.
/// </summary>
public class DoubleClickBehavior : Behavior<Control>
{
    /// <summary>
    /// Komenda wykonywana przy pojedynczym kliknięciu
    /// </summary>
    public static readonly StyledProperty<ICommand?> SingleClickCommandProperty =
        AvaloniaProperty.Register<DoubleClickBehavior, ICommand?>(nameof(SingleClickCommand));

    public ICommand? SingleClickCommand
    {
        get => GetValue(SingleClickCommandProperty);
        set => SetValue(SingleClickCommandProperty, value);
    }

    /// <summary>
    /// Komenda wykonywana przy dwukliknięciu
    /// </summary>
    public static readonly StyledProperty<ICommand?> DoubleClickCommandProperty =
        AvaloniaProperty.Register<DoubleClickBehavior, ICommand?>(nameof(DoubleClickCommand));

    public ICommand? DoubleClickCommand
    {
        get => GetValue(DoubleClickCommandProperty);
        set => SetValue(DoubleClickCommandProperty, value);
    }

    /// <summary>
    /// Parametr przekazywany do komendy
    /// </summary>
    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<DoubleClickBehavior, object?>(nameof(CommandParameter));

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>
    /// Maksymalny czas między kliknięciami aby rozpoznać double click (w milisekundach)
    /// </summary>
    public static readonly StyledProperty<int> DoubleClickIntervalProperty =
        AvaloniaProperty.Register<DoubleClickBehavior, int>(nameof(DoubleClickInterval), 300);

    public int DoubleClickInterval
    {
        get => GetValue(DoubleClickIntervalProperty);
        set => SetValue(DoubleClickIntervalProperty, value);
    }

    private DateTime _lastClickTime = DateTime.MinValue;
    private bool _isWaitingForDoubleClick;
    private DispatcherTimer? _singleClickTimer;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            AssociatedObject.PointerPressed += OnPointerPressed;
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        if (AssociatedObject != null)
        {
            AssociatedObject.PointerPressed -= OnPointerPressed;
        }
        _singleClickTimer?.Stop();
        _singleClickTimer = null;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Sprawdź czy to lewy przycisk myszy
        var point = e.GetCurrentPoint(AssociatedObject);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        var now = DateTime.Now;
        var timeSinceLastClick = now - _lastClickTime;

        // Wykryto double click
        if (timeSinceLastClick.TotalMilliseconds < DoubleClickInterval && _isWaitingForDoubleClick)
        {
            _singleClickTimer?.Stop();
            _isWaitingForDoubleClick = false;
            _lastClickTime = DateTime.MinValue;

            ExecuteCommand(DoubleClickCommand);
            e.Handled = true;
            return;
        }

        // Pierwsze kliknięcie - czekaj na możliwy double click
        _lastClickTime = now;
        _isWaitingForDoubleClick = true;

        // Anuluj poprzedni timer jeśli istnieje
        _singleClickTimer?.Stop();

        // Utwórz timer dla single click
        _singleClickTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(DoubleClickInterval)
        };

        _singleClickTimer.Tick += (s, args) =>
        {
            _singleClickTimer.Stop();
            if (_isWaitingForDoubleClick)
            {
                _isWaitingForDoubleClick = false;
                ExecuteCommand(SingleClickCommand);
            }
        };

        _singleClickTimer.Start();
    }

    private void ExecuteCommand(ICommand? command)
    {
        if (command?.CanExecute(CommandParameter) == true)
        {
            command.Execute(CommandParameter);
        }
    }
}
