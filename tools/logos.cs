#:package SkiaSharp@3.119.4
#:property PublishAot=false
// Disegna le due icone, rosso su un quadrato arrotondato quasi nero:
//   app    = SteamAll (senza fondo: il contorno è il cerchio, come Steam): il logo di Steam (badge rotondo con il pistone) in cui l'anello in alto e il
//            cerchio piccolo in basso sono diventati QUADRATI: nel grande la griglia 2x2 della
//            libreria, nel piccolo un quadratino (come il punto nell'anello di Steam). Scelto il 20/10.
//   theme  = il tema: un pennello classico (setole con il bordo ondulato, ghiera, manico), senza Steam.
// Le misure del pistone vengono dal logo di Steam (riquadro 24x24 di Simple Icons).
// Uso: dotnet run tools/logos.cs -- <cartella di uscita> [anteprima.png]
//   escono steamall.ico (16-256 px), steamall.png e theme.png (512 px).
using SkiaSharp;

string outDir = args[0];
Directory.CreateDirectory(outDir);
int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
WriteIco(Path.Combine(outDir, "steamall.ico"), sizes, sizes.Select(s => Render(s, theme: false)).ToList());
File.WriteAllBytes(Path.Combine(outDir, "steamall.png"), Render(512, theme: false));
File.WriteAllBytes(Path.Combine(outDir, "theme.png"), Render(512, theme: true));
if (args.Length > 1)
{
    // L'anteprima: le due icone grandi, e quella dell'app com'è a 64 e a 32 pixel.
    using var both = new SKBitmap(1100, 560);
    using var c = new SKCanvas(both);
    c.Clear(new SKColor(0x30, 0x30, 0x34));
    using var a = SKBitmap.Decode(Render(512, false));
    using var b = SKBitmap.Decode(Render(512, true));
    using var m = SKBitmap.Decode(Render(64, false));
    using var s = SKBitmap.Decode(Render(32, false));
    c.DrawBitmap(a, 24, 24);
    c.DrawBitmap(b, 560, 24);
    c.DrawBitmap(m, 24, 24);
    c.DrawBitmap(s, 100, 24);
    File.WriteAllBytes(args[1], SKImage.FromBitmap(both).Encode(SKEncodedImageFormat.Png, 100).ToArray());
}
Console.WriteLine("ok");

static byte[] Render(int size, bool theme)
{
    using var bmp = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
    using var c = new SKCanvas(bmp);
    c.Clear(SKColors.Transparent);
    c.Scale(size / 512f);   // si disegna sempre su 512x512, poi si rimpicciolisce

    // Il fondo, solo per il tema: un quadrato arrotondato quasi nero, appena più chiaro in alto.
    // L'icona di SteamAll invece non ha fondo: il contorno è il cerchio rosso, come quella di Steam.
    if (theme)
    {
        var tile = new SKRoundRect(new SKRect(8, 8, 504, 504), 112, 112);
        using var bg = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new SKPoint(256, 8), new SKPoint(256, 504),
            [new SKColor(0x22, 0x20, 0x24), new SKColor(0x08, 0x08, 0x0A)], SKShaderTileMode.Clamp) };
        c.DrawRoundRect(tile, bg);
        c.ClipRoundRect(tile, antialias: true);
    }

    // Il rosso: più acceso in alto a destra, più scuro in basso a sinistra. Il gradiente è legato alla
    // tela, non alla forma: tutti i pezzi rossi hanno la stessa sfumatura e non si vedono giunte.
    using var red = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new SKPoint(440, 70), new SKPoint(70, 450),
        [new SKColor(0xFF, 0x4B, 0x4B), new SKColor(0xE0, 0x1E, 0x2B), new SKColor(0xA8, 0x0E, 0x18)], [0, 0.55f, 1], SKShaderTileMode.Clamp) };

    if (theme) Brush(c, red);
    else SteamAll(c, red);

    using var image = SKImage.FromBitmap(bmp);
    return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
}

// --- L'icona di SteamAll ----------------------------------------------------------------------

static void SteamAll(SKCanvas c, SKPaint red)
{
    // Il "vuoto": quasi lo stesso nero del fondo, così il pistone sembra scavato nel badge.
    using var hole = new SKPaint { IsAntialias = true, Color = new SKColor(0x10, 0x0F, 0x12) };

    // Dal riquadro 24x24 di Steam al disegno da 512: badge di centro (256, 256) e raggio 248 (tutta l'icona).
    const float cx = 256, cy = 256, r = 248, s = 2 * r / 24;
    SKPoint P(float x, float y) => new(cx + (x - 11.979f) * s, cy + (y - 12) * s);

    c.DrawCircle(cx, cy, r, red);

    // I centri dei due "cerchi" di Steam, e la direzione della barra tra i due (esattamente a 45°).
    SKPoint hub = P(15.94f, 8.907f), joint = P(8.372f, 16.51f);
    float ux = joint.X - hub.X, uy = joint.Y - hub.Y, len = MathF.Sqrt(ux * ux + uy * uy);
    ux /= len; uy /= len;
    float nx = -uy, ny = ux;   // la perpendicolare

    // Il pistone: quattro pezzi (barra, coda, due quadrati) uniti uno alla volta con Op(Union), poi
    // tagliati sul bordo del badge (Intersect), così la coda si ferma sul cerchio come in Steam.
    var rod = new SKPath();
    // La barra: si stringe verso il quadrato piccolo come in Steam, ma più sottile (in Steam è larga
    // 6.28 e 4.55), così i due quadrati si staccano e si leggono come forme a sé.
    rod.MoveTo(hub.X + nx * 2.3f * s, hub.Y + ny * 2.3f * s);
    rod.LineTo(joint.X + nx * 1.7f * s, joint.Y + ny * 1.7f * s);
    rod.LineTo(joint.X - nx * 1.7f * s, joint.Y - ny * 1.7f * s);
    rod.LineTo(hub.X - nx * 2.3f * s, hub.Y - ny * 2.3f * s);
    rod.Close();
    // La coda: larga 3.75 come in Steam, dal quadrato piccolo verso il bordo a sinistra.
    float dx = -0.9242f, dy = -0.3819f, tx = -dy, ty = dx;
    var tail = new SKPath();
    tail.MoveTo(joint.X + tx * 1.875f * s, joint.Y + ty * 1.875f * s);
    tail.LineTo(joint.X + (tx * 1.875f + dx * 14) * s, joint.Y + (ty * 1.875f + dy * 14) * s);
    tail.LineTo(joint.X + (-tx * 1.875f + dx * 14) * s, joint.Y + (-ty * 1.875f + dy * 14) * s);
    tail.LineTo(joint.X - tx * 1.875f * s, joint.Y - ty * 1.875f * s);
    tail.Close();
    // I due quadrati al posto dei cerchi (più piccoli dei cerchi di Steam: un quadrato "pesa" di più).
    var big = new SKPath(); big.AddRoundRect(Square(hub, 4.0f * s, 1.0f * s));
    var small = new SKPath(); small.AddRoundRect(Square(joint, 3.0f * s, 0.8f * s));
    var badge = new SKPath(); badge.AddCircle(cx, cy, r);
    SKPath whole = rod;
    foreach (SKPath part in (SKPath[])[tail, big, small])
    {
        SKPath next = whole.Op(part, SKPathOp.Union);
        whole.Dispose(); part.Dispose();
        whole = next;
    }
    SKPath clipped = whole.Op(badge, SKPathOp.Intersect);
    whole.Dispose(); badge.Dispose();
    c.DrawPath(clipped, hole);
    clipped.Dispose();

    // Dentro il quadrato grande: il bordo rosso (come lo stacco tra anello e punto in Steam) e la griglia.
    c.DrawRoundRect(Square(hub, 2.75f * s, 0.55f * s), red);
    Grid(c, hub, 1.95f * s, 0.55f * s, hole);
    // Dentro il quadrato piccolo: il bordo rosso e un quadratino.
    c.DrawRoundRect(Square(joint, 1.95f * s, 0.45f * s), red);
    c.DrawRoundRect(Square(joint, 1.35f * s, 0.35f * s), hole);
}

// Un quadrato arrotondato di centro p e "mezzo lato" half.
static SKRoundRect Square(SKPoint p, float half, float corner) =>
    new(new SKRect(p.X - half, p.Y - half, p.X + half, p.Y + half), corner, corner);

// La griglia 2x2 della libreria, di centro p: quattro quadratini di lato side, staccati di gap.
static void Grid(SKCanvas c, SKPoint p, float side, float gap, SKPaint paint)
{
    float o = gap / 2 + side / 2;
    foreach (int i in (int[])[-1, 1])
        foreach (int j in (int[])[-1, 1])
            c.DrawRoundRect(Square(new SKPoint(p.X + i * o, p.Y + j * o), side / 2, side * 0.18f), paint);
}

// --- L'icona del tema ---------------------------------------------------------------------------

// Un pennello classico, dritto: in alto le setole con il bordo ondulato, poi una riga vuota, la
// ghiera larga che si arrotonda in basso e il manico con la punta tonda. Centrato su 512.
static void Brush(SKCanvas c, SKPaint red)
{
    // Le setole: angoli in alto arrotondati, bordo superiore a onda, in basso dritte.
    using var bristles = new SKPath();
    bristles.MoveTo(120, 172);
    bristles.LineTo(120, 92);
    bristles.QuadTo(120, 72, 140, 79);
    bristles.CubicTo(195, 100, 235, 95, 275, 76);
    bristles.CubicTo(315, 57, 355, 58, 382, 74);
    bristles.QuadTo(392, 80, 392, 94);
    bristles.LineTo(392, 172);
    bristles.Close();
    c.DrawPath(bristles, red);

    // La ghiera e il manico in un pezzo solo (tra setole e ghiera resta una riga vuota, da 172 a 190).
    using var body = new SKPath();
    body.MoveTo(120, 190);
    body.LineTo(392, 190);
    body.LineTo(392, 250);
    body.QuadTo(392, 296, 346, 296);   // l'angolo in basso a destra della ghiera, tondo
    body.LineTo(308, 296);
    body.LineTo(308, 400);               // il manico
    body.CubicTo(308, 428, 285, 451, 256.5f, 451);   // la punta tonda del manico
    body.CubicTo(228, 451, 205, 428, 205, 400);
    body.LineTo(205, 296);
    body.LineTo(166, 296);
    body.QuadTo(120, 296, 120, 250);   // l'angolo in basso a sinistra
    body.Close();
    c.DrawPath(body, red);
}

// --- Il file .ico -------------------------------------------------------------------------------

// Un .ico con dentro un PNG per ogni misura (lo accettano Windows Vista e successivi).
static void WriteIco(string path, int[] sizes, List<byte[]> pngs)
{
    using var w = new BinaryWriter(File.Create(path));
    w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
    int offset = 6 + 16 * sizes.Length;
    for (int i = 0; i < sizes.Length; i++)
    {
        w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        w.Write((byte)0); w.Write((byte)0); w.Write((ushort)1); w.Write((ushort)32);
        w.Write(pngs[i].Length); w.Write(offset);
        offset += pngs[i].Length;
    }
    foreach (byte[] p in pngs) w.Write(p);
}
