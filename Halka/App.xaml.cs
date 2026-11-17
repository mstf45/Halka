using System.IO;
using System.Windows;
using System.Windows.Threading;
using Halka.Core;

namespace Halka;

public partial class App : Application
{
    readonly GestureTracker _gesture = new();
    readonly SemaphoreSlim _gate = new(1, 1);
    Mutex? _mutex;
    EventWaitHandle? _show;
    InputHook? _hook;
    TrayHost? _tray;
    DonutWindow? _donut;
    MainWindow? _home;
    AppSettings _settings = new();
    bool _dialog;
    int _moveQueued;
    int _lastX;
    int _lastY;

    public bool Exiting { get; private set; }
    public bool HookInstalled => _hook?.Installed == true;
    public AppSettings Settings => _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Halka");
            args.Handled = true;
        };

        if (Cli.TryRun(e.Args, out var code))
        {
            Shutdown(code);
            return;
        }

        System.Windows.Forms.Application.EnableVisualStyles();
        _settings = SettingsStore.Load();
        _show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Halka.Show");
        _mutex = new Mutex(true, @"Local\Halka.Single", out var created);
        if (!created)
        {
            try
            {
                _show.Set();
            }
            catch
            {
            }

            Shutdown();
            return;
        }

        var waiter = new Thread(() =>
        {
            while (_show.WaitOne())
                Dispatcher.BeginInvoke(ShowHome);
        })
        {
            IsBackground = true,
            Name = "Halka.Show"
        };
        waiter.Start();

        _hook = new InputHook();
        _hook.MouseDown += (x, y) => Dispatcher.BeginInvoke(() => OnMouseDown(x, y));
        _hook.MouseUp += (_, _) => OnMouseUp();
        _hook.MouseMove += OnMouseMove;
        _hook.Wheel += delta => Dispatcher.BeginInvoke(() =>
        {
            if (_donut is { ClickMode: false } && delta != 0)
                _donut.Flip();
        });
        _hook.Escape += () => Dispatcher.BeginInvoke(CloseDonut);
        _hook.Tab += () => Dispatcher.BeginInvoke(() =>
        {
            if (_donut is { ClickMode: false })
                _donut.Flip();
        });
        _hook.Install();
        _tray = new TrayHost(ShowHome, ExitApp);
        if (_settings.ShowHomeOnStart)
            ShowHome();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        _tray?.Dispose();
        _show?.Dispose();
        try
        {
            _mutex?.Dispose();
        }
        catch
        {
        }

        base.OnExit(e);
    }

    public void SaveSettings() => SettingsStore.Save(_settings);

    public void SetDialog(bool open) => _dialog = open;

    public void ShowHome()
    {
        if (_home == null)
        {
            _home = new MainWindow();
            _home.Closed += (_, _) => _home = null;
        }

        _home.Show();
        if (_home.WindowState == WindowState.Minimized)
            _home.WindowState = WindowState.Normal;
        _home.Activate();
        _home.ReloadRecent();
    }

    public void NotifyHidden()
    {
        if (_settings.ToldAboutTray)
            return;
        _settings.ToldAboutTray = true;
        SaveSettings();
        _tray?.Balloon("Halka tepside açık. Ctrl basılıyken dosyayı sürükleyebilirsin.");
    }

    public void ShowDonutFor(IReadOnlyList<string> files)
    {
        var point = System.Windows.Forms.Cursor.Position;
        OpenDonut(point.X, point.Y, clickMode: true, files);
    }

    public void ExitApp()
    {
        Exiting = true;
        _home?.PrepareClose();
        _home?.Close();
        CloseDonut();
        Shutdown();
    }

    void OnMouseDown(int x, int y)
    {
        if (_dialog || !_settings.GestureEnabled || _donut != null)
            return;
        _gesture.Armed = (NativeMethods.GetAsyncKeyState(NativeMethods.VkControl) & 0x8000) != 0;
        _gesture.Press(x, y);
    }

    void OnMouseMove(int x, int y)
    {
        _lastX = x;
        _lastY = y;
        if (Interlocked.CompareExchange(ref _moveQueued, 1, 0) != 0)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _moveQueued, 0);
            var px = _lastX;
            var py = _lastY;
            if (_donut != null)
            {
                _donut.HoverPixels(px, py);
                return;
            }

            if (_dialog || !_settings.GestureEnabled)
                return;
            _gesture.Armed = (NativeMethods.GetAsyncKeyState(NativeMethods.VkControl) & 0x8000) != 0;
            if (_gesture.Moved(px, py))
                OpenDonut(px, py, clickMode: false, files: null);
        }, DispatcherPriority.Input);
    }

    void OnMouseUp()
    {
        Dispatcher.BeginInvoke(() => _gesture.Release(), DispatcherPriority.Input);
        Dispatcher.BeginInvoke(() =>
        {
            if (_donut is { ClickMode: false, HadDrop: false })
                CloseDonut();
        }, DispatcherPriority.ApplicationIdle);
    }

    void OpenDonut(int x, int y, bool clickMode, IReadOnlyList<string>? files)
    {
        CloseDonut();
        var ffmpeg = FfmpegOps.Find(_settings.FfmpegPath) != null;
        var profile = files is null ? Profile.Empty : Profiles.For(files, ffmpeg);
        var window = new DonutWindow(clickMode);
        window.Load(profile, files ?? [], _settings.JpegQuality);
        window.Chosen += (slice, list) =>
        {
            var copy = list.ToList();
            CloseDonut();
            StartJob(copy, slice);
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_donut, window))
            {
                _donut = null;
                _gesture.Open = false;
            }
        };
        _donut = window;
        _gesture.Open = true;
        window.PreparePixels(x, y);
        window.Show();
        window.PlaceAtPixels(x, y);
        window.ApplyRegion();
    }

    IReadOnlyList<ConvertItem> PlaceResults(IReadOnlyList<string> sources, IReadOnlyList<ConvertItem> results)
    {
        var ready = results.Any(item => item.Ok && item.Outputs.Count > 0);
        if (!ready)
            return results;

        SetDialog(true);
        try
        {
            var start = sources.FirstOrDefault(File.Exists);
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Dönüşen dosyalar nereye kaydedilsin?",
                UseDescriptionForTitle = true,
                InitialDirectory = string.IsNullOrWhiteSpace(start) ? "" : Path.GetDirectoryName(start) ?? ""
            };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                foreach (var item in results)
                {
                    foreach (var path in item.Outputs)
                        TryDeleteTree(path);
                }

                return results.Select(item => item.Ok
                    ? new ConvertItem(item.Source, false, "Kayıt yeri seçilmedi.", [])
                    : item).ToList();
            }

            var folder = dialog.SelectedPath;
            Directory.CreateDirectory(folder);
            return results.Select(item =>
            {
                if (!item.Ok || item.Outputs.Count == 0)
                    return item;
                var moved = new List<string>();
                foreach (var path in item.Outputs)
                {
                    var dest = OutputPaths.Unique(Path.Combine(folder, Path.GetFileName(path)));
                    Relocate(path, dest);
                    moved.Add(dest);
                }

                var message = moved.Count == 1 ? Path.GetFileName(moved[0]) : $"{moved.Count} dosya";
                return new ConvertItem(item.Source, true, message, moved);
            }).ToList();
        }
        finally
        {
            SetDialog(false);
        }
    }

    static void Relocate(string source, string dest)
    {
        try
        {
            if (Directory.Exists(source))
                Directory.Move(source, dest);
            else
                File.Move(source, dest);
        }
        catch (IOException)
        {
            if (Directory.Exists(source))
            {
                CopyTree(source, dest);
                Directory.Delete(source, recursive: true);
            }
            else
            {
                File.Copy(source, dest);
                File.Delete(source);
            }
        }
    }

    static void CopyTree(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(source))
            CopyTree(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    static void TryDeleteTree(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    void CloseDonut()
    {
        var window = _donut;
        if (window == null)
            return;
        _donut = null;
        _gesture.Open = false;
        window.Close();
    }

    void StartJob(IReadOnlyList<string> files, Slice slice)
    {
        var toast = new ToastWindow();
        var cts = new CancellationTokenSource();
        toast.CancelRequested += () => cts.Cancel();
        toast.Show();
        var progress = new Progress<string>(message =>
        {
            if (toast.IsVisible)
                toast.Report(message);
        });
        var quality = _settings.JpegQuality;
        var ffmpeg = _settings.FfmpegPath;
        var token = cts.Token;
        _ = Task.Run(() =>
        {
            var entered = false;
            try
            {
                _gate.Wait(token);
                entered = true;
                var temp = Path.Combine(Path.GetTempPath(), "Halka", Guid.NewGuid().ToString("n"));
                Directory.CreateDirectory(temp);
                IReadOnlyList<ConvertItem> results;
                try
                {
                    results = Converter.Run(new ConvertRequest(files, slice, quality, ffmpeg, temp), progress, token);
                    results = Dispatcher.Invoke(() => PlaceResults(files, results));
                }
                finally
                {
                    TryDeleteTree(temp);
                }

                var entries = results.Select(result => new RecentEntry
                {
                    At = DateTime.Now,
                    Source = result.Source,
                    Output = result.Outputs.FirstOrDefault() ?? "",
                    Ok = result.Ok,
                    Detail = result.Message
                }).ToList();
                Dispatcher.Invoke(() =>
                {
                    RecentStore.Add(entries);
                    if (toast.IsVisible)
                        toast.Finish(results);
                    _home?.ReloadRecent();
                });
            }
            catch (OperationCanceledException)
            {
                Dispatcher.Invoke(() =>
                {
                    if (toast.IsVisible)
                        toast.Finish([new ConvertItem("", false, "Vazgeçildi", [])]);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    if (toast.IsVisible)
                        toast.Finish([new ConvertItem("", false, ex.Message, [])]);
                });
            }
            finally
            {
                if (entered)
                    _gate.Release();
                cts.Dispose();
            }
        });
    }
}
