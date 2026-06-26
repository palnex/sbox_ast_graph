using System;
using Sandbox;
using System.Threading.Tasks;

public sealed class GameManager : Component
{
	public static GameManager Instance { get; private set; }

	[Property, Group( "State" )]
	public GameState CurrentState { get; set; } = GameState.Playing;
	[Property, Group( "Prefabs" ), Title( "🔢 Damage Number Prefab" )]
	public GameObject DamageNumberPrefab { get; set; }

	// --- ЕКОНОМІКА ---
	[Property, Group( "Economy" ), Title( "💲 Money" ), Step( 1f )]
	public double Money { get; private set; } = 0f;

	// --- СИСТЕМА ДОСВІДУ (XP) ---
	[Property, Group( "Leveling" ), Title( "⭐ Level" )]
	public int CurrentLevel { get; set; } = 1;

	[Property, Group( "Leveling" ), Title( "✨ Current XP" )]
	public double CurrentXP { get; set; } = 0;

	[Property, Group( "Leveling" ), Title( "📈 XP Needed" )]
	public double XPToNextLevel { get; set; } = 200;

	[Property, Group( "Leveling" ), Title( "📦 Pending Levels" )]
	public int PendingLevels { get; set; } = 0;

	public Action OnLevelUp;

	// --- ДВІ КОРОБКИ ЗІ СТАТАМИ ---
	[Property, Group( "Stats Containers" ), Title( "🛒 Permanent Stats (Shop)" )]
	public PlayerStats PermanentStats { get; set; } = new();

	[Property, Group( "Stats Containers" ), Title( "🏃 Run Stats (Level Up)" )]
	public PlayerStats RunStats { get; set; } = new();

	// ==========================================
	// 1. ГЛОБАЛЬНІ СТАТИ (Правильні шляхи)
	// ==========================================

	[Property, Group( "Global Stats" ), Title( "⚔️ Global Damage" )]
	public double GlobalDamage =>
		Math.Max( 0, BaseDamage + PermanentStats.Combat.Damage.Flat + RunStats.Combat.Damage.Flat )
		* Math.Max( 0, 1.0 + PermanentStats.Combat.Damage.Multiplier + RunStats.Combat.Damage.Multiplier );

	[Property, Group( "Global Stats" ), Title( "💨 Global Bullet Speed Bonus" )]
	public double GlobalBulletSpeed =>
		Math.Max( 0, BaseBulletSpeed + PermanentStats.Combat.ProjectileSpeed.Flat + RunStats.Combat.ProjectileSpeed.Flat )
		* Math.Max( 0, 1.0 + PermanentStats.Combat.ProjectileSpeed.Multiplier + RunStats.Combat.ProjectileSpeed.Multiplier );

	[Property, Group( "Global Stats" ), Title( "🔢 Global Extra Bullets" )]
	public int GlobalExtraBullets => (int)
		(Math.Max( 0, BaseExtraBullets + PermanentStats.Combat.ExtraBullets.Flat + RunStats.Combat.ExtraBullets.Flat )
		* Math.Max( 0, 1.0 + PermanentStats.Combat.ExtraBullets.Multiplier + RunStats.Combat.ExtraBullets.Multiplier ));

	[Property, Group( "Global Stats" ), Title( "🔫 Fire Rate Multiplier" )]
	public double GlobalFireRate =>
		Math.Max( 0, BaseFireRate + PermanentStats.Combat.FireRate.Flat + RunStats.Combat.FireRate.Flat )
		* Math.Max( 0, 1.0 + PermanentStats.Combat.FireRate.Multiplier + RunStats.Combat.FireRate.Multiplier );



	[Property, Group( "Global Stats" ), Title( "🃏 Global Card Choices" )]
	public int GlobalCardChoices => (int)
		((BaseCardChoices + PermanentStats.Economy.ExtraCardChoices.Flat + RunStats.Economy.ExtraCardChoices.Flat)
		 * (1.0 + PermanentStats.Economy.ExtraCardChoices.Multiplier + RunStats.Economy.ExtraCardChoices.Multiplier));


	[Property, Group( "Global Stats" )]
	public double GlobalMaxHealth =>
		Math.Max( 0, BaseHP + PermanentStats.Body.MaxHealth.Flat + RunStats.Body.MaxHealth.Flat )
		* Math.Max( 0, 1.0 + PermanentStats.Body.MaxHealth.Multiplier + RunStats.Body.MaxHealth.Multiplier );

	[Property, Group( "Global Stats" )]
	public float GlobalRadis => (float)(
		Math.Max( 100, BaseRadius + PermanentStats.Combat.Radius.Flat + RunStats.Combat.Radius.Flat )
		* Math.Max( 0.01, 1.0 + PermanentStats.Combat.Radius.Multiplier + RunStats.Combat.Radius.Multiplier ));

	public float GlobalBulletRange => (float)(
		Math.Max( 100, BaseBulletRange + PermanentStats.Combat.BulletRange.Flat + RunStats.Combat.BulletRange.Flat )
		* Math.Max( 0.01, 1.0 + PermanentStats.Combat.BulletRange.Multiplier + RunStats.Combat.BulletRange.Multiplier ));

	public float GlobalTurnSpeed => (float)(
		Math.Max( 10, BaseTurnSpeed + PermanentStats.Combat.TurnSpeed.Flat + RunStats.Combat.TurnSpeed.Flat )
		* Math.Max( 0.01, 1.0 + PermanentStats.Combat.TurnSpeed.Multiplier + RunStats.Combat.TurnSpeed.Multiplier ));
	// ==========================================
	// time
	// ==========================================
	[Property, Group( "Global Stats / World" )] public TimeSince RunTime { get; private set; } = 0;
	[Property, Group( "Global Stats / World" )]
	public double GlobalDifficulty =>
		(BaseDificulty + PermanentStats.World.Difficulty.Flat + RunStats.World.Difficulty.Flat)
		* (1.0 + PermanentStats.World.Difficulty.Multiplier + RunStats.World.Difficulty.Multiplier);

	[Property, Group( "Global Stats / World" )]
	public double CurrentDifficulty
	{
		get
		{
			// Базова лінійна складність (твоя стара формула)
			double baseDiff = GlobalDifficulty * (1.0 + (RunTime / 60.0 * 0.5));

			if ( IsEnraged )
			{
				// Якщо час вийшов за межі EnrageTime - вмикаємо експоненту
				float timeOverLimit = RunTime - EnrageTime;

				// Формула: Базова * (Intensity в ступені секунд понад ліміт)
				// Кожна секунда після 10-ї хвилини множить складність на 1.05 (наприклад)
				return baseDiff * Math.Pow( EnrageIntensity, timeOverLimit );

			}
			return baseDiff;
		}
	}

	[Property, Group( "Difficulty Settings" )]
	public float EnrageTime { get; set; } = 600f; // 600f = 10 хвилин (в секундах)
	[Property, Group( "Difficulty Settings" )] public float EnrageIntensity { get; set; } = 1.05f; // Наскільки швидко все стає гірше

	public bool IsEnraged => RunTime >= EnrageTime;

	// ==========================================
	// test
	// ==========================================
	[Property, Group( "Base Stats" ), Feature( "Base TEST Stats" )] public double BaseDamage { get; set; } = 80.0;
	[Property, Group( "Base Stats" ), Feature( "Base TEST Stats" )] public double BaseFireRate { get; set; } = 1.0;
	[Property, Group( "Base Stats" ), Feature( "Base TEST Stats" )] public double BaseExtraBullets { get; set; } = 1.0;
	[Property, Group( "Base Stats" ), Feature( "Base TEST Stats" )] public double BaseBulletSpeed { get; set; } = 600.0;
	[Property, Group( "Base Stats" ), Feature( "Base TEST Stats" )] public double BaseDificulty { get; set; } = 1.0;
	[Property, Group( "Base Stats" ), Feature( "Base TEST Stats" )] public double BaseCardChoices { get; set; } = 1.0;
	[Property, Group( "Base Stats" ), Feature( "Base TEST Stats" )] public double BaseHP { get; set; } = 1.0;
	[Property, Group( "Base Stats" ), Feature( "Base TEST Stats" )] public double BaseRadius { get; set; } = 100.0;
	[Property, Group( "Base Stats" ), Feature( "Base TEST Stats" )] public double BaseBulletRange { get; set; } = 100.0;
	[Property, Group( "Base Stats" ), Feature( "Base TEST Stats" )] public double BaseTurnSpeed { get; set; } = 100.0;
	// ==========================================
	// test
	// ==========================================

	// ==========================================
	// shaders-etc
	// ==========================================
	public float GlobalCombatZone => (float)Math.Max(
		GlobalRadis,
		GlobalBulletRange
	);
	// ==========================================
	// shaders-etc
	// ==========================================

	public CombatStats PermanentCombat { get; set; } = new();
	public EconomyStats PermanentEconomy { get; set; } = new();
	public WorldStats PermanentWorld { get; set; } = new();


	public Action<double> OnMoneyEarned;
	public Action<double> OnMoneySpent;
	// Івенти, щоб UI та Спавнер знали, що відбувається
	public Action OnShopOpened;
	public Action OnRunStarted;

	private TimeSince _timer;

	// Словник для збереження прогресу магазину (Ключ: Назва апгрейду, Значення: Поточний рівень)
	//public Dictionary<string, int> ShopItemLevels { get; set; } = new();

	private bool IsLevelMenuOpen { get; set; } = false;
	protected override void OnAwake()
	{
		// Якщо раптом на сцені з'явиться другий такий менеджер — вбиваємо його, щоб був тільки один
		if ( Instance != null && Instance.IsValid() )
		{
			GameObject.DestroyImmediate();
			return;
		}

		Instance = this;
	}

	protected override void OnStart()
	{
		StartRun();
	}
	protected override void OnUpdate()
	{
		if ( PendingLevels > 0 && !IsLevelMenuOpen && CurrentState == GameState.Playing )
		{
			IsLevelMenuOpen = true;
			OnLevelUp?.Invoke();
		}

		if ( CurrentState == GameState.Shopping )
			RunTime = 0;
		if ( _timer >= 1f && CurrentState == GameState.Playing )
		{
			Log.Info( $"Час: {RunTime.Relative:F0} / {EnrageTime} | Enraged: {IsEnraged} | Diff: {CurrentDifficulty:F1} | Step: {EnrageIntensity}" );
			_timer = 0f;
		}

	}

	/// <summary>
	/// Метод для додавання грошей. Його будуть викликати вороги при смерті.
	/// </summary>
	public void AddMoney( double amount )
	{
		Money += amount;
		OnMoneyEarned?.Invoke( amount );
		// Пізніше тут можна буде додати звук "Дзинь!" або партикли вилітаючих монеток
		// Log.Info($"Зароблено: {amount}. Баланс: {Money}");
	}

	/// <summary>
	/// Метод для витрати грошей (на майбутнє, для магазину)
	/// </summary>
	public bool SpendMoney( double amount )
	{
		if ( Money >= amount )
		{
			Money -= amount;
			OnMoneySpent?.Invoke( amount );
			return true; // Грошей вистачило, покупка успішна
		}
		return false; // Бомж, грошей нема
	}

	public void AddXP( double amount )
	{
		CurrentXP += amount;

		// НОВЕ: Крутимо цикл, поки вистачає XP. Весь надлишок перетворюємо на рівні в черзі!
		while ( CurrentXP >= XPToNextLevel )
		{
			CurrentXP -= XPToNextLevel;
			CurrentLevel++;
			XPToNextLevel *= 1.15;

			// Додаємо рівень у чергу
			PendingLevels++;
		}
	}
	public void ConsumePendingLevel()
	{
		PendingLevels--;
		if ( PendingLevels <= 0 )
		{
			IsLevelMenuOpen = false;
		}
	}

	/// <summary>
	/// Головний оркестратор переходу в магазин: політ камери, драматична пауза та радіальний вибух у реальному часі
	/// </summary>
	public async void EnterShop()
	{
		Log.Info( "GM: Initiating Shop Transition..." );
		CurrentState = GameState.Shopping;

		// 1. Скидаємо статистику забігу
		RunStats = new PlayerStats();
		CurrentLevel = 1;
		CurrentXP = 0;
		XPToNextLevel = 2;
		PendingLevels = 0;
		IsLevelMenuOpen = false;

		// 2. Сигнал камері летіти в магазин (вона плавно сповільнить час)
		OnShopOpened?.Invoke();

		// Визначаємо епіцентр вибуху (твою вежу)
		Vector3 center = TowerComponent.Instance.IsValid() ? TowerComponent.Instance.WorldPosition.WithZ( 0 ) : Vector3.Zero;

		// 3. Запускаємо радіальні хвилі знищення безпосередньо в менеджерах
		if ( SwarmManager.Instance.IsValid() )
		{
			SwarmManager.Instance.StartRadialDisintegration( center );
		}

		if ( XPManager.Instance.IsValid() )
		{
			XPManager.Instance.StartRadialDisintegration( center );
		}

		// 4. Очищення додаткових сутностей
		if ( CursorComponent.Instance.IsValid() )
		{
			CursorComponent.Instance.ForceRespawn();
		}

		var allBullets = Scene.GetAllComponents<BulletComponent>();
		foreach ( var bullet in allBullets )
		{
			bullet.GameObject.Destroy();
		}

		// Лікуємо вежу та союзників
		var allUnits = Scene.GetAllComponents<UnitComponent>().ToList();
		foreach ( var unit in allUnits )
		{
			if ( unit.Team == UnitTeam.Tower || unit.Team == UnitTeam.Ally )
			{
				unit.FullHeal();
			}
		}

		// Чекаємо завершення транзиту камери
		await Task.DelayRealtime( 1500 );
		Scene.TimeScale = 1.0f;
	}

	/// <summary>
	/// Асинхронне вибухання окремого гобліна з індивідуальною затримкою
	/// </summary>
	private async Task DisintegrateEnemyWithDelay( UnitComponent enemy, int delayMs )
	{
		await Task.DelayRealtime( delayMs );

		if ( enemy.IsValid() )
		{
			// Тут можна спавнити твої ефекти вибуху/смерті гобліна, наприклад:
			// Particles.Create("particles/explosion.vpcf", enemy.WorldPosition);

			enemy.GameObject.Destroy();
		}
	}

	/// <summary>
	/// Запуск нової гри
	/// </summary>
	public void StartRun()
	{
		CurrentState = GameState.Playing;
		RunTime = 0;

		// ВІДНОВЛЮЄМО ЧАС ДЛЯ БОЙОВОЇ ФАЗИ
		Scene.TimeScale = 1f;

		// СПАВНИМО КУРСОР НА ПОЧАТКУ БОЮ!

		if ( CursorComponent.Instance.IsValid() )
		{
			CursorComponent.Instance.ForceRespawn();
		}

		Log.Info( "NEW WAVE STARTED! Time scale restored to normal." );
		OnRunStarted?.Invoke(); // Спавнер почує це і почне робити ворогів
	}

	/// <summary>
	/// Універсальний метод застосування стата до конкретного контейнера (PermanentStats або RunStats)
	/// </summary>
	public void ApplyStatToContainer( PlayerStats targetContainer, StatModifierData mod, double multiplierBonus = 1.0 )
	{
		// Рахуємо фінальне значення з урахуванням бонусного множника (для левелу карток)
		double finalFlat = mod.Flat * multiplierBonus;
		double finalMult = mod.Multiplier * multiplierBonus;

		switch ( mod.Stat )
		{
			// --- БОЙОВІ ---
			case StatType.Damage:
				var dmg = targetContainer.Combat.Damage;
				dmg.Flat += finalFlat;
				dmg.Multiplier += finalMult;
				targetContainer.Combat.Damage = dmg;
				break;

			case StatType.FireRate:
				var fr = targetContainer.Combat.FireRate;
				fr.Flat += finalFlat;
				fr.Multiplier += finalMult;
				targetContainer.Combat.FireRate = fr;
				break;

			case StatType.ProjectileSpeed:
				var ps = targetContainer.Combat.ProjectileSpeed;
				ps.Flat += finalFlat;
				ps.Multiplier += finalMult;
				targetContainer.Combat.ProjectileSpeed = ps;
				break;

			case StatType.ExtraBullets:
				var eb = targetContainer.Combat.ExtraBullets;
				eb.Flat += finalFlat;
				eb.Multiplier += finalMult;
				targetContainer.Combat.ExtraBullets = eb;
				break;

			case StatType.Radius:
				var rad = targetContainer.Combat.Radius;
				rad.Flat += finalFlat;
				rad.Multiplier += finalMult;
				targetContainer.Combat.Radius = rad;
				break;
			case StatType.BulletRange:
				var br = targetContainer.Combat.BulletRange;
				br.Flat += finalFlat;
				br.Multiplier += finalMult;
				targetContainer.Combat.BulletRange = br;
				break;
			case StatType.TurnSpeed:
				var ts = targetContainer.Combat.TurnSpeed;
				ts.Flat += finalFlat;
				ts.Multiplier += finalMult;
				targetContainer.Combat.TurnSpeed = ts;
				break;

			// --- ЕКОНОМІКА ---
			case StatType.GoldGain:
				var gg = targetContainer.Economy.GoldGain;
				gg.Flat += finalFlat;
				gg.Multiplier += finalMult;
				targetContainer.Economy.GoldGain = gg;
				break;

			case StatType.Luck:
				var lck = targetContainer.Economy.Luck;
				lck.Flat += finalFlat;
				lck.Multiplier += finalMult;
				targetContainer.Economy.Luck = lck;
				break;

			case StatType.ExtraCardChoices:
				var xcc = targetContainer.Economy.ExtraCardChoices;
				xcc.Flat += finalFlat;
				xcc.Multiplier += finalMult;
				targetContainer.Economy.ExtraCardChoices = xcc;
				break;
				// Додаси сюди інші стати (XPGain, EnemyHealth тощо), коли вони знадобляться

		}
	}


}
