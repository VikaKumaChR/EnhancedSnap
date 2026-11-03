using System.Runtime.InteropServices;
using EnhancedSnap.Engine.Native;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace EnhancedSnap.Engine.Overlay;

/// <summary>
/// Owns the thread's DispatcherQueue and the system <see cref="Compositor"/> (the same Visual Layer that
/// WinUI renders with). Visuals are composed by DWM on the GPU and animations run inside the compositor,
/// so a static or animating overlay costs no CPU in this process.
/// </summary>
internal sealed unsafe class CompositionHost
{
    private const int DQTYPE_THREAD_CURRENT = 2;
    private const int DQTAT_COM_NONE = 0;
    private const int DQTAT_COM_STA = 2;

    // windows.ui.composition.interop.h
    private static readonly Guid IID_ICompositorDesktopInterop = new("29E691FA-4567-4DCA-B319-D0F207EB6807");

    private readonly nint _dispatcherQueueController; // Kept for the lifetime of the process.

    public CompositionHost()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is null)
        {
            var options = new DispatcherQueueOptions
            {
                dwSize = sizeof(DispatcherQueueOptions),
                threadType = DQTYPE_THREAD_CURRENT,
                apartmentType = DQTAT_COM_STA,
            };
            int hr = NativeMethods.CreateDispatcherQueueController(options, out _dispatcherQueueController);
            if (hr < 0)
            {
                options.apartmentType = DQTAT_COM_NONE;
                hr = NativeMethods.CreateDispatcherQueueController(options, out _dispatcherQueueController);
            }

            Marshal.ThrowExceptionForHR(hr);
        }

        Compositor = new Compositor();
    }

    public Compositor Compositor { get; }

    /// <summary>ICompositorDesktopInterop::CreateDesktopWindowTarget — hosts a visual tree in a Win32 window.</summary>
    public DesktopWindowTarget CreateDesktopWindowTarget(nint hwnd, bool isTopmost)
    {
        nint unknown = MarshalInspectable<Compositor>.FromManaged(Compositor);
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in IID_ICompositorDesktopInterop, out nint interop));
            try
            {
                var vtable = *(nint**)interop;
                var createDesktopWindowTarget = (delegate* unmanaged<nint, nint, int, nint*, int>)vtable[3];
                nint target;
                Marshal.ThrowExceptionForHR(createDesktopWindowTarget(interop, hwnd, isTopmost ? 1 : 0, &target));
                try
                {
                    return DesktopWindowTarget.FromAbi(target);
                }
                finally
                {
                    Marshal.Release(target);
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
}
