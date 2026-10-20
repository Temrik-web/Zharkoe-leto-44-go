using UnityEngine;

namespace ZharkoeLeto44.AI
{
    [DisallowMultipleComponent]
    public sealed class EnemyVoice : MonoBehaviour
    {
        [Header("Источники звука")]
        [Tooltip("Источник для реплик (крики, боль, смерть). Пусто — создастся сам.")]
        [SerializeField] private AudioSource voiceSource;
        [Tooltip("Источник для звуков тела (шаги, экипировка). Пусто — создастся сам.")]
        [SerializeField] private AudioSource bodySource;

        [Header("Быстрая настройка")]
        [Tooltip("Разово предупредить в консоли, каких важных звуков нет (смерть, боль, шаги, удары). " +
                  "Звуки задаются на каждом враге отдельно, в массивах ниже.")]
        [SerializeField] private bool warnAboutMissingAudio = true;

        [Header("Реплики: обнаружение и бой")]
        [Tooltip("«Вижу его!», «Контакт!» — при переходе в бой.")]
        [SerializeField] private AudioClip[] spottedLines;
        [Tooltip("«Что за шум?» — при переходе в настороженность.")]
        [SerializeField] private AudioClip[] alertLines;
        [Tooltip("«Куда он делся?» — при потере цели и начале поиска.")]
        [SerializeField] private AudioClip[] lostTargetLines;
        [Tooltip("Выкрики во время стрельбы — необязательны.")]
        [SerializeField] private AudioClip[] combatChatterLines;
        [Tooltip("«Перезаряжаюсь!»")]
        [SerializeField] private AudioClip[] reloadLines;
        [Tooltip("«Прикройте!» — при отходе и сильном подавлении.")]
        [SerializeField] private AudioClip[] retreatLines;
        [Tooltip("Крик под плотным огнём (подавление).")]
        [SerializeField] private AudioClip[] suppressedLines;

        [Header("Реплики: урон и смерть")]
        [SerializeField] private AudioClip[] painLines;
        [SerializeField] private AudioClip[] deathLines;
        [Tooltip("Глухие удары попаданий по телу (без голоса). Играют при каждом попадании.")]
        [SerializeField] private AudioClip[] hitImpactClips;
        [SerializeField, Range(0f, 1f)] private float hitImpactVolume = 0.9f;

        [Header("Шаги")]
        [SerializeField] private AudioClip[] footstepClips;
        [Tooltip("Шагов в секунду при скорости 1 м/с. Итоговый темп зависит от скорости.")]
        [SerializeField, Min(0.05f)] private float footstepsPerMeter = 0.55f;
        [Tooltip("Ниже этой скорости шаги не проигрываются.")]
        [SerializeField, Min(0f)] private float footstepMinimumSpeed = 0.35f;
        [SerializeField, Range(0f, 1f)] private float footstepVolume = 0.7f;

        [Header("Громкость реплик")]
        [SerializeField, Range(0f, 1f)] private float voiceVolume = 1f;
        [SerializeField] private Vector2 pitchRange = new(0.94f, 1.06f);

        [Header("Пауза между репликами")]
        [Tooltip("Минимальная пауза между любыми репликами, сек.")]
        [SerializeField, Min(0f)] private float voiceCooldown = 0.6f;
        [Tooltip("Отдельная пауза для боевых выкриков, сек.")]
        [SerializeField, Min(0f)] private float chatterCooldown = 6f;
        [Tooltip("Пауза между вскриками от боли, сек.")]
        [SerializeField, Min(0f)] private float painCooldown = 0.9f;

        [Header("Шум для союзников")]
        [Tooltip("Радиус шума от крика — по нему союзники подтягиваются.")]
        [SerializeField, Min(0f)] private float shoutNoiseRadius = 22f;
        [Tooltip("Эмитить шум при криках обнаружения и боли.")]
        [SerializeField] private bool shoutsMakeNoise = true;

        [Header("3D-звук")]
        [SerializeField, Min(0f)] private float minimumDistance = 3f;
        [SerializeField, Min(1f)] private float maximumDistance = 45f;

        private float _nextVoiceTime;
        private float _nextChatterTime;
        private float _nextPainTime;
        private float _nextImpactTime;
        private float _footstepAccumulator;

        private void Awake()
        {
            voiceSource = EnsureSource(voiceSource, "VoiceSource");
            bodySource = EnsureSource(bodySource, "BodySource");
            WarnAboutMissingAudio();
        }

        private static bool IsEmpty(AudioClip[] clips) => clips == null || clips.Length == 0;

        private void WarnAboutMissingAudio()
        {
            if (!warnAboutMissingAudio) return;
            string missing = "";
            if (IsEmpty(deathLines)) missing += "смерть, ";
            if (IsEmpty(painLines)) missing += "боль, ";
            if (IsEmpty(footstepClips)) missing += "шаги, ";
            if (IsEmpty(hitImpactClips)) missing += "удары, ";
            if (missing.Length > 0)
                Debug.LogWarning($"[EnemyVoice] {name}: нет звуков: {missing.TrimEnd(',', ' ')}. " +
                    "Назначь клипы прямо тут или подложи общий Voice Bank.", this);
        }

        private AudioSource EnsureSource(AudioSource existing, string childName)
        {
            if (existing != null)
            {
                ConfigureSource(existing);
                return existing;
            }

            Transform child = transform.Find(childName);
            GameObject host;

            if (child != null) host = child.gameObject;
            else
            {
                host = new GameObject(childName);
                host.transform.SetParent(transform, false);
                host.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            }

            AudioSource source = host.GetComponent<AudioSource>();
            if (source == null) source = host.AddComponent<AudioSource>();

            ConfigureSource(source);
            return source;
        }

        private void ConfigureSource(AudioSource source)
        {
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = minimumDistance;
            source.maxDistance = maximumDistance;
        }

        public void PlaySpotted()
        {
            if (PlayVoice(spottedLines, 1f) && shoutsMakeNoise)
                EmitShoutNoise(1f);
        }

        public void PlayAlert() => PlayVoice(alertLines, 0.9f);
        public void PlayLostTarget() => PlayVoice(lostTargetLines, 0.85f);

        public void PlayCombatChatter()
        {
            if (Time.time < _nextChatterTime) return;
            if (!PlayVoice(combatChatterLines, 0.85f)) return;
            _nextChatterTime = Time.time + chatterCooldown;
        }
        public void PlayReload() => PlayVoice(reloadLines, 0.9f);

        public void PlayRetreat()
        {
            if (PlayVoice(retreatLines, 1f) && shoutsMakeNoise) EmitShoutNoise(0.8f);
        }
        public void PlaySuppressed() => PlayVoice(suppressedLines, 0.95f);
        public void PlayPain()
        {
            if (Time.time < _nextPainTime) return;
            _nextPainTime = Time.time + painCooldown;

            if (PlayVoice(painLines, 1f, ignoreCooldown: true) && shoutsMakeNoise)
                EmitShoutNoise(0.7f);
        }

        public void PlayDeath()
        {
            AudioClip clip = PickClip(deathLines);
            if (clip == null) return;

            PlayDetached(clip, voiceVolume);

            if (shoutsMakeNoise) EmitShoutNoise(0.9f);
        }

        /// <summary>Глухой удар попадания (без голоса и кулдауна реплик).</summary>
        public void PlayImpact()
        {
            if (Time.time < _nextImpactTime) return;
            _nextImpactTime = Time.time + 0.05f;
            AudioClip clip = PickClip(hitImpactClips);
            if (clip == null || bodySource == null) return;
            bodySource.pitch = Random.Range(pitchRange.x, pitchRange.y);
            bodySource.PlayOneShot(clip, hitImpactVolume);
        }

        public void UpdateFootsteps(float speed, bool grounded)
        {
            if (footstepClips == null || footstepClips.Length == 0) return;

            if (!grounded || speed < footstepMinimumSpeed)
            {
                _footstepAccumulator = 0f;
                return;
            }

            _footstepAccumulator += speed * footstepsPerMeter * Time.deltaTime;

            if (_footstepAccumulator < 1f) return;
            _footstepAccumulator -= 1f;

            AudioClip clip = PickClip(footstepClips);
            if (clip == null || bodySource == null) return;

            bodySource.pitch = Random.Range(pitchRange.x, pitchRange.y);
            bodySource.PlayOneShot(clip, footstepVolume);
        }

        public void PlayBodyOneShot(AudioClip clip, float volume = 1f)
        {
            if (clip == null || bodySource == null) return;
            bodySource.pitch = Random.Range(pitchRange.x, pitchRange.y);
            bodySource.PlayOneShot(clip, volume);
        }

        private bool PlayVoice(AudioClip[] clips, float volumeScale, bool ignoreCooldown = false)
        {
            if (voiceSource == null) return false;
            if (!ignoreCooldown && Time.time < _nextVoiceTime) return false;

            AudioClip clip = PickClip(clips);
            if (clip == null) return false;

            _nextVoiceTime = Time.time + Mathf.Max(voiceCooldown, clip.length * 0.5f);

            voiceSource.pitch = Random.Range(pitchRange.x, pitchRange.y);
            voiceSource.PlayOneShot(clip, voiceVolume * volumeScale);
            return true;
        }

        private static AudioClip PickClip(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0) return null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                AudioClip candidate = clips[Random.Range(0, clips.Length)];
                if (candidate != null) return candidate;
            }

            foreach (AudioClip candidate in clips)
                if (candidate != null) return candidate;

            return null;
        }

        private void PlayDetached(AudioClip clip, float volume)
        {
            var host = new GameObject($"Voice_{clip.name}");
            host.transform.position = voiceSource != null
                ? voiceSource.transform.position
                : transform.position + Vector3.up * 1.5f;

            AudioSource source = host.AddComponent<AudioSource>();
            ConfigureSource(source);
            source.clip = clip;
            source.volume = volume;
            source.pitch = Random.Range(pitchRange.x, pitchRange.y);
            source.Play();

            Destroy(host, clip.length / Mathf.Max(0.1f, source.pitch) + 0.2f);
        }

        private void EmitShoutNoise(float intensity)
        {
            if (shoutNoiseRadius <= 0f) return;
            NoiseSystem.Emit(transform.position, shoutNoiseRadius, gameObject, intensity);
        }
    }
}
