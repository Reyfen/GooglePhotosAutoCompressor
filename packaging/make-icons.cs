#:package Magick.NET-Q8-AnyCPU@15.0.0
// Renders the app icon (Assets/icon.svg) to PNG sizes and a Windows .ico.
// Run from the repo root: dotnet run packaging/make-icons.cs
using ImageMagick;

string assets = Path.Combine("GooglePhotosAutoCompressor.App", "Assets");
string svg = Path.Combine(assets, "icon.svg");

MagickImage Render(uint size)
{
    var settings = new MagickReadSettings { BackgroundColor = MagickColors.Transparent, Density = new Density(96.0 * size / 256) };
    var image = new MagickImage(svg, settings);
    image.Resize(size, size);
    return image;
}

using (MagickImage png = Render(256)) png.Write(Path.Combine(assets, "icon.png"));
using (MagickImage png = Render(512)) png.Write(Path.Combine("packaging", "linux", "icon-512.png"));

using var collection = new MagickImageCollection();
foreach (uint size in new uint[] { 16, 24, 32, 48, 64, 128, 256 })
{
    collection.Add(Render(size));
}
collection.Write(Path.Combine(assets, "icon.ico"), MagickFormat.Ico);
Console.WriteLine("Icons written.");
