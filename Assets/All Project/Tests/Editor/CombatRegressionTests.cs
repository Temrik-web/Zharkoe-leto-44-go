using System.Collections;
using System.Reflection;
using ZharkoeLeto44.AI;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

public class CombatRegressionTests
{
    private GameObject root;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        root = new GameObject("Combat test group");
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Object.Destroy(root);
        yield return null;
        yield return new ExitPlayMode();
    }

    private CharacterHealth Target(string name, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform);
        go.transform.position = position;
        var health = go.AddComponent<CharacterHealth>();
        go.AddComponent<BoxCollider>();
        return health;
    }

    [Test]
    public void PlayerAdapterUsesTheSameHealthForDamageAndHealing()
    {
        var health = Target("Player", Vector3.zero);
        var adapter = health.gameObject.AddComponent<PlayerHealth>();
        int damageEvents = 0, deaths = 0;
        adapter.OnDamaged += _ => damageEvents++;
        adapter.OnDeath += () => deaths++;
        adapter.TakeDamage(25f, Vector3.back);
        Assert.That(health.CurrentHealth, Is.EqualTo(75f));
        adapter.Heal(10f);
        Assert.That(health.CurrentHealth, Is.EqualTo(85f));
        health.TakeDamage(new DamageInfo(200f, Vector3.zero, Vector3.forward, null));
        adapter.Heal(100f);
        Assert.That(adapter.IsDead, Is.True);
        Assert.That(adapter.HealthPercent, Is.Zero);
        Assert.That(damageEvents, Is.EqualTo(2));
        Assert.That(deaths, Is.EqualTo(1));
    }

    [Test]
    public void EnemyAdapterDoesNotKeepASecondHealthPool()
    {
        var health = Target("Enemy", Vector3.zero);
        var adapter = health.gameObject.AddComponent<Enemy>();
        int deaths = 0;
        adapter.OnDeath += () => deaths++;
        adapter.TakeDamage(40f);
        Assert.That(health.CurrentHealth, Is.EqualTo(60f));
        health.TakeDamage(new DamageInfo(60f, Vector3.zero, Vector3.forward, null));
        adapter.TakeDamage(20f);
        Assert.That(adapter.CurrentHealth, Is.Zero);
        Assert.That(adapter.IsDead, Is.True);
        Assert.That(deaths, Is.EqualTo(1));
    }

    [Test]
    public void ReenablingCharacterDoesNotHealOrResurrectIt()
    {
        var health = Target("Enemy", Vector3.zero);
        health.TakeDamage(new DamageInfo(30f, Vector3.zero, Vector3.forward, null));
        health.gameObject.SetActive(false);
        health.gameObject.SetActive(true);
        Assert.That(health.CurrentHealth, Is.EqualTo(70f));
        health.TakeDamage(new DamageInfo(100f, Vector3.zero, Vector3.forward, null));
        health.enabled = false;
        health.enabled = true;
        Assert.That(health.IsAlive, Is.False);
        health.ResetHealth();
        Assert.That(health.IsAlive, Is.True);
        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaximumHealth));
    }

    [Test]
    public void NestedDamageAndHealingCannotRepeatDeath()
    {
        var health = Target("Enemy", Vector3.zero);
        int deaths = 0;
        health.Died += _ => deaths++;
        health.Damaged += _ =>
        {
            Assert.That(health.IsAlive, Is.False);
            health.RestoreHealth(100f);
            health.TakeDamage(new DamageInfo(100f, Vector3.zero, Vector3.forward, null));
        };
        health.TakeDamage(new DamageInfo(100f, Vector3.zero, Vector3.forward, null));
        Assert.That(health.CurrentHealth, Is.Zero);
        Assert.That(deaths, Is.EqualTo(1));
    }

    [Test]
    public void ExplosionDamagesEachCharacterOnceUnderSharedParent()
    {
        var first = Target("First", Vector3.left);
        var second = Target("Second", Vector3.right);
        var limb = new GameObject("Limb");
        limb.transform.SetParent(first.transform, false);
        limb.AddComponent<BoxCollider>();
        int firstHits = 0, secondHits = 0;
        first.Damaged += _ => firstHits++;
        second.Damaged += _ => secondHits++;
        var grenade = CreateGrenade();
        Physics.SyncTransforms();
        ApplyExplosionDamage(grenade);
        Assert.That(firstHits, Is.EqualTo(1));
        Assert.That(secondHits, Is.EqualTo(1));
        Assert.That(first.CurrentHealth, Is.LessThan(100f));
        Assert.That(second.CurrentHealth, Is.LessThan(100f));
    }

    [Test]
    public void WallUnderSharedParentStillBlocksExplosion()
    {
        var target = Target("Enemy", Vector3.right * 3f);
        var wall = new GameObject("Wall");
        wall.transform.SetParent(root.transform);
        wall.transform.position = Vector3.right * 1.5f;
        wall.transform.localScale = new Vector3(0.3f, 4f, 4f);
        wall.AddComponent<BoxCollider>();
        var grenade = CreateGrenade();
        Physics.SyncTransforms();
        ApplyExplosionDamage(grenade);
        Assert.That(target.CurrentHealth, Is.EqualTo(100f));
    }

    [Test]
    public void EnemyGunCanHitAnotherCharacterUnderTheSameParent()
    {
        var shooter = Target("Shooter", Vector3.zero);
        var target = Target("Target", Vector3.forward * 4f);
        var gun = shooter.gameObject.AddComponent<HitscanWeapon>();
        typeof(HitscanWeapon).GetField("spreadAngle", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(gun, 0f);
        Physics.SyncTransforms();
        Assert.That(gun.TryFire(target.transform.position, shooter.gameObject), Is.True);
        Assert.That(target.CurrentHealth, Is.EqualTo(75f));
        Assert.That(shooter.CurrentHealth, Is.EqualTo(100f));
        Assert.That(gun.AmmunitionInMagazine, Is.EqualTo(29));
    }

    private ThrownGrenade CreateGrenade()
    {
        var go = new GameObject("Grenade");
        go.transform.SetParent(root.transform);
        var grenade = go.AddComponent<ThrownGrenade>();
        grenade.damage = 25f;
        grenade.damageRadius = 6f;
        grenade.targetMask = ~0;
        grenade.coverMask = ~0;
        return grenade;
    }

    private static void ApplyExplosionDamage(ThrownGrenade grenade)
    {
        // Проверяем расчёт реальной физикой, без VFX/Destroy и ожидания запала.
        typeof(ThrownGrenade).GetMethod("ApplyDamage", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(grenade, new object[] { Vector3.zero });
    }
}
