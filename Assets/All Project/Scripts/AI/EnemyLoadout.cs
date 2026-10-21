using UnityEngine;

namespace ZharkoeLeto44.AI
{
    [DisallowMultipleComponent]
    public sealed class EnemyLoadout : MonoBehaviour
    {
        [Header("Что дать в руки")]
        [Tooltip("Префаб оружия. На нём (или его детях) желательно иметь HitscanWeapon — " +
                 "если его нет, компонент добавит его сам. " +
                 "ВАЖНО: если в модели уже есть оружие (Mp40 в руке) — префаб ИГНОРИРУЕТСЯ, " +
                 "лишнее оружие не создаётся. Немец со «своей» моделью стреляет из неё.")]
        [SerializeField] private GameObject weaponPrefab;
        [Tooltip("Уже поставленное вручную оружие. Если задано — префаб игнорируется.")]
        [SerializeField] private HitscanWeapon existingWeapon;

        [Header("Куда крепить")]
        [Tooltip("Сокет в руке. Пусто — попробуем найти кость правой руки у Animator, " +
                 "иначе создадим сокет автоматически.")]
        [SerializeField] private Transform handSocket;
        [Tooltip("Искать кость руки у humanoid-аниматора, если сокет не задан.")]
        [SerializeField] private bool useAnimatorHandBone = true;
        [Tooltip("Использовать левую руку вместо правой.")]
        [SerializeField] private bool leftHanded = false;
        [Tooltip("Позиция сокета, если кость не найдена (локально от врага).")]
        [SerializeField] private Vector3 fallbackSocketPosition = new(0.22f, 1.35f, 0.28f);

        [Header("Подгонка оружия в руке")]
        [SerializeField] private Vector3 weaponLocalPosition = Vector3.zero;
        [SerializeField] private Vector3 weaponLocalEuler = Vector3.zero;
        [SerializeField] private Vector3 weaponLocalScale = Vector3.one;

        [Header("Дуло")]
        [Tooltip("Имена, по которым ищется дуло среди детей оружия.")]
        [SerializeField]
        private string[] muzzleNameCandidates = { "Muzzle", "MuzzlePoint", "FirePoint", "Barrel", "Fire" };
        [Tooltip("Если дуло не найдено — создать его в конце модели оружия.")]
        [SerializeField] private bool createMuzzleIfMissing = true;

        [Header("Слои и коллайдеры оружия")]
        [Tooltip("Перевести оружие на слой врага — чтобы его собственные коллайдеры " +
                 "не мешали рейкастам зрения и стрельбы.")]
        [SerializeField] private bool matchOwnerLayer = true;
        [Tooltip("Отключить коллайдеры оружия в руках (иначе они бьются о навмеш и стены).")]
        [SerializeField] private bool disableWeaponColliders = true;

        [Header("Выброс при смерти")]
        [SerializeField] private bool dropWeaponOnDeath = true;
        [Tooltip("Сколько скорости шага забрать в падение (м/с). Стоит — просто роняет.")]
        [SerializeField, Min(0f)] private float dropMoveInherit = 1.5f;
        [Tooltip("Легкий дрейф от тела при выбросе, мин/макс (м/с) — падает рядом и чуть скользит.")]
        [SerializeField] private Vector2 dropShove = new(0.1f, 0.25f);
        [Tooltip("Скорость доворота на бок в полете (рад/с): ствол ровно кренится " +
                 "вокруг длинной оси и ложится боком, а не плюхается на дуло.")]
        [SerializeField, Range(0f, 8f)] private float dropRollRate = 0.8f;
        [Tooltip("Через сколько секунд убрать выброшенное оружие. 0 — не убирать.")]
        [SerializeField, Min(0f)] private float dropLifetime = 30f;
        [Tooltip("Масса выброшенного оружия.")]
        [SerializeField, Min(0.1f)] private float dropMass = 3.5f;

        [Tooltip("Пауза перед включением гравитации. В момент смерти оружие ещё стоит " +
                 "внутри коллайдера тела и пола — мгновенная физика выталкивает его " +
                 "под землю. Пауза даёт кадры на выход из чужой геометрии.")]
        [SerializeField, Min(0f)] private float dropGravityDelay = 0.5f;

        [Tooltip("Слои пола, по которым выброшенное оружие проверяет, не провалилось ли оно.")]
        [SerializeField] private LayerMask dropGroundMask = ~0;

        [Tooltip("Объект, который выпадает при смерти (например, DropMp40 с правильным " +
                  "коллайдером). Если задан — роняется он, а не оружие из рук: " +
                  "меш оружия из рук убирается, спавнится этот объект.")]
        [SerializeField] private GameObject dropObject;

        [Tooltip("Случайный разброс точки дропа по Y и Z (X не трогаем) — ложится около врага.")]
        [SerializeField, Min(0f)] private float dropScatter = 0.4f;
        [Tooltip("Поправка разворота дропа (градусы): если модель Drop Object смотрит " +
                  "не туда же, куда ручное оружие (напр. длинная ось по X, а не по Z).")]
        [SerializeField] private Vector3 dropRotationOffset;

        [Header("Старое оружие в руке (убирается только после смерти)")]
        [Tooltip("Точные имена старых мешей MP40 в руке. При жизни оружие НЕ трогается. " +
                 "После смерти эти объекты убираются из рук, вместо них выпадает Drop Object.")]
        [SerializeField] private string[] legacyWeaponNames =
        {
            "MP40.001_VC_MP40.mo_VC_MP40.md.001",
            "MP40.003_VC_MP40.mo_VC_MP40.md.003",
            "MP40_VC_MP40.mo_VC_MP40.md.001"
        };
        [Tooltip("Убирать любых детей, чьё имя содержит эти подстроки " +
                 "(ловит остальные куски MP40: .002, .013 и т.п.). Пусто — выключить.")]
        [SerializeField] private string[] legacyWeaponContains = { "MP40" };

        [Header("Коллайдер выпавшего оружия")]
        [Tooltip("Центр BoxCollider у выпавшего оружия (в локальных координатах объекта).")]
        [SerializeField] private Vector3 droppedColliderCenter = new(0f, 0f, 0.3f);
        [Tooltip("Размер BoxCollider у выпавшего оружия (в локальных координатах объекта).")]
        [SerializeField] private Vector3 droppedColliderSize = new(0.12f, 0.18f, 1f);

        [Header("Звук экипировки")]
        [SerializeField] private AudioClip equipSound;

        public HitscanWeapon Weapon { get; private set; }

        public Transform Socket => handSocket;

        private EnemyVoice _voice;
        private EnemyMotor _motor;
        private EnemyAI _ai;
        private GameObject _spawnedWeaponRoot;
        private bool _weaponDropped;
        private Transform _adoptedVisual;

        private void Awake()
        {
            _voice = GetComponent<EnemyVoice>();
            _motor = GetComponent<EnemyMotor>();
            _ai = GetComponent<EnemyAI>();
            ResolveSocket();
            EquipInitialWeapon();
        }

        private bool IsLegacyWeaponName(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            if (legacyWeaponNames != null)
                foreach (string exact in legacyWeaponNames)
                {
                    if (string.IsNullOrEmpty(exact)) continue;
                    if (n == exact) return true;
                }
            if (legacyWeaponContains != null)
                foreach (string part in legacyWeaponContains)
                {
                    if (string.IsNullOrEmpty(part)) continue;
                    if (n.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            return false;
        }

        /// <summary>
        /// Убирает старое оружие из рук ТОЛЬКО после смерти: объекты с legacy-именами
        /// сносятся, вместо них выпадает Drop Object. При жизни оружие не трогается.
        /// keepRoot (ручное оружие без Drop Object) не трогаем — оно само и роняется.
        /// </summary>
        private void DestroyLegacyLeftovers(Transform keepRoot)
        {
            Transform[] all = GetComponentsInChildren<Transform>(true);
            foreach (Transform t in all)
            {
                if (t == null || t == transform) continue;
                if (keepRoot != null && (t == keepRoot || t.IsChildOf(keepRoot))) continue;
                if (!IsLegacyWeaponName(t.name)) continue;
                Destroy(t.gameObject);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (dropShove.magnitude < 0.1f) dropShove = new Vector2(0.1f, 0.25f);
        }
#endif

        private Vector3 DropLaunchVelocity(Vector3 fromPos)
        {
            Vector3 inherit = Vector3.zero;
            float up = 0.15f;
            if (_motor != null)
            {
                Vector3 flat = _motor.Velocity;
                flat.y = 0f;
                if (flat.magnitude > 0.3f)
                {
                    inherit = Vector3.ClampMagnitude(flat, dropMoveInherit);
                    up = 0.6f;
                }
            }
            Vector3 outward = fromPos - transform.position;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.01f) outward = transform.forward;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.01f) outward = Vector3.forward;
            float shove = Random.Range(Mathf.Min(dropShove.x, dropShove.y),
                                       Mathf.Max(dropShove.x, dropShove.y));
            return inherit + outward.normalized * shove + Vector3.up * up;
        }

        private Vector3 DropRollSpin(BoxCollider box, Quaternion spawnRot)
        {
            if (box == null || dropRollRate <= 0f) return Vector3.zero;
            Vector3 s = box.size;
            Vector3 local = (s.x >= s.y && s.x >= s.z) ? Vector3.right
                : (s.y >= s.z ? Vector3.up : Vector3.forward);
            float side = Random.value < 0.5f ? 1f : -1f;
            return (spawnRot * local).normalized * (dropRollRate * side);
        }

        private void ResolveSocket()
        {
            if (handSocket != null) return;

            if (useAnimatorHandBone)
            {
                var animator = GetComponentInChildren<Animator>();
                if (animator != null && animator.isHuman)
                {
                    Transform bone = animator.GetBoneTransform(
                        leftHanded ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);

                    if (bone != null)
                    {
                        handSocket = CreateSocket(bone, "WeaponSocket", Vector3.zero);
                        return;
                    }
                }
            }

            Vector3 position = fallbackSocketPosition;
            if (leftHanded) position.x = -Mathf.Abs(position.x);

            handSocket = CreateSocket(transform, "WeaponSocket", position);
        }

        private static Transform CreateSocket(Transform parent, string socketName, Vector3 localPosition)
        {
            Transform existing = parent.Find(socketName);
            if (existing != null) return existing;

            var socket = new GameObject(socketName).transform;
            socket.SetParent(parent, false);
            // Слой — как у родителя: иначе сокет/дуло остаются на Default
            // (новый GameObject всегда создаётся на слое 0) и попадают
            // в рейкасты игрока.
            socket.gameObject.layer = parent.gameObject.layer;
            socket.localPosition = localPosition;
            socket.localRotation = Quaternion.identity;
            return socket;
        }

        private void EquipInitialWeapon()
        {
            if (existingWeapon != null)
            {
                AdoptWeapon(existingWeapon, reparent: true, playSound: false);
                return;
            }

            // Оружие УЖЕ есть на модели (например, Mp40 в руке) — второй экземпляр
            // не создаём: иначе у немца в руках появляется лишнее оружие.
            var alreadyPresent = GetComponentInChildren<HitscanWeapon>(true);
            if (alreadyPresent != null)
            {
                AdoptWeapon(alreadyPresent, reparent: false, playSound: false);
                return;
            }

            if (weaponPrefab == null) return;

            // Модель уже держит оружие мешем (Mp40 в руке), но без компонента
            // HitscanWeapon. Добавляем компонент в держатель — модель оружия
            // НЕ создаём, она уже настроена в модели.
            Transform holder = FindWeaponHolder();
            if (holder != null)
            {
                HitscanWeapon created = holder.gameObject.AddComponent<HitscanWeapon>();
                AdoptWeapon(created, reparent: false, playSound: false);
                return;
            }

            if (weaponPrefab.transform.IsChildOf(transform))
            {
                HitscanWeapon existing = weaponPrefab.GetComponent<HitscanWeapon>() ??
                    weaponPrefab.GetComponentInChildren<HitscanWeapon>(true);
                if (existing == null)
                {
                    existing = weaponPrefab.AddComponent<HitscanWeapon>();
                    Debug.Log($"[EnemyLoadout] {name}: HitscanWeapon добавлен на уже " +
                              "существующую модель оружия — второй экземпляр не создаётся.", this);
                }

                AdoptWeapon(existing, reparent: true, playSound: false);
                return;
            }

            EquipWeapon(weaponPrefab, playSound: false);
        }

        /// <summary>
        /// Держатель оружия в модели: объект «Weapon»/«Gun»/«Оружие», который
        /// уже стоит на модели (в нём или в его детях — Mp40 и т.п.).
        /// Ищем ТОЛЬКО среди прямых детей корня: кости скелета не трогаем.
        /// </summary>
        private Transform FindWeaponHolder()
        {
            foreach (Transform child in transform)
            {
                if (child == null) continue;
                string n = child.name.ToLowerInvariant();
                if (n.Contains("weapon") || n.Contains("gun") || n.Contains("оруж"))
                {
                    if (child.GetComponent<HitscanWeapon>() == null) return child;
                }
            }

            // Держателя нет — сам меш оружия становится носителем компонента.
            return FindModelWeaponMesh();
        }

        public HitscanWeapon EquipWeapon(GameObject prefab, bool playSound = true)
        {
            if (prefab == null) return null;

            ResolveSocket();
            ClearCurrentWeapon();

            GameObject instance = Instantiate(prefab, handSocket);
            instance.name = prefab.name;
            _spawnedWeaponRoot = instance;

            HitscanWeapon weapon = instance.GetComponent<HitscanWeapon>() ??
                                   instance.GetComponentInChildren<HitscanWeapon>(true);

            if (weapon == null)
            {
                weapon = instance.AddComponent<HitscanWeapon>();
                Debug.Log($"[EnemyLoadout] {name}: на префабе «{prefab.name}» не было HitscanWeapon — " +
                          "компонент добавлен автоматически. Настрой урон и темп стрельбы в инспекторе " +
                          "префаба, чтобы значения не сбрасывались.", this);
            }

            AdoptWeapon(weapon, reparent: false, playSound);
            return weapon;
        }

        public void AdoptWeapon(HitscanWeapon weapon, bool reparent, bool playSound)
        {
            if (weapon == null) return;

            ResolveSocket();

            Transform weaponRoot = weapon.transform;

            if (reparent && weaponRoot.parent != handSocket)
            {
                weaponRoot.SetParent(handSocket, false);
                _spawnedWeaponRoot = weaponRoot.gameObject;
            }

            if (weaponRoot.parent == handSocket)
            {
                weaponRoot.localPosition = weaponLocalPosition;
                weaponRoot.localRotation = Quaternion.Euler(weaponLocalEuler);
                weaponRoot.localScale = weaponLocalScale;
            }

            weapon.gameObject.SetActive(true);
            weapon.enabled = true;

            TryAdoptVisual(weapon);
            PrepareMuzzle(weapon);
            PrepareAudio(weapon);
            PrepareCollidersAndLayer(weapon);

            weapon.ResetAmmo();
            Weapon = weapon;
            if (_ai != null) _ai.SetWeapon(weapon);

            if (playSound && equipSound != null)
            {
                if (_voice != null) _voice.PlayBodyOneShot(equipSound, 0.8f);
                else AudioSource.PlayClipAtPoint(equipSound, weaponRoot.position, 0.8f);
            }
        }

        private void ClearCurrentWeapon()
        {
            if (_spawnedWeaponRoot != null)
            {
                Destroy(_spawnedWeaponRoot);
                _spawnedWeaponRoot = null;
            }

            _adoptedVisual = null;
            Weapon = null;
        }

        /// <summary>
        /// Модель уже несёт оружие в руках (Mp40 и т.п.) — найти его.
        /// Возвращает null, если такого меша в модели нет.
        /// </summary>
        private Transform FindModelWeaponMesh()
        {
            foreach (Transform child in transform)
            {
                if (child == null) continue;

                if (IsLegacyWeaponName(child.name) &&
                    child.GetComponentInChildren<Renderer>(true) != null)
                    return child;

                Transform deep = FindWeaponMeshDeep(child);
                if (deep != null) return deep;
            }

            return null;
        }

        private Transform FindWeaponMeshDeep(Transform parent)
        {
            foreach (Transform child in parent)
            {
                if (child == null) continue;
                if (IsLegacyWeaponName(child.name) &&
                    child.GetComponentInChildren<Renderer>(true) != null)
                    return child;

                Transform deep = FindWeaponMeshDeep(child);
                if (deep != null) return deep;
            }

            return null;
        }

        private void TryAdoptVisual(HitscanWeapon weapon)
        {
            if (weapon == null) return;
            foreach (Renderer r in weapon.GetComponentsInChildren<Renderer>(true))
                if (r != null) return;

            // Модель УЖЕ держит оружие (меш Mp40 в руке). Оставляем его на месте
            // и НЕ переносим к стволу: визуал оторвался бы от руки и «появлялся»
            // рядом с немцем. Запоминаем меш, чтобы после смерти выронить его
            // из руки (см. HandleOwnerDeath).
            Transform modelWeapon = FindModelWeaponMesh();
            if (modelWeapon != null)
            {
                _adoptedVisual = modelWeapon;
                return;
            }

            Transform weaponRoot = weapon.transform;
            foreach (Transform child in transform)
            {
                if (child == null || child == handSocket || child == weaponRoot) continue;
                string n = child.name.ToLowerInvariant();
                if (n == "eyepoint" || n == "weapon" || n == "muzzle" ||
                    n == "weaponsocket" || n == "voicesource" || n == "bodysource") continue;
                if (!(n.StartsWith("mp") || n.StartsWith("pp") || n.StartsWith("gun") ||
                      n.StartsWith("kar") || n.StartsWith("stg") || n.StartsWith("mg") ||
                      n.Contains("ppsh") || n.Contains("rifle") || n.Contains("weapon") ||
                      n.Contains("automat") || n.Contains("автомат") || n.Contains("винтовк") ||
                      n.Contains("оружие"))) continue;
                bool hasRenderer = false;
                foreach (Renderer r in child.GetComponentsInChildren<Renderer>(true))
                    if (r != null) { hasRenderer = true; break; }
                if (!hasRenderer) continue;
                if (child.GetComponentInChildren<HitscanWeapon>(true) != null) continue;
                if (child.GetComponent<Animator>() != null) continue;
                child.SetParent(weaponRoot, true);
                if (matchOwnerLayer)
                    foreach (Transform t in child.GetComponentsInChildren<Transform>(true))
                        t.gameObject.layer = gameObject.layer;
                if (disableWeaponColliders)
                    foreach (Collider c in child.GetComponentsInChildren<Collider>(true))
                        c.enabled = false;
                _adoptedVisual = child;
                return;
            }
        }

        private void PrepareMuzzle(HitscanWeapon weapon)
        {
            if (weapon.HasMuzzle) return;

            Transform muzzle = FindMuzzle(weapon.transform);

            if (muzzle == null && createMuzzleIfMissing)
            {
                float forwardExtent = 0.5f;
                var renderers = weapon.GetComponentsInChildren<Renderer>();

                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                        bounds.Encapsulate(renderers[i].bounds);

                    Vector3 localCenter = weapon.transform.InverseTransformPoint(bounds.center);
                    Vector3 localExtents = weapon.transform.InverseTransformVector(bounds.extents);
                    forwardExtent = localCenter.z + Mathf.Abs(localExtents.z);
                }

                muzzle = CreateSocket(weapon.transform, "Muzzle",
                    new Vector3(0f, 0f, Mathf.Max(0.15f, forwardExtent)));
            }

            if (muzzle != null) weapon.SetMuzzle(muzzle);
        }

        private Transform FindMuzzle(Transform weaponRoot)
        {
            if (muzzleNameCandidates == null) return null;

            foreach (Transform child in weaponRoot.GetComponentsInChildren<Transform>(true))
            {
                foreach (string candidate in muzzleNameCandidates)
                {
                    if (string.IsNullOrEmpty(candidate)) continue;
                    if (child.name.IndexOf(candidate, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return child;
                }
            }

            return null;
        }

        private void PrepareAudio(HitscanWeapon weapon)
        {
            if (weapon.HasAudioSource) return;

            AudioSource source = weapon.GetComponent<AudioSource>();
            if (source == null) source = weapon.gameObject.AddComponent<AudioSource>();

            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 5f;
            source.maxDistance = 90f;

            weapon.SetAudioSource(source);
        }

        private void PrepareCollidersAndLayer(HitscanWeapon weapon)
        {
            if (matchOwnerLayer)
            {
                foreach (Transform child in weapon.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = gameObject.layer;
            }

            if (!disableWeaponColliders) return;

            foreach (Collider collider in weapon.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
        }

        public void HandleOwnerDeath()
        {
            HitscanWeapon weapon = Weapon;
            if (weapon == null) return;

            weapon.CancelReload();
            weapon.enabled = false;

            // ИИ больше не должен видеть это оружие: после дропа объект
            // уничтожается, а EnemyAI продолжал держать ссылку и читать
            // weapon.IsReloading → MissingReferenceException.
            DetachWeaponFromAI();

            if (!dropWeaponOnDeath) return;

            Transform weaponRoot = weapon.transform;

            // Невидимое ручное оружие при заданном Drop Object — штатная схема,
            // а не ошибка: предупреждаем только если и дропа нет (тогда после
            // смерти вообще ничего не выпадет). Иначе варнинг спамил каждую смерть.
            if (dropObject == null && _adoptedVisual == null && !HasRenderers(weaponRoot))
                Debug.LogWarning($"[EnemyLoadout] {name}: ручное оружие без модели и Drop Object " +
                                 "не задан — в бою стреляет невидимка, после смерти ничего не выпадет.", this);

            if (dropObject != null)
            {
                // Старое оружие из рук убирается полностью — выпадает только dropped.
                // weaponRoot исключаем: его поза нужна для спавна, его снесёт SpawnDropObject.
                DestroyLegacyLeftovers(weaponRoot);
                SpawnDropObject(weaponRoot);
                _spawnedWeaponRoot = null;
                Weapon = null;
                _weaponDropped = true;
                return;
            }

            // Оружие осталось в руках модели (Mp40) и не переносилось к стволу.
            // Роняем визуал отдельно: копия падает на землю, оригинал уходит
            // из рук трупа. Невидимый держатель оружия тогда не нужен.
            if (_adoptedVisual != null && !_adoptedVisual.IsChildOf(weaponRoot))
            {
                SpawnVisualDrop();
                _adoptedVisual = null;
                if (weaponRoot != transform) Destroy(weaponRoot.gameObject);
                _spawnedWeaponRoot = null;
                Weapon = null;
                _weaponDropped = true;
                return;
            }

            weaponRoot.SetParent(null, true);
            ComputeDropPose(weaponRoot, out _, out Quaternion dropRot);
            if (_adoptedVisual == null) weaponRoot.rotation = dropRot;
            bool hasCollider = false;
            foreach (Collider collider in weaponRoot.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = true;
                collider.isTrigger = false;
                hasCollider = true;
            }

            if (!hasCollider) hasCollider = AddFallbackCollider(weaponRoot.gameObject);

            var body = weaponRoot.GetComponent<Rigidbody>();
            if (body == null) body = weaponRoot.gameObject.AddComponent<Rigidbody>();

            body.mass = dropMass;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            if (matchOwnerLayer)
                foreach (Transform child in weaponRoot.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = 0;

            if (!hasCollider)
            {
                Debug.LogWarning($"[EnemyLoadout] {name}: у выброшенного оружия нет ни одного " +
                                 "коллайдера — оно провалится под пол. Добавь коллайдер на префаб оружия.",
                                 this);
            }

            BoxCollider fitBox = weaponRoot.GetComponent<BoxCollider>()
                ?? weaponRoot.GetComponentInChildren<BoxCollider>();
            // GetComponent, а не AddComponent: на префабе оружия уже может быть
            // DroppedWeapon, а он [DisallowMultipleComponent] — AddComponent
            // бросил бы ошибку и оружие осталось бы без дропа.
            var dropper = weaponRoot.GetComponent<DroppedWeapon>();
            if (dropper == null) dropper = weaponRoot.gameObject.AddComponent<DroppedWeapon>();
            dropper.Initialize(
                gravityDelay: dropGravityDelay,
                launchVelocity: DropLaunchVelocity(weaponRoot.position),
                spin: DropRollSpin(fitBox, weaponRoot.rotation),
                groundMask: dropGroundMask,
                lifetime: dropLifetime);

            _spawnedWeaponRoot = null;
            Weapon = null;
            _weaponDropped = true;
        }

        private static bool HasRenderers(Transform root)
        {
            if (root == null) return false;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                if (r != null) return true;
            return false;
        }

        /// <summary>Отписать ИИ от оружия (оно уничтожено/выброшено).</summary>
        private void DetachWeaponFromAI()
        {
            if (_ai == null) return;
            _ai.SetWeapon(null);
        }

        private void ComputeDropPose(Transform weaponRoot, out Vector3 position, out Quaternion rotation)
        {
            position = weaponRoot != null ? weaponRoot.position : transform.position;
            rotation = weaponRoot != null ? weaponRoot.rotation : transform.rotation;
            if (_adoptedVisual != null)
            {
                position = _adoptedVisual.position;
                rotation = _adoptedVisual.rotation;
            }
            rotation *= Quaternion.Euler(dropRotationOffset);
            position = ScatterDrop(position);
            return;
        }

        private Vector3 ScatterDrop(Vector3 position)
        {
            if (dropScatter <= 0f) return position;
            position.y += Random.Range(-dropScatter, dropScatter) * 0.3f;
            position.z += Random.Range(-dropScatter, dropScatter);
            return position;
        }

        // Флаг выхода: при остановке Play / закрытии сцены OnDestroy тоже
        // вызывается — без этой проверки страховка ниже создавала бы DropMp40
        // из деструктора (отсюда "Some objects were not cleaned up... DropMp40").
        private static bool s_quitting;
        private void OnApplicationQuit() => s_quitting = true;

        // Страховка: врага уничтожили без смерти (чистка сцены, рестарт) —
        // роняем дроп, чтобы оружие не пропадало вместе с телом.
        private void OnDestroy()
        {
            if (s_quitting) return;
            if (_weaponDropped || !dropWeaponOnDeath) return;
            if (Weapon == null) return;
            if (!Application.isPlaying) return;

            _weaponDropped = true;

            if (dropObject == null)
            {
                Debug.LogWarning($"[EnemyLoadout] {name} уничтожен без HandleOwnerDeath, " +
                                 "а Drop Object не задан — оружие пропало вместе с телом. " +
                                 "Задай Drop Object, чтобы выпадение работало и в этом случае.", this);
                return;
            }

            SpawnDropObject(Weapon.transform);
            Weapon = null;
            _spawnedWeaponRoot = null;
        }

        public void DropWeaponImmediate()
        {
            HitscanWeapon weapon = Weapon;
            if (weapon == null) return;

            _weaponDropped = true;

            Transform weaponRoot = weapon.transform;
            weapon.enabled = false;
            DetachWeaponFromAI();

            if (dropObject != null)
            {
                DestroyLegacyLeftovers(weaponRoot);
                SpawnDropObject(weaponRoot);
                _spawnedWeaponRoot = null;
                Weapon = null;
                return;
            }

            weaponRoot.SetParent(null, true);

            foreach (Collider collider in weaponRoot.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = true;
                collider.isTrigger = false;
            }

            foreach (Transform child in weaponRoot.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 0;

            // Порядок ВАЖЕН: сначала создаём DroppedWeapon (его Awake ставит тело
            // кинематическим), иначе импульс броска терялся: AddForce применялся
            // к телу, которое следующим же кадром становилось kinematic = true.
            // Теперь скорость броска передаётся в Initialize — как в HandleOwnerDeath.
            var dropper = weaponRoot.GetComponent<DroppedWeapon>();
            if (dropper == null) dropper = weaponRoot.gameObject.AddComponent<DroppedWeapon>();
            dropper.Initialize(
                gravityDelay: 0f,
                launchVelocity: DropLaunchVelocity(weaponRoot.position),
                spin: Vector3.zero,
                groundMask: dropGroundMask,
                lifetime: dropLifetime);

            _spawnedWeaponRoot = null;
            Weapon = null;
        }

        private void SpawnDropObject(Transform weaponRoot)
        {
            ComputeDropPose(weaponRoot, out Vector3 spawnPosition, out Quaternion spawnRotation);

            // Визуал из рук убираем: падать должен Drop Object (так обещает тултип
            // поля). Раньше здесь стоял early return вместе с SpawnVisualDrop() —
            // и назначенный в инспекторе Drop Object молча НЕ создавался, если у
            // модели был свой меш оружия (типичный случай: MP40 в руке).
            // Если визуал уже уничтожен DestroyLegacyLeftovers — он == null, и
            // эта ветка не выполняется (поведение не меняется).
            if (_adoptedVisual != null)
            {
                if (!_adoptedVisual.IsChildOf(weaponRoot))
                    Destroy(_adoptedVisual.gameObject);
                _adoptedVisual = null;
            }

            if (weaponRoot != null && weaponRoot != transform)
                Destroy(weaponRoot.gameObject);

            GameObject instance = Instantiate(dropObject, spawnPosition, spawnRotation);
            instance.name = dropObject.name;
            instance.transform.SetParent(null);
            instance.SetActive(true);
            foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 0;

            var body = instance.GetComponent<Rigidbody>();
            if (body == null) body = instance.AddComponent<Rigidbody>();

            body.mass = dropMass;
            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            foreach (Collider c in instance.GetComponentsInChildren<Collider>(true))
                c.enabled = true;
            ApplyDropCollider(instance);

            var dropper = instance.GetComponent<DroppedWeapon>();
            if (dropper == null) dropper = instance.AddComponent<DroppedWeapon>();

            dropper.Initialize(
                gravityDelay: dropGravityDelay,
                launchVelocity: DropLaunchVelocity(spawnPosition),
                spin: DropRollSpin(instance.GetComponent<BoxCollider>(), instance.transform.rotation),
                groundMask: dropGroundMask,
                lifetime: dropLifetime);
        }

        private void SpawnVisualDrop()
        {
            Transform visual = _adoptedVisual;
            if (visual == null) return;

            // Оружие лежит в руках (дочернее от кости) — у кости свой мелкий
            // масштаб. Копию ставим в мир с МИРОВЫМ масштабом, иначе ствол
            // вырастет в сто раз.
            Vector3 worldScale = visual.lossyScale;
            Quaternion worldRotation = visual.rotation;
            Vector3 worldPosition = ScatterDrop(visual.position);

            GameObject instance = Instantiate(visual.gameObject);
            instance.name = visual.name;
            instance.transform.SetParent(null, true);
            instance.transform.position = worldPosition;
            instance.transform.rotation = worldRotation;
            instance.transform.localScale = worldScale;
            instance.SetActive(true);

            foreach (HitscanWeapon gun in instance.GetComponentsInChildren<HitscanWeapon>(true))
            {
                gun.enabled = false;
                Destroy(gun);
            }
            foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 0;

            var body = instance.GetComponent<Rigidbody>();
            if (body == null) body = instance.AddComponent<Rigidbody>();

            body.mass = dropMass;
            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            BoxCollider box = instance.GetComponent<BoxCollider>();
            if (box == null) box = instance.AddComponent<BoxCollider>();
            if (!FitColliderToModel(box))
            {
                box.center = droppedColliderCenter;
                box.size = droppedColliderSize;
            }
            box.isTrigger = false;
            foreach (Collider c in instance.GetComponentsInChildren<Collider>(true))
            {
                if (c == box) continue;
                c.enabled = false;
            }

            var dropper = instance.GetComponent<DroppedWeapon>();
            if (dropper == null) dropper = instance.AddComponent<DroppedWeapon>();

            dropper.Initialize(
                gravityDelay: dropGravityDelay,
                launchVelocity: DropLaunchVelocity(instance.transform.position),
                spin: DropRollSpin(box, instance.transform.rotation),
                groundMask: dropGroundMask,
                lifetime: dropLifetime);

            // Оригинал убираем: копия уже лежит на земле, в руках трупа
            // оружия быть не должно.
            if (visual != null && visual.gameObject != instance)
                Destroy(visual.gameObject);
        }

        private void ApplyDropCollider(GameObject instance)
        {
            BoxCollider box = instance.GetComponent<BoxCollider>();
            if (box != null && box.size.sqrMagnitude > 0.0001f)
            {
                box.isTrigger = false;
                box.enabled = true;
            }
            else
            {
                if (box == null) box = instance.AddComponent<BoxCollider>();
                if (!FitColliderToModel(box))
                {
                    box.center = droppedColliderCenter;
                    box.size = droppedColliderSize;
                }
                box.isTrigger = false;
            }
            foreach (Collider c in instance.GetComponentsInChildren<Collider>(true))
            {
                if (c == box) continue;
                c.enabled = false;
            }
        }

        private static bool FitColliderToModel(BoxCollider box)
        {
            if (box == null) return false;
            if (!TryComputeModelBounds(box.transform, out Vector3 center, out Vector3 size))
                return false;
            box.center = center;
            box.size = size;
            return true;
        }

        private static bool TryComputeModelBounds(Transform t, out Vector3 center, out Vector3 size)
        {
            center = default;
            size = default;
            if (t == null) return false;
            Matrix4x4 toLocal = t.worldToLocalMatrix;
            Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            bool hasBounds = false;
            foreach (MeshFilter mf in t.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds lb = mf.sharedMesh.bounds;
                Matrix4x4 m = toLocal * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = new(
                        (i & 1) == 0 ? lb.min.x : lb.max.x,
                        (i & 2) == 0 ? lb.min.y : lb.max.y,
                        (i & 4) == 0 ? lb.min.z : lb.max.z);
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                    hasBounds = true;
                }
            }
            if (!hasBounds) return false;
            center = (min + max) * 0.5f;
            Vector3 s = max - min;
            size = new Vector3(
                Mathf.Max(0.02f, s.x),
                Mathf.Max(0.02f, s.y),
                Mathf.Max(0.02f, s.z));
            return true;
        }

        private static bool AddFallbackCollider(GameObject target)
        {
            if (target == null) return false;
            // БАГФИКС: раньше коллайдер создавался, а при неудаче подгонки тут же
            // сносился через Destroy(). До конца кадра он висел «зомби» и мог
            // попасть в кэш коллайдеров DroppedWeapon — следующий доступ
            // к col.bounds бросал MissingReferenceException при убийстве врага.
            // Теперь габариты считаем ДО создания компонента.
            if (!TryComputeModelBounds(target.transform, out Vector3 center, out Vector3 size))
                return false;
            var box = target.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
            return true;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Transform socket = handSocket;
            Vector3 position = socket != null
                ? socket.position
                : transform.TransformPoint(fallbackSocketPosition);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(position, 0.06f);

            if (Weapon != null && Weapon.HasMuzzle)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(position, Weapon.MuzzlePosition);
            }
        }
#endif
    }
}
