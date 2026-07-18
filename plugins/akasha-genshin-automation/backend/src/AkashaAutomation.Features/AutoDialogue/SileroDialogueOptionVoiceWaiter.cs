using AkashaAutomation.BetterGiPort.Assets;
using AkashaAutomation.BetterGiPort.Compatibility.Audio;
using AkashaAutomation.Core.Abstractions;

namespace AkashaAutomation.Features.AutoDialogue;

public sealed class SileroDialogueOptionVoiceWaiter : IDialogueOptionVoiceWaiter
{
    private static readonly TimeSpan SpeechStartGrace = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan NoSpeechQuietDuration = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan SpeechQuietDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SpeechRiseDuration = TimeSpan.FromMilliseconds(160);
    private readonly IClock _clock;
    private readonly IAssetPathResolver _assetPathResolver;
    private readonly Func<int, string, IBetterGiDialogueVoiceDetector> _detectorFactory;
    private readonly object _gate = new();
    private IBetterGiDialogueVoiceDetector? _detector;
    private WaitState? _state;
    private int? _unavailableProcessId;
    private DateTimeOffset _retryAfterUtc = DateTimeOffset.MinValue;

    public SileroDialogueOptionVoiceWaiter(IClock clock, IAssetPathResolver assetPathResolver)
        : this(clock, assetPathResolver, BetterGiDialogueVoiceDetector.Create)
    {
    }

    public SileroDialogueOptionVoiceWaiter(
        IClock clock,
        IAssetPathResolver assetPathResolver,
        Func<int, string, IBetterGiDialogueVoiceDetector> detectorFactory)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _assetPathResolver = assetPathResolver ?? throw new ArgumentNullException(nameof(assetPathResolver));
        _detectorFactory = detectorFactory ?? throw new ArgumentNullException(nameof(detectorFactory));
    }

    public bool IsWaiting
    {
        get { lock (_gate) return _state is not null; }
    }

    public bool IsFallback
    {
        get { lock (_gate) return _state?.IsFallback == true; }
    }

    public bool Start(int processId, TimeSpan maximumWait, TimeSpan fallbackDelay)
    {
        lock (_gate)
        {
            _state = null;
            if (maximumWait > TimeSpan.Zero)
            {
                var detector = GetDetector(processId);
                if (detector is not null)
                {
                    detector.Reset();
                    _state = WaitState.Audio(detector, _clock.UtcNow, maximumWait, fallbackDelay);
                    return true;
                }
            }

            return StartFallback(fallbackDelay);
        }
    }

    public bool Update()
    {
        lock (_gate)
        {
            if (_state is null)
            {
                return true;
            }

            var now = _clock.UtcNow;
            if (_state.IsFallback)
            {
                if (now < _state.StartedAtUtc + _state.MaximumWait)
                {
                    return false;
                }

                _state = null;
                return true;
            }

            try
            {
                var state = _state;
                var elapsed = now - state.StartedAtUtc;
                if (elapsed >= state.MaximumWait)
                {
                    _state = null;
                    return true;
                }

                var probability = state.Detector!.Update();
                if (float.IsNaN(probability) || float.IsInfinity(probability))
                {
                    probability = 0;
                }

                if (probability >= 0.60f)
                {
                    state.VoiceLikeSinceUtc ??= now;
                    if (now - state.VoiceLikeSinceUtc >= SpeechRiseDuration)
                    {
                        state.HeardSpeech = true;
                    }

                    state.QuietSinceUtc = null;
                    return false;
                }

                state.VoiceLikeSinceUtc = null;
                if (!state.HeardSpeech && probability > 0.35f)
                {
                    state.QuietSinceUtc = null;
                    return false;
                }

                state.QuietSinceUtc ??= now;
                if (!state.HeardSpeech && elapsed < SpeechStartGrace)
                {
                    return false;
                }

                var requiredQuiet = state.HeardSpeech ? SpeechQuietDuration : NoSpeechQuietDuration;
                if (now - state.QuietSinceUtc < requiredQuiet)
                {
                    return false;
                }

                _state = null;
                return true;
            }
            catch
            {
                var fallback = _state?.FallbackDelay ?? TimeSpan.Zero;
                ReleaseDetector();
                _state = null;
                return !StartFallback(fallback);
            }
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            _state = null;
            ReleaseDetector();
        }
    }

    public ValueTask DisposeAsync()
    {
        Cancel();
        return ValueTask.CompletedTask;
    }

    private IBetterGiDialogueVoiceDetector? GetDetector(int processId)
    {
        if (processId <= 0)
        {
            return null;
        }

        if (_detector?.ProcessId == processId)
        {
            return _detector;
        }

        ReleaseDetector();
        if (_unavailableProcessId == processId && _clock.UtcNow < _retryAfterUtc)
        {
            return null;
        }

        try
        {
            _detector = _detectorFactory(
                processId,
                _assetPathResolver.Resolve(BetterGiAssetPaths.SileroVadModel));
            _unavailableProcessId = null;
            _retryAfterUtc = DateTimeOffset.MinValue;
            return _detector;
        }
        catch
        {
            _unavailableProcessId = processId;
            _retryAfterUtc = _clock.UtcNow.AddSeconds(5);
            return null;
        }
    }

    private bool StartFallback(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            return false;
        }

        _state = WaitState.Fallback(_clock.UtcNow, delay);
        return true;
    }

    private void ReleaseDetector()
    {
        _detector?.Dispose();
        _detector = null;
    }

    private sealed class WaitState
    {
        private WaitState(
            IBetterGiDialogueVoiceDetector? detector,
            DateTimeOffset startedAtUtc,
            TimeSpan maximumWait,
            TimeSpan fallbackDelay,
            bool isFallback)
        {
            Detector = detector;
            StartedAtUtc = startedAtUtc;
            MaximumWait = maximumWait;
            FallbackDelay = fallbackDelay;
            IsFallback = isFallback;
        }

        public IBetterGiDialogueVoiceDetector? Detector { get; }
        public DateTimeOffset StartedAtUtc { get; }
        public TimeSpan MaximumWait { get; }
        public TimeSpan FallbackDelay { get; }
        public bool IsFallback { get; }
        public bool HeardSpeech { get; set; }
        public DateTimeOffset? VoiceLikeSinceUtc { get; set; }
        public DateTimeOffset? QuietSinceUtc { get; set; }

        public static WaitState Audio(
            IBetterGiDialogueVoiceDetector detector,
            DateTimeOffset now,
            TimeSpan maximumWait,
            TimeSpan fallbackDelay) =>
            new(detector, now, maximumWait, fallbackDelay, false);

        public static WaitState Fallback(DateTimeOffset now, TimeSpan delay) =>
            new(null, now, delay, TimeSpan.Zero, true);
    }
}
