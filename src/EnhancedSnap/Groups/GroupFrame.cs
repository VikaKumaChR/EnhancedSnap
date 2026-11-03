using System.Numerics;
using System.Runtime.InteropServices;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Geometry;
using EnhancedSnap.Engine.Overlay;
using EnhancedSnap.Engine.Ui;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using EnhancedSnap.Engine.Native;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Groups;

/// <summary>
/// The base of a snap group: one rounded surface in the chosen Fluent material directly behind the group's windows,
/// so the gaps read as one frame (concentric with the windows' 8 px corners, like Edge's frame around its content).
/// It is purely visual — click-through and never activated — so clicking the gaps or margins reaches whatever is
/// underneath, exactly as with Windows Snap. Resizing lives in <see cref="DividerLayer"/>.
/// </summary>
internal sealed unsafe class GroupFrame : IDisposable
{
    private const string ClassName = "EnhancedSnap.GroupFrame";
    private static bool _classRegistered;

    private readonly Compositor _c;
    private readonly GroupMaterials _materials;
    private readonly DesktopWindowTarget _target;
    private readonly ContainerVisual _root;
    private RectI _bounds;
    private float _radius;
    private FluentTheme? _theme;

    public GroupFrame(CompositionHost host, GroupMaterials materials)
    {
        EnsureClass();
        _c = host.Compositor;
        _materials = materials;
        Hwnd = CreateWindowEx(
            WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP | WS_EX_LAYERED | WS_EX_TRANSPARENT,
            ClassName,
            "Enhanced Snap group",
            WS_POPUP,
            0,
            0,
            1,
            1,
            0,
            0,
            GetModuleHandle(null),
            0);
        if (Hwnd == 0)
        {
            throw new InvalidOperationException("CreateWindowEx failed for a group frame");
        }

        SetLayeredWindowAttributes(Hwnd, 0, 255, LWA_ALPHA); // layered + transparent = never hit-tested
        SetDwmInt(Hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_DONOTROUND); // the concentric radius is drawn below
        SetDwmInt(Hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, 1);
        // Not excluded from peek: while a taskbar thumbnail is peeked, the base hides along with its windows instead
        // of staying behind as an empty pale panel.
        SetDwmInt(Hwnd, DWMWA_USE_HOSTBACKDROPBRUSH, 1);
        _target = host.CreateDesktopWindowTarget(Hwnd, isTopmost: false);
        _root = _c.CreateContainerVisual();
        _target.Root = _root;
    }

    public nint Hwnd { get; }

    public bool IsShown { get; private set; }

    /// <summary>Re-render for a new rectangle or theme (a handful of visuals).</summary>
    public void Update(RectI bounds, float radius, FluentTheme theme, float scale)
    {
        if (bounds != _bounds)
        {
            SetWindowPos(Hwnd, 0, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOACTIVATE | SWP_NOZORDER);
        }

        if (bounds == _bounds && radius == _radius && ReferenceEquals(theme, _theme))
        {
            return;
        }

        _bounds = bounds;
        _radius = radius;
        _theme = theme;
        _root.Children.RemoveAll();
        var size = new Vector2(bounds.Width, bounds.Height);
        _root.Size = size;
        _root.Children.InsertAtTop(_materials.Fill(theme.Frame, Vector2.Zero, size, radius));
        var (stroke, _, _) = Shapes.RoundedRect(_c, Vector2.Zero, size, radius, null, theme.FrameStroke, MathF.Max(1, MathF.Round(scale)));
        _root.Children.InsertAtTop(stroke);
    }

    /// <summary>Show directly behind <paramref name="bottomMember"/> (the group's bottom-most window).</summary>
    public void Show(nint bottomMember)
    {
        SetWindowPos(Hwnd, bottomMember, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        IsShown = true;
    }

    public void Hide()
    {
        if (IsShown)
        {
            ShowWindow(Hwnd, SW_HIDE);
            IsShown = false;
        }
    }

    public void Dispose()
    {
        try
        {
            _target.Root = null;
            _target.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn($"Group frame dispose: {ex.Message}");
        }

        DestroyWindow(Hwnd);
    }

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
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam) => msg switch
    {
        WM_MOUSEACTIVATE => MA_NOACTIVATE,
        WM_NCHITTEST => HTTRANSPARENT,
        WM_ERASEBKGND => 1,
        WM_DPICHANGED => 0,
        _ => DefWindowProc(hwnd, msg, wParam, lParam),
    };
}
