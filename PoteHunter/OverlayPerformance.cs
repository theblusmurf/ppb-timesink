using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace PoteHunter;

// Owned by one viewport on its UI thread. Buffers survive unchanged-size captures.
internal sealed class OverlayReadbackBuffer : IDisposable
{
    internal byte[] Pixels { get; private set; } = [];
    byte[] row = [];
    internal Bitmap? Image { get; private set; }
    internal int Allocations { get; private set; }
    internal void Ensure(Size size)
    {
        if(size.Width is <1 or >4096 || size.Height is <1 or >4096)
            throw new ArgumentOutOfRangeException(nameof(size), "Map capture dimensions are outside the supported bound.");
        if(Image?.Size == size)return;
        Dispose();
        Pixels = new byte[checked(size.Width * size.Height * 4)];
        row = new byte[checked(size.Width * 4)];
        Image = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        Allocations++;
    }
    internal void CopyBottomUpRgba()
    {
        var image = Image ?? throw new InvalidOperationException("Readback buffer was not sized.");
        int width = image.Width, height = image.Height;
        var bits = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for(int y = 0; y < height; y++)
            {
                for(int x = 0; x < width; x++)
                {
                    int a = ((height - y - 1) * width + x) * 4, b = x * 4;
                    row[b] = Pixels[a + 2]; row[b + 1] = Pixels[a + 1]; row[b + 2] = Pixels[a]; row[b + 3] = 255;
                }
                Marshal.Copy(row, 0, bits.Scan0 + y * bits.Stride, row.Length);
            }
        }
        finally { image.UnlockBits(bits); }
    }
    public void Dispose() { Image?.Dispose(); Image = null; Pixels = []; row = []; }
}

// The display can update immediately for loot/state/style changes, while elapsed
// time and derived rates need only one presentation per displayed second.
internal sealed class LootPresentationGate
{
    string? previous;
    internal int Presentations { get; private set; }
    internal bool Changed(LootTrackerSnapshot snapshot, int design, int scale, int opacity, bool force = false)
    {
        var key = new StringBuilder(384).Append(design).Append('|').Append(scale).Append('|').Append(opacity)
            .Append('|').Append(snapshot.SessionStartedUtc.Ticks).Append('|').Append(snapshot.RateStartedUtc.Ticks)
            .Append('|').Append((long)Math.Max(0, snapshot.Elapsed.TotalSeconds))
            .Append('|').Append((long)Math.Max(0, snapshot.RateElapsed.TotalSeconds))
            .Append('|').Append(snapshot.Zone).Append('|').Append(snapshot.PendingKills).Append('|').Append(snapshot.Wallet.Known)
            .Append('|').Append(snapshot.Wallet.Current).Append('|').Append(snapshot.Wallet.Baseline).Append('|').Append(snapshot.Wallet.Net)
            .Append('|').Append(snapshot.Wallet.Character.Length).Append(':').Append(snapshot.Wallet.Character)
            .Append('|').Append(snapshot.Wallet.Status.Length).Append(':').Append(snapshot.Wallet.Status);
        foreach(var item in snapshot.TrackedLoot)key.Append('|').Append(item.Name.Length).Append(':').Append(item.Name).Append(':').Append(item.Count);
        foreach(var source in snapshot.Sources)
        {
            key.Append('|').Append(source.Source.Length).Append(':').Append(source.Source).Append(':').Append(source.Kills).Append(':').Append(source.Drops);
            foreach(var item in source.Items)key.Append('|').Append(item.Name.Length).Append(':').Append(item.Name).Append(':').Append(item.Count);
        }
        key.Append('|').Append(snapshot.RecentDrops.Count);
        foreach(var drop in snapshot.RecentDrops.Take(2))key.Append('|').Append(drop.Source.Length).Append(':').Append(drop.Source).Append(':').Append(drop.Name.Length).Append(':').Append(drop.Name).Append(':').Append(drop.SeenUtc.Ticks).Append(':').Append(drop.Position.X).Append(':').Append(drop.Position.Y);
        string next = key.ToString();
        if(!force && next == previous)return false;
        previous = next; Presentations++; return true;
    }
}
