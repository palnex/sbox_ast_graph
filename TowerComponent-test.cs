using Sandbox;
using System;
public enum TargetBase
{
	Closest,   // Кит 1: Найближчий
	Furthest,  // Кит 2: Найвіддаленіший
	Random     // Кит 3: Випадковий
}
public enum BulletFormation
{
	Arc,        // Віяло (під кутом, як дробовик)
	Column,     // Один за одним (Спартанці в ряд)
	SideBySide  // Пліч-о-пліч (Широка лінія)
}
public enum TargetFilter
{
	None,           // Без фільтра (просто б'ємо по дистанції)
	HighestHealth,  // Пріоритет на товстих
	LowestHealth    // Пріоритет на добивання
}
public struct VirtualBullet
{
	public Particle VisualParticle;
	public Vector3 Position;
	public Vector3 Direction;
	public float Speed;
	public float Damage;
	public float DistanceTraveled;
	public float MaxDistance;
	public bool CanPierce;
}
public sealed class TowerComponent : Component, Component.ICollisionListener
{
	// PUBLIC VARS ====================================================//
	#region PUBLIC VARS
	// group assets -------------------------<>

	/// <summary>
	/// Prefab of the bullet to spawn when shooting.
	/// </summary>
	//[Property, Group( "Assets" ), Title( "🚀 Bullet Prefab" )]
	//public GameObject Bullet { get; set; }
	[Property, Group( "Assets" ), Title( "✨ Bullet Particle (На Muzzle)" )]
	public ParticleEffect BulletParticles { get; set; }
	[Property, Group( "Assets" ), Title( "💥 Ефект попадання (Prefab)" )]
	public GameObject HitImpactPrefab { get; set; }

	/// <summary>
	/// The rotating part of the tower (usually the barrel or top half).
	/// </summary>
	[Property, Group( "Assets" ), Title( "🔫 Gun Model" )]
	public GameObject Gun { get; set; }

	/// <summary>
	/// The exact spawn point for bullets (should be placed at the tip of the Gun).
	/// </summary>
	[Property, Group( "Assets" ), Title( "🔥 Muzzle Spawn" )]
	public GameObject Muzzle { get; set; }


	// group attack -------------------------<>

	/// <summary>
	/// The detection radius for finding enemies (Units).
	/// </summary>
	[Property, Group( "Attack" ), Title( "📡 Radius" )]
	[Range( 100f, 2000f ), Step( 50f )] // Слайдер для радіуса з кроком 50
	public float Radius { get; set; } = 500f;

	/// <summary>
	/// How many attacks (shoots/bullets) occur per second (Attacks/Second).
	/// </summary>
	[Property, Group( "Attack" ), Title( "⏱️ Attacks Per Second" )]
	[Range( 0.1f, 20f ), Step( 0.1f )] // Від дуже повільного до кулемета
	public float AttacksPerSecond { get; set; } = 2.0f;

	/// <summary>
	/// How fast the gun rotates towards the target (Degrees/Second).
	/// </summary>
	[Property, Group( "Attack" ), Title( "🔄 Turn Speed" )]
	[Range( 10f, 720f ), Step( 10f )] // До двох обертів за секунду
	public float TurnSpeed { get; set; } = 100f;

	/// <summary>
	/// Maximum distance the bullet can travel before being destroyed (Units).
	/// </summary>
	[Property, Group( "Attack" ), Title( "📏 Bulelt Range" )]
	public float BulletRange { get; set; } = 1000f;

	/// <summary>
	/// The velocity of the fired bullet (Units/Second).
	/// </summary>
	[Property, Group( "Attack" ), Title( "💨 Bullet Speed" )]
	public float BulletSpeed { get; set; } = 600f;

	/// <summary>
	/// Physical weight of the bullet used for physics impacts (Mass).
	/// </summary>
	[Property, Group( "Attack" ), Title( "⚖️ Bullet Mass" )]
	public float BulletMass { get; set; } = 5f;

	[Property, Group( "Attack" ), Title( "🏹 Can Pierce" )]
	public bool CanPierce { get; set; } = true;

	//----------------------------------<>
	// Замість одного Muzzle, ми робимо список. 
	// Спочатку там є тільки дуло, що дивиться вперед. 
	// Коли гравець купує апгрейд "Стрільба назад", ти просто додаєш сюди ще один GameObject!
	[Property, Group( "Assets" ), Title( "🔥 Active Muzzles" )]
	public List<GameObject> ActiveMuzzles { get; set; } = new();

	// === НАЛАШТУВАННЯ ФОРМАЦІЇ ===
	[Property, Group( "Attack Setup" ), Title( "📐 Формація Мульти-пострілу" )]
	public BulletFormation Formation { get; set; } = BulletFormation.Column;

	[Property, Group( "Attack Setup" ), Title( "📏 Відстань між кулями" )]
	[ShowIf( "Formation", BulletFormation.Column )]
	[ShowIf( "Formation", BulletFormation.SideBySide )]
	public float SpacingDistance { get; set; } = 30f;

	/// <summary>
	/// The angle spread of the bullets in degrees (e.g., 90 for a cone, 360 for a full circle).
	/// </summary>
	[Property, Group( "Attack" ), Title( "🌪️ Spread Angle" )]
	[Range( 0f, 360f ), Step( 1f )]
	public float SpreadAngle { get; set; } = 0f;
	/// <summary>
	/// How many bullets are fired in a single shot (Shotgun/Nova effect).
	/// </summary>
	[Property, Group( "Attack" ), Title( "🔢 Bullets Per Shot" )]
	[Range( 1, 100 ), Step( 1f )]
	public int BulletsPerShot { get; set; } = 1;


	// group behavior -------------------------<>

	/// <summary>
	/// If true, the tower shoots while turning. If false, it waits for perfect aim.
	/// </summary>
	[Property, Group( "Behavior" ), Title( "🌪️ Shoot While Turning" )]
	public bool ShootWhileTurning { get; set; } = false;

	/// <summary>
	/// The allowable angle error to consider the gun "aimed" at the target (Degrees).
	/// </summary>
	[Property, Group( "Behavior" ), Title( "📐 Aim Tolerance" )]
	[Range( 1f, 45f ), Step( 1f )] // Стрибає строго по 1 градусу
	[ShowIf( "ShootWhileTurning", false )]
	public float AimTolerance { get; set; } = 1f;


	// group damage -------------------------<>

	/// <summary>
	/// Magic damage dealt to the target upon impact (HP).
	/// </summary>
	[Property, Group( "Damage" ), Title( "✨ Magic Damage" )]
	public float MagicDamage { get; set; } = 5f;

	/// <summary>
	/// Physical damage dealt to the target upon impact (HP).
	/// </summary>
	[Property, Group( "Damage" ), Title( "⚔️ Physical Damage" )]
	public float PhysicalDamage { get; set; } = 0f;


	// group targeting -------------------------<>

	/// <summary>
	/// The primary logic for selecting a target (e.g., Closest, Furthest, Random).
	/// </summary>
	[Property, Group( "Targeting" ), Title( "🎯 Base Aim" )]
	public TargetBase BaseAim { get; set; } = TargetBase.Closest;

	/// <summary>
	/// Secondary filter for target selection (e.g., Highest Health).
	/// </summary>
	[Property, Group( "Targeting" ), Title( "🔍 Filter" )]
	public TargetFilter Filter { get; set; } = TargetFilter.None;

	/// <summary>
	/// If true, the tower won't switch targets until the current one is dead or leaves the radius.
	/// </summary>
	[Property, Group( "Targeting" ), Title( "🔒 Stick To Target" )]
	public bool StickToTarget { get; set; } = false;


	public static TowerComponent Instance { get; private set; }
	private UnitComponent _myUnit; // Компонент ХП самої Башти
	#endregion
	// PRIVATE VARS ====================================================//

	//private TimeSince _timeSinceTargetSearch = 0; // IF enemy so fast then this is the reason why tower doesnt shoot
	private List<VirtualBullet> _activeBullets = new();
	private SwarmUnitHandle _currentTarget = SwarmUnitHandle.Invalid;
	private TimeSince _timeShoot = 0;
	protected override void OnStart()
	{
		// Зберігаємо посилання на себе для інших скриптів
		Instance = this;

		// Шукаємо UnitComponent на собі
		_myUnit = Components.Get<UnitComponent>();

		// Синхронізуємо ХП з GameManager
		if ( _myUnit != null && GameManager.Instance != null )
		{
			// Беремо глобальне ХП і ставимо Башті
			_myUnit.MaxHealth = GameManager.Instance.GlobalMaxHealth;
			_myUnit.Health = _myUnit.MaxHealth;
		}
		// === АВТО-РЕЄСТРАЦІЯ ЦІЛІ ДЛЯ РОЮ ===
		if ( SwarmManager.Instance != null )
		{
			SwarmManager.Instance.Target = GameObject;
			Log.Info( "🎯 Вежа успішно зареєструвала себе як ціль для Рою!" );
		}
	}
	protected override void OnUpdate()
	{

		FindTarget();
		bool isAimed = GunTurn();
		if ( ShootWhileTurning || isAimed )
		{
			Shoot();
		}

		//DrawRadiusDebug();
	}
	protected override void OnFixedUpdate()
	{
		UpdateVirtualBullets();
	}

	public void OnCollisionStart( Collision collision )
	{
		/* 
				//Log.Info( "HEY I COLDED SEMPAI" );
				var unit = collision.Other.GameObject.GetComponentInParent<UnitComponent>();

				if ( unit != null && unit.Team == UnitTeam.Enemy ) // IF I GOT COLLIDED
				{
					var enemyComp = unit.Components.Get<EnemyComponent>();
					if ( enemyComp != null && enemyComp.MyPrefab != null )
					{
						ObjectPool.Instance.Return( unit.GameObject, enemyComp.MyPrefab );
					}
					else
					{
						unit.GameObject.Destroy(); // Запобіжник
					}
				}
				 */
	}

	private void Shoot()
	{
		float finalAps = (float)GameManager.Instance.GlobalFireRate;
		finalAps = Math.Max( finalAps, 0.05f );
		float cooldown = 1.0f / finalAps;

		// 1. Рахуємо, скільки пострілів ми "заборгували" за цей кадр
		int shotsOwed = (int)(_timeShoot / cooldown);

		// Якщо ще не час стріляти — виходимо
		if ( shotsOwed == 0 ) return;

		// 2. Віднімаємо витрачений час (залишок перейде на наступний кадр)
		_timeShoot -= shotsOwed * cooldown;

		int totalBullets = GameManager.Instance.GlobalExtraBullets;

		// 3. Стріляємо, але тепер ПЕРЕДАЄМО КІЛЬКІСТЬ "БОРГУ" як множник урону!
		foreach ( var muzzle in ActiveMuzzles )
		{
			if ( muzzle == null ) continue;
			SpawnProjectiles( muzzle, totalBullets, shotsOwed ); // <--- Додали shotsOwed
		}

		if ( BaseAim == TargetBase.Random && !StickToTarget )
		{
			_currentTarget = SwarmUnitHandle.Invalid;
		}
	}
	private void SpawnProjectiles( GameObject muzzle, int count, int damageMultiplier )
	{
		Vector3 basePos = muzzle.WorldPosition;
		Rotation baseRot = muzzle.WorldRotation;

		for ( int i = 0; i < count; i++ )
		{
			Vector3 spawnPos = basePos;
			Rotation spawnRot = baseRot;

			if ( count > 1 )
			{
				switch ( Formation )
				{
					case BulletFormation.Column:
						spawnPos = basePos - (baseRot.Forward * (i * SpacingDistance));
						break;
					case BulletFormation.SideBySide:
						float lateralOffset = (i - (count - 1) / 2f) * SpacingDistance;
						spawnPos = basePos + (baseRot.Right * lateralOffset);
						break;
					case BulletFormation.Arc:
						float angle = -SpreadAngle / 2f + (i * (SpreadAngle / (count - 1)));
						spawnRot = baseRot * Rotation.From( 0, angle, 0 );
						break;
				}
			}

			Vector3 flyDirection = spawnRot.Forward;
			float bulletSpeed = (float)GameManager.Instance.GlobalBulletSpeed;
			float lifeTime = GameManager.Instance.GlobalBulletRange / bulletSpeed;

			Particle spawnedParticle = null;
			if ( BulletParticles != null )
			{
				spawnedParticle = BulletParticles.Emit( spawnPos, 1.0f );
				if ( spawnedParticle != null )
				{
					spawnedParticle.Velocity = flyDirection * bulletSpeed;
					spawnedParticle.Size = 5f;
					spawnedParticle.Alpha = 1f;
					spawnedParticle.DeathTime = Time.Now + lifeTime;
				}
			}

			// РАХУЄМО ФІНАЛЬНИЙ УРОН ЦІЄЇ "МЕГА-КУЛІ"
			float baseDamage = (float)GameManager.Instance.GlobalDamage;
			float finalDamage = baseDamage * damageMultiplier; // <--- МАГІЯ ТУТ!
															   //( $"[Стрільба] З дула {muzzle.Name} летить {count} куль | Урон кожної: {finalDamage} (Множник боргу: {damageMultiplier})" );
															   // Створюємо "Душу" в коді
			var vBullet = new VirtualBullet
			{
				VisualParticle = spawnedParticle,
				Position = spawnPos,
				Direction = flyDirection,
				Speed = bulletSpeed,
				Damage = finalDamage, // <--- Записуємо помножений урон
				DistanceTraveled = 0f,
				MaxDistance = GameManager.Instance.GlobalBulletRange,

				CanPierce = CanPierce
			};

			_activeBullets.Add( vBullet );
		}
	}

	private void UpdateVirtualBullets()
	{
		if ( SwarmManager.Instance == null ) return;

		for ( int i = _activeBullets.Count - 1; i >= 0; i-- )
		{
			var bullet = _activeBullets[i];
			Vector3 nextPos = bullet.Position + bullet.Direction * bullet.Speed * Time.Delta;

			bool isDestroyed = false;

			// 1. ПЕРЕВІРКА СТІН (Фізичний промінь проти оточення)
			var wallHit = Scene.Trace.Ray( bullet.Position, nextPos )
				.IgnoreGameObjectHierarchy( GameObject )
				.WithTag( "solid" ) // Стріляємо тільки по статичних стінах
				.Run();

			if ( wallHit.Hit )
			{
				isDestroyed = true;
				if ( HitImpactPrefab != null ) HitImpactPrefab.Clone( wallHit.HitPosition );
			}

			// 2. ПЕРЕВІРКА РОЮ (Математична колізія проти точок у сітці)
			if ( !isDestroyed )
			{
				// Задаємо радіус хітбоксу кулі (наприклад, 10 одиниць)
				float bulletHitboxRadius = 10f;

				if ( SwarmManager.Instance.CheckBulletCollision( nextPos, bulletHitboxRadius, out SwarmUnitHandle hitUnit ) )
				{
					isDestroyed = HandleSwarmHit( hitUnit, ref bullet, nextPos );
				}
			}

			// Знищення або переміщення кулі
			if ( isDestroyed )
			{
				if ( bullet.VisualParticle != null )
				{
					bullet.VisualParticle.DeathTime = Time.Now;
					bullet.VisualParticle.Age = 9999f;
				}
				_activeBullets.RemoveAt( i );
				continue;
			}

			bullet.Position = nextPos;
			bullet.DistanceTraveled += bullet.Speed * Time.Delta;

			if ( bullet.DistanceTraveled >= bullet.MaxDistance )
			{
				if ( bullet.VisualParticle != null )
				{
					bullet.VisualParticle.DeathTime = Time.Now;
					bullet.VisualParticle.Age = 9999f;
				}
				_activeBullets.RemoveAt( i );
			}
			else
			{
				_activeBullets[i] = bullet;
			}
		}
	}

	private bool HandleSwarmHit( SwarmUnitHandle hitUnit, ref VirtualBullet bullet, Vector3 hitPosition )
	{
		if ( SwarmManager.Instance == null || !hitUnit.IsValid ) return false;

		// Отримуємо ХП ворога до удару
		float enemyHpBeforeHit = SwarmManager.Instance.GetUnitHealth( hitUnit );
		if ( enemyHpBeforeHit <= 0 ) return false;

		// Наносимо урон ворогу
		SwarmManager.Instance.DamageUnit( hitUnit.Index, bullet.Damage );

		// Спавн ефекту попадання
		if ( HitImpactPrefab != null ) HitImpactPrefab.Clone( hitPosition );

		// Логіка пробиття наскрізь (Pierce)
		if ( bullet.CanPierce )
		{
			bullet.Damage -= enemyHpBeforeHit;
			if ( bullet.Damage <= 0 ) return true; // Куля віддала весь урон і згасла
			return false; // Куля летить далі, втративши частину урону
		}

		return true; // Куля знищується при першому ж влучанні
	}

	/* 
	private bool HandleVirtualHit( GameObject hitObj, ref VirtualBullet bullet )
	{
		if ( hitObj == null ) return false;
		var unit = hitObj.GetComponentInParent<UnitComponent>();

		if ( unit != null && unit.Team == UnitTeam.Enemy )
		{
			if ( !unit.Alive ) return false;

			double enemyHpBeforeHit = unit.Health;

			// --- ДЕБАГ 2 ---
			//Log.Info( $"[HandleHit] Час: {Time.Now} | Б'ємо об'єкт: {hitObj.Name} | Юніт: {unit.GameObject.Name} | HP до: {enemyHpBeforeHit} | Урон кулі: {bullet.Damage}" );
			// ---------------

			//double enemyHp = unit.Health;
			unit.TakeDamage( bullet.Damage );

			if ( bullet.CanPierce )
			{
				bullet.Damage -= (float)enemyHpBeforeHit;
				if ( bullet.Damage <= 0 ) return true;
				return false;
			}
			else
			{
				return true;
			}
		}
		else if ( !hitObj.Tags.Has( "bullet_ignore" ) )
		{
			return true;
		}
		return false;
	}
	*/
	private bool GunTurn()
	{
		if ( Gun == null || SwarmManager.Instance == null || !_currentTarget.IsValid )
			return false;

		// Намагаємось отримати поточну позицію нашого юніта з Рою
		if ( !SwarmManager.Instance.GetUnitPosition( _currentTarget, out Vector3 targetPos ) )
		{
			_currentTarget = SwarmUnitHandle.Invalid; // Якщо не вдалося (юніт помер) — скидаємо ціль
			return false;
		}

		// Рахуємо напрямок до позиції ворога
		Vector3 direction = (targetPos - Gun.WorldPosition).Normal;
		Rotation targetRotation = Rotation.LookAt( direction, Vector3.Up );

		targetRotation *= Rotation.From( 0, -90, 0 ); // Твоя поправка на модельку вежі

		float angleDifference = Rotation.Difference( Gun.WorldRotation, targetRotation ).Angle();

		if ( angleDifference < 0.1f )
		{
			Gun.WorldRotation = targetRotation;
			return true;
		}

		float turnSpeed = GameManager.Instance?.GlobalTurnSpeed ?? TurnSpeed;
		float step = turnSpeed * Time.Delta / angleDifference;

		Gun.WorldRotation = Rotation.Slerp( Gun.WorldRotation, targetRotation, step );

		return angleDifference <= AimTolerance;
	}

	private void FindTarget()
	{
		if ( SwarmManager.Instance == null ) return;

		float currentRadius = GameManager.Instance?.GlobalRadis ?? Radius;

		// 1. Перевірка фіксації на цілі (Lock-in / StickToTarget)
		bool forceLock = StickToTarget || BaseAim == TargetBase.Random;

		if ( forceLock && _currentTarget.IsValid && SwarmManager.Instance.IsUnitAlive( _currentTarget ) )
		{
			// Якщо ціль ще жива, перевіряємо чи вона не вийшла за радіус дії вежі
			if ( SwarmManager.Instance.GetUnitPosition( _currentTarget, out Vector3 targetPos ) )
			{
				float distSq = (targetPos - WorldPosition).LengthSquared;
				if ( distSq <= currentRadius * currentRadius )
				{
					return; // Продовжуємо тримати цю саму ціль
				}
			}
		}

		// 2. Якщо ціль недійсна або вийшла з радіуса — просимо менеджер знайти нову
		_currentTarget = SwarmManager.Instance.FindTargetInRadius(
			WorldPosition,
			currentRadius,
			BaseAim,
			Filter
		);
	}

	private UnitComponent PickBestTarget( List<UnitComponent> enemies )
	{
		// 1. Рандому плювати на ХП, тому його обробляємо одразу
		if ( BaseAim == TargetBase.Random )
		{
			return Game.Random.FromList( enemies );
		}

		// 2. Створюємо базове сортування за ФІЛЬТРОМ (ХП)
		IOrderedEnumerable<UnitComponent> sortedEnemies = null;

		if ( Filter == TargetFilter.HighestHealth )
		{
			sortedEnemies = enemies.OrderByDescending( e => e.Health );
		}
		else if ( Filter == TargetFilter.LowestHealth )
		{
			sortedEnemies = enemies.OrderBy( e => e.Health );
		}

		// 3. Додаємо "КИТІВ" (Дистанцію) через ThenBy
		if ( BaseAim == TargetBase.Closest )
		{
			// Якщо фільтр ХП був включений, ThenBy вирішить "нічию" за дистанцією.
			// Якщо фільтра не було, просто сортуємо всіх за дистанцією.
			return sortedEnemies != null
				? sortedEnemies.ThenBy( e => (e.WorldPosition - WorldPosition).LengthSquared ).First()
				: enemies.OrderBy( e => (e.WorldPosition - WorldPosition).LengthSquared ).First();
		}
		else // BaseAim == TargetBase.Furthest
		{
			return sortedEnemies != null
				? sortedEnemies.ThenByDescending( e => (e.WorldPosition - WorldPosition).LengthSquared ).First()
				: enemies.OrderByDescending( e => (e.WorldPosition - WorldPosition).LengthSquared ).First();
		}
	}
	private void DrawRadiusDebug()
	{
		if ( GameManager.Instance == null ) return;

		// Малюємо коло на підлозі
		float currentRadius = GameManager.Instance.GlobalRadis;

		// Rotation.FromPitch(90) кладе коло рівно на землю (XY площина)
		DebugOverlay.Sphere( new Sphere( WorldPosition, currentRadius ), Color.Cyan.WithAlpha( 5f ) );

		// Також можеш вивести цифру над баштою, щоб бачити точне значення
		// DebugOverlay.Text( WorldPosition + Vector3.Up * 120f, $"Radius: {currentRadius:F0}" );
	}
	public void OnCollisionUpdate( Collision collision ) { }
	public void OnCollisionStop( Collision collision ) { }
}
