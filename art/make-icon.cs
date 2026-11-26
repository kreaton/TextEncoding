#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false

// Draws the TextEncoding logo: a light text file with a German sharp s (ß) on it.
// Run from the repo root:  dotnet run art/make-icon.cs
// Writes art/icon.svg, art/icon.png (512 px) and art/icon.ico (16–256 px, PNG-compressed).
// The ß outline comes from Cascadia Code (SIL Open Font License 1.1, ships with Windows); everything else
// is plain geometry below. Small icon sizes drop the text lines and enlarge the ß so it stays readable.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;

const string FontName = "Cascadia Code";

var outDir = AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Path.Combine(Environment.CurrentDirectory, "art");

// ---- palette -------------------------------------------------------------------------------
var pageTop = Color.FromArgb(0xFF, 0xFF, 0xFF);
var pageBottom = Color.FromArgb(0xE9, 0xEF, 0xF8);
var border = Color.FromArgb(0x8E, 0xA2, 0xBF);
var fold = Color.FromArgb(0xD2, 0xDC, 0xEA);
var line = Color.FromArgb(0xC3, 0xCF, 0xE0);
var glyphTop = Color.FromArgb(0x2A, 0x6B, 0xD6);
var glyphBottom = Color.FromArgb(0x16, 0x3F, 0x8C);

// ---- geometry (256 × 256 design grid) ------------------------------------------------------
const float Left = 40, Right = 216, Top = 16, Bottom = 240, Radius = 18, FoldX = 160, FoldY = 72, FoldRadius = 14;
const float BorderWidth = 6, LineWidth = 10;
(float X1, float X2, float Y)[] textLines = [(64, 140, 46), (64, 126, 70)];

// Large sizes: ß below the text lines. Small sizes: no lines, bigger ß.
var largeGlyph = Glyph(boxTop: 92, boxBottom: 222);
var smallGlyph = Glyph(boxTop: 70, boxBottom: 226);

// ---- SVG -------------------------------------------------------------------------------------
var svg = new StringBuilder();
svg.AppendLine("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">""");
svg.AppendLine("  <title>TextEncoding</title>");
svg.AppendLine("  <defs>");
svg.AppendLine($"""    <linearGradient id="page" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{Hex(pageTop)}"/><stop offset="1" stop-color="{Hex(pageBottom)}"/></linearGradient>""");
svg.AppendLine($"""    <linearGradient id="glyph" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{Hex(glyphTop)}"/><stop offset="1" stop-color="{Hex(glyphBottom)}"/></linearGradient>""");
svg.AppendLine("  </defs>");
svg.AppendLine($"""  <path d="M{Left + Radius},{Top} H{FoldX} L{Right},{FoldY} V{Bottom - Radius} A{Radius},{Radius} 0 0 1 {Right - Radius},{Bottom} H{Left + Radius} A{Radius},{Radius} 0 0 1 {Left},{Bottom - Radius} V{Top + Radius} A{Radius},{Radius} 0 0 1 {Left + Radius},{Top} Z" fill="url(#page)" stroke="{Hex(border)}" stroke-width="{BorderWidth}" stroke-linejoin="round"/>""");
svg.AppendLine($"""  <path d="M{FoldX},{Top} V{FoldY - FoldRadius} A{FoldRadius},{FoldRadius} 0 0 0 {FoldX + FoldRadius},{FoldY} H{Right} Z" fill="{Hex(fold)}" stroke="{Hex(border)}" stroke-width="{BorderWidth}" stroke-linejoin="round"/>""");
foreach (var (x1, x2, y) in textLines)
    svg.AppendLine($"""  <line x1="{x1}" y1="{y}" x2="{x2}" y2="{y}" stroke="{Hex(line)}" stroke-width="{LineWidth}" stroke-linecap="round"/>""");
svg.AppendLine($"""  <path d="{SvgPath(largeGlyph)}" fill="url(#glyph)"/>""");
svg.AppendLine("</svg>");
File.WriteAllText(Path.Combine(outDir, "icon.svg"), svg.ToString(), new UTF8Encoding(false));

// ---- PNG + ICO ---------------------------------------------------------------------------------
using (var big = Render(512)) big.Save(Path.Combine(outDir, "icon.png"), ImageFormat.Png);

int[] icoSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
var frames = icoSizes.Select(s =>
{
    using var bmp = Render(s);
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return (Size: s, Png: ms.ToArray());
}).ToList();
WriteIco(Path.Combine(outDir, "icon.ico"), frames);

Console.WriteLine($"Wrote icon.svg, icon.png and icon.ico ({string.Join(", ", icoSizes)} px) to {outDir}");

// =================================================================================================

GraphicsPath Glyph(float boxTop, float boxBottom)
{
    using var family = new FontFamily(FontName);
    // Variable fonts have overlapping contours: fill with the non-zero rule (also the SVG default), not even-odd.
    var path = new GraphicsPath(FillMode.Winding);
    path.AddString("ß", family, (int)FontStyle.Bold, 100f, PointF.Empty, StringFormat.GenericTypographic);

    var b = path.GetBounds();
    var scale = (boxBottom - boxTop) / b.Height;
    var width = b.Width * scale;
    using var m = new Matrix();
    m.Translate(-b.X, -b.Y);
    m.Scale(scale, scale, MatrixOrder.Append);
    m.Translate(128 - width / 2 + 4, boxTop, MatrixOrder.Append); // +4: optical centre, the stem side is heavier
    path.Transform(m);
    return path;
}

GraphicsPath PagePath()
{
    var p = new GraphicsPath();
    const float d = Radius * 2;
    p.AddLine(Left + Radius, Top, FoldX, Top);
    p.AddLine(FoldX, Top, Right, FoldY);
    p.AddLine(Right, FoldY, Right, Bottom - Radius);
    p.AddArc(Right - d, Bottom - d, d, d, 0, 90);
    p.AddLine(Right - Radius, Bottom, Left + Radius, Bottom);
    p.AddArc(Left, Bottom - d, d, d, 90, 90);
    p.AddLine(Left, Bottom - Radius, Left, Top + Radius);
    p.AddArc(Left, Top, d, d, 180, 90);
    p.CloseFigure();
    return p;
}

GraphicsPath FoldPath()
{
    var p = new GraphicsPath();
    const float d = FoldRadius * 2;
    p.AddLine(FoldX, Top, FoldX, FoldY - FoldRadius);
    p.AddArc(FoldX, FoldY - d, d, d, 180, -90);
    p.AddLine(FoldX + FoldRadius, FoldY, Right, FoldY);
    p.CloseFigure();
    return p;
}

Bitmap Render(int size)
{
    var small = size <= 24;
    var unit = 256f / size;                       // design units per device pixel
    var strokeWidth = Math.Max(BorderWidth, unit); // never thinner than one pixel

    var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.Clear(Color.Transparent);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.CompositingQuality = CompositingQuality.HighQuality;
    g.ScaleTransform(size / 256f, size / 256f);

    using var page = PagePath();
    using var pageBrush = new LinearGradientBrush(new PointF(0, Top - 1), new PointF(0, Bottom + 1), pageTop, pageBottom);
    using var borderPen = new Pen(border, strokeWidth) { LineJoin = LineJoin.Round };
    g.FillPath(pageBrush, page);
    g.DrawPath(borderPen, page);

    using var foldPath = FoldPath();
    using var foldBrush = new SolidBrush(fold);
    g.FillPath(foldBrush, foldPath);
    g.DrawPath(borderPen, foldPath);

    if (!small)
    {
        using var linePen = new Pen(line, Math.Max(LineWidth, unit)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        foreach (var (x1, x2, y) in textLines) g.DrawLine(linePen, x1, y, x2, y);
    }

    var glyph = small ? smallGlyph : largeGlyph;
    var gb = glyph.GetBounds();
    using var glyphBrush = new LinearGradientBrush(new PointF(0, gb.Top - 1), new PointF(0, gb.Bottom + 1), glyphTop, glyphBottom);
    g.FillPath(glyphBrush, glyph);
    return bmp;
}

static string SvgPath(GraphicsPath path)
{
    var pts = path.PathPoints;
    var types = path.PathTypes;
    var sb = new StringBuilder();
    for (var i = 0; i < pts.Length; i++)
    {
        var kind = types[i] & 0x07;
        if (kind == 0) sb.Append('M').Append(P(pts[i]));
        else if (kind == 1) sb.Append('L').Append(P(pts[i]));
        else if (kind == 3)
        {
            sb.Append('C').Append(P(pts[i])).Append(' ').Append(P(pts[i + 1])).Append(' ').Append(P(pts[i + 2]));
            i += 2;
        }
        if ((types[i] & 0x80) != 0) sb.Append('Z');
    }
    return sb.ToString();

    static string P(PointF p) => string.Create(CultureInfo.InvariantCulture, $"{p.X:0.##},{p.Y:0.##}");
}

static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

static void WriteIco(string path, List<(int Size, byte[] Png)> frames)
{
    using var w = new BinaryWriter(File.Create(path));
    w.Write((ushort)0);            // reserved
    w.Write((ushort)1);            // type: icon
    w.Write((ushort)frames.Count);
    var offset = 6 + 16 * frames.Count;
    foreach (var (size, png) in frames)
    {
        w.Write((byte)(size >= 256 ? 0 : size)); // 0 means 256
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)0);          // palette colours
        w.Write((byte)0);          // reserved
        w.Write((ushort)1);        // colour planes
        w.Write((ushort)32);       // bits per pixel
        w.Write(png.Length);
        w.Write(offset);
        offset += png.Length;
    }
    foreach (var (_, png) in frames) w.Write(png);
}
