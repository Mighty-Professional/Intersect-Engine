using Intersect.Client.Framework.Audio;
using Intersect.Client.Framework.Entities;
using Intersect.Client.Framework.Core.Sounds;
using Microsoft.JSInterop;

namespace Intersect.Client.Web.Audio;

/// <summary>
/// Web Audio API-based audio manager.
/// </summary>
public class WebAudioManager : IAudioManager
{
    private readonly IJSRuntime _js;
    private readonly List<WebSoundInstance> _activeSounds = new();
    private readonly string _assetBaseUrl;

    public WebAudioManager(IJSRuntime js, string assetBaseUrl)
    {
        _js = js;
        _assetBaseUrl = assetBaseUrl.TrimEnd('/');
    }

    public IMapSound PlayMapSound(string filename, int x, int y, Guid mapId, bool loop, int loopInterval, int distance, IEntity? parent = null)
    {
        var sound = new WebMapSound(_js, $"{_assetBaseUrl}/sounds/{filename}", filename, x, y, mapId, loop, parent);
        _ = sound.LoadAndPlayAsync();
        return sound;
    }

    public ISound PlaySound(string filename, bool loop)
    {
        var sound = new WebSoundInstance(_js, $"{_assetBaseUrl}/sounds/{filename}", filename, loop);
        _ = sound.LoadAndPlayAsync();
        _activeSounds.Add(sound);
        return sound;
    }

    public void StopSound(ISound sound)
    {
        sound.Stop();
    }

    public void StopSound(IMapSound sound)
    {
        sound.Stop();
    }

    public void StopAllSounds()
    {
        foreach (var s in _activeSounds) s.Stop();
        _activeSounds.Clear();
    }

    public void PlayMusic(string filename, int fadeout = 0, int fadein = 0, bool loop = false)
    {
        var url = $"{_assetBaseUrl}/music/{filename}";
        ((IJSInProcessRuntime)_js).InvokeVoid("IntersectAudio.playMusic", url, 1.0f, loop, fadein);
    }

    public void StopMusic(int fadeout = 0)
    {
        ((IJSInProcessRuntime)_js).InvokeVoid("IntersectAudio.stopMusic", fadeout);
    }
}

public class WebSoundInstance : ISound
{
    private readonly IJSRuntime _js;
    private readonly string _url;
    private int _instanceId = -1;

    public WebSoundInstance(IJSRuntime js, string url, string name, bool loop)
    {
        _js = js;
        _url = url;
        Loop = loop;
    }

    public bool Loaded { get; set; }
    public bool Loop { get; set; }

    public async Task LoadAndPlayAsync()
    {
        try
        {
            var bufferId = await _js.InvokeAsync<int>("IntersectAudio.loadSound", _url);
            if (bufferId > 0)
            {
                Loaded = true;
                _instanceId = ((IJSInProcessRuntime)_js).Invoke<int>(
                    "IntersectAudio.playSound", bufferId, 1.0f, Loop);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load sound: {_url}: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (_instanceId > 0)
        {
            ((IJSInProcessRuntime)_js).InvokeVoid("IntersectAudio.stopSound", _instanceId);
            _instanceId = -1;
        }
    }

    public bool Update() => _instanceId > 0;
}

public class WebMapSound : WebSoundInstance, IMapSound
{
    public WebMapSound(IJSRuntime js, string url, string name, int x, int y, Guid mapId, bool loop, IEntity? parent)
        : base(js, url, name, loop)
    {
    }

    public void UpdatePosition(int x, int y, Guid mapId)
    {
        // Positional audio can be implemented later with Web Audio panner nodes
    }
}
