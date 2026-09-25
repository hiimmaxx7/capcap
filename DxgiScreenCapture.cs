using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;

namespace Capcap;

/// <summary>
/// GPU-accelerated screen capture via DXGI Desktop Duplication. GDI's
/// CopyFromScreen has to go through the desktop compositor's BitBlt path and
/// its cost scales with the captured area, which isn't fast enough to sustain
/// 60fps at full 1080p+ on some hardware. DXGI reads straight from the
/// compositor's own backbuffer, so full-screen capture stays cheap regardless
/// of target fps.
/// </summary>
internal sealed class DxgiScreenCapture : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGIOutputDuplication _duplication;

    private ID3D11Texture2D? _stagingTexture;
    private Size _stagingSize;

    private readonly Rectangle _monitorBounds;

    public DxgiScreenCapture(Rectangle monitorBoundsVirtualScreen)
    {
        _monitorBounds = monitorBoundsVirtualScreen;
        DXGI.CreateDXGIFactory1(out IDXGIFactory1 factory).CheckError();

        // Vortice caches the objects behind factory.Adapters1 / adapter.Outputs and raw-Release()s
        // them itself when the factory is disposed — they're owned by the factory, NOT by us.
        // The old code also called Dispose() on the found adapter, releasing it a second time;
        // D3D11 then touched the already-freed adapter when the device was torn down, which is
        // the AccessViolationException in d3d11.dll that occasionally crashed the app on Ctrl+End.
        // So: take our own reference via QueryInterface, and keep the factory alive until the
        // device and duplication have been created from it.
        IDXGIAdapter1? adapter = null;
        IDXGIOutput1? output = null;
        try
        {
            foreach (var a in factory.Adapters1)
            {
                foreach (var o in a.Outputs)
                {
                    var d = o.Description.DesktopCoordinates;
                    var bounds = new Rectangle(d.Left, d.Top, d.Right - d.Left, d.Bottom - d.Top);
                    if (bounds == monitorBoundsVirtualScreen)
                    {
                        output = o.QueryInterface<IDXGIOutput1>();
                        adapter = a.QueryInterface<IDXGIAdapter1>();
                        break;
                    }
                }
                if (output is not null) break;
            }

            if (adapter is null || output is null)
            {
                throw new InvalidOperationException("Không tìm thấy màn hình tương ứng qua DXGI.");
            }

            D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                null, out _device!, out _context!).CheckError();

            _duplication = output.DuplicateOutput(_device);
        }
        catch
        {
            _context?.Dispose();
            _device?.Dispose();
            throw;
        }
        finally
        {
            output?.Dispose();
            adapter?.Dispose();
            factory.Dispose();
        }
    }

    private void EnsureStaging(Size size)
    {
        if (_stagingTexture is not null && _stagingSize == size) return;

        _stagingTexture?.Dispose();
        _stagingSize = size;
        _stagingTexture = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = size.Width,
            Height = size.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = Vortice.Direct3D11.Usage.Staging,
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.None
        });
    }

    /// <summary>Fills <paramref name="destBuffer"/> with tightly-packed BGRA32 pixels for
    /// <paramref name="region"/> (virtual-screen coordinates). Reuses the last captured
    /// frame when the desktop hasn't changed since the previous call, so the caller always
    /// gets a full frame instead of having to special-case "no new data yet".</summary>
    public void CaptureRegion(Rectangle region, byte[] destBuffer)
    {
        EnsureStaging(region.Size);

        // The duplicated texture is in monitor-local coordinates, not virtual-screen ones
        // (they only coincide on a primary monitor at 0,0). An out-of-bounds box is undefined
        // behaviour for CopySubresourceRegion, so never hand one to the driver.
        var local = new Rectangle(region.X - _monitorBounds.X, region.Y - _monitorBounds.Y, region.Width, region.Height);
        bool inBounds = local.X >= 0 && local.Y >= 0 &&
                        local.Right <= _monitorBounds.Width && local.Bottom <= _monitorBounds.Height;

        bool acquired = false;
        try
        {
            _duplication.AcquireNextFrame(0, out _, out var resource);
            acquired = true;
            using (resource)
            {
                if (inBounds)
                {
                    using var texture = resource.QueryInterface<ID3D11Texture2D>();
                    var box = new Box(local.Left, local.Top, 0, local.Right, local.Bottom, 1);
                    _context.CopySubresourceRegion(_stagingTexture, 0, 0, 0, 0, texture, 0, box);
                }
            }
        }
        catch
        {
            // Timed out (desktop hasn't changed) or a transient duplication hiccup — just
            // re-read whatever is already sitting in the staging texture from last time.
        }
        finally
        {
            if (acquired)
            {
                try { _duplication.ReleaseFrame(); } catch { }
            }
        }

        MappedSubresource mapped = _context.Map(_stagingTexture!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int rowBytes = region.Width * 4;
            for (int y = 0; y < region.Height; y++)
            {
                IntPtr src = mapped.DataPointer + y * mapped.RowPitch;
                Marshal.Copy(src, destBuffer, y * rowBytes, rowBytes);
            }
        }
        finally
        {
            _context.Unmap(_stagingTexture!, 0);
        }
    }

    public void Dispose()
    {
        _stagingTexture?.Dispose();
        _stagingTexture = null;
        _duplication.Dispose();
        _context.ClearState();
        _context.Flush();
        _context.Dispose();
        _device.Dispose();
    }
}
