# Жаркое лето 44 — AI Overhaul

Рабочий чек-лист, принятые решения и список новых полей.
Проект: `ZharkoeLeto44.AI`, Unity 2022.3.62f3, ветка `ai-overhaul`.

---

## Чек-лист этапов

| Этап | Статус | Коммит |
|---|---|---|
| 0. Разведка (код не менять) | ✅ готово | baseline |
| 1. Баги EnemyAI (7 пунктов) | ⬜ не начат | — |
| 2. Баги соседних файлов (HitscanWeapon / InfantrySquad / EnemyAnimator) | ⬜ не начат | — |
| 3. Движение и анимация (EnemyMotor / EnemyAnimator / связка) | ⬜ не начат | — |
| 4. Тактика боя (SquadCoordinator, CoverPoint, жетоны) | ⬜ не начат | — |
| 5. Коммуникация и поиск | ⬜ не начат | — |
| 6. Отладка (Gizmos, опционально выделение Perception/Combat) | ⬜ не начат | — |

Правила: каждый этап компилируется (`powershell -ExecutionPolicy Bypass -File Tools\Check-Compilation.ps1`),
после этапа — отчёт и остановка.

---

## Этап 0 — карта кода (разведка)

### Иерархия и вызовы

```
CharacterHealth (HP, Team, Damaged/Died)  ──► EnemyAI (OnDamaged/OnDied)
        │ onEnable/onDisable                            │
        ▼                                                ▼
EnemyAI.RegisterCharacter/UnregisterCharacter     EnemyMotor (NavMeshAgent / Fallback)
   → статический реестр s_characters                 EnemyAnimator (PlayableGraph)
   → используется только в EnemyAI.FindBestVisibleTarget
                                                        │
NoiseSystem (статический event NoiseCreated) ──────────┤
   ← HitscanWeapon.TryFire, Wep (оружие игрока), EnemyVoice (крики), GermanBeggar (свист)
                                                        │
ProjectilePass (статический event ShotFired) ───────────┤
   ← Wep, HitscanWeapon; читает SuppressionReceiver (свист у игрока / подавление у ИИ)
```

* `EnemyAI` — единственный контроллер поведения. Стейт-машина `EnemyState`
  (Patrol → Alert → Search → Chase → Combat → Dead), всё в `Update()`.
* `EnemyAI.Update()` каждый кадр: спад подавления, тик восприятия (0.15 с), спад `awareness`,
  `UpdatePatrol/Alert/Search/Chase/Combat`, `UpdateAnimator`, `UpdateVoiceFootsteps`.
* `EnemyMotor` владеет перемещением: `SetSpeed/MoveTo/Stop/FaceTowardsAtSpeed/SampleReachablePoint`.
  `EnemyAI` НИКОГДА не пишет `transform.rotation` сам, кроме `UpdateSearch` (см. Этап 1.6).
* `InfantrySquad` (internal static) — только расстановка строя:
  `FindNearestAlly`, `ComputeFormationSlot`, `ApplySeparation`, `IsCrowded`, `AdjustChaseGoal`.
* `EnemyLoadout` — выдача/подбор/выброс оружия, сокет, слой, коллайдеры. Патроны НЕ пополняет.
* `EnemyVoice` — реплики и шаги, сам эмитит шум при криках (`shoutNoiseRadius = 22`).
* `EnemySpawnCube` / `PpshGermanEncounter` / `GermanBeggar` — внешние потребители `EnemyAI`
  (`ForceTarget`, `SetWeapon`, `SetPatrolRoute`, `ApplySuppression`).

### Подписки и события

| Источник | Событие | Подписчик |
|---|---|---|
| `CharacterHealth.Damaged` | `Action<DamageInfo>` | `EnemyAI.OnDamaged` (подавление, боль, захват атакующего) |
| `CharacterHealth.Died` | `Action<DamageInfo>` | `EnemyAI.OnDied` (смерть, дроп оружия, труп) |
| `NoiseSystem.NoiseCreated` | `Action<Noise>` | `EnemyAI.OnNoiseCreated` (слух) |
| `HitscanWeapon.Fired` | `Action<Vector3>` | `EnemyAI.OnWeaponFired` (анимация выстрела, голос) |
| `HitscanWeapon.ReloadStarted` | `Action` | `EnemyAI.OnWeaponReloadStarted` |
| `HitscanWeapon.ReloadFinished` | `Action` | **не используется нигде** (оставить) |
| `ProjectilePass.ShotFired` | `Action<Shot>` | `SuppressionReceiver.OnShot` |

### Где используются публичные члены

**`HitscanWeapon`** (вне самого класса): `EnemyAI` (`DamageScale`, `Range`, `AmmunitionInMagazine`,
`ReserveAmmunition`, `IsReloading`, `BeginReload`, `TryFire`, `Fired`, `ReloadStarted`),
`EnemyLoadout` (`HasMuzzle`, `HasAudioSource`, `MuzzlePosition`, `SetMuzzle`, `SetAudioSource`,
`CancelReload`, `ResetAmmo`), `EnemySetupWizard` (через `SerializedObject`),
`CombatRegressionTests` (`TryFire`, `AmmunitionInMagazine`).
Не используются: `ReloadFinished`, `HasAmmunition`, `NeedsReload`, `SetSounds`, `MuzzleForward` — но по ТЗ их сохраняем.

**`EnemyMotor`**: `EnemyAI` (всё), `GermanBeggar` (`Stop/MoveTo/SetSpeed/SampleReachablePoint/
FaceTowardsAtSpeed/HasDestination/HasArrived`), `EnemyLoadout` (`Velocity`).
Не используются вне: `FaceTowards`, `DesiredSpeed`, `CanMove`, `Destination`, `Mode` (только в гизмо/логах EnemyAI).

**`EnemyAnimator`**: только `EnemyAI` (`SetLocomotion(speed, isMoving)`, `SetAiming`, `PlayShoot`,
`PlayReload`, `PlayHit`, `PlayDeath`), `EnemySetupWizard` (через `SerializedObject`).
Не используются вне: `Rebuild`, `HasLocomotion`, `Animator`, `IsDead`.

**`InfantrySquad`**: `internal static` → только `EnemyAI` (4 вызова + гизмо).

### Сериализованные значения, которые перекрывают дефолты (важно!)

* `Game.unity` (5 экземпляров EnemyAI) и `GermanSoldier.prefab`:
  `enemyTeam: 1` (Axis), `targetMask/visibilityMask: ~0`, `viewDistance: 32`, `patrolSpeed: 1.8`,
  `chaseSpeed: 2.8`, `preferredCombatDistance: 20` (в коде дефолт 7!), `maximumCombatDistance: 38`,
  `useSquadTactics: 1`, `squadFireAnchorDuration: 1.2`, `minimumBurstSize: 16`, `maximumBurstSize: 32`.
* `Enemy.prefab` (эталон): `targetMask/visibilityMask: 247`, `patrolSpeed: 2.2`.
* `HitscanWeapon` в сцене и префабе: `damage: 25`, `range: 150`, `spreadAngle: 1.25`,
  `magazineCapacity: 32`, `startingReserve: 128`, `roundsPerMinute: 500`, `shotNoiseRadius: 35`,
  `shotNoiseMultiplier` (в сцене не задан → дефолт 4).
* `EnemyAnimator` в сцене и префабе: `walkAnchorSpeed: 1.8`, `runAnchorSpeed: 2.8`,
  `sprintAnchorSpeed: 3.2`; `shootClip/reloadClip/hitClip` — **пустые**; `sprintClip` == `runClip`
  (один GUID `e00a96682e88d6c478b83717c08bf4f7`).
* `EnemyMotor` в префабе: `arriveRadius: 1.2`, `allowFallbackMovement: 1`;
  `speedAcceleration/speedDeceleration/arriveDamping` в YAML **не записаны** → берутся дефолты скрипта
  (2.5 / 5 / 0.75).
* `NavMeshAgent` (префаб): `m_Speed: 9`, `m_Acceleration: 12`, `m_AutoBraking: 1`, `m_Radius: 0.4`,
  `m_StoppingDistance: 1.2`, `m_AngularSpeed: 360`, `m_AvoidancePriority` не задан (=50 у всех).
* `CharacterHealth`: игрок `team: 0` (Allies), немцы `team: 1` (Axis).
* У вражеских `Animator` `m_Avatar: {fileID: 0}` (Avatar не назначен), `m_Controller` назначен
  (EnemyAnimator снимает его в рантайме).
* Правило проекта (rule 5): старые поля не переименовываем и не меняем дефолты — новые значения
  только через НОВЫЕ поля.

---

## Расхождения ТЗ и кода (обнаружены на этапе 0)

1. **`enemyTeam` — «своя команда», а не «команда цели».** Подтверждено по трём местам:
   `EvaluateCandidate` отбрасывает кандидата с `cand.Team == enemyTeam`;
   `ForceTarget` отбрасывает `targetHealth.Team == enemyTeam`;
   `OnDamaged` делает атакующего целью только при `attackerHealth.Team != enemyTeam`.
   В сцене `enemyTeam: 1` = Axis = собственная команда немца, а цель — игрок (Allies).
   `Docs/COMBAT.md` утверждает «их цель — Allies (0)» — это описание поведения, а не поля.
   → Источник истины: `CharacterHealth.Team`. Поле `enemyTeam` — только миграция.
2. **`ConfigureForWizard` нигде не вызывается** (в проекте нет ни одного вызова), параметр назван
   `targetTeam`, а присваивается в поле «своя команда».
3. **`EnemySetupWizard` дублирует костыль** `Allies → Axis` в трёх местах
   (`UpgradeSceneEnemies`, `SetupSelectedAsEnemy`, `BuildEnemyObject`).
4. **`HasLineOfSight`**: ветка `else if (hitTransform == targetHealth?.transform || ...)` — действительно
   мёртвая (при `targetHealth == null` обе части ложны). `out bool blockedByAlly` вычисляется,
   но **нигде не читается** — именно поэтому союзник на линии «съедает» обзор.
5. **`_losBuffer` = 16 элементов** — при плотной сцене луч переполняется, сортировка теряет попадания.
6. **`_targetBuffer` = 128 элементов** — при `targetMask = ~0` (так в сцене) и `viewDistance = 32`
   на открытой местности 128 коллайдеров не хватает; `GetComponentInParent<CharacterHealth>()`
   вызывается на каждом (6.7 раз в секунду на солдата).
7. **`CanSeeCurrentTarget()`** — да, вызывается каждый кадр (`UpdateChase`, `UpdateCombat`),
   внутри — 1–2 `HasLineOfSight` (Raycast + OverlapSphere + `GetComponentInParent` на каждый хит)
   плюс `GetTargetAimPoint` → `GetComponentInParent<CharacterController>()` и
   `GetComponentInChildren<Collider>()` каждый кадр.
8. **`UpdateSearch` пишет `transform.rotation`** напрямую (строка ~1233) — конфликт с мотором
   (`FaceTowardsAtSpeed`) и с `NavMeshAgent.updateRotation`.
9. **Слух**: радиус = `noise.Radius * hearingRadiusMultiplier`.
   Выстрел ИИ: `35 * 4 = 140 м` → ×1.5 = **210 м**. Выстрел игрока (`Wep.shotNoiseRadius = 40`,
   `shotNoiseMultiplier = 4` → 160 м) → ×1.5 = **240 м**. Критерий «одиночный выстрел игрока
   со 100 м не поднимает весь уровень» сейчас НЕ выполнен.
10. **`HitscanWeapon.TryFire`** использует `Physics.RaycastAll` (аллокация массива на каждый выстрел)
    + `Array.Sort` с лямбдой, и `GetComponentInParent<IDamageable>()` (интерфейс → медленный путь).
    Трассер — `new GameObject` + `AddComponent<LineRenderer>` на каждый выстрел.
11. **`EnemyAnimator.ComputeTargets`** создаёт `float[] anchors` и `AnimationClip[] clips`
    **в каждом вызове** (каждый кадр) → 2 массива на солдата на кадр = ~2400 GC-аллокаций/с при 20 солдатах.
    `RestoreBindScales` обходит `Dictionary` каждый `LateUpdate` (при `lockScales = 1` в префабе).
12. **Якоря скоростей в ТЗ указаны как 1.8/3.8, в проекте — 1.8 / 2.8 / 3.2**, причём
    `sprintClip` — копия `runClip`. `chaseSpeed` в сцене 2.8, а не 3.8.
13. **Клипов `shoot/reload/hit` в проекте нет вообще** (везде `null`), AvatarMask не задан,
    клипа «на колене» нет, стрейф-клипов нет. → Всё из этапов 3–4 с этими клипами должно
    корректно деградировать к текущему поведению.
14. **`EnemyLoadout` патроны не пополняет**: там только `weapon.ResetAmmo()` при выдаче.
    Источник боеприпасов — `magazineCapacity = 32` + `startingReserve = 128`.
    Значит «отступление при пустом магазине» реализуем в ИИ (этап 4).
15. **Регресс-тест уже сломан до наших правок**:
    `CombatRegressionTests.EnemyGunCanHitAnotherCharacterUnderTheSameParent` ждёт
    `AmmunitionInMagazine == 29`, но `magazineCapacity` в проекте 32 (тест писался при 30).
    Тот же тест сломается от дружественного огня (обе цели — `Team.Axis` по умолчанию).
    → Починим в этапе 2 (тест должен задавать разные команды и считать боезапас от факта).
16. **`#if UNITY_EDITOR` вместо `UNITY_EDITOR || DEVELOPMENT_BUILD`** — в проекте нет ни одного
    `DEVELOPMENT_BUILD`. Для runtime-гизмо (этап 6) понадобится расширить условие.
17. **`EnemyAI.UpdateEngageHitTracking`** сравнивает HP цели с локальным `_lastTargetHp` — HP общий,
    поэтому `_engageHits` растёт у ВСЕХ солдатов одновременно (подтверждено).
18. **`EnemyAnimator.SetLocomotion(float speed)`** (одноаргументный) нигде не используется, но публичный.
19. `EnemyMotor` в режиме NavMesh **не применяет** свой разгон 2.5 м/с²: агент внутренне разгоняется
    к `_agent.speed` со своей `acceleration = 12`. `autoBraking = 1` дублирует ручное торможение мотора.
    Все солдаты имеют `avoidancePriority = 50` (дефолт) → конфликт при толкотне.

---

## Принятые решения

* **D1.** Поле `enemyTeam` оставляем (rule 5), но перестаём использовать как источник истины:
  «своя команда» = `CharacterHealth.Team`. Старое значение поля используется только для миграции
  (расхождение с `CharacterHealth.team` → предупреждение + приведение поля к фактическому).
* **D2.** Слой цели (`targetMask`) при значении 0 = «фильтр не применён» + явное предупреждение,
  вместо молчаливой подстановки `~0` (значение в сцене/префабах не трогаем).
* **D3.** Новые поля — всегда новые, с `Tooltip` на русском; старые `[SerializeField]` не переименовываем.
* **D4.** Тяжёлые расчёты (план отряда, выбор позиций, поиск укрытий) — по таймеру 4–5 Гц со сдвигом
  между солдатами; восприятие — тик 0.1 с со случайным сдвигом на солдата.
* **D5.** Никаких аллокаций в `Update/LateUpdate` и в стрельбе: LINQ нет, массивы/списки в кадре нет,
  `RaycastAll` нет, строки — только под `debugLogging`.

---

## Новые поля (нарастающий список)

| Файл | Поле | Тип / дефолт | Назначение |
|---|---|---|---|
| — | — | — | — |

---

## Что проверить руками в Unity (по этапам)

* **Этап 1:** 4–6 немцев на ровном поле; союзник в шеренге между стрелком и игроком — враг должен
  видеть игрока; выстрел игрока со 100 м — поднимается только ближние; сцена из 2 немцев: урон
  не должен «прыгать» одновременно у обоих.
