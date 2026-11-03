using System.Runtime.InteropServices;
using EnhancedSnap.Core.Diagnostics;
using EnhancedSnap.Core.Interop;
using EnhancedSnap.Core.Localization;
using EnhancedSnap.Engine.Native;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;
using Windows.UI.Composition;
using WinRT;
using static EnhancedSnap.Engine.Native.NativeMethods;

namespace EnhancedSnap.Engine.Ui;

internal enum TextAlign
{
    Leading = 0,
    Trailing = 1,
    Center = 2,
}

/// <summary>
/// Text and icons for composition visuals: Direct2D/DirectWrite render into CompositionDrawingSurfaces
/// (ICompositionDrawingSurfaceInterop). Raw COM vtables keep the engine dependency-free and NativeAOT clean.
/// Surfaces are drawn once when a flyout opens; nothing redraws while it is idle.
/// The devices are created on first use and given back with <see cref="Release"/> once popups go unused: a hardware
/// D3D device and the graphics driver behind it cost tens of MB of private memory, which an idle engine has no reason
/// to keep (measured: 6.6 MB before the first popup, 54 MB with the devices, 21 MB after release).
/// </summary>
internal sealed unsafe class DrawingSurfaces : IDisposable
{
    private const int DXGI_ERROR_DEVICE_REMOVED = unchecked((int)0x887A0005);
    private const int DXGI_ERROR_DEVICE_RESET = unchecked((int)0x887A0007);
    private const int D2DERR_RECREATE_TARGET = unchecked((int)0x8899000C);

    private static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IID_ICompositorInterop = new("25297D5C-3AD4-4C9C-B5CF-E36A38512330");
    private static readonly Guid IID_ICompositionDrawingSurfaceInterop = new("FD04E6E3-FE0C-4C3C-AB19-A07601A576EE");
    private static readonly Guid IID_ID2D1DeviceContext = new("e8f7fe7a-191c-466d-ad95-975678bda998");
    private static readonly Guid IID_IDWriteFactory = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");

    // ID2D1RenderTarget vtable slots (d2d1.h order; slot 3 is ID2D1Resource::GetFactory).
    private const int SlotCreateBitmap = 4;
    private const int SlotCreateSolidColorBrush = 8;
    private const int SlotDrawBitmap = 26;
    private const int SlotDrawText = 27;
    private const int SlotSetTransform = 30;
    private const int SlotSetTextAntialiasMode = 34;
    private const int SlotClear = 47;

    private readonly Compositor _compositor;
    private readonly Dictionary<(int Size, int Weight, int Align), nint> _formats = [];
    private nint _d3dDevice;
    private nint _d2dDevice;
    private nint _dwriteFactory;
    private CompositionGraphicsDevice? _graphicsDevice;
    private bool _initialized;

    public DrawingSurfaces(Compositor compositor) => _compositor = compositor;

    /// <summary>True while the GPU devices exist (between first use and <see cref="Release"/>).</summary>
    public bool HasDevices => _d3dDevice != 0;

    /// <summary>Creates the devices on first use. A failure is retried after the next <see cref="Release"/>.</summary>
    public bool IsAvailable
    {
        get
        {
            if (!_initialized)
            {
                _initialized = true;
                try
                {
                    Initialize();
                }
                catch (Exception ex)
                {
                    Log.Error("Text rendering unavailable (flyouts fall back to icons and thumbnails)", ex);
                    ReleaseDevices();
                }
            }

            return _graphicsDevice is not null && _dwriteFactory != 0;
        }
    }

    /// <summary>Renders a single line of text (trimmed with an ellipsis) into a new surface of the given pixel size.</summary>
    public CompositionDrawingSurface? Text(string text, int width, int height, float fontPx, bool semibold, Color color, TextAlign align = TextAlign.Leading)
    {
        if (!IsAvailable || width <= 0 || height <= 0 || string.IsNullOrEmpty(text))
        {
            return null;
        }

        try
        {
            nint format = GetFormat((int)Math.Round(fontPx * 4), semibold ? 600 : 400, align);
            var surface = _graphicsDevice!.CreateDrawingSurface(new Size(width, height), DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);
            Draw(surface, ctx =>
            {
                ColorF clear = default;
                ((delegate* unmanaged<nint, ColorF*, void>)Slot(ctx, SlotClear))(ctx, &clear);
                ((delegate* unmanaged<nint, int, void>)Slot(ctx, SlotSetTextAntialiasMode))(ctx, 2 /* GRAYSCALE: correct on transparent surfaces */);
                ColorF fill = ColorF.From(color);
                nint brush;
                ThrowIfFailed(((delegate* unmanaged<nint, ColorF*, nint, nint*, int>)Slot(ctx, SlotCreateSolidColorBrush))(ctx, &fill, 0, &brush));
                try
                {
                    RectF layout = new(0, 0, width, height);
                    fixed (char* p = text)
                    {
                        // D2D1_DRAW_TEXT_OPTIONS_CLIP (2) | ENABLE_COLOR_FONT (4): emoji in window titles render in color.
                        ((delegate* unmanaged<nint, char*, uint, nint, RectF*, nint, int, int, void>)Slot(ctx, SlotDrawText))(ctx, p, (uint)text.Length, format, &layout, brush, 6, 0);
                    }
                }
                finally
                {
                    Marshal.Release(brush);
                }
            });
            return surface;
        }
        catch (Exception ex)
        {
            Log.Warn($"Text surface failed: {ex.Message}");
            ReleaseIfDeviceLost(ex);
            return null;
        }
    }

    /// <summary>Renders an HICON (premultiplied BGRA) at <paramref name="size"/> pixels.</summary>
    public CompositionDrawingSurface? Icon(nint hicon, int size)
    {
        if (!IsAvailable || hicon == 0 || size <= 0)
        {
            return null;
        }

        byte[]? pixels = WindowIcons.ToPremultipliedBgra(hicon, size);
        if (pixels is null)
        {
            return null;
        }

        try
        {
            var surface = _graphicsDevice!.CreateDrawingSurface(new Size(size, size), DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);
            Draw(surface, ctx =>
            {
                ColorF clear = default;
                ((delegate* unmanaged<nint, ColorF*, void>)Slot(ctx, SlotClear))(ctx, &clear);
                var properties = new BitmapProperties { Format = 87 /* DXGI_FORMAT_B8G8R8A8_UNORM */, AlphaMode = 1 /* PREMULTIPLIED */, DpiX = 96, DpiY = 96 };
                nint bitmap;
                fixed (byte* p = pixels)
                {
                    ThrowIfFailed(((delegate* unmanaged<nint, SizeU, void*, uint, BitmapProperties*, nint*, int>)Slot(ctx, SlotCreateBitmap))(
                        ctx, new SizeU((uint)size, (uint)size), p, (uint)(size * 4), &properties, &bitmap));
                }

                try
                {
                    RectF destination = new(0, 0, size, size);
                    ((delegate* unmanaged<nint, nint, RectF*, float, int, RectF*, void>)Slot(ctx, SlotDrawBitmap))(ctx, bitmap, &destination, 1f, 1 /* LINEAR */, null);
                }
                finally
                {
                    Marshal.Release(bitmap);
                }
            });
            return surface;
        }
        catch (Exception ex)
        {
            Log.Warn($"Icon surface failed: {ex.Message}");
            ReleaseIfDeviceLost(ex);
            return null;
        }
    }

    /// <summary>
    /// Frees the devices; the next surface creates them again. Only called while no popup shows text (hidden popups
    /// close their surfaces), otherwise visible surfaces would keep the old device alive.
    /// </summary>
    public void Release()
    {
        ReleaseDevices();
        _initialized = false;
    }

    public void Dispose() => Release();

    private void ReleaseDevices()
    {
        foreach (nint format in _formats.Values)
        {
            Marshal.Release(format);
        }

        _formats.Clear();
        ReleaseAndClear(ref _dwriteFactory);
        _graphicsDevice?.Dispose();
        _graphicsDevice = null;
        ReleaseAndClear(ref _d2dDevice);
        ReleaseAndClear(ref _d3dDevice);
    }

    /// <summary>A driver update or GPU reset removes the device for good: start over on the next surface.</summary>
    private void ReleaseIfDeviceLost(Exception ex)
    {
        if (ex.HResult is DXGI_ERROR_DEVICE_REMOVED or DXGI_ERROR_DEVICE_RESET or D2DERR_RECREATE_TARGET)
        {
            Log.Warn("Graphics device lost; it is recreated for the next popup");
            Release();
        }
    }

    private void Initialize()
    {
        const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
        const uint D3D11_SDK_VERSION = 7;
        int hr = D3D11CreateDevice(0, 5 /* WARP */, 0, D3D11_CREATE_DEVICE_BGRA_SUPPORT, null, 0, D3D11_SDK_VERSION, out _d3dDevice, out _, out nint context);
        if (hr < 0)
        {
            hr = D3D11CreateDevice(0, 1 /* HARDWARE */, 0, D3D11_CREATE_DEVICE_BGRA_SUPPORT, null, 0, D3D11_SDK_VERSION, out _d3dDevice, out _, out context);
        }

        ThrowIfFailed(hr);
        if (context != 0)
        {
            Marshal.Release(context);
        }

        ThrowIfFailed(Marshal.QueryInterface(_d3dDevice, in IID_IDXGIDevice, out nint dxgiDevice));
        try
        {
            ThrowIfFailed(D2D1CreateDevice(dxgiDevice, 0, out _d2dDevice));
        }
        finally
        {
            Marshal.Release(dxgiDevice);
        }

        nint compositor = MarshalInspectable<Compositor>.FromManaged(_compositor);
        try
        {
            ThrowIfFailed(Marshal.QueryInterface(compositor, in IID_ICompositorInterop, out nint interop));
            try
            {
                // ICompositorInterop::CreateGraphicsDevice(IUnknown* renderingDevice, ICompositionGraphicsDevice** result)
                nint device;
                ThrowIfFailed(((delegate* unmanaged<nint, nint, nint*, int>)Slot(interop, 5))(interop, _d2dDevice, &device));
                try
                {
                    _graphicsDevice = CompositionGraphicsDevice.FromAbi(device);
                }
                finally
                {
                    Marshal.Release(device);
                }
            }
            finally
            {
                Marshal.Release(interop);
            }
        }
        finally
        {
            Marshal.Release(compositor);
        }

        ThrowIfFailed(DWriteCreateFactory(0 /* SHARED */, in IID_IDWriteFactory, out _dwriteFactory));
    }

    private nint GetFormat(int quarterPx, int weight, TextAlign align)
    {
        var key = (quarterPx, weight, (int)align);
        if (_formats.TryGetValue(key, out nint existing))
        {
            return existing;
        }

        nint format;
        string locale = Strings.Current.Culture.Name;
        fixed (char* family = "Segoe UI Variable Text")
        fixed (char* localeName = locale)
        {
            // IDWriteFactory::CreateTextFormat (slot 15)
            ThrowIfFailed(((delegate* unmanaged<nint, char*, nint, int, int, int, float, char*, nint*, int>)Slot(_dwriteFactory, 15))(
                _dwriteFactory, family, 0, weight, 0 /* NORMAL style */, 5 /* NORMAL stretch */, quarterPx / 4f, localeName, &format));
        }

        // SetTextAlignment (3), SetParagraphAlignment (4: CENTER = 2), SetWordWrapping (5: NO_WRAP = 1)
        ((delegate* unmanaged<nint, int, int>)Slot(format, 3))(format, (int)align);
        ((delegate* unmanaged<nint, int, int>)Slot(format, 4))(format, 2);
        ((delegate* unmanaged<nint, int, int>)Slot(format, 5))(format, 1);

        // Ellipsis trimming: IDWriteFactory::CreateEllipsisTrimmingSign (slot 20), IDWriteTextFormat::SetTrimming (slot 9)
        nint sign;
        if (((delegate* unmanaged<nint, nint, nint*, int>)Slot(_dwriteFactory, 20))(_dwriteFactory, format, &sign) >= 0)
        {
            var trimming = new Trimming { Granularity = 1 /* CHARACTER */ };
            ((delegate* unmanaged<nint, Trimming*, nint, int>)Slot(format, 9))(format, &trimming, sign);
            Marshal.Release(sign);
        }

        _formats[key] = format;
        return format;
    }

    private static void Draw(CompositionDrawingSurface surface, Action<nint> draw)
    {
        nint unknown = MarshalInspectable<CompositionDrawingSurface>.FromManaged(surface);
        try
        {
            ThrowIfFailed(Marshal.QueryInterface(unknown, in IID_ICompositionDrawingSurfaceInterop, out nint interop));
            try
            {
                Guid iid = IID_ID2D1DeviceContext;
                nint context;
                POINT offset;
                // ICompositionDrawingSurfaceInterop::BeginDraw(const RECT*, REFIID, void**, POINT*) (slot 3)
                ThrowIfFailed(((delegate* unmanaged<nint, RECT*, Guid*, nint*, POINT*, int>)Slot(interop, 3))(interop, null, &iid, &context, &offset));
                try
                {
                    var transform = new Matrix3x2F { M11 = 1, M22 = 1, Dx = offset.X, Dy = offset.Y };
                    ((delegate* unmanaged<nint, Matrix3x2F*, void>)Slot(context, SlotSetTransform))(context, &transform);
                    draw(context);
                }
                finally
                {
                    Marshal.Release(context);
                    ((delegate* unmanaged<nint, int>)Slot(interop, 4))(interop); // EndDraw
                }
            }
            finally
            {
                Marshal.Release(interop);
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    private static nint Slot(nint comObject, int index) => (*(nint**)comObject)[index];

    private static void ThrowIfFailed(int hr)
    {
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }

    private static void ReleaseAndClear(ref nint pointer)
    {
        if (pointer != 0)
        {
            Marshal.Release(pointer);
            pointer = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ColorF
    {
        public float R;
        public float G;
        public float B;
        public float A;

        public static ColorF From(Color c) => new() { R = c.R / 255f, G = c.G / 255f, B = c.B / 255f, A = c.A / 255f };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectF(float left, float top, float right, float bottom)
    {
        public float Left = left;
        public float Top = top;
        public float Right = right;
        public float Bottom = bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SizeU(uint width, uint height)
    {
        public uint Width = width;
        public uint Height = height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Matrix3x2F
    {
        public float M11;
        public float M12;
        public float M21;
        public float M22;
        public float Dx;
        public float Dy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapProperties
    {
        public int Format;
        public int AlphaMode;
        public float DpiX;
        public float DpiY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Trimming
    {
        public int Granularity;
        public uint Delimiter;
        public uint DelimiterCount;
    }
}
