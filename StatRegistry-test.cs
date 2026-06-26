using System;
using System.Collections.Generic;

public struct PriceThreshold
{
    public double ValueThreshold { get; set; }
    public double Multiplier { get; set; }

    public PriceThreshold( double value, double mult )
    {
        ValueThreshold = value;
        Multiplier = mult;
    }
}

public class StatDefinition
{
    public StatType Type { get; set; }
    public string FriendlyName { get; set; }
    public string FlatUnit { get; set; }
    public ShopTab Tab { get; set; } = ShopTab.Tower; // Вкладка за замовчуванням

    public ProgressionType CardProgression { get; set; } = ProgressionType.Linear;
    public double[] CardParams { get; set; } = new double[] { 10.0, 1.0 };

    public ProgressionType ShopProgression { get; set; } = ProgressionType.Linear;
    public double[] ShopParams { get; set; } = new double[] { 1.0, 10.0 };

    public double UnlockCost { get; set; }
    public List<PriceThreshold> PriceThresholds { get; set; } = new();
}

public static class StatRegistry
{
    public static readonly Dictionary<StatType, StatDefinition> Stats = new();
    private static bool _initialized = false;

    /// <summary>
    /// Безпечний запуск ініціалізації в рантаймі (захист від гарячого перезавантаження S&box)
    /// </summary>
    public static void Initialize()
    {
        if ( _initialized && Stats.Count > 0 ) return;
        _initialized = true;

        Stats.Clear();

        // 1. Damage (Урон) -> i = 1
        Register( new StatDefinition
        {
            Type = StatType.Damage,
            FriendlyName = "Damage",
            FlatUnit = "dmg",
            UnlockCost = 10.0,
            CardProgression = ProgressionType.Linear,
            CardParams = new double[] { 10.0, 1.0 },
            ShopProgression = ProgressionType.Linear,
            ShopParams = new double[] { 1.0, 10.0 }
        } );

        // 2. Fire Rate (Швидкість атаки) -> i = 2
        Register( new StatDefinition
        {
            Type = StatType.FireRate,
            FriendlyName = "Fire Rate",
            FlatUnit = "a/s",
            UnlockCost = 20.0,
            CardProgression = ProgressionType.Linear,
            CardParams = new double[] { 20.0, 2.0 },
            ShopProgression = ProgressionType.Linear,
            ShopParams = new double[] { 2.0, 20.0 }
        } );

        // 3. Crit Chance (Крит) -> i = 3
        Register( new StatDefinition
        {
            Type = StatType.CritChance,
            FriendlyName = "Critical Chance",
            FlatUnit = "%",
            UnlockCost = 30.0,
            CardProgression = ProgressionType.Linear,
            CardParams = new double[] { 30.0, 3.0 },
            ShopProgression = ProgressionType.Linear,
            ShopParams = new double[] { 3.0, 30.0 },
            PriceThresholds = new List<PriceThreshold>
            {
                new PriceThreshold( 50.0, 3.0 ),
                new PriceThreshold( 100.0, 10.0 )
            }
        } );

        // 4. Projectile Speed (Швидкість куль) -> i = 4
        Register( new StatDefinition
        {
            Type = StatType.ProjectileSpeed,
            FriendlyName = "Bullet Speed",
            FlatUnit = "u/s",
            UnlockCost = 40.0,
            CardProgression = ProgressionType.Linear,
            CardParams = new double[] { 40.0, 4.0 },
            ShopProgression = ProgressionType.Linear,
            ShopParams = new double[] { 4.0, 40.0 }
        } );

        // 5. Extra Bullets (Додаткові кулі) -> i = 5
        Register( new StatDefinition
        {
            Type = StatType.ExtraBullets,
            FriendlyName = "Extra Bullets",
            FlatUnit = "pcs",
            UnlockCost = 50.0,
            CardProgression = ProgressionType.Linear,
            CardParams = new double[] { 50.0, 5.0 },
            ShopProgression = ProgressionType.Linear,
            ShopParams = new double[] { 5.0, 50.0 }
        } );

        // 6. Pierce Count (Пробиття) -> i = 6
        Register( new StatDefinition
        {
            Type = StatType.PierceCount,
            FriendlyName = "Pierce Count",
            FlatUnit = "pcs",
            UnlockCost = 60.0,
            CardProgression = ProgressionType.Linear,
            CardParams = new double[] { 60.0, 6.0 },
            ShopProgression = ProgressionType.Linear,
            ShopParams = new double[] { 6.0, 60.0 }
        } );

        // 7. Radius (Радіус вибуху) -> i = 7
        Register( new StatDefinition
        {
            Type = StatType.Radius,
            FriendlyName = "Blast Radius",
            FlatUnit = "units",
            UnlockCost = 70.0,
            CardProgression = ProgressionType.Linear,
            CardParams = new double[] { 70.0, 7.0 },
            ShopProgression = ProgressionType.Linear,
            ShopParams = new double[] { 7.0, 70.0 }
        } );

        // 8. Bullet Range (Дальність) -> i = 8
        Register( new StatDefinition
        {
            Type = StatType.BulletRange,
            FriendlyName = "Bullet Range",
            FlatUnit = "units",
            UnlockCost = 80.0,
            CardProgression = ProgressionType.Linear,
            CardParams = new double[] { 80.0, 8.0 },
            ShopProgression = ProgressionType.Linear,
            ShopParams = new double[] { 8.0, 80.0 }
        } );

        // 9. Turn Speed (Швидкість повороту) -> i = 9
        Register( new StatDefinition
        {
            Type = StatType.TurnSpeed,
            FriendlyName = "Turn Speed",
            FlatUnit = "°/s",
            UnlockCost = 90.0,
            CardProgression = ProgressionType.Linear,
            CardParams = new double[] { 90.0, 9.0 },
            ShopProgression = ProgressionType.Linear,
            ShopParams = new double[] { 9.0, 90.0 }
        } );

        // 10. Gold Gain (Золото) -> i = 10
        Register( new StatDefinition
        {
            Type = StatType.GoldGain,
            FriendlyName = "Gold Gain",
            FlatUnit = "gp",
            UnlockCost = 100.0,
            CardProgression = ProgressionType.Linear,
            CardParams = new double[] { 100.0, 10.0 },
            ShopProgression = ProgressionType.Linear,
            ShopParams = new double[] { 10.0, 100.0 }
        } );

        // 11. XP Gain (Досвід) -> i = 11
        Register( new StatDefinition
        {
            Type = StatType.XPGain,
            FriendlyName = "XP Gain",
            FlatUnit = "xp",
            UnlockCost = 110.0,
            CardProgression = ProgressionType.Linear,
            CardParams = new double[] { 110.0, 11.0 },
            ShopProgression = ProgressionType.Linear,
            ShopParams = new double[] { 11.0, 110.0 }
        } );

        // 12. Luck (Удача) -> i = 12
        Register( new StatDefinition
        {
            Type = StatType.Luck,
            FriendlyName = "Luck",
            FlatUnit = "pts",
            UnlockCost = 120.0,
            CardProgression = ProgressionType.Linear,
            CardParams = new double[] { 120.0, 12.0 },
            ShopProgression = ProgressionType.Linear,
            ShopParams = new double[] { 12.0, 120.0 }
        } );

        // 13. ExtraCardChoices (Вибір карт) -> i = 13
        Register( new StatDefinition
        {
            Type = StatType.ExtraCardChoices,
            FriendlyName = "Card Choices",
            FlatUnit = "pcs",
            UnlockCost = 130.0,
            CardProgression = ProgressionType.Linear,
            CardParams = new double[] { 130.0, 13.0 },
            ShopProgression = ProgressionType.Linear,
            ShopParams = new double[] { 13.0, 130.0 }
        } );

        // 14. Enemy Health (Здоров'я ворогів) -> i = 14
        Register( new StatDefinition
        {
            Type = StatType.EnemyHealth,
            FriendlyName = "Enemy Health",
            FlatUnit = "hp",
            UnlockCost = 140.0,
            CardProgression = ProgressionType.Linear,
            CardParams = new double[] { 140.0, 14.0 },
            ShopProgression = ProgressionType.Linear,
            ShopParams = new double[] { 14.0, 140.0 }
        } );

        // 15. Enemy Spawn Rate (Спавн ворогів) -> i = 15
        Register( new StatDefinition
        {
            Type = StatType.EnemySpawnRate,
            FriendlyName = "Enemy Spawn Rate",
            FlatUnit = "/s",
            UnlockCost = 150.0,
            CardProgression = ProgressionType.Linear,
            CardParams = new double[] { 150.0, 15.0 },
            ShopProgression = ProgressionType.Linear,
            ShopParams = new double[] { 15.0, 150.0 }
        } );

        // 16. Enemy Speed (Швидкість ворогів) -> i = 16
        Register( new StatDefinition
        {
            Type = StatType.EnemySpeed,
            FriendlyName = "Enemy Speed",
            FlatUnit = "u/s",
            UnlockCost = 160.0,
            CardProgression = ProgressionType.Linear,
            CardParams = new double[] { 160.0, 16.0 },
            ShopProgression = ProgressionType.Linear,
            ShopParams = new double[] { 16.0, 160.0 }
        } );

        // 17. Difficulty (Складність) -> i = 17
        Register( new StatDefinition
        {
            Type = StatType.Difficulty,
            FriendlyName = "Difficulty",
            FlatUnit = "lvl",
            UnlockCost = 170.0,
            CardProgression = ProgressionType.Linear,
            CardParams = new double[] { 170.0, 17.0 },
            ShopProgression = ProgressionType.Linear,
            ShopParams = new double[] { 17.0, 170.0 }
        } );

        // Сортуємо пороги
        foreach ( var def in Stats.Values )
        {
            def.PriceThresholds.Sort( ( a, b ) => a.ValueThreshold.CompareTo( b.ValueThreshold ) );
        }
    }

    private static void Register( StatDefinition def ) => Stats[def.Type] = def;

    public static StatDefinition Get( StatType type )
    {

        // ЯКЩО ГРА ЗАПУЩЕНА В РЕДАКТОРІ — примусово скидаємо ініціалізацію при кожному запиті стату!
        // Це миттєво оминає оптимізацію IL Hotload двигуна та оновлює ціни при збереженні C# коду.
        if ( Sandbox.Game.IsEditor )
        {
            _initialized = false;
        }
        // Перед кожним читаннням перевіряємо, чи словник ініціалізований
        Initialize();

        if ( Stats.TryGetValue( type, out var def ) )
        {
            if ( def.CardParams == null || def.CardParams.Length < 2 )
            {
                def.CardParams = new double[] { 10.0, 1.0 };
            }

            if ( def.ShopParams == null || def.ShopParams.Length < 2 )
            {
                def.ShopParams = new double[] { 1.0, 10.0 };
            }

            return def;
        }

        return new StatDefinition
        {
            Type = type,
            FriendlyName = type.ToString(),
            FlatUnit = "units",
            UnlockCost = 9999.0,
            CardProgression = ProgressionType.Linear,
            CardParams = new double[] { 99.0, 9.0 },
            ShopProgression = ProgressionType.Linear,
            ShopParams = new double[] { 9.0, 99.0 }
        };
    }

    /* // Використовуємо повне ім'я класу атрибута для обходу обмежень Sandbox.Event
    [Event( "hotloaded" )]
    public static void OnHotload()
    {
        Initialize( true ); // Примусово перезавантажуємо з параметром force = true
        Log.Info( "⚙️ StatRegistry: Баланс успішно оновлено після Hot-Reload!" );
    } */
}