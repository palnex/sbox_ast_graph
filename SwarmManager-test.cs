using Sandbox;
using System;
using System.Collections.Generic;

public enum GoblinAnimState : byte
{
	StartRun,   // Кадри 0 - 12 (програється 1 раз при старті бігу)
	RunLoop,    // Кадри 13 - 36 (циклічний біг)
	PreAttack,  // Кадри 36 - 52 (програється 1 раз при зупинці й першому ударі)
	AttackLoop  // Кадри 52 - 72 (циклічні удари)
}

/// <summary>
/// Дані одного юніта Рою. 
/// Це struct (Value Type), що лежить у суцільному масиві в пам'яті. Це дає максимальну швидкість для CPU.
/// </summary>
public struct SwarmUnit
{
	public bool IsAlive;
	// Додаємо версію для валідації посилань. 
	// Заміни на int для абсолютної безпеки в Endless mode, якщо в майбутньому проблеми.
	// Якщо вбивати 10 000 ворогів на секунду ushort оновлюється лише 1/5 частина всього масиву
	// (ліміт 65 535) 65535×5 секунд ≈ 327 675 секунд ≈ 91 година безперервної гри.
	// З int 340 років безперервної гри.
	public ushort Version;

	public Vector3 Position;
	public Vector3 Velocity;
	public Vector3 TargetPosition;

	// ЗБЕРЕЖЕННЯ ФІЗИКИ ДЛЯ ОПТИМІЗАЦІЇ (Обов'язково розкоментовано!)
	public Vector3 CachedSeparation;

	public float Speed;
	public float Health;
	public float MaxHealth;

	public float XPValue; // Скільки XP випадає після смерті цього юніта
	public float Scale;
	public float AttackDamage;

	public float AttackRange;
	public float NextAttackTime;
	public float AttackCooldown;

	public GoblinAnimState AnimState;
	public float CurrentFrame;
	public float AnimTimeOffset; // Зсув по часу, щоб вороги не бігли синхронно
	public float AnimSpeedMultiplier; // Швидкість індивідуальної анімації
									  // Індекс у масиві гріда
	public int CurrentGridIndex;
}
public struct SwarmUnitHandle
{
	public int Index;
	public ushort Version;

	public SwarmUnitHandle( int index, ushort version )
	{
		Index = index;
		Version = version;
	}

	public static SwarmUnitHandle Invalid => new SwarmUnitHandle( -1, 0 );
	public bool IsValid => Index >= 0;
}

public sealed class SwarmManager : Component
{
	public static SwarmManager Instance { get; private set; }

	[Property, Group( "Debug" ), ReadOnly, Description( "Кількість живих юнітів зараз" )]
	public int ActiveUnitsCount { get; private set; }

	[Property, Group( "Debug" ), ReadOnly, Description( "Вільних слотів у пулі" )]
	public int FreeSlotsCount => _freeIndices?.Count ?? 0;

	[Property, Group( "Targeting" )] public GameObject Target { get; set; }
	[Property, Group( "Targeting" )] public GameObject LogicReference { get; set; }

	[Property, Group( "LOD Settings" )] public float NearDistance { get; set; } = 800f;
	[Property, Group( "LOD Settings" )] public float FarDistance { get; set; } = 2500f;
	[Property, Group( "LOD Settings" )] public int NearUpdateRate { get; set; } = 1;
	[Property, Group( "LOD Settings" )] public int MidUpdateRate { get; set; } = 4;
	[Property, Group( "LOD Settings" )] public int FarUpdateRate { get; set; } = 12;

	[Property, Group( "Capacity" )] public int MaxUnits { get; set; } = 50000;

	[Property, Group( "Grid System" )] public float GridCellSize { get; set; } = 64f;
	[Property, Group( "Grid System" )] public int GridResolution { get; set; } = 256;
	[Property, Group( "Grid System" ), ReadOnly, Title( "🗺️ Total World Size" )]
	public string GridWorldSize => $"{GridCellSize * GridResolution} x {GridCellSize * GridResolution} units";

	[Property, Group( "Swarm Physics" )] public float SeparationForce { get; set; } = 500f;
	[Property, Group( "Swarm Physics" )] public float UnitRadius { get; set; } = 32f;
	[Property, Group( "Swarm Physics" )] public int MaxNeighborsChecked { get; set; } = 8;

	[Property, Group( "Debug Options" )] public bool ShowDebugSpheres { get; set; } = false;
	[Property, Group( "Debug Options" )] public bool ShowGrid { get; set; } = false;

	[Property, Group( "Rendering" )] public Model UnitModel { get; set; }
	[Property, Group( "Rendering" ), Title( "Base Scale" )]
	public float BaseUnitScale { get; set; } = 1.0f;

	// НОВЕ: На скільки вони можуть відрізнятись (0 = всі однакові)
	//[Property, Group( "Rendering" ), Range( 0, 1 )]
	//public float ScaleVariation { get; set; } = 0;
	[Property, Group( "Rendering" )] public float CullingMargin { get; set; } = 100f;
	[Property, Group( "Rendering" )] public float ViewWidth { get; set; } = 3000f;  // Зона видимості вліво/вправо
	[Property, Group( "Rendering" )] public float ViewHeight { get; set; } = 2000f; // Зона видимості вверх/вниз
	[Property, Group( "Rendering" )] public string PositionsPath { get; set; } = "positions_baked.bytes";
	[Property, Group( "Rendering" )] public string NormalsPath { get; set; } = "normals_baked.bytes";


	[Property, Group( "Annihilation Settings" ), Title( "⏳ Total Annihilation Duration" )]
	public float AnnihilationDuration { get; set; } = 0.6f; // Загальний час анігіляції в секундах
	[Property, Group( "Annihilation Settings" ), Title( "📈 Wave Acceleration Exponent" )]
	[Description( "1.0 = Linear. 2.0+ = Starts slow (200ms, 100ms), then accelerates rapidly." )]
	public float AnnihilationExponent { get; set; } = 2.5f;
	[Property, Group( "Annihilation Settings" ), Title( "📏 Max Annihilation Radius" )]
	public float AnnihilationMaxRadius { get; set; } = 2500f;
	// --- ДАНІ ---
	private SwarmUnit[] _units;
	private Stack<int> _freeIndices;
	private Transform[] _renderTransforms;
	private int _renderCount = 0;
	private SwarmRenderObject _renderObject;
	private int _tickCounter = 0;


	// --- anime
	// Модульні межі кадрів анімацій
	private const float RunStartMin = 0f;
	private const float RunStartMax = 12f;
	private const float RunLoopMin = 13f;
	private const float RunLoopMax = 36f;
	private const float AttackStartMin = 36f;
	private const float AttackStartMax = 52f;
	private const float AttackLoopMin = 52f;
	private const float AttackLoopMax = 72f;
	private const float VatFps = 24f; // Фреймрейт запікання VAT текстури

	private Texture _frameTexture;
	private float[] _tempFrames;


	private Texture _positionsTexture;
	private Texture _normalsTexture;
	private RenderAttributes _renderAttributes;
	// Оптимізований Грід на масиві
	private List<int>[] _gridArray;


	private bool _isDisintegrating = false;
	private Vector3 _disintegrationCenter;
	private float _disintegrationRadius = 0f;

	private float _disintegrationTimeElapsed = 0f;

	protected override void OnAwake()
	{
		Instance = this;
		_units = new SwarmUnit[MaxUnits];

		_freeIndices = new Stack<int>( MaxUnits );
		for ( int i = MaxUnits - 1; i >= 0; i-- ) _freeIndices.Push( i );

		// Ініціалізуємо масив гріда
		_gridArray = new List<int>[GridResolution * GridResolution];
		for ( int i = 0; i < _gridArray.Length; i++ ) _gridArray[i] = new List<int>( 16 );

		_renderTransforms = new Transform[MaxUnits];
		if ( Scene.SceneWorld != null )
		{
			_renderObject = new SwarmRenderObject( Scene.SceneWorld, this );
			var minG = new Vector3( -100000f, -100000f, -10f );
			var maxG = new Vector3( 100000f, 100000f, 80f );

			_renderObject.Bounds = new BBox( minG, maxG );
		}

		// Завантажуємо VAT текстури безпосередньо у менеджер рою
		_positionsTexture = CreateTextureFromBytes( PositionsPath );
		_normalsTexture = CreateTextureFromBytes( NormalsPath );

		_frameTexture = new Texture2DBuilder()
			.WithName( "g_tGoblinFrames" )
			.WithSize( 256, 256 )
			.WithFormat( ImageFormat.R32F )
			.WithDynamicUsage()
			.Finish();

		_tempFrames = new float[65536];

		_renderAttributes = new RenderAttributes();
		if ( _positionsTexture != null ) _renderAttributes.Set( "GoblinPositionsTex", _positionsTexture );
		_renderAttributes.Set( "g_tGoblinFrames", _frameTexture );

	}

	public SwarmUnitHandle SpawnUnit(
		Vector3 startPosition,
		float speed,
		float health,
		float xpValue,
		float scale = 1.0f,
		float attackDamage = 10f,
		float attackRange = -1f,      // Якщо -1, поставиться дефолтний радіус ближнього бою
		float attackCooldown = -1f    // Якщо -1, поставиться кулдаун в 1 секунду
	)
	{
		if ( _freeIndices.Count == 0 ) return SwarmUnitHandle.Invalid;
		int index = _freeIndices.Pop();

		// Збільшуємо версію при повторному використанні слоту
		ushort nextVersion = (ushort)(_units[index].Version + 1);

		float finalRange = attackRange < 0f ? 120f : attackRange;

		float finalCooldown = attackCooldown < 0f ? 1.0f : attackCooldown;

		_units[index] = new SwarmUnit
		{
			IsAlive = true,
			Version = nextVersion,
			Position = startPosition,
			Velocity = Vector3.Zero,
			TargetPosition = startPosition,
			CachedSeparation = Vector3.Zero,
			Speed = speed,
			Health = health,
			MaxHealth = health,
			XPValue = xpValue,
			Scale = scale,

			AttackDamage = attackDamage,
			AttackRange = finalRange,
			AttackCooldown = finalCooldown,
			NextAttackTime = Time.Now + Game.Random.Float( 0f, finalCooldown ),

			// Початкові стани анімацій
			AnimState = GoblinAnimState.StartRun,
			CurrentFrame = Game.Random.Float( RunStartMin, RunStartMax ),

			AnimTimeOffset = Game.Random.Float( 0f, 10f ),
			AnimSpeedMultiplier = Game.Random.Float( 0.85f, 1.15f ),

			CurrentGridIndex = GetGridIndex( startPosition )
		};
		return new SwarmUnitHandle( index, nextVersion );
	}

	public void KillUnit( int index )
	{
		if ( index < 0 || index >= MaxUnits || !_units[index].IsAlive ) return;
		_units[index].IsAlive = false;
		_freeIndices.Push( index );
	}

	/// <summary>
	/// Перевіряє, чи живий ще юніт за його дескриптором.
	/// </summary>
	public bool IsUnitAlive( SwarmUnitHandle handle )
	{
		if ( handle.Index < 0 || handle.Index >= MaxUnits ) return false;
		var unit = _units[handle.Index];
		return unit.IsAlive && unit.Version == handle.Version;
	}

	/// <summary>
	/// Отримує позицію юніта, якщо він живий.
	/// </summary>
	public bool GetUnitPosition( SwarmUnitHandle handle, out Vector3 position )
	{
		position = Vector3.Zero;
		if ( !IsUnitAlive( handle ) ) return false;

		position = _units[handle.Index].Position;
		return true;
	}
	/// <summary>
	/// Отримує поточне здоров'я юніта.
	/// </summary>
	public float GetUnitHealth( SwarmUnitHandle handle )
	{
		if ( !IsUnitAlive( handle ) ) return 0f;
		return _units[handle.Index].Health;
	}
	/// <summary>
	/// Наносить урон юніту за його індексом. Повертає true, якщо юніт загинув від цього удару.
	/// </summary>
	public bool DamageUnit( int index, float damage )
	{
		if ( index < 0 || index >= MaxUnits || !_units[index].IsAlive ) return false;

		_units[index].Health -= damage;

		if ( _units[index].Health <= 0 )
		{
			// 1. Запам'ятовуємо позицію та цінність XP до того, як очистити юніта
			Vector3 deathPosition = _units[index].Position;
			float xpReward = _units[index].XPValue;

			// 2. Вбиваємо юніта (повертаємо слот у пул)
			KillUnit( index );

			// 3. Спавнимо кульку досвіду через безколайдерний XPManager!
			if ( XPManager.Instance != null )
			{
				XPManager.Instance.SpawnXP( deathPosition, xpReward );
			}

			// 4. Отримуємо золото за вбивство (як у твоїй старій економіці)
			if ( GameManager.Instance != null )
			{
				GameManager.Instance.AddMoney( 10f ); // Налаштуй суму за бажанням
			}

			return true; // Юніт загинув
		}

		return false; // Юніт вижив після удару
	}

	protected override void OnUpdate()
	{
		_tickCounter++;
		DebugSpawn();

		// Обробка радіального вибуху
		if ( _isDisintegrating )
		{
			_disintegrationTimeElapsed += Time.Delta;

			// Рахуємо прогрес від 0.0 до 1.0
			float progress = Math.Clamp( _disintegrationTimeElapsed / Math.Max( 0.01f, AnnihilationDuration ), 0f, 1f );

			// Застосовуємо експоненту для нелінійного прискорення
			float curveT = MathF.Pow( progress, AnnihilationExponent );
			_disintegrationRadius = curveT * AnnihilationMaxRadius;
			float radSq = _disintegrationRadius * _disintegrationRadius;

			for ( int i = 0; i < MaxUnits; i++ )
			{
				if ( !_units[i].IsAlive ) continue;

				if ( _units[i].Position.WithZ( 0 ).DistanceSquared( _disintegrationCenter ) < radSq )
				{
					KillUnit( i );
				}
			}

			if ( _disintegrationTimeElapsed >= AnnihilationDuration )
			{
				_isDisintegrating = false;
				ClearActiveUnits();
			}
		}

		UpdateGridSystem(); // Спочатку оновлюємо Грід
		SimulateSwarm();    // Потім рахуємо фізику

		if ( ShowDebugSpheres || ShowGrid )
			DrawDebug();
	}

	private void SimulateSwarm()
	{
		float dt = Time.Delta;
		float radiusSq = UnitRadius * UnitRadius;
		Vector3 refPos = LogicReference.IsValid() ? LogicReference.WorldPosition : Vector3.Zero;
		Vector3 targetPos = Target.IsValid() ? Target.WorldPosition : Vector3.Zero;

		int currentTick = _tickCounter;

		// 1. ФІЗИКА (БАГАТОПОТОЧНІСТЬ) - ПРАЦЮЄ ІДЕАЛЬНО, НЕ ЧІПАЄМО
		Sandbox.Utility.Parallel.For( 0, MaxUnits, i =>
		{
			if ( !_units[i].IsAlive ) return;

			// LOD розрахунки
			float dSq = _units[i].Position.DistanceSquared( refPos );

			int rate = FarUpdateRate;
			if ( dSq < NearDistance * NearDistance ) rate = Math.Max( 1, NearUpdateRate );
			else if ( dSq < FarDistance * FarDistance ) rate = Math.Max( 2, MidUpdateRate );

			_units[i].TargetPosition = targetPos;

			// Розрахунок напрямку руху
			Vector3 offset = _units[i].TargetPosition - _units[i].Position;
			float approxDist = Math.Abs( offset.x ) + Math.Abs( offset.y ) + Math.Abs( offset.z );
			Vector3 directionToTarget = offset / (approxDist + 0.001f);

			// --- ЗУПИНКА РУХУ ПРИ АТАЦІ ---
			float distToTargetSq = _units[i].Position.DistanceSquared( targetPos );
			float attackRangeSq = _units[i].AttackRange * _units[i].AttackRange;

			Vector3 desiredVelocity = Vector3.Zero;

			// Якщо ми ще не добігли на дальність атаки — рухаємось вперед
			if ( distToTargetSq > attackRangeSq )
			{
				desiredVelocity = directionToTarget * _units[i].Speed;
			}

			// Оновлення фізики розштовхування за розкладом
			if ( currentTick % rate == i % rate )
			{
				_units[i].CachedSeparation = CalculateSeparation( i, radiusSq );
			}

			// Сила розштовхування працює завжди (щоб вони гарно розподілялися навколо вежі)
			desiredVelocity += _units[i].CachedSeparation * SeparationForce;

			// Плавний перехід і рух
			_units[i].Velocity = Vector3.Lerp( _units[i].Velocity, desiredVelocity.WithZ( 0 ), dt * 5f );
			_units[i].Position += _units[i].Velocity * dt;

			// ПРИМУСОВИЙ ЛОК ВИСОТИ (фіксуємо ворогів на площині землі)
			_units[i].Position = _units[i].Position.WithZ( 0f );

			// ==========================================
			// НОВЕ: ВИКЛИК СТЕЙТ-МАШИНИ АНІМАЦІЙ
			// ==========================================
			UpdateUnitAnimation( ref _units[i], dt, distToTargetSq, attackRangeSq );
		} );
		// Викликаємо атаку
		ProcessSwarmAttacks();

		// 2. РЕНДЕР (БЕЗ ЖОДНИХ ОБМЕЖЕНЬ КАМЕРИ - МАЛЮЄМО ВСІХ ЖИВИХ)
		int liveCount = 0;

		for ( int i = 0; i < MaxUnits; i++ )
		{
			if ( !_units[i].IsAlive ) continue;

			// Безпечний розрахунок напрямку на вежу (запобігає NaN при нульовій дистанції)
			Vector3 directionToTarget = (targetPos - _units[i].Position).WithZ( 0 );
			Rotation rot;
			if ( directionToTarget.LengthSquared > 0.01f )
			{
				rot = Rotation.LookAt( directionToTarget.Normal, Vector3.Up );
			}
			else
			{
				rot = Rotation.Identity;
			}

			// Якщо ворог НЕ атакує і рухається — дивиться по напрямку свого руху
			float distToTargetSq = _units[i].Position.DistanceSquared( targetPos );
			float attackRangeSq = _units[i].AttackRange * _units[i].AttackRange;

			if ( distToTargetSq > attackRangeSq && _units[i].Velocity.LengthSquared > 1f )
			{
				rot = Rotation.LookAt( _units[i].Velocity.Normal, Vector3.Up );
			}

			// 1. Отримуємо чистий бажаний візуальний масштаб гобліна
			float visualScale = _units[i].Scale * BaseUnitScale;

			// 2. Округляємо номер кадру до цілого, щоб його дробова частина не заважала
			int frameInt = (int)_units[i].CurrentFrame;

			// 3. ПАКУВАННЯ: Ціла частина = Кадр + 10. Дробова частина = Масштаб * 0.01
			// Наприклад: кадр 13, масштаб 1.2 => 23 + 0.012 = 23.012
			float packedScale = (frameInt + 10f) + (visualScale * 0.01f);

			_renderTransforms[liveCount] = new Transform( _units[i].Position, rot, packedScale );
			liveCount++;
		}

		_renderCount = liveCount;
		_tickCounter++;
		ActiveUnitsCount = liveCount;

		// Більше не потрібно передавати текстуру кадрів на GPU через CPU-апдейти!
	}

	private Vector3 CalculateSeparation( int unitIndex, float radiusSq )
	{
		Vector3 separationForce = Vector3.Zero;
		int gridIdx = _units[unitIndex].CurrentGridIndex;

		int gx = gridIdx % GridResolution;
		int gy = gridIdx / GridResolution;

		int neighborsChecked = 0; // ДОДАЄМО ЛІЧИЛЬНИК

		for ( int x = -1; x <= 1; x++ )
		{
			for ( int y = -1; y <= 1; y++ )
			{
				int nx = gx + x;
				int ny = gy + y;

				if ( nx < 0 || nx >= GridResolution || ny < 0 || ny >= GridResolution ) continue;

				int neighborGridIdx = ny * GridResolution + nx;
				var neighbors = _gridArray[neighborGridIdx];

				for ( int i = 0; i < neighbors.Count; i++ )
				{
					int nIdx = neighbors[i];
					if ( nIdx == unitIndex ) continue;

					Vector3 offset = _units[unitIndex].Position - _units[nIdx].Position;
					float dSq = offset.LengthSquared;

					if ( dSq < radiusSq && dSq > 0.001f )
					{
						float pushWeight = 1.0f - (dSq / radiusSq);

						separationForce += offset * (pushWeight / UnitRadius);


						neighborsChecked++;
						// ЛІМІТ! Якщо знайшли MaxNeighborsChecked сусідів, з якими перетинаємось - припиняємо пошук.
						// Цей Break рятує CPU, коли юніти збиваються в кашу.
						if ( neighborsChecked >= MaxNeighborsChecked ) return separationForce;
					}
				}
			}
		}
		return separationForce;
	}

	[System.Runtime.CompilerServices.MethodImpl( System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining )]
	private void UpdateUnitAnimation( ref SwarmUnit unit, float dt, float distToTargetSq, float attackRangeSq )
	{
		// 1. Стейт-транзиції
		if ( distToTargetSq > attackRangeSq )
		{
			// Якщо біжимо, але стан був атакуючим — перемикаємось на біг
			if ( unit.AnimState == GoblinAnimState.PreAttack || unit.AnimState == GoblinAnimState.AttackLoop )
			{
				unit.AnimState = GoblinAnimState.StartRun;
				unit.CurrentFrame = RunStartMin;
			}
		}
		else
		{
			// Якщо атакуємо, але стан був біговим — перемикаємось на атаку
			if ( unit.AnimState == GoblinAnimState.StartRun || unit.AnimState == GoblinAnimState.RunLoop )
			{
				unit.AnimState = GoblinAnimState.PreAttack;
				unit.CurrentFrame = AttackStartMin;
			}
		}

		// 2. Накопичуємо кадри
		unit.CurrentFrame += dt * VatFps * unit.AnimSpeedMultiplier;

		// 3. Контролюємо межі кадрів та переходи
		switch ( unit.AnimState )
		{
			case GoblinAnimState.StartRun:
				if ( unit.CurrentFrame >= RunStartMax )
				{
					unit.AnimState = GoblinAnimState.RunLoop;
					unit.CurrentFrame = RunLoopMin;
				}
				break;

			case GoblinAnimState.RunLoop:
				if ( unit.CurrentFrame >= RunLoopMax )
				{
					float overflow = unit.CurrentFrame - RunLoopMax;
					unit.CurrentFrame = RunLoopMin + (overflow % (RunLoopMax - RunLoopMin));
				}
				break;

			case GoblinAnimState.PreAttack:
				if ( unit.CurrentFrame >= AttackStartMax )
				{
					unit.AnimState = GoblinAnimState.AttackLoop;
					unit.CurrentFrame = AttackLoopMin;
				}
				break;

			case GoblinAnimState.AttackLoop:
				if ( unit.CurrentFrame >= AttackLoopMax )
				{
					float overflow = unit.CurrentFrame - AttackLoopMax;
					unit.CurrentFrame = AttackLoopMin + (overflow % (AttackLoopMax - AttackLoopMin));
				}
				break;
		}
	}

	#region Grid System

	private void UpdateGridSystem()
	{
		for ( int i = 0; i < _gridArray.Length; i++ )
		{
			_gridArray[i].Clear();
		}

		for ( int i = 0; i < MaxUnits; i++ )
		{
			if ( !_units[i].IsAlive ) continue;
			int gridIdx = GetGridIndex( _units[i].Position );
			_units[i].CurrentGridIndex = gridIdx;
			_gridArray[gridIdx].Add( i );
		}
	}

	public int GetGridIndex( Vector3 pos )
	{
		int x = MathX.FloorToInt( pos.x / GridCellSize ) + (GridResolution / 2);
		int y = MathX.FloorToInt( pos.y / GridCellSize ) + (GridResolution / 2);

		x = Math.Clamp( x, 0, GridResolution - 1 );
		y = Math.Clamp( y, 0, GridResolution - 1 );

		return y * GridResolution + x;
	}

	#endregion

	#region Debugging

	private void DrawDebug()
	{
		if ( ShowDebugSpheres )
		{
			for ( int i = 0; i < MaxUnits; i++ )
			{
				if ( !_units[i].IsAlive ) continue;
				DebugOverlay.Sphere( new Sphere( _units[i].Position, 15f ), Color.Red );
				DebugOverlay.Line( _units[i].Position, _units[i].Position + _units[i].Velocity.Normal * 30f, Color.Yellow );
			}
		}

		if ( ShowGrid )
		{
			for ( int i = 0; i < _gridArray.Length; i++ )
			{
				if ( _gridArray[i].Count == 0 ) continue; // Малюємо тільки клітинки, де є вороги

				// Зворотна математика: отримуємо координати світу з індексу масиву
				int x = i % GridResolution;
				int y = i / GridResolution;

				float worldX = (x - (GridResolution / 2)) * GridCellSize;
				float worldY = (y - (GridResolution / 2)) * GridCellSize;

				Vector3 center = new Vector3( worldX + GridCellSize / 2, worldY + GridCellSize / 2, 0 );
				BBox box = new BBox( center - new Vector3( GridCellSize / 2, GridCellSize / 2, 10 ), center + new Vector3( GridCellSize / 2, GridCellSize / 2, 10 ) );

				DebugOverlay.Box( box, Color.Green );
				DebugOverlay.Text( center, $"{_gridArray[i].Count}", color: Color.White );
			}
		}
	}


	private void DebugSpawn()
	{
		if ( Input.Pressed( "attack2" ) )
		{
			for ( int i = 0; i < 1000; i++ ) // Зробив 10к за клік, щоб не вбивати фізику моментально
			{
				float angle = Game.Random.Float( 0, MathF.PI * 2 );
				// Додав рандом в дистанцію спавну, щоб вони не спавнились в одному ідеальному колі
				float dist = Game.Random.Float( 1800f, 2200f );

				Vector3 pos = new Vector3( MathF.Cos( angle ) * dist, MathF.Sin( angle ) * dist, 0 );
				SpawnUnit( pos, Game.Random.Float( 100f, 200f ), 100f, 5f, BaseUnitScale, 1f );
			}
		}
	}

	#endregion

	public class SwarmRenderObject : SceneCustomObject
	{
		private SwarmManager _manager;

		public SwarmRenderObject( SceneWorld world, SwarmManager manager ) : base( world )
		{
			_manager = manager;
			// Можеш розкоментувати, коли налаштуєш лоу-полі модельку і світло
			//Flags.CastShadows = true;
			//Flags.IsOpaque = true;
		}

		public override void RenderSceneObject()
		{
			if ( _manager.UnitModel == null || _manager._renderCount == 0 ) return;
			Span<Transform> instances = new Span<Transform>( _manager._renderTransforms, 0, _manager._renderCount );

			// Передаємо тільки трансформації та атрибути текстур VAT
			Graphics.DrawModelInstanced( _manager.UnitModel, instances, _manager._renderAttributes );
		}
	}

	/// <summary>
	/// Повертає Span (швидке посилання на пам'ять) з усіма позиціями активних ворогів.
	/// Це дозволяє іншим системам (наприклад Canvas) малювати ворогів без копіювання масиву.
	/// </summary>
	public Span<Transform> GetActiveTransforms()
	{
		if ( _renderTransforms == null || _renderCount == 0 ) return new Span<Transform>();
		return new Span<Transform>( _renderTransforms, 0, _renderCount );
	}


	/// <summary>
	/// Знаходить найкращу ціль (SwarmUnitHandle) у заданому радіусі відповідно до правил вибору.
	/// </summary>
	public SwarmUnitHandle FindTargetInRadius( Vector3 center, float radius, TargetBase baseAim, TargetFilter filter )
	{
		float radiusSq = radius * radius;
		int centerCell = GetGridIndex( center );
		int cx = centerCell % GridResolution;
		int cy = centerCell / GridResolution;

		// Визначаємо, скільки клітинок сітки покриває наш радіус
		int cellRange = MathX.CeilToInt( radius / GridCellSize );

		SwarmUnitHandle bestTarget = SwarmUnitHandle.Invalid;

		// Змінні для порівняння пріоритетів
		float bestDistanceSq = (baseAim == TargetBase.Closest) ? float.MaxValue : -1f;
		float bestHealth = (filter == TargetFilter.HighestHealth) ? -1f : float.MaxValue;

		List<SwarmUnitHandle> candidates = null;
		if ( baseAim == TargetBase.Random )
		{
			candidates = new List<SwarmUnitHandle>();
		}

		// Шукаємо лише в сусідніх клітинках, що потрапляють в радіус
		for ( int x = -cellRange; x <= cellRange; x++ )
		{
			for ( int y = -cellRange; y <= cellRange; y++ )
			{
				int nx = cx + x;
				int ny = cy + y;

				// Перевірка меж сітки
				if ( nx < 0 || nx >= GridResolution || ny < 0 || ny >= GridResolution ) continue;

				int gridIdx = ny * GridResolution + nx;
				var cellUnits = _gridArray[gridIdx];

				for ( int i = 0; i < cellUnits.Count; i++ )
				{
					int uIdx = cellUnits[i];
					if ( !_units[uIdx].IsAlive ) continue;

					float dSq = _units[uIdx].Position.DistanceSquared( center );
					if ( dSq > radiusSq ) continue;

					SwarmUnitHandle candidateHandle = new SwarmUnitHandle( uIdx, _units[uIdx].Version );

					// Окремий випадок для рандому
					if ( baseAim == TargetBase.Random )
					{
						candidates.Add( candidateHandle );
						continue;
					}

					float uHealth = _units[uIdx].Health;

					// Логіка відбору цілі
					bool isBetter = false;

					if ( filter == TargetFilter.None )
					{
						// Сортування суто по дистанції (Closest / Furthest)
						if ( baseAim == TargetBase.Closest )
						{
							if ( dSq < bestDistanceSq ) { bestDistanceSq = dSq; isBetter = true; }
						}
						else // Furthest
						{
							if ( dSq > bestDistanceSq ) { bestDistanceSq = dSq; isBetter = true; }
						}
					}
					else
					{
						// Сортування спочатку по ХП (Highest / Lowest), а потім по дистанції
						if ( filter == TargetFilter.HighestHealth )
						{
							if ( uHealth > bestHealth ) { bestHealth = uHealth; isBetter = true; }
							else if ( MathX.AlmostEqual( uHealth, bestHealth, 0.1f ) && dSq < bestDistanceSq ) { isBetter = true; }
						}
						else // LowestHealth
						{
							if ( uHealth < bestHealth ) { bestHealth = uHealth; isBetter = true; }
							else if ( MathX.AlmostEqual( uHealth, bestHealth, 0.1f ) && dSq < bestDistanceSq ) { isBetter = true; }
						}
					}

					if ( isBetter )
					{
						bestTarget = candidateHandle;
						if ( filter != TargetFilter.None ) bestHealth = uHealth;
						bestDistanceSq = dSq;
					}
				}
			}
		}

		if ( baseAim == TargetBase.Random && candidates.Count > 0 )
		{
			return Game.Random.FromList( candidates );
		}

		return bestTarget;
	}

	private void ProcessSwarmAttacks()
	{
		if ( !Target.IsValid() ) return;

		var targetUnit = Target.Components.GetInParentOrSelf<UnitComponent>();
		if ( targetUnit == null || !targetUnit.Alive ) return;

		float currentTime = Time.Now;
		Vector3 targetPos = Target.WorldPosition;

		for ( int i = 0; i < MaxUnits; i++ )
		{
			if ( !_units[i].IsAlive ) continue;

			// ЗАПОБІЖНИК: якщо під час цього циклу вежа вже загинула або почався шопінг — негайно виходимо!
			if ( !targetUnit.Alive || GameManager.Instance.CurrentState == GameState.Shopping )
				break;

			float distSq = _units[i].Position.DistanceSquared( targetPos );
			float rangeSq = _units[i].AttackRange * _units[i].AttackRange;

			if ( distSq <= rangeSq )
			{
				if ( currentTime >= _units[i].NextAttackTime )
				{
					targetUnit.TakeDamage( _units[i].AttackDamage );
					_units[i].NextAttackTime = currentTime + _units[i].AttackCooldown;
				}
			}
		}
	}

	/// <summary>
	/// Перевіряє зіткнення кулі з юнітами Рою. 
	/// Якщо колізія є — повертає true та дескриптор юніта.
	/// </summary>
	public bool CheckBulletCollision( Vector3 position, float bulletHitboxRadius, out SwarmUnitHandle hitUnit )
	{
		hitUnit = SwarmUnitHandle.Invalid;

		int gridIdx = GetGridIndex( position );
		int gx = gridIdx % GridResolution;
		int gy = gridIdx / GridResolution;

		// Радіус колізії: радіус самого юніта + радіус хітбоксу кулі
		float maxCollisionDist = UnitRadius + bulletHitboxRadius;
		float maxCollisionDistSq = maxCollisionDist * maxCollisionDist;

		// Перевіряємо поточну та сусідні клітинки
		for ( int x = -1; x <= 1; x++ )
		{
			for ( int y = -1; y <= 1; y++ )
			{
				int nx = gx + x;
				int ny = gy + y;

				if ( nx < 0 || nx >= GridResolution || ny < 0 || ny >= GridResolution ) continue;

				int neighborGridIdx = ny * GridResolution + nx;
				var cellUnits = _gridArray[neighborGridIdx];

				for ( int i = 0; i < cellUnits.Count; i++ )
				{
					int uIdx = cellUnits[i];
					if ( !_units[uIdx].IsAlive ) continue;

					float dSq = _units[uIdx].Position.DistanceSquared( position );

					if ( dSq <= maxCollisionDistSq )
					{
						hitUnit = new SwarmUnitHandle( uIdx, _units[uIdx].Version );
						return true;
					}
				}
			}
		}

		return false;
	}


	// 1. Допоміжний метод математики (для s&box використовуємо DistanceBetween)
	private float GetDistanceToSegment( Vector3 p, Vector3 a, Vector3 b, out float t )
	{
		Vector3 ab = b - a;
		Vector3 ap = p - a;
		float abLenSq = ab.LengthSquared;

		if ( abLenSq < 0.001f )
		{
			t = 0f;
			return Vector3.DistanceBetween( p, a );
		}

		t = Vector3.Dot( ap, ab ) / abLenSq;
		t = Math.Clamp( t, 0f, 1f ); // Обмежуємо межами відрізка
		Vector3 closest = a + t * ab;
		return Vector3.DistanceBetween( p, closest );
	}

	/// <summary>
	/// Прораховує геометричні зіткнення, урон та відскоки від сегментів зброї.
	/// </summary>
	public void ProcessWeaponCollision( Vector3 gripStart, Vector3 pivot, Vector3 bladeTip, float gripRadius, float bladeRadius, float currentSpeed )
	{
		var rb = CursorComponent.Instance?.Components.Get<Rigidbody>();
		if ( !rb.IsValid() ) return;

		// 1. Прораховуємо 2D-межі для оптимізації сітки
		float maxRad = MathF.Max( gripRadius, bladeRadius );
		float minXVal = MathF.Min( gripStart.x, MathF.Min( pivot.x, bladeTip.x ) ) - maxRad;
		float maxXVal = MathF.Max( gripStart.x, MathF.Max( pivot.x, bladeTip.x ) ) + maxRad;
		float minYVal = MathF.Min( gripStart.y, MathF.Min( pivot.y, bladeTip.y ) ) - maxRad;
		float maxYVal = MathF.Max( gripStart.y, MathF.Max( pivot.y, bladeTip.y ) ) + maxRad;

		int minX = MathX.FloorToInt( minXVal / GridCellSize ) + (GridResolution / 2);
		int maxX = MathX.FloorToInt( maxXVal / GridCellSize ) + (GridResolution / 2);
		int minY = MathX.FloorToInt( minYVal / GridCellSize ) + (GridResolution / 2);
		int maxY = MathX.FloorToInt( maxYVal / GridCellSize ) + (GridResolution / 2);

		minX = Math.Clamp( minX, 0, GridResolution - 1 );
		maxX = Math.Clamp( maxX, 0, GridResolution - 1 );
		minY = Math.Clamp( minY, 0, GridResolution - 1 );
		maxY = Math.Clamp( maxY, 0, GridResolution - 1 );

		HashSet<int> processedUnits = new HashSet<int>();

		// Накопичуємо параметри для усередненого імпульсу меча
		Vector3 totalSwordImpulse = Vector3.Zero;
		Vector3 averageContactPoint = Vector3.Zero;
		int collisionCount = 0;

		for ( int x = minX; x <= maxX; x++ )
		{
			for ( int y = minY; y <= maxY; y++ )
			{
				int gridIdx = y * GridResolution + x;
				var cellUnits = _gridArray[gridIdx];

				for ( int i = 0; i < cellUnits.Count; i++ )
				{
					int uIdx = cellUnits[i];
					if ( !processedUnits.Add( uIdx ) ) continue;

					if ( !_units[uIdx].IsAlive ) continue;

					Vector3 unitPos = _units[uIdx].Position.WithZ( 0 );

					// === 2. ПЕРЕВІРКА НА ЗІТКНЕННЯ З ДВОМА СЕГМЕНТАМИ ===

					// Перевіряємо Лезо (Pivot -> BladeTip)
					float distToBlade = GetDistanceToSegment( unitPos, pivot, bladeTip, out float tBlade );
					bool hitBlade = distToBlade <= (UnitRadius + bladeRadius);

					// Перевіряємо Руків'я (GripStart -> Pivot)
					float distToGrip = GetDistanceToSegment( unitPos, gripStart, pivot, out float tGrip );
					bool hitGrip = distToGrip <= (UnitRadius + gripRadius);

					if ( hitBlade )
					{
						// --- СЦЕНАРІЙ А: ЗІТКНЕННЯ З ЛЕЗОМ ---
						float penetration = (UnitRadius + bladeRadius) - distToBlade;
						Vector3 pushDir = (unitPos - (pivot + tBlade * (bladeTip - pivot))).WithZ( 0 ).Normal;

						// Наносимо урон, якщо швидкість достатня
						float speedFactor = MathF.Min( currentSpeed / CursorComponent.Instance.MaxSpeed, 1.0f );

						if ( speedFactor >= CursorComponent.Instance.MinSpeedToDamage )
						{
							double currentBase = CursorComponent.Instance.BaseCursorDamage + (GameManager.Instance?.GlobalDamage ?? 0);
							double maxAllowedDamage = currentBase * CursorComponent.Instance.SpeedDamageMultiplier;
							float fraction = (speedFactor - CursorComponent.Instance.MinSpeedToDamage) / (1.0f - CursorComponent.Instance.MinSpeedToDamage);
							fraction = fraction.Clamp( 0, 1 );
							double finalDamage = currentBase + (maxAllowedDamage - currentBase) * fraction;

							DamageUnit( uIdx, (float)finalDamage );

							// М'яке відкидання ворога (Knockback)
							if ( CursorComponent.Instance.EnableKnockback )
							{
								float finalKnockback = (speedFactor * 400f) * CursorComponent.Instance.KnockbackForceMultiplier;
								_units[uIdx].Velocity = (_units[uIdx].Velocity + pushDir * finalKnockback).WithZ( 0 );
							}
						}
						else
						{
							// Повільний дотик — вороги просто м'яко відходять
							float passivePush = 80f;
							_units[uIdx].Velocity = (_units[uIdx].Velocity + pushDir * passivePush).WithZ( 0 );
						}

						// Накопичуємо імпульс віддачі для меча (пропорційно зануренню)
						float impulseMagnitude = (speedFactor * 150f + penetration * 100f) * CursorComponent.Instance.Components.Get<Rigidbody>().Mass * 0.5f;
						totalSwordImpulse += (pushDir * -impulseMagnitude).WithZ( 0 );
						averageContactPoint += (pivot + tBlade * (bladeTip - pivot));
						collisionCount++;
					}
					else if ( hitGrip )
					{
						// --- СЦЕНАРІЙ Б: ЗІТКНЕННЯ З РУКІВ'ЯМ ---
						// Урону немає, але ворог і гравець відштовхуються
						float penetration = (UnitRadius + gripRadius) - distToGrip;
						Vector3 pushDir = (unitPos - (gripStart + tGrip * (pivot - gripStart))).WithZ( 0 ).Normal;

						// Сильно відштовхуємо ворога
						_units[uIdx].Velocity = (_units[uIdx].Velocity + pushDir * 200f).WithZ( 0 );

						// Накопичуємо сильну віддачу для меча (блокування замаху)
						float impulseMagnitude = (100f + penetration * 300f) * CursorComponent.Instance.Components.Get<Rigidbody>().Mass;
						totalSwordImpulse += (pushDir * -impulseMagnitude).WithZ( 0 );
						averageContactPoint += (gripStart + tGrip * (pivot - gripStart));
						collisionCount++;
					}
				}
			}
		}

		// === 3. ЗАСТОСУВАННЯ ВІДДАЧІ ТА КРУТІННЯ МЕЧА ===
		if ( collisionCount > 0 )
		{
			averageContactPoint /= collisionCount;

			// 1. ЛІНІЙНИЙ ВІДСКОК МЕЧА (Відштовхування назад)
			Vector3 finalRecoil = totalSwordImpulse / MathF.Sqrt( collisionCount );
			float maxRecoil = 1200f;
			if ( finalRecoil.Length > maxRecoil )
			{
				finalRecoil = finalRecoil.Normal * maxRecoil;
			}

			// Прикладаємо лінійну віддачу
			rb.ApplyImpulse( finalRecoil.WithZ( 0 ) );

			// 2. КАСТОМНЕ КУТОВЕ ОБЕРТАННЯ (ПРОПЕЛЕР)
			// Знаходимо напрямок леза
			Vector3 bladeDir = (bladeTip - pivot).Normal;
			// Знаходимо перпендикуляр до леза у 2D (напрямок, куди меч може крутитися)
			Vector3 perpendicular = new Vector3( -bladeDir.y, bladeDir.x, 0f );

			// Рахуємо, як сильно сила удару тисне саме на прокручування меча (через Dot Product)
			float rotationalForce = Vector3.Dot( finalRecoil, perpendicular );

			// Застосовуємо правило важеля: чим далі від гарди (Pivot) був удар, тим сильніше крутимо меч
			float leverArm = Vector3.DistanceBetween( averageContactPoint, pivot );
			float torque = rotationalForce * leverArm * 0.05f; // 0.05f - чутливість крутіння

			// Напряму додаємо кутову швидкість обертання навколо осі Z (Yaw)
			rb.AngularVelocity = rb.AngularVelocity.WithZ( rb.AngularVelocity.z + torque );

			// 3. ЕФЕКТ УДАРУ (Hit Stop)
			if ( CursorComponent.Instance.EnableSwordRecoil )
			{
				CursorComponent.Instance.ApplyRecoil( finalRecoil.Length / 1200f );
			}
		}
	}

	// Допоміжний метод-заглушка для обмеження максимальної віддачі
	private float maxTotalRecoil() => 1000f;
	/// <summary>
	/// Миттєво вбиває всіх активних ворогів та очищає сітку (використовувати при переході в Магазин).
	/// </summary>
	public void ClearActiveUnits()
	{
		for ( int i = 0; i < MaxUnits; i++ )
		{
			if ( !_units[i].IsAlive ) continue;
			KillUnit( i );
		}

		// Очищаємо списки сітки
		for ( int i = 0; i < _gridArray.Length; i++ )
		{
			_gridArray[i].Clear();
		}

		_renderCount = 0;
		ActiveUnitsCount = 0;

		Log.Info( "Рой повністю очищено для магазину." );
	}
	public void StartRadialDisintegration( Vector3 center )
	{
		_disintegrationCenter = center.WithZ( 0 );
		_disintegrationTimeElapsed = 0f;
		_isDisintegrating = true;
	}

	// Статичний кеш, який зберігає завантажені текстури протягом роботи редактора
	private static Dictionary<string, Texture> _textureCache = new();

	private Texture CreateTextureFromBytes( string localPath )
	{
		// Якщо текстура вже завантажена — просто повертаємо її з кешу
		if ( _textureCache.TryGetValue( localPath, out var cachedTex ) && cachedTex != null )
		{
			return cachedTex;
		}

		if ( !FileSystem.Mounted.FileExists( localPath ) )
		{
			Log.Warning( $"[VAT] File not found: {localPath}" );
			return null;
		}

		byte[] fileBytes = FileSystem.Mounted.ReadAllBytes( localPath ).ToArray();
		if ( fileBytes.Length < 8 ) return null;

		int width = BitConverter.ToInt32( fileBytes, 0 );
		int height = BitConverter.ToInt32( fileBytes, 4 );

		int dataOffset = 8;
		int dataSize = fileBytes.Length - dataOffset;
		float[] floatData = new float[dataSize / sizeof( float )];
		Buffer.BlockCopy( fileBytes, dataOffset, floatData, 0, dataSize );

		Log.Info( $"[VAT ПЛЕЄР] Завантажено асет: {localPath} ({width}x{height})" );

		Texture tex = new Texture2DBuilder()
			.WithName( $"VAT_Texture_{Guid.NewGuid()}" )
			.WithSize( width, height )
			.WithFormat( ImageFormat.RGBA32323232F )
			.WithData<float>( floatData )
			.WithStaticUsage()
			.WithAnonymous( true )
			.Finish();

		// Зберігаємо в кеш
		_textureCache[localPath] = tex;
		return tex;
	}

	protected override void OnDestroy()
	{
		_positionsTexture = null;
		_normalsTexture = null;
		_frameTexture = null;
	}
}