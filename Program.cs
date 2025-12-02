using System.Numerics;
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
    public static void Main(string[] args)
    {
        createColouredImage(15, 2);
        return;
        List<Polynomial> littlewoods = Polynomial.LittlewoodUpTo(15);
        List<Complex> roots = [];

        foreach (var p in littlewoods)
        {
            roots.AddRange(p.GetRootsAberth());
        }

        Plot plot = new();

        plot.Add.Markers(
            roots.Select(r => r.Real).ToArray(),
            roots.Select(r => r.Imaginary).ToArray(),
            MarkerShape.FilledCircle,
            2
        );
        plot.SavePng("plot.png", 1920, 1080);
    }

    private static void createColouredImage(int n, double scale)
    {
        (int width, int height) fourK = new(4096, 2160);
        (int width, int height) HD = new(1920, 1080);
        (int width, int height) low = new(720, 480);
        (int width, int height) res = HD;
        SixLabors.ImageSharp.Image img = new Image<Rgba32>(res.width, res.height);
        img.Mutate(x => x.Fill(SixLabors.ImageSharp.Color.Black));

        goto roots;
        for (int i = 1; i < n; i++)
        {
            List<Polynomial> littlewoods = [.. Polynomial.Littlewood(i)];
            Console.WriteLine($"Solving Polynomials of degree {i}");
            foreach (Polynomial l in littlewoods)
            {
                img.Mutate(ctx =>
                    ctx.ProcessPixelRowsAsVector4(
                        (span, point) =>
                        {
                            for (int x = 0; x < span.Length; x++)
                            {
                                // Calculate the actual x coordinate
                                int actualX = point.X + x;
                                int y = point.Y;

                                // Modify the pixel based on coordinates
                                // span[x] contains the RGBA values as Vector4 (values from 0.0 to 1.0)
                                var pixel = span[x];
                                var uvX =
                                    ((double)actualX / img.Height - 0.5 * img.Width / img.Height)
                                    * 2;
                                var uvY = -((double)y / img.Height - 0.5) * 2;
                                Complex c = new(uvX * scale, uvY * scale);
                                Complex r = l.Evaluate(c);
                                double a = Math.Atan2(r.Imaginary, r.Real);
                                var col = new Hsl(
                                    (float)(a / Math.PI * 180),
                                    1,
                                    (float)(0.1 / Math.Exp(r.Magnitude) + 0.1)
                                );

                                var rgb = ColorSpaceConverter.ToRgb(col);

                                pixel.X += rgb.R / n;
                                pixel.Y += rgb.G / n;
                                pixel.Z += rgb.B / n;
                                // pixel.X = rgb.R;
                                // pixel.Y = rgb.G;
                                // pixel.Z = rgb.B;
                                pixel.W = 1.0f;

                                span[x] = pixel;
                            }
                        }
                    )
                );
            }
        }

        roots:

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

            Hsl col = new((float)((double)(i - 1) / (n - 1) * 360), 1, 0.5f);
            var rgb = ColorSpaceConverter.ToRgb(col);

            // foreach (Complex root in roots)
            // {
            //     EllipsePolygon circle = new(
            //         new PointF(
            //             (float)(root.Real / scale / 2 + 0.5 * img.Width / img.Height) * img.Height,
            //             (float)(-root.Imaginary / scale / 2 + 0.5) * img.Height
            //         ),
            //         4f
            //     );
            //     img.Mutate(x =>
            //         x.Fill(
            //             new SixLabors.ImageSharp.Color(new Vector4(rgb.R, rgb.G, rgb.B, 2f / n)),
            //             circle
            //         )
            //     );
            // }

            img.Mutate(ctx =>
                ctx.ProcessPixelRowsAsVector4(
                    (span, point) =>
                    {
                        for (int x = 0; x < span.Length; x++)
                        {
                            int actualX = point.X + x;
                            int y = point.Y;

                            // Modify the pixel based on coordinates
                            // span[x] contains the RGBA values as Vector4 (values from 0.0 to 1.0)
                            var pixel = span[x];
                            var uvX =
                                ((double)actualX / img.Height - 0.5 * img.Width / img.Height) * 2;
                            var uvY = -((double)y / img.Height - 0.5) * 2;
                            Complex c = new(uvX * scale, uvY * scale);
                            double d = 50 * roots.Select(r => (c - r).Magnitude).Min();

                            pixel.X += (float)(rgb.R / d / n);
                            pixel.Y += (float)(rgb.G / d / n);
                            pixel.Z += (float)(rgb.B / d / n);
                            // pixel.X = rgb.R;
                            // pixel.Y = rgb.G;
                            // pixel.Z = rgb.B;
                            pixel.W = 1.0f;

                            span[x] = pixel;
                        }
                    }
                )
            );
        }
        img.Save("out.png");
    }
}
