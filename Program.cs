using System.Numerics;

using Microsoft.Extensions.Logging;

using Polynomials.GPU;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Polynomials;

internal class Program
{

    public static void Main(string[] args)
    {
        createColouredImage(15, 3);
    }

    private static void createColouredImage(int n, double scale)
    {
        (int width, int height) fourK = new(4096, 2160);
        (int width, int height) HD = new(1920, 1080);
        (int width, int height) low = new(720, 480);
        (int width, int height) res = fourK;
        Image<Rgba32> img = new Image<Rgba32>(res.width, res.height);
        img.Mutate(x => x.Fill(Color.Black));


        using GLComputeRenderer renderer = new();
        using (ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug)))
        {
            renderer.Logger = factory.CreateLogger<GLComputeRenderer>();
        }
        renderer.Initialise((uint)res.width, (uint)res.height);
        renderer.ClearOutput();

        for (int i = 1; i < n; i++)
        {
            Console.WriteLine($"Finding roots {i}");
            List<Complex> roots = [];
            object rootsLock = new();

            Parallel.ForEach(
                Polynomial.Littlewood(i),
                l =>
                {
                    var r = l.GetRootsAberth();
                    lock (rootsLock)
                    {
                        roots.AddRange(r);
                    }
                }
            );

            Hsl col = new((float)((double)(i - 1) / (n - 1) * 360 + 100) % 360, 0.5f, 0.3f);
            var rgb = ColorSpaceConverter.ToRgb(col);

            renderer.Render(roots, new Vector4(rgb.R, rgb.G, rgb.B, 1), scale);
            using var shaded = renderer.GetResult();

            img.Mutate(ctx => ctx.DrawImage(shaded, PixelColorBlendingMode.Add, 1));
        }
        img.Save("out.png");
    }
}