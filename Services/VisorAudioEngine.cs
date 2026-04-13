using Android.Media;
using Android.Media.Audiofx;
using System;
using System.Threading.Tasks;

namespace Visor.Services
{
    public class VisorAudioEngine
    {
        private AudioRecord _recorder;
        private int _bufferSizeInBytes;
        private NoiseSuppressor? _noiseSuppressor;
        private AutomaticGainControl? _automaticGainControl;
        private AcousticEchoCanceler? _acousticEchoCanceler;

        public bool Start()
        {
            try
            {
                Stop();

                int sampleRate = 16000;
                ChannelIn channels = ChannelIn.Mono;
                Encoding format = Encoding.Pcm16bit;

                _bufferSizeInBytes = AudioRecord.GetMinBufferSize(sampleRate, channels, format);
                if (_bufferSizeInBytes < 0) return false;

                // Prefer VoiceCommunication (better AEC/NS/AGC behavior on many devices), fallback to VoiceRecognition.
                _recorder = new AudioRecord(AudioSource.VoiceCommunication, sampleRate, channels, format, _bufferSizeInBytes);
                if (_recorder.State != State.Initialized)
                {
                    try { _recorder.Release(); } catch { }
                    _recorder = new AudioRecord(AudioSource.VoiceRecognition, sampleRate, channels, format, _bufferSizeInBytes);
                }

                if (_recorder.State != State.Initialized) return false;

                TryEnableVoiceEffects(_recorder.AudioSessionId);

                _recorder.StartRecording();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("AudioRecord Start Error: " + ex.Message);
                return false;
            }
        }

        private void TryEnableVoiceEffects(int audioSessionId)
        {
            try
            {
                if (audioSessionId <= 0) return;

                if (NoiseSuppressor.IsAvailable)
                {
                    _noiseSuppressor = NoiseSuppressor.Create(audioSessionId);
                    if (_noiseSuppressor is not null) _noiseSuppressor.SetEnabled(true);
                }

                if (AutomaticGainControl.IsAvailable)
                {
                    _automaticGainControl = AutomaticGainControl.Create(audioSessionId);
                    if (_automaticGainControl is not null) _automaticGainControl.SetEnabled(true);
                }

                if (AcousticEchoCanceler.IsAvailable)
                {
                    _acousticEchoCanceler = AcousticEchoCanceler.Create(audioSessionId);
                    if (_acousticEchoCanceler is not null) _acousticEchoCanceler.SetEnabled(true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("AudioFx init error: " + ex.Message);
            }
        }

        public int Read(short[] buffer, int offsetInShorts, int sizeInShorts)
        {
            if (_recorder == null || _recorder.RecordingState != RecordState.Recording) return -1;
            return _recorder.Read(buffer, offsetInShorts, sizeInShorts);
        }

        public void Stop()
        {
            try
            {
                try
                {
                    _noiseSuppressor?.Release();
                    _noiseSuppressor?.Dispose();
                }
                catch { }
                finally { _noiseSuppressor = null; }

                try
                {
                    _automaticGainControl?.Release();
                    _automaticGainControl?.Dispose();
                }
                catch { }
                finally { _automaticGainControl = null; }

                try
                {
                    _acousticEchoCanceler?.Release();
                    _acousticEchoCanceler?.Dispose();
                }
                catch { }
                finally { _acousticEchoCanceler = null; }

                if (_recorder != null)
                {
                    if (_recorder.RecordingState == RecordState.Recording)
                        _recorder.Stop();
                    _recorder.Release();
                    _recorder = null;
                }
            }
            catch { }
        }
    }
}
