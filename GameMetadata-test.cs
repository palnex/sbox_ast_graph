using Sandbox;
using System;

public static class GameMetadata
{
    // =============================================================
    // ФОРМАТУВАННЯ ЧИСЕЛ (1K / 1M / 1B / 1T / 1Qa ...)
    // =============================================================
    public static string FormatNumber( double value, string format = "0.#" )
    {
        if ( value >= 1000000000000000 ) return $"{(value / 1000000000000000.0).ToString( "0.#" )}Qa"; // Квадрильйони
        if ( value >= 1000000000000 ) return $"{(value / 1000000000000.0).ToString( "0.#" )}T";   // Трильйони
        if ( value >= 1000000000 ) return $"{(value / 1000000000.0).ToString( "0.#" )}B";      // Мільярди
        if ( value >= 1000000 ) return $"{(value / 1000000.0).ToString( "0.#" )}M";         // Мільйони
        if ( value >= 1000 ) return $"{(value / 1000.0).ToString( "0.#" )}K";            // Тисячі
        return value.ToString( format );
    }

    // =============================================================
    // ЛОКАЛІЗАЦІЯ ТА НАЗВИ ЧЕРЕЗ РЕЄСТР (БЕЗ SWITCH!)
    // =============================================================
    public static string GetFriendlyStatName( StatType type )
    {
        return StatRegistry.Get( type ).FriendlyName;
    }

    public static string GetFlatUnitName( StatType type )
    {
        return StatRegistry.Get( type ).FlatUnit;
    }

    public static string GetFormattedUnit( StatType type, bool isMultiplier, bool showDetailed = false )
    {
        if ( isMultiplier ) return " %";

        var def = StatRegistry.Get( type );

        if ( showDetailed )
        {
            return type switch
            {
                StatType.Damage => " damage",
                StatType.FireRate => " attacks per second",
                StatType.ProjectileSpeed => " units per second",
                StatType.TurnSpeed => " degrees per second",
                _ => " " + def.FlatUnit
            };
        }

        return " " + def.FlatUnit;
    }

    public static string GetTabFriendlyName( ShopTab tab )
    {
        return tab switch
        {
            ShopTab.Tower => "🧱 TOWER",
            ShopTab.Cursor => "🎯 CURSOR",
            ShopTab.Chances => "🎲 CHANCES",
            ShopTab.Game => "⚙️ WORLD",
            ShopTab.Ally => "🤖 ALLIES",
            ShopTab.Cards => "🃏 CARDS",
            _ => tab.ToString()
        };
    }

    public static string GetRarityNameByLevel( int level )
    {
        return level switch
        {
            1 => "Common",
            2 => "Uncommon",
            3 => "Rare",
            4 => "Epic",
            5 => "Legendary",
            6 => "Ethereal",
            7 => "Godless",
            _ => "Common"
        };
    }
}