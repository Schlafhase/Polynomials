using System.Numerics;
using System.Text;

namespace Polynomials;

public class Polynomial
{
    public int Degree { get; init; }
    public List<Complex> Coefficients { get; private init; }

    public Polynomial(int degree, List<Complex> coefficients)
    {
        Degree = degree;
        Coefficients =
            coefficients.Count == degree + 1
                ? coefficients
                : throw new ArgumentException("Polynomial must have degree + 1 coefficients");

        if (Coefficients[Degree] == 0)
        {
            throw new ArgumentException("nth coefficient must not be 0");
        }
    }

    public Complex Evaluate(Complex x)
    {
        return Coefficients
            .AsEnumerable()
            .Reverse()
            .Aggregate((result, coeff) => result * x + coeff);
    }

    public Polynomial Derivative =>
        new Polynomial(
            Degree - 1,
            [.. Coefficients.Slice(1, Coefficients.Count - 1).Select((c, i) => c * (i + 1))]
        );

    public (double upper, double lower) Bounds
    {
        get
        {
            double upper =
                1
                + Coefficients
                    .Select(c => (c / Coefficients[Coefficients.Count - 1]).Magnitude)
                    .Max();
            double lower = 1 / upper;
            return (upper, lower);
        }
    }

    private List<Complex> fibonacciAnnulus(int n, double rMin, double rMax)
    {
        double goldenRatio = (1d + Math.Sqrt(5d)) / 2d;
        return
        [
            .. (
                from i in Enumerable.Range(0, n)
                let t = (double)i / n
                let r = Math.Sqrt(t * (rMax * rMax - rMin * rMin) + rMin * rMin)
                let theta = 2d * Math.PI * i / goldenRatio
                select new Complex(r * Math.Cos(theta), r * Math.Sin(theta))
            ),
        ];
    }

    public List<Complex> GetRootsAberth(int maxIterations = 100000, double threshold = 0.001)
    {
        (double upper, double lower) bounds = Bounds;
        List<Complex> roots = fibonacciAnnulus(Degree, bounds.lower, bounds.upper);
        Polynomial derivative = Derivative;

        for (int i = 0; i < maxIterations; i++)
        {
            List<Complex> offsets = new List<Complex>(Degree);

            for (int k = 0; k < Degree; k++)
            {
                Complex zk = roots[k];
                Complex thisOverDerivative = (Evaluate(zk) / derivative.Evaluate(zk));

                Complex sum = Complex.Zero;
                for (int j = 0; j < Degree; j++)
                {
                    if (j != k)
                    {
                        sum += 1d / (zk - roots[j]);
                    }
                }

                offsets.Add(thisOverDerivative / (1 - thisOverDerivative * sum));
            }

            for (int k = 0; k < Degree; k++)
            {
                roots[k] -= offsets[k];
            }

            if (offsets.Select(o => o.Magnitude).Max() < threshold)
            {
                return roots;
            }
        }

        return roots;
    }

    public override string ToString()
    {
        StringBuilder sb = new();
        for (int i = 0; i < Coefficients.Count; i++)
        {
            Complex c = Coefficients[i];
            if (c != 0)
            {
                _ = sb.Insert(0, "(" + c.ToString() + ")x^" + i + "+ ");
            }
        }

        return sb.ToString();
    }

    public static List<Polynomial> LittlewoodUpTo(int n)
    {
        List<Polynomial> result = [];
        for (int i = 1; i < n; i++)
        {
            result.AddRange(Littlewood(i));
        }

        return result;
    }

    public static IEnumerable<Polynomial> Littlewood(int n)
    {
        List<Complex> choices = [new Complex(-1, 0), new Complex(1, 0)];

        return cartesian(Enumerable.Repeat(choices, n + 1))
            .Select(coeffs => new Polynomial(n, [.. coeffs]));
    }

    private static IEnumerable<IEnumerable<T>> cartesian<T>(IEnumerable<IEnumerable<T>> sequences)
    {
        IEnumerable<IEnumerable<T>> emptyProduct = new[] { Enumerable.Empty<T>() };

        return sequences.Aggregate(
            emptyProduct,
            (acc, seq) => from set in acc from item in seq select set.Concat(new[] { item })
        );
    }
}
