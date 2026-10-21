# Жаркое лето 44 — AI Overhaul

Рабочий чек-лист, принятые решения и список новых полей.
Проект: `ZharkoeLeto44.AI`, Unity 2022.3.62f3, ветка `ai-overhaul`.

---

## Чек-лист этапов

| Этап | Статус | Коммит |
|---|---|---|
| 0. Разведка (код не менять) | ✅ готово (+ повторная сверка 21.10.2026) | baseline |
| 1. Баги EnemyAI (7 пунктов) | ✅ сделано | рабочее дерево |
| 2. Баги соседних файлов (HitscanWeapon / InfantrySquad / EnemyAnimator) | ✅ сделано | рабочее дерево |
| 3. Движение и анимация (EnemyMotor / EnemyAnimator / связка) | ✅ сделано | рабочее дерево |
| 4. Тактика боя (SquadCoordinator, CoverPoint, жетоны) | ✅ сделано | рабочее дерево |
| 5. Коммуникация и поиск | ✅ сделано | рабочее дерево |
| 6. Отладка (Gizmos, экранный оверлей) | ✅ сделано (выделение Perception/Combat — необязательно, не делалось) | рабочее дерево |

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

### Инвентарь ИИ-объектов в `Game.unity` (на 21.10.2026)

| Объект | Что это | `enemyTeam` | `CharacterHealth.team` | Заметка |
|---|---|---|---|---|
| 5 шт. (обычные GO, `m_PrefabInstance: 0`) | EnemyAI + мотор + аниматор + оружие | `1` (Axis) | `1` (Axis) | Своя команда совпадает — миграция не нужна |
| 2 шт. `Enemy.prefab` (guid `e52a9ba2…`) | эталонный капсульный враг | override `0` (Allies) | `1` (Axis) | **Расхождение** → срабатывает костыль и warning |
| `GermanSoldier.prefab` (guid `d30e1adf…`) | спавнится в рантайме | `1` (Axis) | `1` (Axis) | В сцене не размещён; `useSquadTactics: 1`, `chaseSpeed: 2.8`, `patrolSpeed: 1.8` |
| Игрок (`PlayerHealth` → `CharacterHealth`) | — | — | `0` (Allies) | Единственный «чужой» персонаж |

Прочее: `EnemyAnimator` в префабе — `shootClip/reloadClip/hitClip = null`, `sprintClip == runClip`,
`m_Avatar: {fileID: 0}` (Avatar не назначен → клипы играют в generic-режиме), `m_Controller` назначен
(снимается `EnemyAnimator.StripForeignController` в рантайме). `NavMeshAgent` — `m_Speed: 9`,
`m_Acceleration: 12`, `m_AutoBraking: 1`, `m_Radius: 0.4`, `m_StoppingDistance: 1.2`,
`m_AngularSpeed: 360`, `m_AvoidancePriority` не задан (50 у всех).

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

### Дополнения повторной сверки (21.10.2026, код не менялся)

20. **Кто именно лежит в сцене под `enemyTeam: 0`.** Prefab-override с `propertyPath: enemyTeam / value: 0`
    стоит у **2 экземпляров `Enemy.prefab`** (guid `e52a9ba2…`, блоки сцены `Game.unity` от строки 13553
    и 32510). `GermanSoldier.prefab` (guid `d30e1adf…`) в `Game.unity` **не размещён вообще** — он
    спавнится в рантайме (`EnemySpawnCube`, `PpshGermanEncounter`, `GermanRaidDirector`).
    Пять остальных `EnemyAI` в сцене — обычные объекты (не префабы), у них `enemyTeam: 1` (Axis).
    → Итог: костыль `Allies→Axis` реально срабатывает на 2 объектах и **дважды пишет warning в консоль**
    при каждом запуске игры. Утверждение `Docs/COMBAT.md` («у четырёх экземпляров префаба изменены
    соответствующие overrides») не соответствует YAML: overrides = `0`, а не `1`.
21. **`EnemyMotor` аллоцирует память в кадре.** `TryFindGround()` (вызывается из `LateUpdate` →
    `UpdateNavMeshGroundStick`, и из `UpdateFallbackMovement`) при переполнении буфера `_groundHits[32]`
    уходит в `Physics.RaycastAll` → **новая managed-массив каждый кадр** на открытой местности с травой/пропсами.
    Аналогично `IsDirectionWalkable()` → `Physics.SphereCastAll` (режим Fallback, буфер 32).
    В критерии готовности «0 B GC alloc в кадре» это надо закрыть (этап 3).
22. **`Physics.RaycastNonAlloc` при переполнении буфера возвращает произвольное подмножество** —
    в `HasLineOfSight` буфер `_losBuffer[16]`. То есть при плотной сцене (16+ хит-ов на луч) результат
    видимости становится **случайным**, а не просто «дорогим».
23. **`GetTargetAimPoint()` дёргает `GetComponentInParent<CharacterController>()` и
    `GetComponentInChildren<Collider>()`** — и в `CanSeeCurrentTarget()` (каждый кадр, Chase+Combat),
    и в `EvaluateCandidate()` (на каждого кандидата, 6.7 Гц). Это самые дорогие места восприятия.
24. **`HasLineOfSight` сортирует только элементы 1..count-1** (инсерционная сортировка) — массив
    после сортировки корректен, но вставка выполняется на каждый луч (лишняя работа в HotPath).
25. **`HasLineOfSight` при попадании в цель возвращает `true` немедленно**, т.е. луч до цели
    не проверяется «за целью» — это правильно, но из-за этого союзник **перед** целью
    (ближе к стрелку) блокирует обзор, а союзник **за** целью — нет. Именно это и лечится этапом 1/2.
26. **Разброс двойной.** `EnemyAI.ComputeAimPoint` смещает точку прицеливания в метрах
    (`baseSpread 0.35` + `0.45` на 10 м, × moving × suppressed × pacing), и поверх этого
    `HitscanWeapon.ApplySpread` добавляет ещё `spreadAngle = 1.25°`. При 20 м смещение ≈ 1.25 м
    (радиус!), т.е. в упор-режиме солдат физически не может попасть. → Этап 2 (единая модель `SpreadScale`).
27. **Дед-код/лишние поля:** в `GermanSoldier.prefab` у `EnemyAI` записаны уже удалённые поля
    `combatDecisionInterval`, `minStandFireTime`, `minApproachTime` — Unity их молча отбросит.
28. **`InfantrySquad.ComputeFormationSlot` считает ВСЕХ солдат уровня с той же командой**, а не отряд:
    при 20 солдатах шаг строя = `squadLateralSpread / 19` ≈ 0.47 м — строй «схлопывается».
    То же в `IsCrowded` (проверяет всех, а не только соседей по отряду) — это и есть причина
    пункта «ComputeFormationSlot учитывать только живых союзников в пределах squadAwarenessRadius».
29. **`EnemyAI.SquadTeam` уже берёт `CharacterHealth.Team`** (`_health != null ? _health.Team : enemyTeam`) —
    то есть внутри проекта «своя команда» де-факто уже привязана к `CharacterHealth`. Это подтверждает D1:
    достаточно убрать `enemyTeam` из всех сравнений цели и оставить поле только для миграции.
30. **Проверка компиляции:** `powershell -ExecutionPolicy Bypass -File Tools\Check-Compilation.ps1`
    на baseline (21.10.2026) → `Player compilation passed.` + `Editor compilation passed.`
    Скрипт компилирует ВСЕ `*.cs` из `Assets` (в т.ч. новые), отдельно в режимах Player и Editor,
    используя реальные Unity-ссылки из `Assembly-CSharp.csproj` + Roslyn из редактора.

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
* **D6.** «Своя команда» берётся **только** из `CharacterHealth.Team`. Поле `enemyTeam` — аналог
  `CharacterHealth.team`, который в старых сценах мог быть рассинхронизирован; в `Awake` значение
  `enemyTeam` приводится к `CharacterHealth.Team` (с warning только при реальном расхождении),
  костыль «Allies → Axis» удаляется. Сравнение цели — `cand.Team != _health.Team` (любой живой
  `CharacterHealth` с другой командой).
* **D7.** Слух: `maxHearingDistance` — **новое** поле с дефолтом 90 м, «и работающее независимо от
  значений в сцене» → оно верхняя граница, а не замена `hearingRadiusMultiplier`: эффективный радиус
  = `min(noise.Radius * hearingRadiusMultiplier, maxHearingDistance)`. Старое поле не трогаем.
* **D8.** «Стадия темпа схватки» — общая для отряда: считается от **доли потерянного здоровья цели**
  (`CharacterHealth.NormalizedHealth`), а не от числа попаданий. Индекс стадии =
  `floor((1 - normalizedHealth) * pacingDamageStages.Length)`; `pacingDamageJitter` сохраняется.
  Сброс — 10 с вне боя или восстановление здоровья цели (`RestoreHealth` → `NormalizedHealth` вырос).

---


## Новые поля (нарастающий список)

Правило: новое значение по умолчанию — только через новое поле; старые `[SerializeField]`
не переименовываются и не меняют дефолты (rule 4/5).

| Файл | Поле | Тип / дефолт | Назначение |
|---|---|---|---|
| EnemyAI | `maxHearingDistance` | float / 90 | Верхняя граница слышимости (м). Было 210–240 м от одного выстрела |
| EnemyAI | `hearingPositionErrorRatio` | float / 0.08 | Ошибка позиции услышанного шума (доля от дистанции) |
| EnemyAI | `perceptionTickJitter` | float / 0.15 | Разброс тиков восприятия между солдатами (с) |
| EnemyAI | `engageResetDelay` | float / 10 | Сброс темпа схватки после N секунд вне боя |
| EnemyAI | `alertSpeed` | float / 2.2 | Отдельная скорость настороженного шага (jog) |
| EnemyAI | `sprintSpeedMultiplier` | float / 1.25 | Множитель скорости перебежки от chaseSpeed |
| EnemyAI | `suddenTargetReactionDelay` | float / 0.3 | Задержка реакции на внезапно появившуюся цель (с) |
| EnemyAI | `chaseLeadTime` | float / 1.2 | На сколько секунд просчитывается уход цели в погоне |
| EnemyAI | `chaseLeadMaxDistance` | float / 6 | Максимальное упреждение по уходу цели (м) |
| EnemyAI | `contactShareDelay` | Vector2 / 0.3–1.0 | Задержка передачи контакта союзникам (с) |
| EnemyAI | `searchPointCount` | int / 3 | Сколько точек проверяет солдат при поиске (2–3) |
| EnemyAI | `squadCommitTime` | float / 1.5 | Минимальное время фиксации решения отряда (с) |
| EnemyAI | `maneuverDistance` | float / 10 | Длина перебежки роли «Манёвр» (м) |
| EnemyAI | `tacticMinTargetDistance` | float / 6 | Не подходить к игроку ближе (м) |
| EnemyAI | `woundedHealthThreshold` | float / 0.35 | Доля здоровья, ниже которой солдат ранен |
| EnemyAI | `woundedFireRateScale` | float / 1.8 | Множитель паузы между очередями раненого |
| EnemyAI | `coverPeekDuration` | float / 1.6 | Сколько секунд солдат выглядывает из укрытия |
| EnemyAI | `coverHideDuration` | float / 1.8 | Сколько секунд солдат прячется |
| EnemyAI | `tacticalMinimumBurst` / `tacticalMaximumBurst` | int / 3–8 | Длина очереди в тактическом режиме |
| EnemyAI | `tacticalMinimumPause` / `tacticalMaximumPause` | float / 0.4–1.2 | Пауза между очередями в тактическом режиме |
| EnemyAI | `spreadUnseenMultiplier` | float / 2 | Множитель разброса сразу после обнаружения |
| EnemyAI | `spreadSeenMultiplier` | float / 0.8 | Множитель разброса после нескольких секунд видимости |
| EnemyAI | `spreadSettleTime` | float / 3 | За сколько секунд точность доходит до нормы |
| EnemyAI | `debugOverlay` | bool / false | Экранная отладка (роль, жетон, состояние) |
| HitscanWeapon | `allowFriendlyFire` | bool / false | Разрешить ли стрелять по союзникам |
| HitscanWeapon | `tracerPoolSize` | int / 8 | Размер пула трассеров (без создания объектов на выстрел) |
| EnemyAnimator | `strafeLeftClip` / `strafeRightClip` / `walkBackClip` | AnimationClip / null | Клипы стрейфа (без них бокового шага нет) |
| EnemyAnimator | `aimIdleClip` / `aimWalkClip` | AnimationClip / null | Прицельные позы (верх тела) |
| EnemyAnimator | `upperBodyMask` | AvatarMask / null | Маска верхнего тела для экшенов и прицельных поз |
| EnemyAnimator | `shootRecoilClip` | AnimationClip / null | Короткая отдача в очереди (клип выстрела не перезапускается) |
| EnemyMotor | `sprintAcceleration` / `sprintDeceleration` | float / 12–16 | Разгон/торможение на перебежке (м/с²) |
| EnemyMotor | `validatePath` | bool / true | Проверять ли наличие полного пути до точки |
| EnemyMotor | `maxPathLengthRatio` | float / 2.5 | Во сколько раз путь может длиннее прямой |
| EnemyMotor | `agentAccelerationOverride` | float / 60 | Разгон агента (согласование с мотором) |
| EnemyMotor | `disableAgentAutoBraking` | bool / true | Отключить автоторможение агента (тормозит мотор) |
| EnemyMotor | `agentRadius` | float / 0.4 | Радиус агента (совпадение с телом) |
| CoverPoint | `highCover` | bool / false | Высокое укрытие (только выглядывать) |
| CoverPoint | `facingOverride` | Transform / null | Откуда безопасно смотреть из укрытия |
| CoverPoint | `claimRadius` | float / 1.5 | Радиус занятости укрытия |
| CoverPoint | `maxTargetDistance` | float / 45 | Максимальная дистанция от укрытия до цели |

### Новые публичные члены (старые не сломаны)

| Класс | Член | Назначение |
|---|---|---|
| HitscanWeapon | `SpreadScale` (get/set) | Множитель собственного разброса (как `DamageScale`) |
| HitscanWeapon | `BaseSpreadAngle` | Базовый разброс оружия в градусах |
| HitscanWeapon | `AllowFriendlyFire` | Текущее значение флага |
| HitscanWeapon | `CanFireNow` | Оружие готово к выстрелу прямо сейчас |
| HitscanWeapon | `HasClearShot(aimPoint, owner, out blockedByAlly)` | Чистая линия от дула до цели |
| EnemyMotor | `SetSprintMode(bool)` | Резкий разгон/торможение на перебежке |
| EnemyMotor | `SetAvoidancePriority(int)` | Свой приоритет обхода у солдата |
| EnemyMotor | `FaceYawAtSpeed(yaw, degPerSec)` | Доворот корпуса на угол рыскания |
| EnemyMotor | `SetManualRotation(bool)` | Ручное управление поворотом (агент не крутит тело) |
| EnemyMotor | `TryGetPathLength(dest, out len)` | Длина пути по NavMesh |
| EnemyMotor | `SampleReachablePoint(..., requirePath)` | Перегрузка с проверкой пути |
| EnemyAnimator | `ConfigureSpeedAnchors(walk, run, sprint)` | Единый источник скоростей |
| EnemyAnimator | `SetLocomotion(worldVelocity, isMoving, speed)` | Перегрузка с направлением (стрейф) |
| EnemyAnimator | `PlayShootRecoil()` | Короткая отдача без перезапуска клипа выстрела |
| EnemyAnimator | `HasStrafeClips` / `HasAimClips` | Наличие опциональных клипов |

---

## Что сделано по этапам

### Этап 1 — баги `EnemyAI` (7 пунктов) — сделано

1. **Союзники не блокируют обзор.** В `HasLineOfSight` попадание в персонажа своей команды
   помечается (`blockedByAlly = true`) и луч продолжается; мёртвая ветка
   `else if (hitTransform == targetHealth?.transform …)` удалена.
2. **Стадия схватки — общая и от потерь цели.** `_engageHits` удалён, появилась `_engageStage`
   = `floor((1 - NormalizedHealth) * pacingDamageStages.Length)` — одинакова у всего отряда
   (HP общий). `pacingDamageJitter` сохранён. Сброс: 10 с вне боя (`engageResetDelay`) и
   восстановление здоровья цели.
3. **Команда — только `CharacterHealth.Team`.** Добавлено `OwnTeam`; `enemyTeam` больше не решает,
   кто цель. Костыль «Allies → Axis» удалён, осталась миграция (предупреждение о расхождении
   и приведение поля к фактической команде). Тот же костыль убран в `EnemySetupWizard`
   (2 места) — там поле теперь берётся из `CharacterHealth`.
4. **Поиск целей — только по реестру `s_characters`.** `OverlapSphereNonAlloc` + `GetComponentInParent`
   по коллайдерам убраны; ранний отсев по квадрату дистанции. `targetMask == 0` → предупреждение,
   молчаливой подстановки `~0` больше нет. Добавлен кеш ссылок на `CharacterController`/`Collider`
   персонажа (точка прицеливания считается живой, ссылки кешируются).
5. **`CanSeeCurrentTarget()` кешируется на 0.1 с** со случайным сдвигом на солдата; тики восприятия
   разнесены (`perceptionTickJitter` + случайная фаза на старте).
6. **`UpdateSearch` вращает только через мотор** — добавлен `EnemyMotor.FaceYawAtSpeed` и
   `SetManualRotation` (агент не спорит с ИИ за угол тела).
7. **Слух.** `maxHearingDistance` (90 м) — верхняя граница; ошибка позиции шума ~8 % дистанции.

### Этап 2 — `HitscanWeapon` / `InfantrySquad` / `EnemyAnimator` — сделано

* **Дружественный огонь:** `allowFriendlyFire = false` — попадание в `CharacterHealth` своей команды
  пропускается, пуля летит дальше.
* **`RaycastNonAlloc` (буфер 16) + ручная сортировка** по дистанции вместо `RaycastAll` + `Array.Sort`.
* **Пул трассеров** (`LineRenderer` × `tracerPoolSize`) — объекты не создаются/уничтожаются на выстрел.
* **`SpreadScale`** — множитель собственного разброса оружия (как `DamageScale`).
* **`HasClearShot(aimPoint, owner, out blockedByAlly)`** — проверка от дула до цели.
* **`InfantrySquad.ComputeFormationSlot`** — только живые союзники в пределах `squadAwarenessRadius`,
  находящиеся в Chase/Combat. `IsCrowded` тоже ограничен радиусом отряда.
* **`EnemyAnimator` без аллокаций в кадре:** кеш якорей/клипов (`_anchorCache`, `_clipCache`) вместо
  `new float[]`/`new AnimationClip[]` в `ComputeTargets`; bind-масштабы в массивах
  (`Transform[]`/`Vector3[]`) вместо обхода `Dictionary` каждый `LateUpdate`.

### Этап 3 — движение и анимация — сделано

* **Правило «идёт — не стреляет».** В бою: при движении `SetAutoRotation(true)` (смотрит по ходу) и
  стрельбы нет; стоя — `SetAutoRotation(false)` + доворот на цель через `combatTurnSpeed`.
* **Единый источник скоростей.** `EnemyAI.ApplySpeedAnchors()` → `EnemyAnimator.ConfigureSpeedAnchors
  (patrol, chase, chase×sprint)`. Скорости разделены: патруль / настороженный шаг / погоня / перебежка.
  Поле `squadPursuitSpeedScale` оставлено для совместимости сцен, но больше не используется.
* **Масштаб клипа = скорость/якорь в пределах 0.8–1.2** — ноги не скользят.
* **Верхний слой:** `upperBodyMask` (AvatarMask) — экшены и прицельные позы играют верхом тела,
  смерть — всем телом. Граф перестроен на `AnimationLayerMixerPlayable` (5 слоёв).
  `SetAiming` реально работает: `aimIdle`/`aimWalk` (верх тела) на время боя.
* **Анимация выстрела не перезапускается в очереди:** первый выстрел — полный клип, дальше
  `PlayShootRecoil()` (короткий клип отдачи, если задан; иначе — ничего).
* **Гистерезис idle↔move** в `EnemyAnimator` (порог входа 0.8, выхода 1.35 от `idleMaxSpeed` + задержка).
* **Стрейф-клипы** `strafeLeft/strafeRight/walkBack` + перегрузка `SetLocomotion` с мировой
  скоростью; без клипов — прежнее поведение.
* **EnemyMotor:** `SampleReachablePoint(..., requirePath)`, `TryGetPathLength`, `SetAvoidancePriority`
  (уникальный приоритет по хэшу id), `sprintAcceleration/sprintDeceleration`, согласование настроек
  агента (`acceleration` 60, `autoBraking` выкл, радиус 0.4) — с пояснением в коде.
* **Fallback:** в режиме без навмеша тактика (укрытия/перебежки) отключается, один раз warning.

### Этап 4 — тактика боя — сделано

* **Новые файлы:** `SquadCoordinator.cs` (статический координатор) и `CoverPoint.cs` (маркер укрытия).
* **Группировка** — живые союзники в `squadAwarenessRadius`, пересчёт 0.5 с; **план ролей** — 0.25 с.
* **Роли:** почти весь отряд — «Огонь». Солдаты выходят на свои места в строю,
  останавливаются и стреляют; постоянных перебежек «Манёвр/Фланк» больше нет.
* **Жетоны атаки:** стреляет `N-1` солдат (при N≥4), один остаётся в резерве;
  жетон отпускается, если стрелок мёртв, потерял цель или кончились патроны.
* **Позиции:** широкий строй вокруг цели (`squadSeparationDistance` — минимальный
  интервал), без клипа бокового шага в 1.5 м — отряд не сбивается в кучу.
* **Укрытие:** осталось как опция для роли `Cover`, но координатор её больше не выдаёт.
* **Очереди:** новые поля `tacticalMinimum/MaximumBurst` и `tacticalMinimum/MaximumPause`
  (3–8 патронов, 0.4–1.2 с) при `useSquadTactics = true`; старые `minimum/maximumBurstSize` —
  при `useSquadTactics = false` (старый бой сохранён полностью).
* **Перед каждым `TryFire` — `weapon.HasClearShot`;** нет чистой линии — солдат не стреляет,
  но продолжает держать позицию (без постоянных перебежек).
* **Единая модель разброса:** ИИ считает всё сам и отдаёт `weapon.SpreadScale`
  (2.0 → 0.8 за ~3 с непрерывной видимости, сброс при потере). Старый «метровый» разброс в
  `ComputeAimPoint` убран — двойного разброса больше нет.
* **Перезарядка/пустые патроны:** магазин пуст → `BeginReload`; патронов нет вообще — солдат
  остаётся на месте и ждёт, без перебежек.
* **Раненый** (HP < `woundedHealthThreshold`) — не получает жетон и стреляет реже.
* **Подавление** учитывается в разбросе; отдельного «залёг»-клипа в проекте нет — солдат держит
  позицию в строю.

### Этап 5 — коммуникация и поиск — сделано

* **Оповещение отряда:** заметивший (переход в Chase) передаёт союзникам в радиусе точку и цель;
  каждый союзник реагирует через 0.3–1.0 с (случайно) и говорит реплику (`EnemyVoice.PlayAlert`).
* **Поиск:** 2–3 точки вокруг последней известной позиции (свой сектор у каждого солдата), точки
  берутся только при наличии пути; на каждой — осмотр 1.5 с; затем возврат к патрулю.
* **Погоня:** цель предсказывается по `_targetVelocity` (упреждение ≤ `chaseLeadMaxDistance`).
* **Внезапная цель:** короткая задержка реакции `suddenTargetReactionDelay`; в бою корпус
  доворачивается через `combatTurnSpeed`, огонь — только после доворота (`maxFireAngle`).

### Этап 6 — отладка — сделано

* Gizmos (роль, жетон, цель отряда, укрытие, точки поиска) и экранный оверлей — под
  `#if UNITY_EDITOR || DEVELOPMENT_BUILD`; включается новым флагом `debugOverlay`.
* Выделение `Perception`/`Combat` в отдельные компоненты — **не делалось** (пункт необязательный):
  поведение не менялось, публичный API не тронут, риск регрессий выше выгоды.

---

## Авария после этапов 1–6 (21.10.2026) — найдена и исправлена

**Симптомы:** в консоли сотни `NullReferenceException`, враги «не замечают» игрока, стоят,
иногда стреляют.

**Корень (один на все ошибки):** `EnemyMotor` создавался так:

```csharp
private readonly NavMeshPath _path = new();     // ← инициализатор поля
```

Unity запрещает создавать `NavMeshPath` в конструкторе MonoBehaviour
(`InitializeNavMeshPath is not allowed to be called from a MonoBehaviour constructor`).
Исключение **обрывало конструктор**, и ВСЕ поля, объявленные ПОСЛЕ `_path`
(`_obstacleCheckBuffer`, `_walkabilityHits`, `_groundHits`), оставались `null`:

* `TryFindGround` → `hitCount == _groundHits.Length` → **NRE** (каждый кадр, каждый солдат);
* `NavMesh.CalculatePath(..., _path)` с `null` → **NRE** (координатор отряда, 4 раза в секунду);
* `SnapDestinationY` → `TryFindGround` → NRE → `MoveTo` падал → **враг не мог двигаться**;
* исключение в `Update` обрывало кадр **до стейт-машины** → враг не стрелял и не реагировал.

**Исправление:**
* `NavMeshPath` создаётся в `Awake` (`_path = new NavMeshPath()`), в `TryGetPathLength` — проверка на `null`;
* буферы рейкастов больше не зависят от порядка инициализации;
* `EnemyAI.Update` — вся логика в `try/catch`: сбой одной подсистемы не «замораживает» врага,
  ошибка пишется в консоль ОДИН раз (`_loggedStateError`).

**Сопутствующие укрепления:**
* обход реестра персонажей — по индексу списка (регистрация нового персонажа во время
  итерации по `HashSet` бросила бы `InvalidOperationException`);
* `NavMeshPath.GetCornersNonAlloc` вместо `path.corners` — убрана аллокация массива на каждый расчёт пути;
* `HasClearShot`: попадания ближе 0.35 м от дула не считаются помехой.

**Урок:** любой Unity-объект, создаваемый в инициализаторе поля MonoBehaviour
(`NavMeshPath`, `AnimationClipPlayable`, `PlayableGraph` и т.п.), обязан создаваться в `Awake`.

---


* **Этап 1:** 4–6 немцев на ровном поле; союзник в шеренге между стрелком и игроком — враг должен
  видеть игрока; выстрел игрока со 100 м — поднимаются только ближние; сцена из 2 немцев: урон
  не должен «прыгать» одновременно у обоих; в консоли должно быть одно предупреждение о
  `enemyTeam` (для 2 экземпляров `Enemy.prefab`), а не молчание.
* **Этап 2:** солдаты в шеренге не убивают друг друга; пуля пролетает сквозь союзника;
  `CombatRegressionTests` (EditMode) — `EnemyGunCanHitAnotherCharacterUnderTheSameParent`,
  `EnemyGunPassesThroughFriendlySoldierInTheLine`, `FriendlySoldierIsReportedAsBlockingTheShot`.
* **Этап 3:** при ходьбе корпус смотрит по ходу и солдат НЕ стреляет; в бою стоит и довёрнут;
  ноги не скользят; нет мерцания idle↔walk; в режиме Fallback — одно предупреждение, укрытия и
  перебежки отключены.
* **Этап 4:** 4–6 солдат против игрока — видно чередование огня и перебежек, одновременно стреляют
  не более 3, никто не стоит столбом; с 3–4 `CoverPoint` — солдаты занимают и покидают укрытия.
* **Этап 5:** игрок скрылся за углом — враги идут к последней точке, проверяют 2–3 соседних,
  затем возвращаются к патрулю; одиночный выстрел игрока со 100 м не поднимает весь уровень.
* **Профилировщик:** 0 B GC alloc в кадре от `EnemyAI`, `EnemyAnimator`, `HitscanWeapon`
  при 10 стреляющих солдатах (Profiler → GC Alloc).

---

## Правки «бег, подтягивание, ППШ» (по правке игрока)

* **Смена позиции — бегом.** `SetSprintMode(true)` + скорость × `sprintSpeedMultiplier`
  в `UpdateChase`, `UpdateAlert` и при выходе на боевой якорь (`ExecuteFireRole` и
  fallback-бой). Солдат не топает на месте — быстро занимает место в строю и стреляет.
* **Подтягивание отряда.** Радиус тревоги `contactAlertRadius` (45 м, больше
  `squadAwarenessRadius`), задержка передачи контакта × `contactShareDelayScale` (0.25),
  осведомлённость от контакта 0.9. Тревога рассылается и при входе в бой, не только
  в погоню.
* **Почти все стреляют.** Жетонов атаки — `N-1` (у отряда из 5 стреляют 4), ротация
  жетона раз в 4 с. Роли у всех — «Огонь», манёвров/флангов больше нет.
* **ППШ.** У `GermanSoldier.prefab` убран дубль оружия (модель висела на бедре) —
  ствол остался только в руках. Засада от ППШ работает как раньше: при отказе
  немец свистит, `NoiseSystem` поднимает отряд, шестеро немцев выбегают на
  игрока (`raiderCount = 6`, `alertAllEnemies = 1`).
* **Бесконечный марш вперёд.** Когда цель видима и стрелять уже можно — якорь
  замораживается (`holdInPlace` в `UpdateCombatAnchor`): солдат доходит до своего
  места в строю и стоит, а не топает за уходящим якорем. Движение вперёд
  остаётся только когда цель далеко или её не видно.
* **Слух.** `maxHearingDistance` 90 → 140 м (и в коде, и в сцене), осведомлённость
  от звука растёт быстрее (0.95), услышавший сразу бежит на звук (`Alert` — спринт).
* **Стрельба.** Жетоны атаки — всем отряду (`N`), `maxFireAngle` 8° → 20°
  (солдат успевает довернуться и начать огонь).



## Рабочая система «отряд поднимается на бой» (собрано целиком)

1. **Увидел** — `FindBestVisibleTarget` (игрок всегда цель: отдельный реестр,
   слой цели игнорируется, ближнее обнаружение 10 м, FOV по корпусу).
2. **Позвал своих** — `NotifyAlliesOfContact`: точка и цель уходят всем союзникам
   в радиусе `contactAlertRadius` (80 м), реакция ~0.1-0.25 с.
3. **«Радио»** — стреляющий солдат каждые `radioInterval` (1.2 с) сообщает отряду,
   где цель: `RadioContact()` из `UpdateFiring` и при входе в бой.
4. **Услышал стрельбу своего** — `OnAllyGunfire`: выстрел союзника (шум с радиусом
   >= 12 м) слышен в `allyGunshotRadius` (120 м) -> идём на его цель, даже если бой
   не видно. Тихие звуки (шаги, попадания) игнорируются.
5. **Сбежался** — `Chase`/`Alert` на `RunSpeed` (chaseSpeed x sprintSpeedMultiplier
   = 3.5 м/с, выше порога «run» у аниматора — видно бег, а не шаг).
6. **Занял позицию** — место в строю (интервал >= `squadSeparationDistance`); при
   видимой цели в боевой дистанции якорь замораживается: солдат доходит до места
   и стоит, а не топает бесконечно вперёд.
7. **Стреляет** — жетон атаки есть у всех, `maxFireAngle` 20°, союзник на линии
   огня не мешает (хитскан проходит сквозь своих).
