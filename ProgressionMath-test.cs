using System;

public static class ProgressionMath
{
    public static double GetLinearBulkCost( double baseCost, double step, int fromLevel, int count )
    {
        return count * baseCost + step * (2 * fromLevel + count - 1) * count / 2.0;
    }

    public static int GetLinearAffordableLevels( double baseCost, double step, int fromLevel, double playerGold )
    {
        if ( playerGold <= 0 || baseCost <= 0 ) return 0;
        if ( step <= 0 ) return (int)Math.Floor( playerGold / baseCost );

        double a = step / 2.0;
        double b = baseCost + step * fromLevel - step / 2.0;
        double c = -playerGold;

        double discriminant = b * b - 4 * a * c;
        if ( discriminant < 0 ) return 0;

        double k = (-b + Math.Sqrt( discriminant )) / (2 * a);
        return (int)Math.Floor( k );
    }

    private static double SumOfSquares( long n )
    {
        return n * (n + 1) * (2 * n + 1) / 6.0;
    }

    public static double GetQuadraticBulkCost( double baseCost, double step, int fromLevel, int count )
    {
        double baseSum = baseCost * count;
        double squaresSum = SumOfSquares( fromLevel + count - 1 ) - SumOfSquares( fromLevel - 1 );
        return baseSum + step * squaresSum;
    }

    public static int GetQuadraticAffordableLevels( double baseCost, double step, int fromLevel, double playerGold )
    {
        if ( playerGold <= 0 ) return 0;

        double estimatedK = Math.Pow( (playerGold * 3.0) / (step > 0 ? step : 1.0), 1.0 / 3.0 );
        int count = (int)Math.Floor( estimatedK );

        while ( GetQuadraticBulkCost( baseCost, step, fromLevel, count ) > playerGold && count > 0 )
        {
            count--;
        }
        while ( GetQuadraticBulkCost( baseCost, step, fromLevel, count + 1 ) <= playerGold )
        {
            count++;
        }

        return count;
    }

    public static double GetGeometricBulkCost( double baseCost, double multiplier, int fromLevel, int count )
    {
        if ( Math.Abs( multiplier - 1.0 ) < 0.0001 ) return baseCost * count;

        double powN = Math.Pow( multiplier, fromLevel );
        double powK = Math.Pow( multiplier, count );

        if ( double.IsInfinity( powN ) || double.IsInfinity( powK ) ) return double.MaxValue;

        return baseCost * powN * (powK - 1.0) / (multiplier - 1.0);
    }

    public static int GetGeometricAffordableLevels( double baseCost, double multiplier, int fromLevel, double playerGold )
    {
        if ( Math.Abs( multiplier - 1.0 ) < 0.0001 ) return (int)Math.Floor( playerGold / baseCost );

        double powN = Math.Pow( multiplier, fromLevel );
        if ( double.IsInfinity( powN ) ) return 0;

        double val = (playerGold * (multiplier - 1.0)) / (baseCost * powN) + 1.0;
        if ( val <= 0 || double.IsNaN( val ) ) return 0;

        double k = Math.Log( val, multiplier );
        if ( double.IsNaN( k ) || double.IsInfinity( k ) ) return 0;

        return (int)Math.Floor( k );
    }
}