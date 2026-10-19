using UnityEngine;

namespace AgaRPG.Combat
{
    /// <summary>
    /// Latency compensation for melee strike animations on the owning client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With client-side prediction the owner starts a strike immediately, but the server - which
    /// decides whether the weapon actually hits - only simulates that input roughly half a round
    /// trip (plus its input buffer) later. If the owner played the swing at full speed, its blade
    /// would visibly pass through opponents before the authoritative hit window even started.
    /// </para>
    /// <para>
    /// Instead, the owner plays the swing with a playback speed that ramps up quadratically,
    /// <c>speed(t) = (t / T)²</c>, reaching full speed after <c>T</c> seconds. The animation then has
    /// advanced <c>∫₀ᵀ (t/T)² dt = T / 3</c> instead of <c>T</c>, i.e. it lags real time by
    /// <c>2T / 3</c> from then on. Choosing <c>T = 1.5 × delay</c> makes the owner's swing line up
    /// with the server's swing, while the start still reacts instantly to the button press (the
    /// wind-up is slowed, not postponed).
    /// </para>
    /// <para>This class is plain C# with no engine state so the maths can be unit tested.</para>
    /// </remarks>
    public sealed class StrikeTimingCompensator
    {
        /// <summary>Ratio between the catch-up time and the delay it compensates (see remarks).</summary>
        public const float CatchUpFactor = 1.5f;

        private float _elapsed;
        private float _catchUpTime;

        /// <summary>True while the swing is still being slowed down.</summary>
        public bool IsCompensating => _catchUpTime > 0f && _elapsed < _catchUpTime;

        /// <summary>Total delay (seconds) the current strike compensates.</summary>
        public float Delay => _catchUpTime / CatchUpFactor;

        /// <summary>Starts compensating a new strike.</summary>
        /// <param name="delaySeconds">How far the server lags behind the owner, in seconds.</param>
        public void Begin(float delaySeconds)
        {
            _elapsed = 0f;
            _catchUpTime = Mathf.Max(0f, delaySeconds) * CatchUpFactor;
        }

        /// <summary>Stops compensating; <see cref="Step"/> returns full speed afterwards.</summary>
        public void Reset()
        {
            _elapsed = 0f;
            _catchUpTime = 0f;
        }

        /// <summary>Advances time and returns the animation playback speed to use (0..1).</summary>
        public float Step(float deltaTime)
        {
            if (_catchUpTime <= 0f) return 1f;

            _elapsed += deltaTime;
            float progress = _elapsed / _catchUpTime;
            return progress >= 1f ? 1f : progress * progress;
        }

        /// <summary>
        /// Estimates how far (in seconds) the server's simulation of this client's input lags behind
        /// the client: half the round trip time plus the ticks the input waits in the server queue.
        /// </summary>
        /// <param name="roundTripTimeMs">Measured round trip time in milliseconds.</param>
        /// <param name="tickDelta">Duration of one network tick in seconds.</param>
        /// <param name="bufferedTicks">Ticks the input is buffered before the server runs it.</param>
        public static float EstimateServerDelay(long roundTripTimeMs, double tickDelta, int bufferedTicks)
        {
            if (roundTripTimeMs <= 0) return 0f;
            return (float)(roundTripTimeMs * 0.5 / 1000.0 + bufferedTicks * tickDelta);
        }
    }
}
