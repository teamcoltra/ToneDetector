﻿using System;
using System.Collections.Generic;
using System.Linq;
using log4net;
using Mp3Reader.Interface;
using NAudio.Dsp;

namespace Mp3Reader
{
    public class TonePatternDetector : ITonePatternDetector
    {
        private readonly int _targetFrequency1;
        private readonly int _targetFrequency2;
        private readonly int _sampleRate;
        private DateTime _lastTargetFrequencyHitTimeUtc;

        private PatternState _state;
        private int _previousDominantFrequency;
        private static readonly ILog Log =
            LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public TonePatternDetector(int targetFrequency1, int targetFrequency2, int sampleRate)
        {
            _targetFrequency1 = targetFrequency1;
            _targetFrequency2 = targetFrequency2;
            _sampleRate = sampleRate;
            _state = PatternState.NoTargetFrequencyDetected;
        }

        public void Reset()
        {
            _state = PatternState.NoTargetFrequencyDetected;
        }

        public bool Detected(float[] samples)
        {
            if (_state == PatternState.ToneDetected) return true;
            Validate(samples);

            var fft = CreateFftBuffer(samples);
            FastFourierTransform.FFT(true, GetLog(samples.Length), fft);
            UpdateState(fft);

            return _state == PatternState.ToneDetected;
        }

        private static void Validate(float[] samples)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (!samples.Any()) throw new ArgumentException("samples cannot be empty");
        }

        private static Complex[] CreateFftBuffer(float[] samples)
        {
            var fft = new Complex[samples.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                var fftComplexInput = CreateComplexInput(samples, i);
                fft[i] = fftComplexInput;
            }
            return fft;
        }

        private static Complex CreateComplexInput(IReadOnlyList<float> buffer, int index)
        {
            var real = CreateRealComponent(buffer, index);
            const int imaginary = 0;

            return new Complex { X = real, Y = imaginary };
        }

        private static float CreateRealComponent(IReadOnlyList<float> buffer, int index)
        {
            return (float)(buffer[index] * FastFourierTransform.HammingWindow(index, buffer.Count));
        }

        private static int GetLog(int bufferSize)
        {
            return (int)Math.Log(bufferSize, 2);
        }

        private void UpdateState(Complex[] fft)
        {
            var currentDominantFrequency = GetDominantFrequency(fft);

            if (_previousDominantFrequency == currentDominantFrequency) return;

            var currentTargetFrequency = GetCurrentTargetFrequency();
            if (currentDominantFrequency == currentTargetFrequency)
            {
                Log.Debug($"Detected target frequency {currentDominantFrequency}");
                _state = StateTransitionMap[_state];
                _lastTargetFrequencyHitTimeUtc = DateTime.UtcNow;
            }
            else if (TooMuchTimeHasElapsed())
            {
                _state = PatternState.NoTargetFrequencyDetected;
            }

            _previousDominantFrequency = currentDominantFrequency;
        }

        //todo - this is not an ideal method
        //Would be good to calculate the elapsed time using sample rate instead
        private bool TooMuchTimeHasElapsed()
        {
            return DateTime.UtcNow - TimeSpan.FromMilliseconds(4) > _lastTargetFrequencyHitTimeUtc;
        }

        private static readonly Dictionary<PatternState, PatternState> StateTransitionMap = new Dictionary<PatternState, PatternState>
        {
            {PatternState.NoTargetFrequencyDetected, PatternState.Repetion1Frequency1},
            {PatternState.Repetion1Frequency1, PatternState.Repetion1Frequency2},
            {PatternState.Repetion1Frequency2, PatternState.Repetion2Frequency1},
            {PatternState.Repetion2Frequency1, PatternState.Repetion2Frequency2},
            {PatternState.Repetion2Frequency2, PatternState.Repetion3Frequency1},
            {PatternState.Repetion3Frequency1, PatternState.ToneDetected}
        };

        private int GetDominantFrequency(Complex[] fft)
        {
            var indexOfMaxMagnitude = GetIndexOfMaxMagnitude(fft);
            return GetFrequency(indexOfMaxMagnitude, _sampleRate, fft.Length);
        }

        private static int GetIndexOfMaxMagnitude(Complex[] fft)
        {
            double maxMagnitude = 0;
            var indexOfMaxMagnitude = 0;
            for (var i = 0; i < fft.Length; i++)
            {
                var magnitude = CalculateMagnitude(fft[i]);
                if (magnitude > maxMagnitude)
                {
                    indexOfMaxMagnitude = i;
                    maxMagnitude = magnitude;
                }
            }
            return indexOfMaxMagnitude;
        }

        private static double CalculateMagnitude(Complex complex)
        {
            return Math.Sqrt((complex.X * complex.X) + (complex.Y * complex.Y));
        }

        private static int GetFrequency(int indexOfMaxMagnitude, int sampleRate, int bufferSize)
        {
            return indexOfMaxMagnitude * sampleRate / bufferSize;
        }

        private int GetCurrentTargetFrequency()
        {
            switch (_state)
            {
                case PatternState.Repetion1Frequency1:
                case PatternState.Repetion2Frequency1: 
                case PatternState.Repetion3Frequency1:
                    return _targetFrequency2;
                case PatternState.NoTargetFrequencyDetected:
                case PatternState.Repetion1Frequency2:
                case PatternState.Repetion2Frequency2:
                    return _targetFrequency1;
                default:
                    throw new Exception("Component has entered an unexpected state");
            }
        }
    }
}
