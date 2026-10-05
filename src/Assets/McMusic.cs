using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace OuterCraft.Assets
{
    /// A C418 track at the start of every loop, quietly over Timber Hearth's own sounds, like
    /// Minecraft's game music. Streamed from the player's Minecraft assets; one track per loop.
    public sealed class McMusic
    {
        public float Volume = 0.3f;
        private AudioSource _src;
        private bool _started;
        private float _startAt = float.MaxValue;
        private static readonly System.Random Rng = new System.Random();
        private int _last = -1;

        /// A new loop: a track a few seconds after waking up (the old one went with the scene).
        public void OnNewLoop()
        {
            _src = null;
            _started = false;
            _startAt = Time.unscaledTime + 6f;
        }

        public void Update(bool enabled, bool quiet)
        {
            if (!_started && enabled && Time.unscaledTime >= _startAt && McSounds.MusicFiles.Count > 0)
            {
                _started = true;
                int i = Rng.Next(McSounds.MusicFiles.Count);
                if (i == _last && McSounds.MusicFiles.Count > 1) i = (i + 1) % McSounds.MusicFiles.Count;
                _last = i;
                OuterCraft.Instance.StartCoroutine(Play(McSounds.MusicFiles[i]));
            }
            if (_src == null) return;
            float target = enabled && !quiet ? Volume * McSounds.Volume : 0f;
            _src.volume = Mathf.MoveTowards(_src.volume, target, Time.unscaledDeltaTime * Mathf.Max(0.05f, Volume) / 3f);
        }

        private IEnumerator Play(string path)
        {
            using (var req = UnityWebRequestMultimedia.GetAudioClip(new System.Uri(path).AbsoluteUri, UnityEngine.AudioType.OGGVORBIS))
            {
                ((DownloadHandlerAudioClip)req.downloadHandler).compressed = true; // a few minutes of stereo: keep it small
                yield return req.SendWebRequest();
                if (req.isNetworkError || req.isHttpError) yield break;
                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null) yield break;
                var go = new GameObject("OuterCraft_Music"); // a scene object: gone with the loop
                _src = go.AddComponent<AudioSource>();
                _src.clip = clip;
                _src.spatialBlend = 0f;
                _src.loop = false;
                _src.volume = 0f;
                _src.priority = 0;
                _src.Play();
                OuterCraft.Log("music: " + System.IO.Path.GetFileName(path));
            }
        }
    }
}
