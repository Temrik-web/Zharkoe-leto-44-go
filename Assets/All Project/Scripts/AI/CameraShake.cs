using UnityEngine;
using System.Collections;

namespace ZharkoeLeto44.AI
{
    /// <summary>
    /// Тряска камеры. Базовый (не root-motion) сдвиг локальной позиции.
    /// ВАЖНО: позиция-оригинал запоминается ОДИН раз и не перезаписывается
    /// на время тряски — иначе вложенные тряски уводили камеру навсегда.
    /// </summary>
    public sealed class CameraShake : MonoBehaviour
    {
        [Header("Настройки тряски")]
        [SerializeField] private float defaultDuration = 0.15f;
        [SerializeField] private float defaultMagnitude = 0.1f;
        [SerializeField] private float dampingSpeed = 2f;

        private Vector3 _originalLocalPosition;
        private Coroutine _shakeRoutine;
        private bool _shaking;

        private void Awake()
        {
            _originalLocalPosition = transform.localPosition;
        }

        public void Shake(float duration, float magnitude)
        {
            if (duration <= 0f || magnitude <= 0f) return;

            // Оригинал запоминаем ТОЛЬКО когда тряски нет: раньше он
            // перезаписывался текущей (уже сдвинутой) позицией, и вложенная
            // тряска оставляла камеру сдвинутой навсегда.
            if (!_shaking) _originalLocalPosition = transform.localPosition;

            if (_shakeRoutine != null) StopCoroutine(_shakeRoutine);
            _shakeRoutine = StartCoroutine(ShakeRoutine(duration, magnitude));
        }

        public void TriggerShake(float duration, float magnitude)
        {
            Shake(duration > 0f ? duration : defaultDuration,
                  magnitude > 0f ? magnitude : defaultMagnitude);
        }

        private void OnDisable()
        {
            // Камеру выключили посреди тряски — корутина встала, позиция
            // осталась сдвинутой. Возвращаем на место.
            if (_shakeRoutine != null)
            {
                StopCoroutine(_shakeRoutine);
                _shakeRoutine = null;
            }
            _shaking = false;
            transform.localPosition = _originalLocalPosition;
        }

        private IEnumerator ShakeRoutine(float duration, float magnitude)
        {
            _shaking = true;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float x = Random.Range(-1f, 1f) * magnitude;
                float y = Random.Range(-1f, 1f) * magnitude;
                float z = Random.Range(-1f, 1f) * magnitude * 0.3f;
                transform.localPosition = _originalLocalPosition + new Vector3(x, y, z);
                elapsed += Time.deltaTime;
                yield return null;
            }

            while (Vector3.Distance(transform.localPosition, _originalLocalPosition) > 0.001f)
            {
                if (dampingSpeed <= 0f) break;
                // Lerp НЕ зажимает t: при dampingSpeed * dt > 1 камера
                // пролетала мимо оригинала и металась. Зажимаем.
                float t = Mathf.Clamp01(dampingSpeed * Time.deltaTime);
                transform.localPosition = Vector3.Lerp(
                    transform.localPosition, _originalLocalPosition, t);
                yield return null;
            }

            transform.localPosition = _originalLocalPosition;
            _shaking = false;
            _shakeRoutine = null;
        }
    }
}
