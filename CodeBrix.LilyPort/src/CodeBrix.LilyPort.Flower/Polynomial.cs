/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
                           Jan Nieuwenhuizen <janneke@gnu.org>

  LilyPond is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  LilyPond is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;

namespace CodeBrix.LilyPort.Flower; //was previously: flower/polynomial.cc, flower/include/polynomial.hh;
// Modified by Jeremy Ellis on 2026-08-02 as part of the CodeBrix port:
//   - translated from C++17 to C# targeting net10.0
//   - std::vector<Real> coefs_ becomes List<double> Coefficients
//   - the closed-form solvers are translated LITERALLY, including Cardano's
//     formula and its three discriminant branches. These are numerical routines
//     whose behaviour on degenerate input is depended upon by the slur and beam
//     code, so faithfulness beats tidiness here.

/// <summary>
/// A polynomial with real coefficients, stored lowest-order first, with closed-form
/// solvers up to cubics. LilyPond uses these for Bézier geometry in slurs and ties.
/// </summary>
public sealed class Polynomial
{
    /// <summary>
    /// The relative tolerance used when discarding negligible leading coefficients.
    /// Upstream compares RELATIVELY, because absolute comparisons break down in
    /// degenerate cases.
    /// </summary>
    public const double Fudge = 1e-8;

    /// <summary>Initializes the zero polynomial.</summary>
    public Polynomial()
    {
        Coefficients = new List<double>();
    }

    /// <summary>Initializes a constant or linear polynomial.</summary>
    /// <param name="a">The constant term.</param>
    /// <param name="b">The coefficient of x.</param>
    public Polynomial(double a, double b = 0.0)
    {
        Coefficients = new List<double> { a, b };
    }

    /// <summary>Initializes a polynomial from its coefficients, lowest order first.</summary>
    /// <param name="coefficients">The coefficients, index equal to the power of x.</param>
    public Polynomial(IEnumerable<double> coefficients)
    {
        Coefficients = new List<double>(coefficients ?? Array.Empty<double>());
    }

    /// <summary>Gets the coefficients, lowest order first.</summary>
    public List<double> Coefficients { get; }

    /// <summary>
    /// Gets the degree, which is one less than the coefficient count. Note this follows
    /// upstream and does NOT account for a zero leading coefficient — call
    /// <see cref="Clean"/> first if that matters.
    /// </summary>
    public int Degree => Coefficients.Count - 1;

    /// <summary>Adds another polynomial into this one, in place.</summary>
    /// <param name="other">The polynomial to add.</param>
    public void Add(Polynomial other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        while (Degree < other.Degree)
        {
            Coefficients.Add(0.0);
        }

        for (int i = 0; i <= other.Degree; i++)
        {
            Coefficients[i] += other.Coefficients[i];
        }
    }

    /// <summary>Subtracts another polynomial from this one, in place.</summary>
    /// <param name="other">The polynomial to subtract.</param>
    public void Subtract(Polynomial other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        while (Degree < other.Degree)
        {
            Coefficients.Add(0.0);
        }

        for (int i = 0; i <= other.Degree; i++)
        {
            Coefficients[i] -= other.Coefficients[i];
        }
    }

    /// <summary>Returns an independent copy.</summary>
    /// <returns>The copy.</returns>
    public Polynomial Copy() => new Polynomial(Coefficients);

    /// <summary>Evaluates the polynomial at a point, by Horner's method.</summary>
    /// <param name="x">The point to evaluate at.</param>
    /// <returns>The value.</returns>
    public double Evaluate(double x)
    {
        double p = 0.0;
        for (int i = Coefficients.Count - 1; i >= 0; i--)
        {
            p = (x * p) + Coefficients[i];
        }

        return p;
    }

    /// <summary>Drops negligible leading coefficients, comparing relatively.</summary>
    public void Clean()
    {
        while (Degree > 0
               && (Math.Abs(Coefficients[Coefficients.Count - 1])
                   < Fudge * Math.Abs(Coefficients[Coefficients.Count - 2])
                   || Coefficients[Coefficients.Count - 1] == 0.0))
        {
            Coefficients.RemoveAt(Coefficients.Count - 1);
        }
    }

    /// <summary>Scales every coefficient.</summary>
    /// <param name="factor">The scale factor.</param>
    public void ScalarMultiply(double factor)
    {
        for (int i = 0; i < Coefficients.Count; i++)
        {
            Coefficients[i] *= factor;
        }
    }

    /// <summary>Differentiates the polynomial in place.</summary>
    public void Differentiate()
    {
        for (int i = 1; i <= Degree; i++)
        {
            Coefficients[i - 1] = Coefficients[i] * i;
        }

        if (Coefficients.Count > 0)
        {
            Coefficients.RemoveAt(Coefficients.Count - 1);
        }
    }

    /// <summary>Multiplies two polynomials.</summary>
    /// <param name="p1">The first factor.</param>
    /// <param name="p2">The second factor.</param>
    /// <returns>The product.</returns>
    public static Polynomial Multiply(Polynomial p1, Polynomial p2)
    {
        Polynomial destination = new Polynomial();
        int degree = p1.Degree + p2.Degree;
        for (int i = 0; i <= degree; i++)
        {
            destination.Coefficients.Add(0.0);
            for (int j = 0; j <= i; j++)
            {
                if (i - j <= p2.Degree && j <= p1.Degree)
                {
                    destination.Coefficients[destination.Coefficients.Count - 1]
                        += p1.Coefficients[j] * p2.Coefficients[i - j];
                }
            }
        }

        return destination;
    }

    /// <summary>Raises a polynomial to an integer power, by repeated squaring.</summary>
    /// <param name="exponent">The exponent.</param>
    /// <param name="source">The base polynomial.</param>
    /// <returns>The power.</returns>
    public static Polynomial Power(int exponent, Polynomial source)
    {
        int e = exponent;
        Polynomial destination = new Polynomial(1.0);
        Polynomial baseValue = new Polynomial(source.Coefficients);

        // Classic integer power; the invariant is source^exponent = destination * source^e.
        while (e > 0)
        {
            if (e % 2 != 0)
            {
                destination = Multiply(destination, baseValue);
                e--;
            }
            else
            {
                baseValue = Multiply(baseValue, baseValue);
                e /= 2;
            }
        }

        return destination;
    }

    /// <summary>Adds two polynomials.</summary>
    /// <param name="a">The first addend.</param>
    /// <param name="b">The second addend.</param>
    /// <returns>The sum.</returns>
    public static Polynomial operator +(Polynomial a, Polynomial b) => Combine(a, b, 1.0);

    /// <summary>Subtracts one polynomial from another.</summary>
    /// <param name="a">The minuend.</param>
    /// <param name="b">The subtrahend.</param>
    /// <returns>The difference.</returns>
    public static Polynomial operator -(Polynomial a, Polynomial b) => Combine(a, b, -1.0);

    /// <summary>Multiplies two polynomials.</summary>
    /// <param name="a">The first factor.</param>
    /// <param name="b">The second factor.</param>
    /// <returns>The product.</returns>
    public static Polynomial operator *(Polynomial a, Polynomial b) => Multiply(a, b);

    /// <summary>Scales a polynomial.</summary>
    /// <param name="p">The polynomial.</param>
    /// <param name="factor">The scale factor.</param>
    /// <returns>The scaled polynomial.</returns>
    public static Polynomial operator *(Polynomial p, double factor)
    {
        Polynomial result = new Polynomial(p.Coefficients);
        result.ScalarMultiply(factor);
        return result;
    }

    private static Polynomial Combine(Polynomial a, Polynomial b, double sign)
    {
        int count = Math.Max(a.Coefficients.Count, b.Coefficients.Count);
        Polynomial result = new Polynomial();
        for (int i = 0; i < count; i++)
        {
            double left = i < a.Coefficients.Count ? a.Coefficients[i] : 0.0;
            double right = i < b.Coefficients.Count ? b.Coefficients[i] : 0.0;
            result.Coefficients.Add(left + (sign * right));
        }

        return result;
    }

    /// <summary>
    /// The real cube root, defined for negative input too — <c>Math.Cbrt</c> matches
    /// upstream's helper, which flips the sign rather than returning NaN.
    /// </summary>
    private static double CubicRoot(double x) => Math.Cbrt(x);

    /// <summary>Solves a linear polynomial.</summary>
    /// <returns>The single root, or nothing when the leading coefficient is zero.</returns>
    public List<double> SolveLinear()
    {
        List<double> solutions = new List<double>();
        if (Coefficients[1] != 0.0)
        {
            solutions.Add(-Coefficients[0] / Coefficients[1]);
        }

        return solutions;
    }

    /// <summary>Solves a quadratic polynomial.</summary>
    /// <returns>The real roots; empty when the discriminant is not positive.</returns>
    public List<double> SolveQuadric()
    {
        List<double> solutions = new List<double>();

        // Normal form: x^2 + px + q = 0
        double p = Coefficients[1] / (2 * Coefficients[2]);
        double q = Coefficients[0] / Coefficients[2];
        double discriminant = (p * p) - q;

        if (discriminant > 0)
        {
            discriminant = Math.Sqrt(discriminant);
            solutions.Add(discriminant - p);
            solutions.Add(-discriminant - p);
        }

        return solutions;
    }

    /// <summary>Solves a cubic polynomial by Cardano's formula.</summary>
    /// <returns>The real roots.</returns>
    public List<double> SolveCubic()
    {
        List<double> solutions = new List<double>();

        // Normal form: x^3 + Ax^2 + Bx + C = 0
        double a = Coefficients[2] / Coefficients[3];
        double b = Coefficients[1] / Coefficients[3];
        double c = Coefficients[0] / Coefficients[3];

        // Substitute x = y - A/3 to eliminate the quadratic term: x^3 + px + q = 0
        double squaredA = a * a;
        double p = 1.0 / 3 * ((-1.0 / 3 * squaredA) + b);
        double q = 1.0 / 2 * ((2.0 / 27 * a * squaredA) - (1.0 / 3 * a * b) + c);

        // Cardano's formula
        double cb = p * p * p;
        double discriminant = (q * q) + cb;

        if (discriminant == 0)
        {
            if (q == 0)
            {
                // One triple solution.
                solutions.Add(0);
                solutions.Add(0);
                solutions.Add(0);
            }
            else
            {
                // One single and one double solution.
                double u = CubicRoot(-q);
                solutions.Add(2 * u);
                solutions.Add(-u);
            }
        }
        else if (discriminant < 0)
        {
            // Casus irreducibilis: three real solutions.
            double phi = 1.0 / 3 * Math.Acos(-q / Math.Sqrt(-cb));
            double t = 2 * Math.Sqrt(-p);
            solutions.Add(t * Math.Cos(phi));
            solutions.Add(-t * Math.Cos(phi + (Math.PI / 3)));
            solutions.Add(-t * Math.Cos(phi - (Math.PI / 3)));
        }
        else
        {
            // One real solution.
            double sqrtDiscriminant = Math.Sqrt(discriminant);
            double u = CubicRoot(sqrtDiscriminant - q);
            double v = -CubicRoot(sqrtDiscriminant + q);
            solutions.Add(u + v);
        }

        // Resubstitute.
        double substitution = 1.0 / 3 * a;
        for (int i = 0; i < solutions.Count; i++)
        {
            solutions[i] -= substitution;
        }

        return solutions;
    }

    /// <summary>
    /// Solves the polynomial, dispatching on degree after cleaning. Degrees above three
    /// yield no solutions, as upstream does.
    /// </summary>
    /// <returns>The real roots.</returns>
    public List<double> Solve()
    {
        Clean();
        switch (Degree)
        {
            case 1: return SolveLinear();
            case 2: return SolveQuadric();
            case 3: return SolveCubic();
            default: return new List<double>();
        }
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The coefficients, lowest order first.</returns>
    public override string ToString()
        => "Polynomial[" + string.Join(", ", Coefficients) + "]";
}
