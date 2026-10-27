namespace YTRadioMini.Models;

/// <summary>播放状态。由 RadioController 统一维护，UI 只读取。</summary>
public enum PlaybackState
{
    Idle,
    Resolving,
    Loading,
    Playing,
    Paused,
    Error,
    Stopped,
}
