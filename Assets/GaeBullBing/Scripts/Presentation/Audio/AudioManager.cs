using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

namespace GaeBullBing.Presentation.Audio
{
    public enum AudioPlaybackChannel { Sfx, Ui }

    [System.Serializable]
    public sealed class BgmAudioClips
    {
        public AudioClip Title;
        public AudioClip Gameplay;
        public AudioClip Boss;
        public AudioClip Victory;
        public AudioClip Defeat;
    }

    [System.Serializable]
    public sealed class UiAudioClips
    {
        public AudioClip ButtonClick;
        public AudioClip PanelOpen;
        public AudioClip PanelClose;
        public AudioClip Selection;
        public AudioClip Cancel;
    }

    [System.Serializable]
    public sealed class DiceAudioClips
    {
        public AudioClip Select;
        public AudioClip Equip;
        public AudioClip Roll;
        public AudioClip Land;
        public AudioClip Reward;
    }

    [System.Serializable]
    public sealed class PlayerAudioClips
    {
        public AudioClip Step;
        public AudioClip Land;
        public AudioClip LapComplete;
        public AudioClip Teleport;
    }

    [System.Serializable]
    public sealed class TowerAudioClips
    {
        public AudioClip Build;
        public AudioClip Upgrade;
        public AudioClip OverUpgrade;
        public AudioClip FireAttack;
        public AudioClip IceAttack;
        public AudioClip ElectricAttack;
        public AudioClip PhysicsAttack;
        public AudioClip RollingStone;
        public AudioClip ProjectileHit;
    }

    [System.Serializable]
    public sealed class MonsterAudioClips
    {
        public AudioClip Spawn;
        public AudioClip Move;
        public AudioClip Hit;
        public AudioClip Capture;
        public AudioClip Escape;
        public AudioClip BossSpawn;
        public AudioClip BossFeather;
    }

    [System.Serializable]
    public sealed class StatusAudioClips
    {
        public AudioClip Burn;
        public AudioClip Freeze;
        public AudioClip ElectricShock;
        public AudioClip Knockback;
        public AudioClip FireTile;
        public AudioClip IceTile;
        public AudioClip TileCancel;
    }

    [System.Serializable]
    public sealed class GameFlowAudioClips
    {
        public AudioClip PlayerTurn;
        public AudioClip EnemyTurn;
        public AudioClip LapReward;
        public AudioClip Victory;
        public AudioClip Defeat;
    }

    public sealed class AudioManager : MonoBehaviour
    {
        private const string MasterKey = "audio.master.volume";
        private const string BgmKey = "audio.bgm.volume";
        private const string SfxKey = "audio.sfx.volume";
        private const string UiKey = "audio.ui.volume";

        [Header("Mixer Routing (Optional)")]
        [SerializeField] private AudioMixerGroup bgmMixerGroup;
        [SerializeField] private AudioMixerGroup sfxMixerGroup;
        [SerializeField] private AudioMixerGroup uiMixerGroup;
        [Header("Audio Clip Library")]
        [SerializeField] private BgmAudioClips bgm = new();
        [SerializeField] private UiAudioClips ui = new();
        [SerializeField] private DiceAudioClips dice = new();
        [SerializeField] private PlayerAudioClips player = new();
        [SerializeField] private TowerAudioClips tower = new();
        [SerializeField] private MonsterAudioClips monster = new();
        [SerializeField] private StatusAudioClips status = new();
        [SerializeField] private GameFlowAudioClips gameFlow = new();
        [Header("Source Pool")]
        [SerializeField, Min(1)] private int initialPoolSize = 12;
        [SerializeField, Min(1)] private int maximumPoolSize = 32;
        [SerializeField, Range(0f, 1f)] private float worldSpatialBlend;
        [Header("BGM")]
        [SerializeField, Min(0f)] private float defaultBgmFadeDuration = .5f;

        private sealed class PooledSource
        {
            public AudioSource Source;
            public AudioPlaybackChannel Channel;
            public float BaseVolume;
        }

        private readonly List<PooledSource> pool = new();
        private readonly AudioSource[] bgmSources = new AudioSource[2];
        private Coroutine bgmFadeRoutine;
        private int activeBgmSourceIndex;

        public static AudioManager Instance { get; private set; }
        public float MasterVolume { get; private set; } = 1f;
        public float BgmVolume { get; private set; } = 1f;
        public float SfxVolume { get; private set; } = 1f;
        public float UiVolume { get; private set; } = 1f;
        public BgmAudioClips Bgm => bgm;
        public UiAudioClips Ui => ui;
        public DiceAudioClips Dice => dice;
        public PlayerAudioClips Player => player;
        public TowerAudioClips Tower => tower;
        public MonsterAudioClips Monster => monster;
        public StatusAudioClips Status => status;
        public GameFlowAudioClips GameFlow => gameFlow;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadSettings();
            CreateBgmSources();
            for (var index = 0; index < initialPoolSize; index++) CreatePooledSource();
        }

        private void Start() => BindUiButtonSounds();

        private void OnDestroy() { if (Instance == this) Instance = null; }
        private void OnApplicationQuit() => PlayerPrefs.Save();

        public void BindUiButtonSounds()
        {
            var buttons = FindObjectsByType<Button>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (var target in buttons)
                if (target.GetComponent<UIButtonSound>() == null)
                    target.gameObject.AddComponent<UIButtonSound>();
        }

        public void SetMasterVolume(float value) { MasterVolume = Save(MasterKey, value); RefreshVolumes(); }
        public void SetBgmVolume(float value) { BgmVolume = Save(BgmKey, value); RefreshVolumes(); }
        public void SetSfxVolume(float value) { SfxVolume = Save(SfxKey, value); RefreshVolumes(); }
        public void SetUiVolume(float value) { UiVolume = Save(UiKey, value); RefreshVolumes(); }

        public AudioSource PlaySfx(AudioClip clip, float volume = 1f) =>
            Play(clip, AudioPlaybackChannel.Sfx, Vector3.zero, false, volume);
        public AudioSource PlayUi(AudioClip clip, float volume = 1f) =>
            Play(clip, AudioPlaybackChannel.Ui, Vector3.zero, false, volume);
        public void PlayPanelOpen() => PlayUi(ui.PanelOpen);
        public void PlayPanelClose() => PlayUi(ui.PanelClose);
        public AudioSource PlayAt(AudioClip clip, Vector3 position, float volume = 1f,
            AudioPlaybackChannel channel = AudioPlaybackChannel.Sfx) =>
            Play(clip, channel, position, true, volume);

        public void PlayBgm(AudioClip clip, float fadeDuration = -1f)
        {
            if (clip == null) return;
            var active = bgmSources[activeBgmSourceIndex];
            if (active != null && active.clip == clip && active.isPlaying) return;
            StopBgmFade();
            bgmFadeRoutine = StartCoroutine(CrossFadeBgm(
                clip, fadeDuration < 0f ? defaultBgmFadeDuration : fadeDuration));
        }

        public void StopBgm(float fadeDuration = -1f)
        {
            StopBgmFade();
            bgmFadeRoutine = StartCoroutine(FadeOutBgm(
                fadeDuration < 0f ? defaultBgmFadeDuration : fadeDuration));
        }

        private AudioSource Play(AudioClip clip, AudioPlaybackChannel channel,
            Vector3 position, bool spatial, float volume)
        {
            if (clip == null) return null;
            var pooled = FindAvailableSource();
            if (pooled == null) return null;
            pooled.Channel = channel;
            pooled.BaseVolume = Mathf.Clamp01(volume);
            var source = pooled.Source;
            source.Stop();
            source.clip = clip;
            source.loop = false;
            source.transform.position = position;
            source.spatialBlend = spatial ? worldSpatialBlend : 0f;
            source.outputAudioMixerGroup = channel == AudioPlaybackChannel.Ui
                ? uiMixerGroup : sfxMixerGroup;
            source.volume = GetGain(channel) * pooled.BaseVolume;
            source.Play();
            return source;
        }

        private PooledSource FindAvailableSource()
        {
            foreach (var pooled in pool)
                if (!pooled.Source.isPlaying) return pooled;
            return pool.Count < maximumPoolSize ? CreatePooledSource() : null;
        }

        private PooledSource CreatePooledSource()
        {
            var child = new GameObject($"One Shot Audio {pool.Count + 1}");
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.dopplerLevel = 0f;
            var pooled = new PooledSource
                { Source = source, Channel = AudioPlaybackChannel.Sfx, BaseVolume = 1f };
            pool.Add(pooled);
            return pooled;
        }

        private void CreateBgmSources()
        {
            for (var index = 0; index < bgmSources.Length; index++)
            {
                var child = new GameObject($"BGM Audio {index + 1}");
                child.transform.SetParent(transform, false);
                var source = child.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.ignoreListenerPause = true;
                source.outputAudioMixerGroup = bgmMixerGroup;
                bgmSources[index] = source;
            }
        }

        private IEnumerator CrossFadeBgm(AudioClip clip, float duration)
        {
            var oldSource = bgmSources[activeBgmSourceIndex];
            var nextIndex = 1 - activeBgmSourceIndex;
            var nextSource = bgmSources[nextIndex];
            nextSource.clip = clip;
            nextSource.volume = 0f;
            nextSource.Play();
            var oldStart = oldSource.volume;
            var target = MasterVolume * BgmVolume;
            duration = Mathf.Max(0f, duration);
            for (var elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                var progress = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
                oldSource.volume = Mathf.Lerp(oldStart, 0f, progress);
                nextSource.volume = Mathf.Lerp(0f, target, progress);
                yield return null;
            }
            oldSource.Stop();
            oldSource.clip = null;
            oldSource.volume = 0f;
            nextSource.volume = target;
            activeBgmSourceIndex = nextIndex;
            bgmFadeRoutine = null;
        }

        private IEnumerator FadeOutBgm(float duration)
        {
            var source = bgmSources[activeBgmSourceIndex];
            var start = source.volume;
            duration = Mathf.Max(0f, duration);
            for (var elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                source.volume = Mathf.Lerp(start, 0f,
                    duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            source.Stop();
            source.clip = null;
            source.volume = 0f;
            bgmFadeRoutine = null;
        }

        private void StopBgmFade()
        {
            if (bgmFadeRoutine == null) return;
            StopCoroutine(bgmFadeRoutine);
            bgmFadeRoutine = null;
        }

        private void LoadSettings()
        {
            MasterVolume = PlayerPrefs.GetFloat(MasterKey, 1f);
            BgmVolume = PlayerPrefs.GetFloat(BgmKey, 1f);
            SfxVolume = PlayerPrefs.GetFloat(SfxKey, 1f);
            UiVolume = PlayerPrefs.GetFloat(UiKey, 1f);
        }

        private void RefreshVolumes()
        {
            foreach (var pooled in pool)
                pooled.Source.volume = GetGain(pooled.Channel) * pooled.BaseVolume;
            foreach (var source in bgmSources)
                if (source != null && source.isPlaying)
                    source.volume = MasterVolume * BgmVolume;
        }

        private float GetGain(AudioPlaybackChannel channel) => MasterVolume *
            (channel == AudioPlaybackChannel.Ui ? UiVolume : SfxVolume);

        private static float Save(string key, float value)
        {
            value = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(key, value);
            return value;
        }
    }
}
