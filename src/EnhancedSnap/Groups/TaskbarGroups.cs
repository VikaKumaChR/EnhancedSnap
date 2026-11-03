using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Core.Interop;
using EnhancedSnap.Core.Localization;
using EnhancedSnap.Engine.Input;
using EnhancedSnap.Engine.Native;
using EnhancedSnap.Engine.Snapping;
using EnhancedSnap.Engine.Ui;
using Windows.UI;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Groups;

internal interface ITaskbarGroupsHost
{
    FluentTheme Theme { get; }

    RectI WorkArea(SnapGroup group);

    float DisplayScale(SnapGroup group);

    RectI MemberRect(SnapGroup group, int[] zones);

    void Activate(SnapGroup group);

    void CloseWindows(SnapGroup group);
}

/// <summary>
/// Snap groups on the taskbar, in Alt + Tab and in Task View, like Windows' own. Each group gets a proxy window per app
/// it contains, carrying that app's AppUserModelID so the taskbar files it under the app's button. Proxies are
/// minimized; their thumbnail is an iconic bitmap of the arrangement. The first proxy of a group can be activated (it
/// is what Alt + Tab and Task View show); the others are non-activating, so each group appears there once.
/// <list type="bullet">
/// <item>Thumbnails: the shell waits only a moment (Task View shows a black card when the answer comes late), so each
/// group keeps a snapshot of its arrangement, rendered in the background when the group forms or changes, and requests
/// are answered from it at once.</item>
/// <item>Peek (the pointer resting on the thumbnail): the group's windows in place, like Windows' groups, instead of an
/// empty desktop. DWM draws a live preview bitmap at the proxy's restored position, which is the group's work area. A
/// late answer shows a blank placeholder window, so the request is answered at once from the snapshot, scaled up, and
/// a full-resolution capture replaces it moments later. Full-size captures are freed shortly after.</item>
/// <item>Close (X) closes every window of the group.</item>
/// <item>Order: Alt + Tab and Task View list windows by when they were last activated. The input thread hands
/// Alt + Tab and Win + Tab pressed in a group window to <see cref="BeginSwitch"/>, which activates the group's proxy
/// before the switcher opens, so the group leads the list as Windows' own groups do.</item>
/// </list>
/// </summary>
internal sealed unsafe class TaskbarGroups(ITaskbarGroupsHost host) : IDisposable
{
    private const string ClassName = "EnhancedSnap.TaskbarGroup";
    private const uint WM_APP_CHOSEN = WM_APP + 1;
    private const uint WM_APP_CLOSE = WM_APP + 2;
    private const uint WM_APP_RENDERED = WM_APP + 3;
    private const nuint RenderTimer = 1;
    private const nuint SwitchTimer = 2;
    private const nuint SwitchEndTimer = 3;
    private const nuint PeekTimer = 4;
    private const uint RenderDelayMs = 800;  // after the last change: a divider drag or a burst of activations renders once
    private const long StaleMs = 3000;       // a snapshot older than this is re-rendered after it has been shown
    private const long PeekFreshMs = 3000;   // an older peek is shown, then captured again
    private const uint PeekKeepMs = 20_000;  // full-size peeks are freed this long after the last one was shown
    private static readonly Dictionary<nint, Proxy> Instances = [];
    private static bool _classRegistered;

    private readonly Dictionary<(Guid Group, string AppId), Proxy> _proxies = [];
    private readonly Dictionary<Guid, Snapshot> _snapshots = [];
    private readonly Dictionary<Guid, Snapshot> _peeks = [];
    private readonly Dictionary<Guid, (nint Proxy, long At)> _peekAsked = [];
    private readonly HashSet<Guid> _peekWanted = [];
    private readonly HashSet<Guid> _rendering = [];
    private readonly HashSet<Guid> _renderAgain = [];
    private readonly ConcurrentQueue<Rendered> _rendered = new();
    private (int Width, int Height) _snapshotSize = (480, 300); // grows to the largest thumbnail the shell asks for
    private Dictionary<nint, nint> _switcherProxies = [];       // group window → its group's primary proxy (input thread)
    private Proxy? _switching;
    private Proxy? _switched;

    private ITaskbarGroupsHost Host => host;

    /// <summary>Create, update or remove proxies so they match the groups (two or more windows each).</summary>
    public void Sync(IReadOnlyList<SnapGroup> groups, bool enabled)
    {
        var wanted = new Dictionary<(Guid, string), (SnapGroup Group, nint Member, bool Primary)>();
        if (enabled && groups.Any(g => g.Members.Count >= 2))
        {
            var taskbar = AppIds.TaskbarIds();
            foreach (var group in groups.Where(g => g.Members.Count >= 2))
            {
                bool primary = true;
                foreach (nint member in group.RecentOrder.Where(group.Members.ContainsKey).Where(IsWindow))
                {
                    if (AppIds.Resolve(member, taskbar) is not { } appId || wanted.ContainsKey((group.Id, appId)))
                    {
                        continue;
                    }

                    wanted[(group.Id, appId)] = (group, member, primary);
                    primary = false;
                }
            }
        }

        foreach (var key in _proxies.Keys.Where(k => !wanted.ContainsKey(k)).ToList())
        {
            _proxies.Remove(key, out var stale);
            stale!.Dispose();
        }

        foreach (var (key, want) in wanted)
        {
            if (_proxies.TryGetValue(key, out var proxy) && proxy.Primary != want.Primary)
            {
                _proxies.Remove(key);
                proxy.Dispose(); // Alt + Tab membership changes with the style: recreate
                proxy = null;
            }

            if (proxy is null)
            {
                try
                {
                    _proxies[key] = new Proxy(this, want.Group, key.Item2, want.Member, want.Primary);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Taskbar preview for {want.Group} failed: {ex.Message}");
                }
            }
            else
            {
                proxy.UpdateTitle();
                proxy.UpdatePlacement();
            }
        }

        var switcher = new Dictionary<nint, nint>();
        foreach (var proxy in _proxies.Values.Where(p => p.Primary))
        {
            foreach (nint member in proxy.Group.Members.Keys)
            {
                switcher[member] = proxy.Hwnd;
            }
        }

        Volatile.Write(ref _switcherProxies, switcher);
        TakeRendered();
        foreach (var id in _snapshots.Keys.Where(id => !_proxies.Keys.Any(k => k.Group == id)).ToList())
        {
            _snapshots.Remove(id, out var stale);
            stale!.Dispose();
        }

        foreach (var id in _peeks.Keys.Where(id => !_proxies.Keys.Any(k => k.Group == id)).ToList())
        {
            _peeks.Remove(id, out var stale);
            stale!.Dispose();
        }

        foreach (var group in wanted.Values.Select(w => w.Group).Distinct())
        {
            if (!_snapshots.ContainsKey(group.Id) && !_rendering.Contains(group.Id))
            {
                StartRender(group); // ready before the shell first asks
            }
        }
    }

    /// <summary>The arrangement changed or a member was activated: a fresh snapshot once things settle.</summary>
    public void Invalidate(SnapGroup group)
    {
        foreach (var proxy in _proxies.Values)
        {
            if (proxy.Group == group)
            {
                proxy.UpdateTitle();
            }
        }

        if (_peeks.Remove(group.Id, out var peek))
        {
            peek.Dispose(); // never peek at an arrangement that is gone
        }

        Schedule(group);
    }

    /// <summary>The proxy that stands for a group window in Alt + Tab and Task View, or 0. Called on the input thread.</summary>
    public nint SwitcherProxy(nint member) => Volatile.Read(ref _switcherProxies).GetValueOrDefault(member);

    /// <summary>
    /// Alt + Tab or Win + Tab was pressed in a group window and the input thread held the Tab back: activate the group's
    /// proxy so the switcher lists the group first, then let the Tab through.
    /// </summary>
    public void BeginSwitch(nint proxyHwnd, bool altTab)
    {
        if (_switching is not null || !Instances.TryGetValue(proxyHwnd, out var proxy) || !_proxies.ContainsValue(proxy))
        {
            ReplayTab(altTab, groupFirst: false);
            return;
        }

        _switching = proxy;
        _switched = proxy;
        proxy.BeginSwitch(altTab);
    }

    /// <summary>The modifier was released: the switcher closed. Nothing chosen (Esc, or the group itself) = back to the group.</summary>
    public void EndSwitch()
    {
        if (_switching is { } busy)
        {
            busy.EndPending = true; // still replaying: check once that is done
        }
        else
        {
            _switched?.ScheduleEndCheck(150);
        }
    }

    /// <summary>Which proxies exist and how the group's windows resolved (for the log).</summary>
    public string Summary(SnapGroup group)
    {
        string proxies = _proxies.Count == 0 ? "no taskbar previews"
            : string.Join("; ", _proxies.Select(p => $"{p.Key.AppId}{(p.Value.Primary ? " (primary)" : "")} 0x{p.Value.Hwnd:X}"));
        var taskbar = AppIds.TaskbarIds();
        return proxies + " | " + string.Join(" | ", group.Members.Keys.Select(h => AppIds.Describe(h, taskbar)));
    }

    /// <summary>Test probes: the group's thumbnail (or, with <paramref name="peek"/>, its peek) as a 32-bit BMP.</summary>
    public bool SavePreview(SnapGroup group, string path, int maxWidth, int maxHeight, bool peek = false)
    {
        if (Prepare(group, peek) is not { } job)
        {
            return false;
        }

        var done = Render(job, maxWidth, maxHeight, capture: true);
        try
        {
            var image = peek ? done.Peek : done.Thumbnail;
            if (image is null)
            {
                return false;
            }

            int imageSize = image.Width * image.Height * 4;
            using var file = File.Create(path);
            using var writer = new BinaryWriter(file);
            writer.Write((ushort)0x4D42);
            writer.Write(14 + 40 + imageSize);
            writer.Write(0);
            writer.Write(14 + 40);
            writer.Write(40);
            writer.Write(image.Width);
            writer.Write(-image.Height);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(0);
            writer.Write(imageSize);
            writer.Write(2835);
            writer.Write(2835);
            writer.Write(0);
            writer.Write(0);
            writer.Write(new ReadOnlySpan<byte>(image.Pixels, imageSize));
            return true;
        }
        finally
        {
            done.Thumbnail?.Dispose();
            done.Peek?.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var proxy in _proxies.Values)
        {
            proxy.Dispose();
        }

        _proxies.Clear();
        TakeRendered(); // disposes what finished; a render still running leaves with the process
        foreach (var image in _snapshots.Values.Concat(_peeks.Values))
        {
            image.Dispose();
        }

        _snapshots.Clear();
        _peeks.Clear();
    }

    /// <summary>Answers the shell's thumbnail request at once, from the group's snapshot.</summary>
    private void SendThumbnail(Proxy proxy, int maxWidth, int maxHeight)
    {
        // UPDATE: Live thumbnails (Windows.Graphics.Capture of each member) instead of snapshots, for Alt + Tab and Task View.
        TakeRendered();
        var group = proxy.Group;
        _snapshotSize = (Math.Max(_snapshotSize.Width, Math.Min(maxWidth, 800)), Math.Max(_snapshotSize.Height, Math.Min(maxHeight, 500)));
        nint bitmap = 0;
        int width = 0, height = 0;
        if (_snapshots.TryGetValue(group.Id, out var snapshot))
        {
            bitmap = Scale(snapshot, maxWidth, maxHeight, out width, out height);
            if (Environment.TickCount64 - snapshot.Taken > StaleMs || width > snapshot.Width || height > snapshot.Height)
            {
                Schedule(group); // window contents move on, or the shell wants it bigger: better next time
            }

            if (PointerOnTaskbar() && (!_peeks.TryGetValue(group.Id, out var peek) || Environment.TickCount64 - peek.Taken > PeekFreshMs))
            {
                // The taskbar flyout is opening: the pointer may rest on the group next, so capture its peek now.
                _peekWanted.Add(group.Id);
                StartRender(group);
                SetTimer(proxy.Hwnd, PeekTimer, PeekKeepMs, 0);
            }
        }
        else if (Prepare(group, peek: false) is { } job)
        {
            // Not rendered yet: the wallpaper and app icons now (no waiting on the apps), the full render follows.
            var quick = Render(job, maxWidth, maxHeight, capture: false);
            if (quick.Thumbnail is { } image)
            {
                bitmap = image.Bitmap;
                (width, height) = (image.Width, image.Height);
                image.Detach();
            }

            if (!_rendering.Contains(group.Id))
            {
                StartRender(group);
            }
        }

        int hr = bitmap == 0 ? -1 : DwmSetIconicThumbnail(proxy.Hwnd, bitmap, 0);
        Log.Debug($"Taskbar group 0x{proxy.Hwnd:X}: thumbnail {width}x{height} (max {maxWidth}x{maxHeight}, {(snapshot is null ? "icons" : "snapshot")}) hr=0x{hr:X8}");
        if (bitmap != 0)
        {
            DeleteObject(bitmap);
        }
    }

    /// <summary>
    /// Peek: answers at once — with the last full-size capture, or the snapshot scaled up — and captures a sharp one when
    /// that is missing or old; it replaces this one while the peek is still showing.
    /// </summary>
    private void SendLivePreview(Proxy proxy)
    {
        TakeRendered();
        var group = proxy.Group;
        nint bitmap;
        bool owned = false;
        if (_peeks.TryGetValue(group.Id, out var peek))
        {
            bitmap = peek.Bitmap;
        }
        else
        {
            bitmap = QuickPeek(group);
            owned = true;
        }

        if (peek is null || Environment.TickCount64 - peek.Taken > PeekFreshMs)
        {
            _peekWanted.Add(group.Id);
            StartRender(group);
        }

        _peekAsked[group.Id] = (proxy.Hwnd, Environment.TickCount64);
        int hr = bitmap == 0 ? -1 : DwmSetIconicLivePreviewBitmap(proxy.Hwnd, bitmap, null, 0);
        Log.Debug($"Taskbar group 0x{proxy.Hwnd:X}: peek ({(owned ? "scaled snapshot" : "capture")}) hr=0x{hr:X8}");
        if (owned && bitmap != 0)
        {
            DeleteObject(bitmap);
        }

        SetTimer(proxy.Hwnd, PeekTimer, PeekKeepMs, 0);
    }

    /// <summary>Frees the full-size peeks and makes DWM ask again next time (a cached peek would show old contents).</summary>
    private void ReleasePeeks()
    {
        foreach (var peek in _peeks.Values)
        {
            peek.Dispose();
        }

        _peeks.Clear();
        _peekAsked.Clear();
        foreach (var proxy in _proxies.Values)
        {
            DwmInvalidateIconicBitmaps(proxy.Hwnd);
        }
    }

    /// <summary>Re-render a group's snapshot once things settle (each call restarts the delay).</summary>
    private void Schedule(SnapGroup group)
    {
        if (_proxies.Values.Where(p => p.Group == group).OrderBy(p => !p.Primary).FirstOrDefault() is { } proxy)
        {
            SetTimer(proxy.Hwnd, RenderTimer, RenderDelayMs, 0);
        }
    }

    /// <summary>
    /// Renders a group's snapshot (and its peek, when the shell asked for one) on a pool thread: PrintWindow waits on
    /// every member app, and a slow or hung one must neither stall the engine nor delay the answer to the shell.
    /// </summary>
    private void StartRender(SnapGroup group)
    {
        TakeRendered();
        if (_rendering.Contains(group.Id))
        {
            _renderAgain.Add(group.Id); // changed while rendering
            return;
        }

        var job = Prepare(group, _peekWanted.Contains(group.Id));
        var notify = _proxies.Values.FirstOrDefault(p => p.Group == group);
        if (job is null || notify is null)
        {
            return;
        }

        _rendering.Add(group.Id);
        var (maxWidth, maxHeight) = _snapshotSize;
        nint hwnd = notify.Hwnd;
        ThreadPool.UnsafeQueueUserWorkItem(
            state =>
            {
                var done = new Rendered(job.Group, null, null);
                try
                {
                    done = Render(job, maxWidth, maxHeight, capture: true);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Group preview failed: {ex.Message}");
                }

                _rendered.Enqueue(done);
                Win32.PostMessage(hwnd, WM_APP_RENDERED, 0, 0);
            },
            null);
    }

    /// <summary>
    /// Adopts finished renders (UI thread). The shell asks again for what it is showing; a peek on screen is swapped for
    /// the sharp one directly, since asking again would show DWM's blank placeholder in between.
    /// </summary>
    private void TakeRendered()
    {
        while (_rendered.TryDequeue(out var done))
        {
            _rendering.Remove(done.Group);
            var proxies = _proxies.Values.Where(p => p.Group.Id == done.Group).ToList();
            if (done.Thumbnail is { } snapshot && proxies.Count > 0)
            {
                if (_snapshots.Remove(done.Group, out var old))
                {
                    old.Dispose();
                }

                _snapshots[done.Group] = snapshot;
            }
            else
            {
                done.Thumbnail?.Dispose();
            }

            bool peeking = false;
            if (done.Peek is { } peek && proxies.Count > 0)
            {
                if (_peeks.Remove(done.Group, out var old))
                {
                    old.Dispose();
                }

                _peeks[done.Group] = peek;
                _peekWanted.Remove(done.Group);
                if (_peekAsked.TryGetValue(done.Group, out var asked) && Environment.TickCount64 - asked.At < PeekKeepMs && IsWindow(asked.Proxy))
                {
                    DwmSetIconicLivePreviewBitmap(asked.Proxy, peek.Bitmap, null, 0);
                    peeking = true;
                }
            }
            else
            {
                done.Peek?.Dispose();
            }

            if (done.Thumbnail is not null && !peeking)
            {
                foreach (var proxy in proxies)
                {
                    DwmInvalidateIconicBitmaps(proxy.Hwnd);
                }
            }

            if (_renderAgain.Remove(done.Group) && proxies.Count > 0)
            {
                if (_peekWanted.Contains(done.Group))
                {
                    StartRender(proxies[0].Group); // a peek is waiting for it
                }
                else
                {
                    Schedule(proxies[0].Group);
                }
            }
        }
    }

    /// <summary>Thumbnail requests while the pointer is on the taskbar come from its preview flyout opening.</summary>
    private static bool PointerOnTaskbar()
    {
        if (!Win32.GetCursorPos(out var point))
        {
            return false;
        }

        nint window = GetAncestor(WindowFromPoint(point), GA_ROOT);
        return window != 0 && ClassIs(window, "Shell_TrayWnd", "Shell_SecondaryTrayWnd");
    }

    private static bool ClassIs(nint hwnd, params ReadOnlySpan<string> names)
    {
        char* buffer = stackalloc char[32];
        var name = new ReadOnlySpan<char>(buffer, GetClassName(hwnd, buffer, 32));
        foreach (string candidate in names)
        {
            if (name.SequenceEqual(candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What a render needs, taken on the UI thread so that rendering never reads group state.</summary>
    private RenderJob? Prepare(SnapGroup group, bool peek)
    {
        var area = host.WorkArea(group);
        if (area.IsEmpty)
        {
            return null;
        }

        var members = Enumerable.Reverse(group.RecentOrder).Where(group.Members.ContainsKey) // back to front
            .Select(hwnd => (hwnd, host.MemberRect(group, group.Members[hwnd]))).ToArray();
        var theme = host.Theme;
        return new RenderJob(group.Id, area, members, Opaque(theme.Frame.Fallback), Opaque(theme.Placeholder.Fallback), peek, host.DisplayScale(group));
    }

    /// <summary>
    /// One capture of every member (PrintWindow with full content, so DirectComposition apps work too), drawn twice: the
    /// thumbnail — the arrangement at its scaled positions over the wallpaper — and, for <see cref="RenderJob.Peek"/>, the
    /// peek — the windows at full size where they are, rounded and shadowed like real windows, the rest transparent.
    /// Minimized, hung or elevated windows show their icon, and so do all windows without <paramref name="capture"/>.
    /// Runs on any thread.
    /// </summary>
    private static Rendered Render(RenderJob job, int maxWidth, int maxHeight, bool capture)
    {
        var area = job.Area;
        if (maxWidth <= 0 || maxHeight <= 0)
        {
            return new Rendered(job.Group, null, null);
        }

        double k = Math.Min(maxWidth / (double)area.Width, maxHeight / (double)area.Height);
        int width = Math.Max(1, (int)(area.Width * k));
        int height = Math.Max(1, (int)(area.Height * k));
        nint bitmap = CreateDib(width, height, out uint* pixels);
        if (bitmap == 0)
        {
            return new Rendered(job.Group, null, null);
        }

        new Span<uint>(pixels, width * height).Fill(job.Backdrop);
        nint dc = CreateCompatibleDC(0);
        nint old = SelectObject(dc, bitmap);
        Wallpaper.Draw(dc, width, height); // like Windows' group thumbnails; the frame color when there is none
        GdiFlush();
        uint[] backdrop = new Span<uint>(pixels, width * height).ToArray(); // what the rounded corners reveal
        SetStretchBltMode(dc, HALFTONE);
        SetBrushOrgEx(dc, 0, 0, 0);

        uint* peekPixels = null;
        nint peekBitmap = job.Peek ? CreateDib(area.Width, area.Height, out peekPixels) : 0; // zeroed: transparent
        nint peekDc = peekBitmap == 0 ? 0 : CreateCompatibleDC(0);
        nint peekOld = peekDc == 0 ? 0 : SelectObject(peekDc, peekBitmap);
        var screen = new RectI(0, 0, area.Width, area.Height);
        var shown = new List<RectI>();

        int radius = Math.Max(2, (int)Math.Round(width / 160.0));
        foreach (var (hwnd, rect) in job.Members)
        {
            var r = new RectI(
                (int)Math.Round((rect.Left - area.Left) * k),
                (int)Math.Round((rect.Top - area.Top) * k),
                (int)Math.Round((rect.Right - area.Left) * k),
                (int)Math.Round((rect.Bottom - area.Top) * k)).Intersect(new RectI(0, 0, width, height));
            if (r.IsEmpty)
            {
                continue;
            }

            var full = rect.Offset(-area.Left, -area.Top);
            if (!capture || !DrawWindow(dc, hwnd, r, peekDc, area, ref full))
            {
                GdiFlush();
                FillRect(pixels, width, r, job.Placeholder);
                DrawIcon(dc, hwnd, r, 32);
                full = full.Intersect(screen);
                if (peekDc != 0 && !full.IsEmpty)
                {
                    FillRect(peekPixels, area.Width, full, job.Placeholder);
                    DrawIcon(peekDc, hwnd, full, (int)(48 * job.Scale));
                }
            }

            GdiFlush();
            RoundCorners(pixels, width, r, radius, backdrop);
            full = full.Intersect(screen);
            if (peekDc != 0 && !full.IsEmpty)
            {
                shown.Add(full);
            }
        }

        SelectObject(dc, old);
        DeleteDC(dc);
        GdiFlush();
        MakeOpaque(pixels, width * height);
        var thumbnail = new Snapshot(bitmap, pixels, width, height);
        if (peekDc == 0)
        {
            return new Rendered(job.Group, thumbnail, null);
        }

        SelectObject(peekDc, peekOld);
        DeleteDC(peekDc);
        GdiFlush();
        FinishPeek(peekPixels, area.Width, area.Height, shown, job.Scale);
        return new Rendered(job.Group, thumbnail, new Snapshot(peekBitmap, peekPixels, area.Width, area.Height));
    }

    /// <summary>A peek scaled up from the snapshot (or icons when there is none), shown while a sharp one is captured.</summary>
    private nint QuickPeek(SnapGroup group)
    {
        if (Prepare(group, peek: false) is not { } job)
        {
            return 0;
        }

        var area = job.Area;
        nint bitmap = CreateDib(area.Width, area.Height, out uint* pixels);
        if (bitmap == 0)
        {
            return 0;
        }

        var screen = new RectI(0, 0, area.Width, area.Height);
        var shown = new List<RectI>();
        nint dc = CreateCompatibleDC(0);
        nint old = SelectObject(dc, bitmap);
        _snapshots.TryGetValue(group.Id, out var snapshot);
        nint source = snapshot is null ? 0 : CreateCompatibleDC(0);
        nint oldSource = source == 0 ? 0 : SelectObject(source, snapshot!.Bitmap);
        SetStretchBltMode(dc, HALFTONE);
        SetBrushOrgEx(dc, 0, 0, 0);
        foreach (var (hwnd, rect) in job.Members)
        {
            var full = rect.Offset(-area.Left, -area.Top).Intersect(screen);
            if (full.IsEmpty)
            {
                continue;
            }

            if (snapshot is not null)
            {
                double k = snapshot.Width / (double)area.Width;
                StretchBlt(dc, full.Left, full.Top, full.Width, full.Height, source, (int)(full.Left * k), (int)(full.Top * k),
                    Math.Max(1, (int)(full.Width * k)), Math.Max(1, (int)(full.Height * k)), SRCCOPY);
            }
            else
            {
                GdiFlush();
                FillRect(pixels, area.Width, full, job.Placeholder);
                DrawIcon(dc, hwnd, full, (int)(48 * job.Scale));
            }

            shown.Add(full);
        }

        if (source != 0)
        {
            SelectObject(source, oldSource);
            DeleteDC(source);
        }

        SelectObject(dc, old);
        DeleteDC(dc);
        GdiFlush();
        FinishPeek(pixels, area.Width, area.Height, shown, job.Scale);
        return bitmap;
    }

    /// <summary>
    /// The windows in a peek become opaque with Windows 11's 8 epx rounded corners and a soft shadow, as on the desktop
    /// (captures carry neither); everything else stays transparent, so the real desktop shows through. Premultiplied.
    /// </summary>
    private static void FinishPeek(uint* pixels, int width, int height, List<RectI> windows, float scale)
    {
        int radius = Math.Max(2, (int)MathF.Round(8 * scale));
        int border = Math.Min(3, Math.Max(1, (int)MathF.Round(1.5f * scale)));
        foreach (var r in windows)
        {
            for (int y = r.Top; y < r.Bottom; y++)
            {
                var row = new Span<uint>(pixels + (y * width) + r.Left, r.Width);
                bool edgeRow = y < r.Top + border || y >= r.Bottom - border;
                for (int x = 0; x < row.Length; x++)
                {
                    // Captures paint a standard frame's 1 epx border opaque black, where DWM draws a faint line: draw that.
                    bool edge = edgeRow || x < border || x >= row.Length - border;
                    row[x] = edge && (row[x] & 0x00FFFFFF) == 0 ? 0x38000000 : row[x] | 0xFF000000;
                }
            }

            int rr = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
            for (int dy = 0; dy < rr; dy++)
            {
                for (int dx = 0; dx < rr; dx++)
                {
                    double ox = rr - dx - 0.5;
                    double oy = rr - dy - 0.5;
                    double coverage = Math.Clamp(rr - Math.Sqrt((ox * ox) + (oy * oy)) + 0.5, 0, 1);
                    if (coverage >= 1)
                    {
                        continue;
                    }

                    foreach (int index in (ReadOnlySpan<int>)[
                        ((r.Top + dy) * width) + r.Left + dx,
                        ((r.Top + dy) * width) + r.Right - 1 - dx,
                        ((r.Bottom - 1 - dy) * width) + r.Left + dx,
                        ((r.Bottom - 1 - dy) * width) + r.Right - 1 - dx])
                    {
                        pixels[index] = Premultiply(pixels[index], coverage);
                    }
                }
            }
        }

        // Shadow: black (so premultiplied color stays), fading over 16 epx, a little lower than the window.
        int spread = Math.Max(6, (int)MathF.Round(16 * scale));
        int drop = (int)MathF.Round(3 * scale);
        const double strength = 0.26;
        var bounds = new RectI(0, 0, width, height);
        foreach (var r in windows)
        {
            var band = r.Inflate(spread, spread - drop, spread, spread + drop).Intersect(bounds);
            double cx = r.CenterX, cy = r.CenterY + drop;
            double hx = r.Width / 2.0, hy = r.Height / 2.0;
            int rr = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
            for (int y = band.Top; y < band.Bottom; y++)
            {
                bool middle = y >= r.Top + rr && y < r.Bottom - rr;
                for (int x = band.Left; x < band.Right; x++)
                {
                    if (middle && x >= r.Left + rr && x < r.Right - rr)
                    {
                        x = r.Right - rr - 1; // the window covers the middle of the row
                        continue;
                    }

                    // Signed distance to the rounded rectangle (shifted down by the drop).
                    double qx = Math.Abs(x + 0.5 - cx) - (hx - rr);
                    double qy = Math.Abs(y + 0.5 - cy) - (hy - rr);
                    double d = Math.Sqrt((Math.Max(qx, 0) * Math.Max(qx, 0)) + (Math.Max(qy, 0) * Math.Max(qy, 0))) + Math.Min(Math.Max(qx, qy), 0) - rr;
                    if (d <= 0 || d >= spread)
                    {
                        continue;
                    }

                    double t = 1 - (d / spread);
                    double shadow = strength * t * t;
                    uint p = pixels[(y * width) + x];
                    double a = (p >> 24) / 255.0;
                    uint alpha = (uint)Math.Round((a + (shadow * (1 - a))) * 255);
                    pixels[(y * width) + x] = (p & 0x00FFFFFF) | (alpha << 24);
                }
            }
        }
    }

    private static uint Premultiply(uint p, double a) =>
        ((uint)Math.Round(((p >> 24) & 0xFF) * a) << 24) |
        ((uint)Math.Round(((p >> 16) & 0xFF) * a) << 16) |
        ((uint)Math.Round(((p >> 8) & 0xFF) * a) << 8) |
        (uint)Math.Round((p & 0xFF) * a);

    /// <summary>The snapshot fitted into <paramref name="maxWidth"/> × <paramref name="maxHeight"/> (a new bitmap).</summary>
    private static nint Scale(Snapshot snapshot, int maxWidth, int maxHeight, out int width, out int height)
    {
        double k = Math.Min(maxWidth / (double)snapshot.Width, maxHeight / (double)snapshot.Height);
        width = Math.Max(1, (int)(snapshot.Width * k));
        height = Math.Max(1, (int)(snapshot.Height * k));
        nint bitmap = CreateDib(width, height, out uint* pixels);
        if (bitmap == 0)
        {
            return 0;
        }

        nint dc = CreateCompatibleDC(0);
        nint source = CreateCompatibleDC(0);
        nint old = SelectObject(dc, bitmap);
        nint oldSource = SelectObject(source, snapshot.Bitmap);
        SetStretchBltMode(dc, HALFTONE);
        SetBrushOrgEx(dc, 0, 0, 0);
        StretchBlt(dc, 0, 0, width, height, source, 0, 0, snapshot.Width, snapshot.Height, SRCCOPY);
        SelectObject(source, oldSource);
        SelectObject(dc, old);
        DeleteDC(source);
        DeleteDC(dc);
        GdiFlush();
        MakeOpaque(pixels, width * height);
        return bitmap;
    }

    private static nint CreateDib(int width, int height, out uint* pixels)
    {
        var header = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = width, biHeight = -height, biPlanes = 1, biBitCount = 32 };
        nint bitmap = CreateDIBSection(0, &header, 0, out nint bits, 0, 0);
        pixels = (uint*)bits;
        return bitmap;
    }

    private static void MakeOpaque(uint* pixels, int count)
    {
        var all = new Span<uint>(pixels, count);
        for (int i = 0; i < all.Length; i++)
        {
            all[i] |= 0xFF000000; // GDI leaves alpha at 0; the thumbnail is opaque
        }
    }

    /// <summary>
    /// Captures a window once and draws its visible frame into the thumbnail at <paramref name="target"/> and, when
    /// <paramref name="peekDc"/> is set, at full size where it really is (<paramref name="full"/>, area-relative).
    /// </summary>
    private static bool DrawWindow(nint dc, nint hwnd, RectI target, nint peekDc, RectI area, ref RectI full)
    {
        if (IsIconic(hwnd) || IsHungAppWindow(hwnd) || !GetWindowRect(hwnd, out var window) || !TryGetFrameBounds(hwnd, out var frame))
        {
            return false;
        }

        int w = window.Right - window.Left;
        int h = window.Bottom - window.Top;
        if (w <= 0 || h <= 0 || w > 16384 || h > 16384)
        {
            return false;
        }

        var header = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        nint source = CreateDIBSection(0, &header, 0, out _, 0, 0);
        if (source == 0)
        {
            return false;
        }

        nint sourceDc = CreateCompatibleDC(0);
        nint old = SelectObject(sourceDc, source);
        bool ok = PrintWindow(hwnd, sourceDc, PW_RENDERFULLCONTENT);
        if (ok)
        {
            int fx = frame.Left - window.Left;
            int fy = frame.Top - window.Top;
            int fw = frame.Right - frame.Left;
            int fh = frame.Bottom - frame.Top;
            StretchBlt(dc, target.Left, target.Top, target.Width, target.Height, sourceDc, fx, fy, fw, fh, SRCCOPY);
            if (peekDc != 0)
            {
                full = new RectI(frame.Left - area.Left, frame.Top - area.Top, frame.Right - area.Left, frame.Bottom - area.Top);
                BitBlt(peekDc, full.Left, full.Top, fw, fh, sourceDc, fx, fy, SRCCOPY);
            }
        }

        SelectObject(sourceDc, old);
        DeleteDC(sourceDc);
        DeleteObject(source);
        return ok;
    }

    private static void DrawIcon(nint dc, nint hwnd, RectI r, int maxSize)
    {
        nint icon = WindowIcons.GetIcon(hwnd, out bool owned);
        if (icon == 0)
        {
            return;
        }

        int size = Math.Max(8, Math.Min(maxSize, Math.Min(r.Width, r.Height) / 2));
        DrawIconEx(dc, r.Left + ((r.Width - size) / 2), r.Top + ((r.Height - size) / 2), icon, size, size, 0, 0, DI_NORMAL);
        if (owned)
        {
            DestroyIcon(icon);
        }
    }

    private static void FillRect(uint* pixels, int stride, RectI r, uint color)
    {
        for (int y = r.Top; y < r.Bottom; y++)
        {
            new Span<uint>(pixels + (y * stride) + r.Left, r.Width).Fill(color);
        }
    }

    /// <summary>Rounds a miniature's corners by putting back the backdrop (wallpaper) pixels outside the radius.</summary>
    private static void RoundCorners(uint* pixels, int stride, RectI r, int radius, uint[] backdrop)
    {
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        for (int dy = 0; dy < radius; dy++)
        {
            for (int dx = 0; dx < radius; dx++)
            {
                double ox = radius - dx - 0.5;
                double oy = radius - dy - 0.5;
                if ((ox * ox) + (oy * oy) <= radius * radius)
                {
                    continue;
                }

                foreach (int index in (ReadOnlySpan<int>)[
                    ((r.Top + dy) * stride) + r.Left + dx,
                    ((r.Top + dy) * stride) + r.Right - 1 - dx,
                    ((r.Bottom - 1 - dy) * stride) + r.Left + dx,
                    ((r.Bottom - 1 - dy) * stride) + r.Right - 1 - dx])
                {
                    pixels[index] = backdrop[index];
                }
            }
        }
    }

    private static uint Opaque(Color c) => 0xFF000000 | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    /// <summary>
    /// Replays the Tab the input thread held back. With the group first, Alt + Tab gets one more press, so the selection
    /// lands where Windows would have put it (the window before the one the user is in). If the user already let go of
    /// the modifier — a quick tap — the whole shortcut is replayed.
    /// </summary>
    private static void ReplayTab(bool altTab, bool groupFirst)
    {
        bool held = altTab ? IsKeyDown(VK_MENU) : IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN);
        int modifier = altTab ? VK_MENU : VK_LWIN;
        INPUT* inputs = stackalloc INPUT[6];
        int count = 0;
        if (!held)
        {
            inputs[count++] = Key(modifier, up: false);
        }

        for (int i = altTab && groupFirst ? 2 : 1; i > 0; i--)
        {
            inputs[count++] = Key(VK_TAB, up: false);
            inputs[count++] = Key(VK_TAB, up: true);
        }

        if (!held)
        {
            inputs[count++] = Key(modifier, up: true);
        }

        SendInput((uint)count, inputs, sizeof(INPUT));

        static INPUT Key(int vk, bool up) => new()
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = up ? KEYEVENTF_KEYUP : 0, dwExtraInfo = InputHook.InjectedMarker },
        };
    }

    /// <summary>A rendered image (32-bit DIB section), owned by the UI thread once adopted.</summary>
    private sealed class Snapshot(nint bitmap, uint* pixels, int width, int height) : IDisposable
    {
        public nint Bitmap { get; private set; } = bitmap;

        public uint* Pixels { get; } = pixels;

        public int Width { get; } = width;

        public int Height { get; } = height;

        public long Taken { get; } = Environment.TickCount64;

        /// <summary>The caller takes over the bitmap.</summary>
        public void Detach() => Bitmap = 0;

        public void Dispose()
        {
            if (Bitmap != 0)
            {
                DeleteObject(Bitmap);
                Bitmap = 0;
            }
        }
    }

    private sealed record RenderJob(Guid Group, RectI Area, (nint Hwnd, RectI Rect)[] Members, uint Backdrop, uint Placeholder, bool Peek, float Scale);

    private sealed record Rendered(Guid Group, Snapshot? Thumbnail, Snapshot? Peek);

    private static void EnsureClass()
    {
        if (_classRegistered)
        {
            return;
        }

        fixed (char* name = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = GetModuleHandle(null),
                lpszClassName = name,
            };
            RegisterClassExW(&wc);
        }

        _classRegistered = true;
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (!Instances.TryGetValue(hwnd, out var proxy))
        {
            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        try
        {
            switch (msg)
            {
                case WM_DWMSENDICONICTHUMBNAIL: // the taskbar flyout, Alt + Tab and Task View
                    proxy.SendThumbnail((int)((lParam >> 16) & 0xFFFF), (int)(lParam & 0xFFFF));
                    return 0;
                case WM_DWMSENDICONICLIVEPREVIEWBITMAP: // peek: the pointer rests on the thumbnail
                    proxy.SendLivePreview();
                    return 0;
                case WM_TIMER when wParam == RenderTimer:
                    KillTimer(hwnd, RenderTimer);
                    proxy.RenderSnapshot();
                    return 0;
                case WM_TIMER when wParam == SwitchTimer:
                    proxy.SwitchTick();
                    return 0;
                case WM_TIMER when wParam == SwitchEndTimer:
                    KillTimer(hwnd, SwitchEndTimer);
                    proxy.EndCheck();
                    return 0;
                case WM_TIMER when wParam == PeekTimer:
                    KillTimer(hwnd, PeekTimer);
                    proxy.PeeksDone();
                    return 0;
                case WM_APP_RENDERED:
                    proxy.Rendered();
                    return 0;
                case WM_ACTIVATE:
                    if ((wParam & 0xFFFF) != WA_INACTIVE && !proxy.Switching)
                    {
                        Win32.PostMessage(hwnd, WM_APP_CHOSEN, 0, 0);
                    }

                    break;
                case WM_SYSCOMMAND:
                    uint command = (uint)wParam & 0xFFF0;
                    if (command == SC_RESTORE)
                    {
                        if (!proxy.Switching)
                        {
                            Win32.PostMessage(hwnd, WM_APP_CHOSEN, 0, 0);
                        }

                        return 0; // never restore the proxy itself
                    }

                    if (command == SC_CLOSE)
                    {
                        Win32.PostMessage(hwnd, WM_APP_CLOSE, 0, 0);
                        return 0;
                    }

                    break;
                case WM_CLOSE:
                    Win32.PostMessage(hwnd, WM_APP_CLOSE, 0, 0); // X on the group: close its windows, as Windows does
                    return 0;
                case WM_WINDOWPOSCHANGING:
                    var pos = (WINDOWPOS*)lParam;
                    if (!IsIconic(hwnd) && !proxy.Placing) // placing sets the restored position (where peeks are drawn); it stays hidden
                    {
                        pos->x = -32000; // if anything restores it, it stays off-screen
                        pos->y = -32000;
                        pos->flags &= ~SWP_NOMOVE;
                    }

                    return 0;
                case WM_WINDOWPOSCHANGED:
                    if (!IsIconic(hwnd) && IsWindowVisible(hwnd) && !proxy.Switching)
                    {
                        Win32.PostMessage(hwnd, WM_APP_CHOSEN, 0, 0); // non-activating proxies are restored, not activated
                    }

                    break;
                case WM_APP_CHOSEN:
                    proxy.Choose();
                    return 0;
                case WM_APP_CLOSE:
                    proxy.CloseGroup();
                    return 0;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Taskbar group message 0x{msg:X} failed", ex);
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private sealed class Proxy : IDisposable
    {
        private readonly TaskbarGroups _owner;
        private readonly nint _icon;
        private string _title = string.Empty;
        private RectI _normal;
        private bool _choosing;
        private long _chosenAt;
        private bool _switchAlt;
        private bool _switchSettled;
        private int _switchTicks;

        public Proxy(TaskbarGroups owner, SnapGroup group, string appId, nint member, bool primary)
        {
            EnsureClass();
            _owner = owner;
            Group = group;
            Primary = primary;
            Hwnd = CreateWindowEx(
                WS_EX_APPWINDOW | (primary ? 0 : WS_EX_NOACTIVATE),
                ClassName,
                Title(),
                WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX,
                -32000,
                -32000,
                320,
                200,
                0,
                0,
                GetModuleHandle(null),
                0);
            if (Hwnd == 0)
            {
                throw new InvalidOperationException("CreateWindowEx failed for a taskbar group");
            }

            Instances[Hwnd] = this;
            Platform.Elevation.AllowMessages(Hwnd, WM_SYSCOMMAND, WM_CLOSE); // the taskbar's "close" reaches an elevated engine
            AppIds.Set(Hwnd, appId); // before the first show: the taskbar files the button when it appears
            SetDwmInt(Hwnd, DWMWA_FORCE_ICONIC_REPRESENTATION, 1);
            SetDwmInt(Hwnd, DWMWA_HAS_ICONIC_BITMAP, 1);
            SetDwmInt(Hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, 1);

            nint icon = WindowIcons.GetIcon(member, out bool owned);
            _icon = icon == 0 ? 0 : CopyIcon(icon);
            if (owned && icon != 0)
            {
                DestroyIcon(icon);
            }

            if (_icon != 0)
            {
                SendMessage(Hwnd, WM_SETICON, ICON_SMALL, _icon);
                SendMessage(Hwnd, WM_SETICON, ICON_BIG, _icon);
            }

            UpdatePlacement(); // shown minimized: on the taskbar, never on screen
        }

        public nint Hwnd { get; }

        public SnapGroup Group { get; }

        public bool Primary { get; }

        /// <summary>Activated for Alt + Tab / Win + Tab: not a choice of the group (yet).</summary>
        public bool Switching { get; private set; }

        /// <summary>Inside <see cref="UpdatePlacement"/>.</summary>
        public bool Placing { get; private set; }

        public bool EndPending { get; set; }

        public void UpdateTitle()
        {
            string title = Title();
            if (title != _title)
            {
                _title = title;
                SetWindowText(Hwnd, title);
            }
        }

        /// <summary>
        /// Restored position = the group's work area: DWM draws the peek there, over the group's own windows, and Task View
        /// and Alt + Tab shape the group's card after its size.
        /// </summary>
        public void UpdatePlacement()
        {
            var area = _owner.Host.WorkArea(Group);
            if (area == _normal && _normal != default)
            {
                return;
            }

            _normal = area;
            var normal = area.IsEmpty ? RectI.FromSize(-32000, -32000, 640, 400) : WindowPlacer.ToWorkspace(Hwnd, area);
            var placement = new WINDOWPLACEMENT
            {
                length = (uint)sizeof(WINDOWPLACEMENT),
                showCmd = SW_SHOWMINNOACTIVE,
                rcNormalPosition = RECT.From(normal),
            };
            Placing = true;
            try
            {
                SetWindowPlacement(Hwnd, in placement);
            }
            finally
            {
                Placing = false;
            }
        }

        public void SendThumbnail(int maxWidth, int maxHeight) => _owner.SendThumbnail(this, maxWidth, maxHeight);

        public void SendLivePreview() => _owner.SendLivePreview(this);

        public void PeeksDone() => _owner.ReleasePeeks();

        public void RenderSnapshot() => _owner.StartRender(Group);

        public void Rendered() => _owner.TakeRendered();

        public void Choose()
        {
            if (_choosing || Environment.TickCount64 - _chosenAt < 400)
            {
                return; // Alt + Tab can both restore and activate the proxy for one choice
            }

            _choosing = true;
            try
            {
                _owner.Host.Activate(Group); // while the proxy still holds the activation the shell gave it
                ShowWindow(Hwnd, SW_SHOWMINNOACTIVE);
                _chosenAt = Environment.TickCount64;
            }
            finally
            {
                _choosing = false;
            }
        }

        public void CloseGroup() => _owner.Host.CloseWindows(Group);

        public void BeginSwitch(bool altTab)
        {
            Switching = true;
            EndPending = false;
            _switchAlt = altTab;
            _switchSettled = false;
            _switchTicks = 0;
            InputHook.SendDummyKey(); // Windows lets the process that sent the last input take the foreground
            SwitchToThisWindow(Hwnd, true);
            SetTimer(Hwnd, SwitchTimer, 10, 0);
        }

        /// <summary>Waits (10 ms ticks, at most 150 ms) until the proxy is active and one tick more, then lets the Tab through.</summary>
        public void SwitchTick()
        {
            bool active = GetForegroundWindow() == Hwnd;
            if (!active && ++_switchTicks < 15)
            {
                return;
            }

            if (active && !_switchSettled)
            {
                _switchSettled = true; // the shell records the activation a moment after it happens
                return;
            }

            KillTimer(Hwnd, SwitchTimer);
            Switching = false;
            ReplayTab(_switchAlt, groupFirst: active);
            Log.Debug($"{(_switchAlt ? "Alt + Tab" : "Win + Tab")} from {Group}: {(active ? "group first" : "proxy not activated")}");
            _owner._switching = null;
            if (EndPending)
            {
                ScheduleEndCheck(300); // the modifier was already up: let the replayed switch finish first
            }
        }

        public void ScheduleEndCheck(uint delayMs)
        {
            if (IsWindow(Hwnd))
            {
                SetTimer(Hwnd, SwitchEndTimer, delayMs, 0);
            }
        }

        /// <summary>
        /// The switcher closed without picking another window (Esc, or the group itself): bring the group back. Windows
        /// leaves the activation with the proxy then, or hands it to the taskbar since the proxy is minimized.
        /// </summary>
        public void EndCheck()
        {
            nint foreground = GetForegroundWindow();
            if (foreground == Hwnd || foreground == 0 || ClassIs(foreground, "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW"))
            {
                Choose();
            }
        }

        public void Dispose()
        {
            Instances.Remove(Hwnd);
            if (_owner._switching == this)
            {
                _owner._switching = null;
            }

            DestroyWindow(Hwnd);
            if (_icon != 0)
            {
                DestroyIcon(_icon);
            }
        }

        private string Title()
        {
            nint top = Group.RecentOrder.FirstOrDefault(Group.Members.ContainsKey);
            string name = top != 0 ? WindowIcons.GetTitle(top) : string.Empty;
            return Strings.Current.Format(Strings.Current.Group_TaskbarTitle, name);
        }
    }
}
