using System.Numerics;
using Polynomials.GPU;
using ScottPlot;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Polynomials;

internal class Program
{
    private static string _shaderSource = File.ReadAllText("createImage.frag");

    public static void Main(string[] args)
    {
        createColouredImage(15, 2);
    }

    private static void createColouredImage(int n, double scale)
    {
        (int width, int height) fourK = new(4096, 2160);
        (int width, int height) HD = new(1920, 1080);
        (int width, int height) low = new(720, 480);
        (int width, int height) res = fourK;
        SixLabors.ImageSharp.Image<Rgba32> img = new Image<Rgba32>(res.width, res.height);
        img.Mutate(x => x.Fill(SixLabors.ImageSharp.Color.Black));

        using GLComputeRenderer renderer = new();
        renderer.Initialise((uint)res.width, (uint)res.height);

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

            Hsl col = new((float)((double)(i - 1) / (n - 1) * 760) % 360, 1, 0.5f);
            var rgb = ColorSpaceConverter.ToRgb(col);

            renderer.Render(roots, new Vector4(rgb.R, rgb.G, rgb.B, 1), scale);
            using var shaded = renderer.GetResult();

            img.Mutate(ctx => ctx.DrawImage(shaded, PixelColorBlendingMode.Add, 1)); // foreach (Complex root in roots
        }
        img.Save("out.png");
    }
}
