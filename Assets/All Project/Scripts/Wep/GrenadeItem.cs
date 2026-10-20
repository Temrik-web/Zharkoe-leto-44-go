using System.Collections;
using UnityEngine;

/// <summary>Граната в руках: замах, бросок, взрыв. ПКМ — приготовиться, ЛКМ — бросок, X — выдернуть запал заранее.</summary>
[DisallowMultipleComponent]
public class GrenadeItem : HeldItem
{
    [Header("Снаряд")]
    [Tooltip("Префаб летящей гранаты. Пусто — будет создана копия модели из рук " +
             "с добавленным Rigidbody, коллайдером и ThrownGrenade.")]
    public GameObject grenadePrefab;

    [Tooltip("Откуда вылетает граната. Пусто — точка чуть впереди камеры.")]
    public Transform throwOrigin;

    [Tooltip("Сдвиг точки вылета относительно камеры, если Throw Origin не задан.")]
    public Vector3 throwOriginOffset = new Vector3(0.25f, -0.1f, 0.55f);

    [Header("Бросок")]
    [Tooltip("Сила обычного броска.")]
    public float throwForce = 15f;

    [Tooltip("Сила слабого броска (подкатить под ноги).")]
    public float weakThrowForce = 5.5f;

    [Tooltip("Разрешить слабый бросок по ЛКМ, пока держишь ПКМ.")]
    public bool allowWeakThrow = true;

    [Tooltip("Подъём броска над направлением взгляда, градусы. Дуга вместо прямой линии.")]
    public float throwUpwardAngle = 12f;

    [Tooltip("Добавлять скорость игрока к скорости гранаты — на бегу летит дальше.")]
    public bool inheritPlayerVelocity = true;

    [Tooltip("Начальное вращение гранаты в полёте.")]
    public Vector3 throwSpin = new Vector3(-7f, 1.5f, 0.5f);

    [Header("Запал")]
    [Tooltip("Время горения запала после броска.")]
    [Min(0.1f)] public float fuseTime = 3.8f;

    [Tooltip("Клавиша: выдернуть запал заранее (готовить в руке). " +
             "None — механика выключена. G не используем: он занят " +
             "выбрасыванием предметов из инвентаря (G + цифра).")]
    public KeyCode cookKey = KeyCode.X;

    [Tooltip("Взорвётся в руке, если передержать. Реалистично, но злобно.")]
    public bool explodeInHandsIfOvercooked = true;

    [Header("Тайминги анимации")]
    [Tooltip("Время замаха до момента вылета.")]
    public float windupTime = 0.32f;
    [Tooltip("Время проводки руки после вылета.")]
    public float followThroughTime = 0.22f;
    [Tooltip("Пауза перед появлением следующей гранаты в руке.")]
    public float rearmTime = 0.45f;

    [Header("Поза замаха")]
    public Vector3 windupPosition = new Vector3(0.42f, -0.12f, 0.18f);
    public Vector3 windupRotation = new Vector3(-38f, 22f, 12f);

    [Header("Поза броска")]
    public Vector3 releasePosition = new Vector3(0.1f, -0.05f, 0.72f);
    public Vector3 releaseRotation = new Vector3(22f, -14f, -8f);

    [Header("Поза выдернутого запала")]
    public Vector3 cookedPosition = new Vector3(0.2f, -0.26f, 0.42f);
    public Vector3 cookedRotation = new Vector3(-14f, 12f, 6f);

    [Header("Отдача руки")]
    public Vector3 throwKickPosition = new Vector3(-0.05f, 0.03f, -0.12f);
    public Vector3 throwKickRotation = new Vector3(-9f, 4f, 0f);

    [Header("Взрыв")]
    [Tooltip("Префаб эффекта взрыва: партиклы, свет, дым. Например BigExplosion " +
             "из ParticlePack. Настройки этого блока применяются к брошенной " +
             "гранате и перекрывают значения на ThrownGrenade.")]
    public GameObject explosionEffectPrefab;

    [Tooltip("Через сколько секунд удалить эффект взрыва.")]
    public float explosionEffectLifetime = 2.5f;

    [Tooltip("Звук взрыва.")]
    public AudioClip explosionSound;
    [Range(0f, 1f)] public float explosionVolume = 1f;

    [Tooltip("Звук удара гранаты о землю.")]
    public AudioClip bounceSound;
    [Range(0f, 1f)] public float bounceVolume = 0.5f;

    [Tooltip("Урон в центре взрыва. У игрока 100 HP: 150 убивает примерно в 2.6 м.")]
    public float explosionDamage = 150f;

    [Tooltip("Радиус поражения, метры.")]
    public float explosionDamageRadius = 6f;

    [Tooltip("Доля урона на границе радиуса. 0.35 при уроне 150 — это 52 урона на краю.")]
    [Range(0f, 1f)] public float explosionEdgeDamageFactor = 0.35f;

    [Tooltip("Задержка между попаданием пули в лежащую гранату и взрывом, сек. " +
             "Реалистичная пауза срабатывания.")]
    [Range(0f, 1f)] public float shotDetonationDelay = 0.12f;

    [Header("Расход из инвентаря")]
    [Tooltip("Списывать одну гранату из инвентаря при броске.")]
    public bool consumeFromInventory = true;

    [Tooltip("Id оружия, по которому ищется предмет в инвентаре. " +
             "Пусто — берётся Weapon Id с EquippableWeapon на этом же объекте.")]
    public string inventoryWeaponId = "";

    [Header("Звуки")]
    public AudioClip pinPullSound;
    public AudioClip throwSound;
    public AudioClip rearmSound;
    [Range(0f, 1f)] public float soundVolume = 0.85f;

    [Header("Отладка")]
    public bool logActions = true;
    private EquippableWeapon equippable;
    private Coroutine throwRoutine;

    private bool isThrowing;
    private bool pinPulled;
    private float cookTimer;

    /// <summary>Идёт ли бросок прямо сейчас.</summary>
    public bool IsThrowing => isThrowing;

    /// <summary>Выдернут ли запал (граната «горит» в руке).</summary>
    public bool IsPinPulled => pinPulled;

    /// <summary>Сколько осталось до взрыва, если запал выдернут.</summary>
    public float CookTimeLeft => pinPulled ? Mathf.Max(0f, fuseTime - cookTimer) : fuseTime;
    protected override void Awake()
    {
        base.Awake();
        equippable = GetComponent<EquippableWeapon>();
        if (equippable == null) equippable = GetComponentInParent<EquippableWeapon>();
        if (string.IsNullOrEmpty(inventoryWeaponId) && equippable != null)
            inventoryWeaponId = equippable.weaponId;
        DisableHeldPhysics();
    }

    protected override void OnEnable()
    {
        base.OnEnable();

        isThrowing = false;
        pinPulled = false;
        cookTimer = 0f;
        SetModelVisible(true);
    }

    protected override void OnDisable()
    {
        base.OnDisable();

        if (throwRoutine != null)
        {
            StopCoroutine(throwRoutine);
            throwRoutine = null;
        }

        // Спрятать гранату с выдернутым запалом — не способ отменить запал.
        // Раньше это давало бесплатный сброс: выдернул кольцо, нажал 0, взял снова.
        if (pinPulled && !isThrowing) DropLiveGrenade();

        isThrowing = false;
        pinPulled = false;
        cookTimer = 0f;
        ClearPoseOverride();
    }

    // Сброс запала убором в карман запрещён — роняем живую под ноги, иначе был бесплатный сброс кольцом
    void DropLiveGrenade()
    {
        if (!Application.isPlaying) return;
        if (!gameObject.scene.isLoaded) return;
        float remainingFuse = Mathf.Max(0.3f, fuseTime - cookTimer);

        Transform cam = playerCamera != null ? playerCamera.transform : transform;
        Vector3 dropPoint = cam.position + cam.forward * 0.4f - cam.up * 0.35f;

        GameObject instance = grenadePrefab != null
            ? Instantiate(grenadePrefab, dropPoint, Quaternion.identity)
            : BuildRuntimeGrenade(dropPoint, cam.forward);

        ThrownGrenade thrown = instance.GetComponent<ThrownGrenade>();
        if (thrown == null) thrown = instance.AddComponent<ThrownGrenade>();
        ApplyExplosionSettings(thrown);

        GameObject thrower = Controller != null ? Controller.gameObject : gameObject;
        thrown.Launch(cam.forward * 1.2f, Vector3.zero, thrower, remainingFuse);

        ConsumeGrenade();

        if (logActions)
            Debug.LogWarning($"[Grenade] Убрал гранату с выдернутым запалом — она упала под ноги " +
                             $"(до взрыва {remainingFuse:0.##} с).");
    }

    // =====================================================================
    /// <summary>
    /// Взорвать лежащую в мире гранату-пикап выстрелом: с минимальной реалистичной
    /// задержкой срабатывания. Настройки взрыва — этого предмета. Вызывается из Wep.
    /// </summary>
    public void DetonatePickup(GameObject pickupObject, GameObject shooter)
    {
        if (pickupObject == null) return;

        foreach (Pickup p in pickupObject.GetComponentsInChildren<Pickup>(true))
        {
            p.enabled = false;
            Destroy(p);
        }
        foreach (ZharkoeLeto44.AI.DroppedWeapon d in pickupObject.GetComponentsInChildren<ZharkoeLeto44.AI.DroppedWeapon>(true))
            Destroy(d);

        ThrownGrenade thrown = pickupObject.GetComponent<ThrownGrenade>();
        if (thrown == null) thrown = pickupObject.AddComponent<ThrownGrenade>();
        ApplyExplosionSettings(thrown);
        // Launch ради привязки Thrower (реакция ИИ, засчитывание урона) и запала-паузы.
        thrown.Launch(Vector3.zero, Vector3.zero, shooter, Mathf.Max(0.01f, shotDetonationDelay));
        if (shotDetonationDelay <= 0f) thrown.Explode();

        if (logActions) Debug.Log("[Grenade] Выстрел по лежащей гранате — детонация с паузой срабатывания.");
    }

    // =====================================================================
    protected override void HandleInput()
    {
        if (isThrowing) return;

        if (cookKey != KeyCode.None && !pinPulled && Input.GetKeyDown(cookKey))
            PullPin();

        if (Input.GetMouseButtonDown(0))
        {
            bool weak = allowWeakThrow && Input.GetMouseButton(1);
            Throw(weak ? weakThrowForce : throwForce);
        }
    }

    protected override void Update()
    {
        base.Update();
        if (!pinPulled || isThrowing) return;
        cookTimer += Time.deltaTime;
        SetPoseOverride(cookedPosition, cookedRotation, 0.7f);
        if (cookTimer >= fuseTime && explodeInHandsIfOvercooked)
            ExplodeInHands();
    }
    public void PullPin()
    {
        if (pinPulled || isThrowing) return;

        pinPulled = true;
        cookTimer = 0f;
        PlaySound(pinPullSound, soundVolume);
        AddKick(new Vector3(-0.02f, 0f, -0.03f), new Vector3(-3f, 2f, 0f));
    }

    /// <summary>Бросить гранату с заданной силой.</summary>
    public void Throw(float force)
    {
        if (isThrowing) return;
        if (!HasGrenadeInInventory()) return;

        throwRoutine = StartCoroutine(ThrowSequence(force));
    }

    IEnumerator ThrowSequence(float force)
    {
        isThrowing = true;
        // Модель ещё едет к hip-позе после экипировки — стартуем замах строго от неё
        Transform model = itemModel != null ? itemModel : transform;
        model.localPosition = hipPosition;
        model.localRotation = Quaternion.Euler(hipRotation);
        SetPoseOverride(hipPosition, hipRotation, 1f);
        float t = 0f;
        while (t < windupTime)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.01f, windupTime));
            SetPoseOverride(
                Vector3.Lerp(hipPosition, windupPosition, k),
                Vector3.Lerp(hipRotation, windupRotation, k),
                k);
            yield return null;
        }

        if (!pinPulled) PullPin();
        SetPoseOverride(releasePosition, releaseRotation, 1f);
        SpawnGrenade(force);
        SetModelVisible(false);
        PlaySound(throwSound, soundVolume);
        AddKick(throwKickPosition, throwKickRotation);
        bool hasMore = ConsumeGrenade();
        t = 0f;
        while (t < followThroughTime)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.01f, followThroughTime));
            SetPoseOverride(
                Vector3.Lerp(releasePosition, hipPosition, k),
                Vector3.Lerp(releaseRotation, hipRotation, k),
                1f - k);
            yield return null;
        }

        ClearPoseOverride();
        pinPulled = false;
        cookTimer = 0f;

        if (!hasMore)
        {
            isThrowing = false;
            throwRoutine = null;
            if (WeaponSlotManager.Instance != null) WeaponSlotManager.Instance.Holster();
            else SetModelVisible(false);
            yield break;
        }
        if (rearmTime > 0f) yield return new WaitForSeconds(rearmTime);

        SetModelVisible(true);
        PlaySound(rearmSound, soundVolume);

        isThrowing = false;
        throwRoutine = null;
    }

    void SpawnGrenade(float force)
    {
        Vector3 origin = GetThrowOrigin();
        Vector3 direction = GetThrowDirection();
        GameObject instance = grenadePrefab != null
            ? Instantiate(grenadePrefab, origin, Quaternion.LookRotation(direction))
            : BuildRuntimeGrenade(origin, direction);
        ThrownGrenade thrown = instance.GetComponent<ThrownGrenade>();
        if (thrown == null) thrown = instance.AddComponent<ThrownGrenade>();
        ApplyExplosionSettings(thrown);
        Vector3 velocity = direction * force;
        if (inheritPlayerVelocity) velocity += PlayerVelocity;
        float remainingFuse = pinPulled ? Mathf.Max(0.15f, fuseTime - cookTimer) : fuseTime;
        GameObject thrower = Controller != null ? Controller.gameObject : gameObject;
        thrown.Launch(velocity, throwSpin * Mathf.Deg2Rad * 30f, thrower, remainingFuse);
    }
    // Настройки взрыва переносим на снаряд-копию: у AddComponent иначе только дефолты без эффекта и звука
    void ApplyExplosionSettings(ThrownGrenade thrown)
    {
        if (thrown == null) return;

        if (explosionEffectPrefab != null)
        {
            thrown.explosionEffectPrefab = explosionEffectPrefab;
            thrown.effectLifetime = explosionEffectLifetime;
        }

        if (explosionSound != null)
        {
            thrown.explosionSound = explosionSound;
            thrown.explosionVolume = explosionVolume;
        }

        if (bounceSound != null)
        {
            thrown.bounceSound = bounceSound;
            thrown.bounceVolume = bounceVolume;
        }

        thrown.damage = explosionDamage;
        thrown.damageRadius = explosionDamageRadius;
        thrown.edgeDamageFactor = explosionEdgeDamageFactor;
        thrown.impactDetonationDelay = shotDetonationDelay;
    }

    // Копия модели из рук как снаряд — чтобы летала даже без префаба
    GameObject BuildRuntimeGrenade(Vector3 origin, Vector3 direction)
    {
        Vector3 worldScale = transform.lossyScale;
        Quaternion rotation = Quaternion.LookRotation(direction);
        GameObject copy;
        HeldItem.SuppressAttachOnAwake = true;
        try
        {
            copy = Instantiate(gameObject, origin, rotation);
        }
        finally
        {
            HeldItem.SuppressAttachOnAwake = false;
        }
        copy.name = name + " (Thrown)";
        // Destroy срабатывает в конце кадра — скрипты гасим сразу, иначе Update вернёт копию в руки
        foreach (HeldItem held in copy.GetComponentsInChildren<HeldItem>(true))
        {
            held.enabled = false;
            Destroy(held);
        }
        foreach (EquippableWeapon eq in copy.GetComponentsInChildren<EquippableWeapon>(true))
        {
            eq.enabled = false;
            Destroy(eq);
        }
        foreach (Pickup pickup in copy.GetComponentsInChildren<Pickup>(true))
        {
            pickup.enabled = false;
            Destroy(pickup);
        }
        foreach (AudioSource src in copy.GetComponentsInChildren<AudioSource>(true)) Destroy(src);

        copy.transform.SetParent(null, false);
        copy.transform.position = origin;
        copy.transform.rotation = rotation;
        copy.transform.localScale = worldScale;

        foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        copy.SetActive(true);

        EnsureProjectileColliders(copy);

        return copy;
    }

    void EnsureProjectileColliders(GameObject target)
    {
        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
        bool hasSolid = false;
        foreach (Collider c in colliders)
        {
            if (c == null) continue;
            c.isTrigger = false;
            c.enabled = true;
            hasSolid = true;
        }
        if (hasSolid) return;
        float radius = 0.08f;
        Renderer rend = target.GetComponentInChildren<Renderer>();
        if (rend != null) radius = Mathf.Max(0.03f, rend.bounds.extents.magnitude * 0.5f);
        SphereCollider sphere = target.AddComponent<SphereCollider>();
        sphere.radius = radius;
    }

    Vector3 GetThrowOrigin()
    {
        if (throwOrigin != null) return throwOrigin.position;

        Transform cam = playerCamera != null ? playerCamera.transform : transform;
        return cam.position
               + cam.right * throwOriginOffset.x
               + cam.up * throwOriginOffset.y
               + cam.forward * throwOriginOffset.z;
    }

    Vector3 GetThrowDirection()
    {
        Transform cam = playerCamera != null ? playerCamera.transform : transform;
        return Quaternion.AngleAxis(-throwUpwardAngle, cam.right) * cam.forward;
    }

    bool HasGrenadeInInventory()
    {
        if (!consumeFromInventory) return true;
        InventorySystem inv = InventorySystem.Instance;
        if (inv == null || string.IsNullOrEmpty(inventoryWeaponId)) return true;
        return inv.CountWeaponItem(inventoryWeaponId) > 0;
    }
    /// <summary>Списать одну. True если ещё осталось.</summary>
    bool ConsumeGrenade()
    {
        if (!consumeFromInventory) return true;

        InventorySystem inv = InventorySystem.Instance;
        if (inv == null || string.IsNullOrEmpty(inventoryWeaponId)) return true;

        ItemData item = inv.GetWeaponItem(inventoryWeaponId);
        if (item == null) return false;

        inv.RemoveItem(item, 1);
        return inv.CountWeaponItem(inventoryWeaponId) > 0;
    }

    // =====================================================================
    void ExplodeInHands()
    {
        if (logActions) Debug.LogWarning("[Grenade] Передержал — взрыв в руках.");

        Vector3 point = playerCamera != null ? playerCamera.transform.position : transform.position;

        GameObject instance = grenadePrefab != null
            ? Instantiate(grenadePrefab, point, Quaternion.identity)
            : BuildRuntimeGrenade(point, Vector3.forward);

        ThrownGrenade thrown = instance.GetComponent<ThrownGrenade>();
        if (thrown == null) thrown = instance.AddComponent<ThrownGrenade>();
        ApplyExplosionSettings(thrown);

        // Thrower не указываем: игрок должен получить урон за свою ошибку
        thrown.Launch(Vector3.zero, Vector3.zero, null, 0.01f);
        thrown.Explode();
        pinPulled = false;
        cookTimer = 0f;
        ConsumeGrenade();
        if (WeaponSlotManager.Instance != null) WeaponSlotManager.Instance.Holster();
    }
    // Rigidbody вырвал бы модель из держателя — в руках физику гасим, коллайдер оставляем для копии-снаряда
    void DisableHeldPhysics()
    {
        foreach (Rigidbody rb in GetComponentsInChildren<Rigidbody>(true))
        {
            if (rb == null) continue;
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        foreach (Collider c in GetComponentsInChildren<Collider>(true))
        {
            if (c == null) continue;
            // Коллайдер оставляем на объекте: он нужен снаряду-копии
            c.enabled = false;
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Запомнить текущую позу как замах")]
    void CaptureWindupPose()
    {
        windupPosition = transform.localPosition;
        windupRotation = transform.localEulerAngles;
    }

    [ContextMenu("Запомнить текущую позу как бросок")]
    void CaptureReleasePose()
    {
        releasePosition = transform.localPosition;
        releaseRotation = transform.localEulerAngles;
    }
#endif
}
