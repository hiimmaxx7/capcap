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

    public DxgiScreenCapture(Rectangle monitorBoundsVirtualScreen)
    {
        DXGI.CreateDXGIFactory1(out IDXGIFactory1 factory).CheckError();

        IDXGIAdapter1? foundAdapter = null;
        IDXGIOutput1? foundOutput = null;

        foreach (var adapter in factory.Adapters1)
        {
            foreach (var output in adapter.Outputs)
            {
                var d = output.Description.DesktopCoordinates;
                var bounds = new Rectangle(d.Left, d.Top, d.Right - d.Left, d.Bottom - d.Top);
                if (foundOutput is null && bounds == monitorBoundsVirtualScreen)
                {
                    foundOutput = output.QueryInterface<IDXGIOutput1>();
                    foundAdapter = adapter;
                }
            }
        }
        factory.Dispose();

        if (foundAdapter is null || foundOutput is null)
        {
            throw new InvalidOperationException("Không tìm thấy màn hình tương ứng qua DXGI.");
        }

        D3D11.D3D11CreateDevice(foundAdapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
            null, out _device!, out _context!);

        _duplication = foundOutput.DuplicateOutput(_device);

        foundOutput.Dispose();
        foundAdapter.Dispose();
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

        try
        {
            _duplication.AcquireNextFrame(0, out _, out var resource);
            using (resource)
            {
                using var texture = resource.QueryInterface<ID3D11Texture2D>();
                var box = new Box(region.Left, region.Top, 0, region.Right, region.Bottom, 1);
                _context.CopySubresourceRegion(_stagingTexture, 0, 0, 0, 0, texture, 0, box);
            }
            _duplication.ReleaseFrame();
        }
        catch
        {
            // Timed out (desktop hasn't changed) or a transient duplication hiccup — just
            // re-read whatever is already sitting in the staging texture from last time.
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
        _duplication.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}
