using UnityEngine;

/// <summary>
/// Метка visual-only персонажа: модель без ИИ (ни EnemyAI, ни GermanBeggar,
/// ни HitscanWeapon). Ставится на корень префаба GermanStatic — по ней
/// сценарий понимает, что это статичная фигура, а не боевой солдат.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Flame of History/German Static Marker")]
public class GermanStaticMarker : MonoBehaviour
{
}
