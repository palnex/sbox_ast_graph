using Sandbox;
using System;
using System.Collections.Generic;

public static class Formulas
{
    // =============================================================
    // ПОЛЯ СУМІСНОСТІ ДЛЯ РЕШТИ СИСТЕМИ
    // =============================================================
    public static double CardNewModifierUnlockCost = 1000.0;
    public static double CardWeightUpgradeCost = 500.0;

    public static ProgressionType GetProgressionType( StatType stat ) => StatRegistry.Get( stat ).CardProgression;

    public static double GetCardStatBaseCost( StatType stat )
    {
        var def = StatRegistry.Get( stat );
        if ( def.CardParams == null || def.CardParams.Length == 0 ) return 10.0; // Безпечний дефолт
        return def.CardParams[0];
    }

    public static double GetCardStatMultiplier( StatType stat )
    {
        var def = StatRegistry.Get( stat );
        if ( def.CardParams == null || def.CardParams.Length < 2 ) return 1.0; // Безпечний дефолт
        return def.CardParams[1];
    }

    public static double GetCardStatStepValue( StatType stat, bool isMultiplier )
    {
        var def = StatRegistry.Get( stat );
        if ( def.CardParams == null || def.CardParams.Length < 2 ) return 1.0; // Безпечний дефолт
        return def.CardParams[1];
    }

    public static double GetCardStatBaseCost( StatType stat, double valuePerLevel, bool isMultiplier )
    {
        double standardBase = GetCardStatBaseCost( stat );
        double defaultStep = GetCardStatStepValue( stat, isMultiplier );
        if ( defaultStep <= 0 ) return standardBase;

        double powerMultiplier = valuePerLevel / defaultStep;
        return standardBase * Math.Max( 1.0, powerMultiplier );
    }

    public static bool CheckMilestone( StatType stat, int nextLevel, out double milestoneCost, out string milestoneName )
    {
        milestoneCost = 0;
        milestoneName = "";
        return false;
    }

    // =============================================================
    // ОБЧИСЛЕННЯ ЦІНИ КАРТКИ ЗА ОДИН КОНКРЕТНИЙ РІВЕНЬ
    // =============================================================
    public static double GetCardStatCostAtLevel( StatType stat, int level, double valuePerLevel, bool isMultiplier )
    {
        int nextLevel = level + 1;

        if ( CheckMilestone( stat, nextLevel, out double milestoneCost, out _ ) )
        {
            return milestoneCost;
        }

        if ( level == 0 )
        {
            return StatRegistry.Get( stat ).UnlockCost;
        }

        return GetCardStatBulkCost( stat, valuePerLevel, isMultiplier, level, 1 );
    }

    // =============================================================
    // СУМІСНІСТЬ З ІСНУЮЧИМ КОДОМ КАРТОК (Обгортки для LeveledStat)
    // =============================================================
    public static double GetCardStatBulkCost( LeveledStat stat, int fromLevel, int count )
    {
        return GetCardStatBulkCost( stat.Type, stat.ValuePerLevel, stat.IsMultiplier, fromLevel, count );
    }

    public static int GetCardStatAffordableLevels( LeveledStat stat, int fromLevel, double playerGold )
    {
        return GetCardStatAffordableLevels( stat.Type, stat.ValuePerLevel, stat.IsMultiplier, fromLevel, playerGold );
    }

    // =============================================================
    // ЧИСТИЙ РОЗРАХУНОК O(1) ДЛЯ КАРТОК (Використовує CardParams)
    // =============================================================
    public static double GetCardStatBulkCost( StatType statType, double valuePerLevel, bool isMultiplier, int fromLevel, int count )
    {
        if ( count <= 0 ) return 0;

        double totalCost = 0;
        int remainingCount = count;
        int currentLvl = fromLevel;

        if ( currentLvl == 0 )
        {
            totalCost += GetCardStatCostAtLevel( statType, 0, valuePerLevel, isMultiplier );
            remainingCount--;
            currentLvl++;
        }

        if ( remainingCount <= 0 ) return totalCost;

        var def = StatRegistry.Get( statType );
        double valueScale = isMultiplier ? 100.0 : 1.0;
        double stepFactor = valuePerLevel * valueScale;

        if ( stepFactor <= 0.0001 ) stepFactor = 1.0;

        var boundaries = new List<(int Level, double Multiplier)>();
        foreach ( var t in def.PriceThresholds )
        {
            int thresholdLvl = (int)Math.Ceiling( t.ValueThreshold / stepFactor );
            boundaries.Add( (thresholdLvl, t.Multiplier) );
        }

        double currentMult = 1.0;
        foreach ( var b in boundaries )
        {
            if ( currentLvl >= b.Level ) currentMult = b.Multiplier;
        }

        while ( remainingCount > 0 )
        {
            int nextBoundaryLvl = int.MaxValue;
            double nextMult = currentMult;

            foreach ( var b in boundaries )
            {
                if ( b.Level > currentLvl )
                {
                    nextBoundaryLvl = b.Level;
                    nextMult = b.Multiplier;
                    break;
                }
            }

            int levelsToThreshold = nextBoundaryLvl - currentLvl;
            int currentChunk = Math.Min( remainingCount, levelsToThreshold );

            double rawCost = GetRawBulkCost( def.CardProgression, def.CardParams, currentLvl, currentChunk );

            totalCost += rawCost * currentMult;

            currentLvl += currentChunk;
            remainingCount -= currentChunk;
            currentMult = nextMult;
        }

        return totalCost;
    }

    public static int GetCardStatAffordableLevels( StatType statType, double valuePerLevel, bool isMultiplier, int fromLevel, double playerGold )
    {
        if ( playerGold <= 0 ) return 0;

        double remainingGold = playerGold;
        int totalPurchased = 0;
        int currentLvl = fromLevel;

        if ( currentLvl == 0 )
        {
            double unlockCost = GetCardStatCostAtLevel( statType, 0, valuePerLevel, isMultiplier );
            if ( remainingGold < unlockCost ) return 0;

            remainingGold -= unlockCost;
            totalPurchased++;
            currentLvl++;
        }

        var def = StatRegistry.Get( statType );
        double valueScale = isMultiplier ? 100.0 : 1.0;
        double stepFactor = valuePerLevel * valueScale;

        if ( stepFactor <= 0.0001 ) stepFactor = 1.0;

        var boundaries = new List<(int Level, double Multiplier)>();
        foreach ( var t in def.PriceThresholds )
        {
            int thresholdLvl = (int)Math.Ceiling( t.ValueThreshold / stepFactor );
            boundaries.Add( (thresholdLvl, t.Multiplier) );
        }

        double currentMult = 1.0;
        foreach ( var b in boundaries )
        {
            if ( currentLvl >= b.Level ) currentMult = b.Multiplier;
        }

        while ( remainingGold > 0 )
        {
            int nextBoundaryLvl = int.MaxValue;
            double nextMult = currentMult;

            foreach ( var b in boundaries )
            {
                if ( b.Level > currentLvl )
                {
                    nextBoundaryLvl = b.Level;
                    nextMult = b.Multiplier;
                    break;
                }
            }

            int maxInSegment = nextBoundaryLvl - currentLvl;
            if ( maxInSegment <= 0 ) break;

            double adjustedGold = remainingGold / currentMult;

            int affordable = GetRawAffordableLevels( def.CardProgression, def.CardParams, currentLvl, adjustedGold );
            int actualBuy = Math.Min( maxInSegment, affordable );

            if ( actualBuy <= 0 ) break;

            double chunkCost = GetRawBulkCost( def.CardProgression, def.CardParams, currentLvl, actualBuy ) * currentMult;

            remainingGold -= chunkCost;
            totalPurchased += actualBuy;
            currentLvl += actualBuy;

            if ( actualBuy < maxInSegment ) break;

            currentMult = nextMult;
        }

        return totalPurchased;
    }

    // =============================================================
    // ЧИСТИЙ РОЗРАХУНОК O(1) ДЛЯ МАГАЗИНУ (Використовує ShopParams)
    // =============================================================
    public static double GetShopStatCostAtLevel( StatType stat, int level, double valuePerLevel, bool isMultiplier )
    {
        if ( level == 0 )
        {
            return StatRegistry.Get( stat ).UnlockCost;
        }

        return GetShopStatBulkCost( stat, level, 1 );
    }

    /// <summary>
    /// Розраховує доступну кількість рівнів для магазину вежі відповідно до балансу гравця.
    /// </summary>
    public static int GetShopStatAffordableLevels( StatType statType, int fromLevel, double playerGold )
    {
        if ( playerGold <= 0 ) return 0;

        double remainingGold = playerGold;
        int totalPurchased = 0;
        int currentLvl = fromLevel;

        if ( currentLvl == 0 )
        {
            double unlockCost = GetShopStatCostAtLevel( statType, 0, 1.0, false );
            if ( remainingGold < unlockCost ) return 0;

            remainingGold -= unlockCost;
            totalPurchased++;
            currentLvl++;
        }

        var def = StatRegistry.Get( statType );
        double stepFactor = def.ShopParams[1];
        if ( stepFactor <= 0.0001 ) stepFactor = 1.0;

        var boundaries = new List<(int Level, double Multiplier)>();
        foreach ( var t in def.PriceThresholds )
        {
            int thresholdLvl = (int)Math.Ceiling( t.ValueThreshold / stepFactor );
            boundaries.Add( (thresholdLvl, t.Multiplier) );
        }

        double currentMult = 1.0;
        foreach ( var b in boundaries )
        {
            if ( currentLvl >= b.Level ) currentMult = b.Multiplier;
        }

        while ( remainingGold > 0 )
        {
            int nextBoundaryLvl = int.MaxValue;
            double nextMult = currentMult;

            foreach ( var b in boundaries )
            {
                if ( b.Level > currentLvl )
                {
                    nextBoundaryLvl = b.Level;
                    nextMult = b.Multiplier;
                    break;
                }
            }

            int maxInSegment = nextBoundaryLvl - currentLvl;
            if ( maxInSegment <= 0 ) break;

            double adjustedGold = remainingGold / currentMult;

            int affordable = GetRawAffordableLevels( def.ShopProgression, def.ShopParams, currentLvl, adjustedGold );
            int actualBuy = Math.Min( maxInSegment, affordable );

            if ( actualBuy <= 0 ) break;

            double chunkCost = GetRawBulkCost( def.ShopProgression, def.ShopParams, currentLvl, actualBuy ) * currentMult;

            remainingGold -= chunkCost;
            totalPurchased += actualBuy;
            currentLvl += actualBuy;

            if ( actualBuy < maxInSegment ) break;

            currentMult = nextMult;
        }

        return totalPurchased;
    }
    public static double GetShopStatBulkCost( StatType statType, int fromLevel, int count )
    {
        if ( count <= 0 ) return 0;

        double totalCost = 0;
        int remainingCount = count;
        int currentLvl = fromLevel;

        if ( currentLvl == 0 )
        {
            totalCost += StatRegistry.Get( statType ).UnlockCost;
            remainingCount--;
            currentLvl++;
        }

        if ( remainingCount <= 0 ) return totalCost;

        var def = StatRegistry.Get( statType );

        // Для магазину крок подорожчання береться з параметрів магазину
        double stepFactor = def.ShopParams[1];
        if ( stepFactor <= 0.0001 ) stepFactor = 1.0;

        var boundaries = new List<(int Level, double Multiplier)>();
        foreach ( var t in def.PriceThresholds )
        {
            int thresholdLvl = (int)Math.Ceiling( t.ValueThreshold / stepFactor );
            boundaries.Add( (thresholdLvl, t.Multiplier) );
        }

        double currentMult = 1.0;
        foreach ( var b in boundaries )
        {
            if ( currentLvl >= b.Level ) currentMult = b.Multiplier;
        }

        while ( remainingCount > 0 )
        {
            int nextBoundaryLvl = int.MaxValue;
            double nextMult = currentMult;

            foreach ( var b in boundaries )
            {
                if ( b.Level > currentLvl )
                {
                    nextBoundaryLvl = b.Level;
                    nextMult = b.Multiplier;
                    break;
                }
            }

            int levelsToThreshold = nextBoundaryLvl - currentLvl;
            int currentChunk = Math.Min( remainingCount, levelsToThreshold );

            double rawCost = GetRawBulkCost( def.ShopProgression, def.ShopParams, currentLvl, currentChunk );

            totalCost += rawCost * currentMult;

            currentLvl += currentChunk;
            remainingCount -= currentChunk;
            currentMult = nextMult;
        }

        return totalCost;
    }

    // =============================================================
    // ГЕНЕРИЧНІ МАТЕМАТИЧНІ МЕТОДИ O(1) БЕЗ ПОРОГІВ (УНІВЕРСАЛЬНІ)
    // =============================================================
    private static double GetRawBulkCost( ProgressionType type, double[] p, int fromLevel, int count )
    {
        if ( p == null || p.Length < 2 ) return 0;
        double baseCost = p[0];
        double step = p[1]; // У разі Exponential це множник (Multiplier)

        // ОПТИМІЗАЦІЯ: Зсуваємо рівень на -1, щоб перший платний апгрейд (з LVL 1 на LVL 2)
        // коштував рівно BaseCost (як при 0-му рівні у формулі)
        int adjustedLvl = Math.Max( 0, fromLevel - 1 );

        return type switch
        {
            ProgressionType.Linear => ProgressionMath.GetLinearBulkCost( baseCost, step, adjustedLvl, count ),
            ProgressionType.Quadratic => ProgressionMath.GetQuadraticBulkCost( baseCost, step, adjustedLvl, count ),
            ProgressionType.Exponential => ProgressionMath.GetGeometricBulkCost( baseCost, step, adjustedLvl, count ),
            _ => 0
        };
    }

    private static int GetRawAffordableLevels( ProgressionType type, double[] p, int fromLevel, double playerGold )
    {
        if ( p == null || p.Length < 2 ) return 0;
        double baseCost = p[0];
        double step = p[1];

        // ОПТИМІЗАЦІЯ: Зсуваємо рівень на -1
        int adjustedLvl = Math.Max( 0, fromLevel - 1 );

        return type switch
        {
            ProgressionType.Linear => ProgressionMath.GetLinearAffordableLevels( baseCost, step, adjustedLvl, playerGold ),
            ProgressionType.Quadratic => ProgressionMath.GetQuadraticAffordableLevels( baseCost, step, adjustedLvl, playerGold ),
            ProgressionType.Exponential => ProgressionMath.GetGeometricAffordableLevels( baseCost, step, adjustedLvl, playerGold ),
            _ => 0
        };
    }

    // =============================================================
    // ЗВИЧАЙНИЙ МАГАЗИН ВЕЖІ (Залишено для сумісності застарілих викликів)
    // =============================================================
    public static double GetBulkCost( double baseCost, double multiplier, int fromLevel, int count )
    {
        return ProgressionMath.GetGeometricBulkCost( baseCost, multiplier, fromLevel, count );
    }

    public static int GetAffordableLevels( double baseCost, double multiplier, int fromLevel, double playerGold )
    {
        return ProgressionMath.GetGeometricAffordableLevels( baseCost, multiplier, fromLevel, playerGold );
    }

    // =============================================================
    // СУМІСНІСТЬ ПІСЛЯ ВИДАЛЕННЯ UNLOCK/UPGRADE FORMULAS
    // =============================================================
    public static double GetShopItemUnlockCost( ShopTab tab, StatType stat )
    {
        return StatRegistry.Get( stat ).UnlockCost;
    }

    public static double GetBaseCostPerUnit( StatType stat, bool isMultiplier )
    {
        return StatRegistry.Get( stat ).CardParams[0];
    }

    // =============================================================
    // УНІКАЛЬНА ПРОГРЕСІЯ ВАГИ КАРТОК (CARD WEIGHTS)
    // =============================================================
    public static double GetWeightBulkCost(
        UpgradeResource.CardRarity rarity,
        int fromLevel,
        int count,
        double? customBaseCost = null,
        double? customMultiplier = null
    )
    {
        double baseCost = customBaseCost ?? GetWeightBaseCost( rarity );
        double multiplier = customMultiplier ?? GetWeightCostMultiplier( rarity );

        return ProgressionMath.GetGeometricBulkCost( baseCost, multiplier, fromLevel, count );
    }

    public static int GetWeightAffordableLevels(
        UpgradeResource.CardRarity rarity,
        int fromLevel,
        double playerGold,
        double? customBaseCost = null,
        double? customMultiplier = null
    )
    {
        double baseCost = customBaseCost ?? GetWeightBaseCost( rarity );
        double multiplier = customMultiplier ?? GetWeightCostMultiplier( rarity );

        return ProgressionMath.GetGeometricAffordableLevels( baseCost, multiplier, fromLevel, playerGold );
    }

    public static double GetCardWeightStep( UpgradeResource.CardRarity rarity, double? customStep = null )
    {
        if ( customStep.HasValue ) return customStep.Value;

        return rarity switch
        {
            UpgradeResource.CardRarity.Common => 5.0,
            UpgradeResource.CardRarity.Uncommon => 10.0,
            UpgradeResource.CardRarity.Rare => 25.0,
            UpgradeResource.CardRarity.Epic => 50.0,
            UpgradeResource.CardRarity.Legendary => 100.0,
            _ => 5.0
        };
    }

    public static double GetWeightBaseCost( UpgradeResource.CardRarity rarity ) => rarity switch
    {
        UpgradeResource.CardRarity.Common => 100.0,
        UpgradeResource.CardRarity.Uncommon => 250.0,
        UpgradeResource.CardRarity.Rare => 500.0,
        UpgradeResource.CardRarity.Epic => 1000.0,
        UpgradeResource.CardRarity.Legendary => 2500.0,
        _ => 500.0
    };

    public static double GetWeightCostMultiplier( UpgradeResource.CardRarity rarity ) => rarity switch
    {
        UpgradeResource.CardRarity.Common => 1.12,
        UpgradeResource.CardRarity.Uncommon => 1.15,
        UpgradeResource.CardRarity.Rare => 1.18,
        UpgradeResource.CardRarity.Epic => 1.22,
        UpgradeResource.CardRarity.Legendary => 1.26,
        _ => 1.15
    };

    // =============================================================
    // ІНШІ СИСТЕМНІ МЕТОДИ (Рідкості, Максимальні рівні, Ліміти слотів)
    // =============================================================
    public static int GetCardStatMaxLevel( StatType stat ) => stat switch
    {
        StatType.Luck => 10,
        _ => 999999
    };

    public static int GetMaxSlotsForRarity( UpgradeResource.CardRarity rarity ) => rarity switch
    {
        UpgradeResource.CardRarity.Common => 1,
        UpgradeResource.CardRarity.Uncommon => 2,
        UpgradeResource.CardRarity.Rare => 3,
        UpgradeResource.CardRarity.Epic => 4,
        UpgradeResource.CardRarity.Legendary => 5,
        _ => 6
    };

    public static double GetRarityUpgradeCost( UpgradeResource.CardRarity currentRarity ) => currentRarity switch
    {
        UpgradeResource.CardRarity.Common => 1000.0,
        UpgradeResource.CardRarity.Uncommon => 2500.0,
        UpgradeResource.CardRarity.Rare => 5000.0,
        UpgradeResource.CardRarity.Epic => 10000.0,
        _ => 999999.0
    };

    public static UpgradeResource.CardRarity GetNextRarity( UpgradeResource.CardRarity current ) => current switch
    {
        UpgradeResource.CardRarity.Common => UpgradeResource.CardRarity.Uncommon,
        UpgradeResource.CardRarity.Uncommon => UpgradeResource.CardRarity.Rare,
        UpgradeResource.CardRarity.Rare => UpgradeResource.CardRarity.Epic,
        UpgradeResource.CardRarity.Epic => UpgradeResource.CardRarity.Legendary,
        _ => current
    };

    public static UpgradeResource.CardRarity GetPreviousRarity( UpgradeResource.CardRarity current ) => current switch
    {
        UpgradeResource.CardRarity.Uncommon => UpgradeResource.CardRarity.Common,
        UpgradeResource.CardRarity.Rare => UpgradeResource.CardRarity.Uncommon,
        UpgradeResource.CardRarity.Epic => UpgradeResource.CardRarity.Rare,
        UpgradeResource.CardRarity.Legendary => UpgradeResource.CardRarity.Epic,
        _ => current
    };
}