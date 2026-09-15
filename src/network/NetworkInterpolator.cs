using System.Collections.Generic;
using Godot;

namespace Game.Network
{
    public enum NetworkStateOperation
    {
        None,
        Interpolation,
        Extrapolation,
    }

    public class NetworkInterpolator<T>
        where T : struct, INetworkState
    {
        private List<T> _stateBuffer = new();
        private int _interpolationOffsetMs;
        private NetworkManager _networkManager;

        public NetworkInterpolator(
            NetworkManager networkManager,
            int interpolationOffsetMs = 100
        )
        {
            _networkManager = networkManager;
            _interpolationOffsetMs = interpolationOffsetMs;
        }

        public Vector3 Extrapolate(
            T state1,
            T state2,
            System.Func<T, Vector3> selector,
            float extrapolationFactor
        )
        {
            var s1 = selector(state1);
            var s2 = selector(state2);
            var velocityDelta = s2 - s1;
            return s2 + (velocityDelta * extrapolationFactor);
        }

        public Vector2 Extrapolate(
            T state1,
            T state2,
            System.Func<T, Vector2> selector,
            float extrapolationFactor
        )
        {
            var s1 = selector(state1);
            var s2 = selector(state2);
            var velocityDelta = s2 - s1;
            return s2 + (velocityDelta * extrapolationFactor);
        }

        public float Extrapolate(
            T state1,
            T state2,
            System.Func<T, float> selector,
            float extrapolationFactor
        )
        {
            var s1 = selector(state1);
            var s2 = selector(state2);
            var velocityDelta = s2 - s1;
            return s2 + (velocityDelta * extrapolationFactor);
        }

        public void AddState(T state)
        {
            _stateBuffer.Add(state);
        }

        public bool TryGetNextState(
            out T result,
            System.Func<T, T, float, NetworkStateOperation, T> lerpFunction
        )
        {
            result = default;

            // We need at least 2 states to interpolate/extrapolate
            if (_stateBuffer.Count < 2)
                return false;

            var renderTime =
                _networkManager.ClientClock - _interpolationOffsetMs;

            // Remove all transforms older than the current render time, until
            // we end up with just the last two, one representing the last
            // "snapshot" in the past, and the closest upcoming one.
            while (
                _stateBuffer.Count > 2 && renderTime > _stateBuffer[2].Timestamp
            )
            {
                _stateBuffer.RemoveAt(0);
            }

            // If we have the most recent past state + 2 closest future states,
            // we interpolate.
            if (_stateBuffer.Count > 2)
            {
                float timeDifference =
                    _stateBuffer[2].Timestamp - _stateBuffer[1].Timestamp;

                // If timestamps are equal or inverted, don't interpolate
                var interpolationFactor =
                    timeDifference <= 0
                        ? 1.0f
                        : Mathf.Clamp(
                            (renderTime - _stateBuffer[1].Timestamp)
                                / timeDifference,
                            0.0f,
                            1.0f
                        );

                result = lerpFunction(
                    _stateBuffer[1],
                    _stateBuffer[2],
                    interpolationFactor,
                    NetworkStateOperation.Interpolation
                );
                return true;
            }
            // We extrapolate if we have 2 past world states (but not 3,
            // otherwise we would be interpolating above).
            else if (renderTime > _stateBuffer[1].Timestamp)
            {
                float timeDifference =
                    _stateBuffer[1].Timestamp - _stateBuffer[0].Timestamp;

                var extrapolationFactor =
                    timeDifference <= 0
                        ? 1.0f
                        : Mathf.Clamp(
                            (renderTime - _stateBuffer[0].Timestamp)
                                / timeDifference
                                - 1.00f,
                            0.0f,
                            1.0f
                        );

                result = lerpFunction(
                    _stateBuffer[0],
                    _stateBuffer[1],
                    extrapolationFactor,
                    NetworkStateOperation.Extrapolation
                );
                return true;
            }

            return false;
        }
    }

    // Interface for network states that can be interpolated
    public interface INetworkState
    {
        long Timestamp { get; }
    }
}
