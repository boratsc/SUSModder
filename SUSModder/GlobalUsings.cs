// ReactiveUI 26 "System.Reactive" flavor (ReactiveUI.Avalonia.Reactive):
// the packages ship these namespaces as implicit global usings only when <ImplicitUsings> is
// enabled; this project keeps implicit usings off, so they are declared once here instead.
// - ReactiveUI.Reactive: ReactiveObject, ReactiveCommand, RaiseAndSetIfChanged, WhenActivated, RxSchedulers
// - ReactiveUI.Binding.Reactive: WhenAnyValue, ToProperty, ObservableAsPropertyHelper, InvokeCommand
// - System.Reactive.Disposables.Fluent: DisposableExtensions.DisposeWith (moved here in System.Reactive 7)
// See https://www.reactiveui.net/documentation/reactiveui/upgrading/reactiveui-binding-migration/
global using ReactiveUI.Reactive;
global using ReactiveUI.Binding.Reactive;
global using System.Reactive.Disposables.Fluent;
